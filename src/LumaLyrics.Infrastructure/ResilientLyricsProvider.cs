using System.Collections;
using LumaLyrics.Core;
namespace LumaLyrics.Infrastructure;

public enum LyricsCatalogStatus { None, Unconfirmed, MatchedWithoutLyrics }

public sealed class LyricsLookupResult(IEnumerable<LyricsCandidate> candidates,IEnumerable<string>? unavailable=null,
    IReadOnlyList<TranslationCandidate>? translationCandidates=null,LyricsCatalogStatus catalogStatus=LyricsCatalogStatus.None):IReadOnlyList<LyricsCandidate>
{
    readonly LyricsCandidate[] entries=candidates.ToArray();
    public IReadOnlyList<string> UnavailableSources {get;}=(unavailable??[]).Distinct(StringComparer.Ordinal).ToArray();
    public IReadOnlyList<TranslationCandidate>? TranslationCandidates {get;}=translationCandidates;
    public LyricsCatalogStatus CatalogStatus {get;}=catalogStatus;
    public int Count=>entries.Length;
    public LyricsCandidate this[int index]=>entries[index];
    public IEnumerator<LyricsCandidate> GetEnumerator()=>((IEnumerable<LyricsCandidate>)entries).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
}

public sealed class ResilientLyricsProvider:ILyricsProvider,IManualLyricsSearch,ITranslationProvider,IDisposable
{
    const int EmptyCacheCapacity=2048;
    readonly record struct EmptySources(DateTimeOffset? PrimaryEmptyUntil,DateTimeOffset? SecondaryEmptyUntil);
    Dictionary<(string Fingerprint,long DurationBucket),EmptySources> emptyCache=new();
    readonly ILyricsProvider primary;
    readonly ITranslationProvider secondary;
    internal bool IsPrimaryRateLimited => primary is LrclibProvider lrclib && lrclib.IsRateLimited;
    internal bool IsSecondaryRateLimited => secondary is NetEaseTranslationProvider netease && netease.IsRateLimited;
    public ResilientLyricsProvider(ILyricsProvider? primary=null,ITranslationProvider? secondary=null)
    {this.primary=primary??new LrclibProvider();this.secondary=secondary??new NetEaseTranslationProvider();}
    public Task<LyricsLookupResult?> LookupExactAsync(Track track,CancellationToken token)=>primary is LrclibProvider lrclib?Probe(lrclib,track,token):Task.FromResult<LyricsLookupResult?>(null);
    static async Task<LyricsLookupResult?> Probe(LrclibProvider source,Track track,CancellationToken token)
    {
        try{return await source.LookupExactAsync(track,token);}
        catch(Exception e) when(e is LyricsProviderException or HttpRequestException){return null;}
    }
    Task<IReadOnlyList<TranslationCandidate>> ITranslationProvider.LookupAsync(Track track,CancellationToken token)=>secondary.LookupAsync(track,token);
    public Task<IReadOnlyList<LyricsCandidate>> LookupAsync(Track track,CancellationToken token)=>LookupAsync(track,token,preferTranslation:true);
    public async Task<IReadOnlyList<LyricsCandidate>> LookupAsync(Track track,CancellationToken token,bool preferTranslation=true,bool bypassSessionCache=false)
        =>await CompleteLookupAsync(track,null,token,preferTranslation,bypassSessionCache);
    public async Task<LyricsLookupResult> CompleteLookupAsync(Track track,LyricsLookupResult? exact,CancellationToken token,
        bool preferTranslation=true,bool bypassSessionCache=false)
    {
        track=TrackMetadata.FromAppleSession(track);
        token.ThrowIfCancellationRequested();
        var cache=Volatile.Read(ref emptyCache);
        var key=(track.Fingerprint,DurationBucket(track.DurationMs));
        EmptySources previous=default;
        if(!bypassSessionCache)
            lock(cache)cache.TryGetValue(key,out previous);
        var now=DateTimeOffset.UtcNow;
        bool skipPrimary=exact is null && previous.PrimaryEmptyUntil>now;
        bool skipSecondary=previous.SecondaryEmptyUntil>now;

        var found=new List<LyricsCandidate>();var unavailable=new List<string>();int reachable=0;
        bool primaryUnavailable=false,secondaryUnavailable=false,secondaryReached=false;
        IReadOnlyList<TranslationCandidate>? translationCandidates=null;
        var catalogStatus=LyricsCatalogStatus.None;
        if(!skipPrimary)
        {
            try
            {
                var first=exact is not null && primary is LrclibProvider lrclib
                    ?await lrclib.CompleteLookupAsync(track,exact,token):await primary.LookupAsync(track,token);
                token.ThrowIfCancellationRequested();found.AddRange(first);reachable++;
                if(first is LyricsLookupResult{UnavailableSources.Count:>0} status)
                {
                    primaryUnavailable=true;
                    unavailable.AddRange(status.UnavailableSources);
                }
                if(AutomaticLyricsMatcher.Select(track,found) is {Candidate.Instrumental:false})
                {
                    ForgetEmptySources();
                    return new LyricsLookupResult(found,unavailable);
                }
            }
            catch(Exception ex) when(ex is LyricsProviderException or HttpRequestException)
            {
                primaryUnavailable=true;
                unavailable.Add(Diagnostic("LRCLIB",ex));
            }
        }
        token.ThrowIfCancellationRequested();
        if(!skipSecondary)
        {
            try
            {
                var lookup=secondary is NetEaseTranslationProvider netease
                    ?await netease.LookupRecordingsAsync(track,token,preferTranslation):null;
                translationCandidates=lookup?.Candidates??await secondary.LookupAsync(track,token);
                token.ThrowIfCancellationRequested();reachable++;
                secondaryReached=true;
                catalogStatus=lookup?.CatalogStatus??LyricsCatalogStatus.None;
                secondaryUnavailable=lookup?.CatalogUnavailable==true;
                if(secondaryUnavailable)unavailable.Add("Apple Music / NetEase");
                found.AddRange(translationCandidates.Where(c=>NetEaseTranslationProvider.RecordingMatches(track,c))
                    .Select(c=>c.OriginalCandidateFor(track)));
            }
            catch(Exception ex) when(ex is LyricsProviderException or HttpRequestException)
            {
                secondaryUnavailable=true;
                unavailable.Add(Diagnostic("NetEase",ex));
            }
        }
        token.ThrowIfCancellationRequested();
        if(reachable==0)throw SourcesUnavailable();
        if(AutomaticLyricsMatcher.Select(track,found) is null && AutomaticLyricsSearch.CanExpand(track))
        {
            var known=found.ToArray();var expansions=new List<(bool Primary,Task<IReadOnlyList<LyricsCandidate>> Search)>();
            async Task<IReadOnlyList<LyricsCandidate>> ExpandAsync(string name,IAutomaticLyricsSearch source)
            {
                try{return await source.SearchAutomaticAsync(track,known,token);}
                catch(Exception ex) when(ex is LyricsProviderException or HttpRequestException)
                {return new LyricsLookupResult([],[Diagnostic(name,ex)]);}
            }
            if(!skipPrimary && !primaryUnavailable && primary is IAutomaticLyricsSearch first)expansions.Add((true,ExpandAsync("LRCLIB",first)));
            if(secondaryReached && secondary is IAutomaticLyricsSearch second)expansions.Add((false,ExpandAsync("NetEase",second)));
            var expanded=await Task.WhenAll(expansions.Select(entry=>entry.Search));token.ThrowIfCancellationRequested();
            for(int i=0;i<expanded.Length;i++)
            {
                found.AddRange(expanded[i]);
                if(expanded[i] is not LyricsLookupResult result)continue;
                if(result.UnavailableSources.Count>0)
                {
                    unavailable.AddRange(result.UnavailableSources);
                    if(expansions[i].Primary)primaryUnavailable=true;
                    else secondaryUnavailable=true;
                }
                if(result.CatalogStatus>catalogStatus)catalogStatus=result.CatalogStatus;
            }
        }
        var lookupResult=new LyricsLookupResult(found.DistinctBy(c=>(c.ProviderName,c.Id)),unavailable,translationCandidates,catalogStatus);
        if(AutomaticLyricsMatcher.Select(track,lookupResult) is not null)ForgetEmptySources();
        else
        {
            // A partial response can still carry a checked static original.
            if((skipPrimary && secondaryUnavailable || skipSecondary && primaryUnavailable)
                && AutomaticLyricsMatcher.SelectStatic(track,lookupResult) is null)throw SourcesUnavailable();
            if(!bypassSessionCache)
            {
                bool primaryCleanEmpty=!skipPrimary && !primaryUnavailable;
                bool secondaryCleanEmpty=!skipSecondary && !secondaryUnavailable;
                var expires=DateTimeOffset.UtcNow.AddMinutes(30);
                if(primaryCleanEmpty && secondaryUnavailable)RememberEmptySource(new(expires,null));
                else if(secondaryCleanEmpty && primaryUnavailable)RememberEmptySource(new(null,expires));
            }
        }
        return lookupResult;

        void ForgetEmptySources(){lock(cache)cache.Remove(key);}
        void RememberEmptySource(EmptySources entry)
        {
            lock(cache)
            {
                if(cache.Count>=EmptyCacheCapacity && !cache.ContainsKey(key))
                    cache.Remove(cache.MinBy(pair=>pair.Value.PrimaryEmptyUntil??pair.Value.SecondaryEmptyUntil).Key);
                // The other source was unavailable and must remain eligible for retry.
                cache[key]=entry;
            }
        }
        LyricsProviderException SourcesUnavailable()=>new("歌词来源均暂不可用："+string.Join("、",unavailable)+"；已保留本地歌词，请稍后重试。");
    }
    static long DurationBucket(double? durationMs)=>durationMs is >0 && double.IsFinite(durationMs.Value)
        ?long.CreateSaturating(Math.Round(durationMs.Value/1000.0)):-1;

    public void ClearSessionCache()
    {
        Interlocked.Exchange(ref emptyCache,new());
        (secondary as NetEaseTranslationProvider)?.ClearSessionCache();
    }
    static string Diagnostic(string source,Exception error)=>error is LyricsProviderException{StatusCode:{ } code}?$"{source}（{code}）":source;
    public async Task<IReadOnlyList<LyricsCandidate>> SearchManualAsync(Track query,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(ManualLyricsSearch.Queries(query).Count==0)return [];
        async Task<IReadOnlyList<LyricsCandidate>> CaptureAsync(string source,Func<Task<IReadOnlyList<LyricsCandidate>>> search)
        {
            try{return await search();}
            catch(Exception ex) when(ex is LyricsProviderException or HttpRequestException)
            {return new LyricsLookupResult([], [ManualLyricsSearch.Unavailable(source,ex)]);}
        }
        async Task<IReadOnlyList<LyricsCandidate>> SecondaryAsync()=>secondary is IManualLyricsSearch search
            ?await search.SearchManualAsync(query,token):(await secondary.LookupAsync(query,token)).Select(c=>c.OriginalCandidate).ToArray();
        var tasks=new[]
        {
            CaptureAsync("LRCLIB",()=>primary is IManualLyricsSearch search?search.SearchManualAsync(query,token):primary.LookupAsync(query,token)),
            CaptureAsync("NetEase",SecondaryAsync)
        };
        var results=await Task.WhenAll(tasks);token.ThrowIfCancellationRequested();
        return new LyricsLookupResult(ManualLyricsSearch.Candidates(query,results.SelectMany(r=>r)),
            results.OfType<LyricsLookupResult>().SelectMany(r=>r.UnavailableSources));
    }
    public void Dispose(){(primary as IDisposable)?.Dispose();(secondary as IDisposable)?.Dispose();}
}
