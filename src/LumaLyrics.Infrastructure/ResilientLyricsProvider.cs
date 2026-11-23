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
    readonly ILyricsProvider primary;
    readonly ITranslationProvider secondary;
    internal bool IsPrimaryRateLimited => primary is LrclibProvider lrclib && lrclib.IsRateLimited;
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
    public async Task<IReadOnlyList<LyricsCandidate>> LookupAsync(Track track,CancellationToken token,bool preferTranslation=true)
        =>await CompleteLookupAsync(track,null,token,preferTranslation);
    public async Task<LyricsLookupResult> CompleteLookupAsync(Track track,LyricsLookupResult? exact,CancellationToken token,bool preferTranslation=true)
    {
        track=TrackMetadata.FromAppleSession(track);
        token.ThrowIfCancellationRequested();var found=new List<LyricsCandidate>();var unavailable=new List<string>();int reachable=0;
        bool primaryAvailable=false,secondaryAvailable=false;
        IReadOnlyList<TranslationCandidate>? translationCandidates=null;
        var catalogStatus=LyricsCatalogStatus.None;
        try
        {
            var first=exact is not null && primary is LrclibProvider lrclib
                ?await lrclib.CompleteLookupAsync(track,exact,token):await primary.LookupAsync(track,token);
            token.ThrowIfCancellationRequested();found.AddRange(first);reachable++;
            primaryAvailable=first is not LyricsLookupResult{UnavailableSources.Count:>0};
            if(first is LyricsLookupResult status)unavailable.AddRange(status.UnavailableSources);
            if(AutomaticLyricsMatcher.Select(track,found) is {Candidate.Instrumental:false})return new LyricsLookupResult(found,unavailable);
        }
        catch(Exception ex) when(ex is LyricsProviderException or HttpRequestException){unavailable.Add(Diagnostic("LRCLIB",ex));}
        token.ThrowIfCancellationRequested();
        try
        {
            var lookup=secondary is NetEaseTranslationProvider netease
                ?await netease.LookupRecordingsAsync(track,token,preferTranslation):null;
            translationCandidates=lookup?.Candidates??await secondary.LookupAsync(track,token);
            token.ThrowIfCancellationRequested();reachable++;
            secondaryAvailable=true;
            catalogStatus=lookup?.CatalogStatus??LyricsCatalogStatus.None;
            if(lookup?.CatalogUnavailable==true)unavailable.Add("Apple Music / NetEase");
            found.AddRange(translationCandidates.Where(c=>NetEaseTranslationProvider.RecordingMatches(track,c))
                .Select(c=>c.OriginalCandidateFor(track)));
        }
        catch(Exception ex) when(ex is LyricsProviderException or HttpRequestException){unavailable.Add(Diagnostic("NetEase",ex));}
        if(reachable==0)throw new LyricsProviderException("歌词来源均暂不可用："+string.Join("、",unavailable)+"；已保留本地歌词，请稍后重试。");
        if(AutomaticLyricsMatcher.Select(track,found) is null && AutomaticLyricsSearch.CanExpand(track))
        {
            var known=found.ToArray();var expansions=new List<Task<IReadOnlyList<LyricsCandidate>>>();
            async Task<IReadOnlyList<LyricsCandidate>> ExpandAsync(string name,IAutomaticLyricsSearch source)
            {
                try{return await source.SearchAutomaticAsync(track,known,token);}
                catch(Exception ex) when(ex is LyricsProviderException or HttpRequestException)
                {return new LyricsLookupResult([],[Diagnostic(name,ex)]);}
            }
            if(primaryAvailable && primary is IAutomaticLyricsSearch first)expansions.Add(ExpandAsync("LRCLIB",first));
            if(secondaryAvailable && secondary is IAutomaticLyricsSearch second)expansions.Add(ExpandAsync("NetEase",second));
            var expanded=await Task.WhenAll(expansions);token.ThrowIfCancellationRequested();
            found.AddRange(expanded.SelectMany(r=>r));
            foreach(var result in expanded.OfType<LyricsLookupResult>())
            {
                unavailable.AddRange(result.UnavailableSources);
                if(result.CatalogStatus>catalogStatus)catalogStatus=result.CatalogStatus;
            }
        }
        return new LyricsLookupResult(found.DistinctBy(c=>(c.ProviderName,c.Id)),unavailable,translationCandidates,catalogStatus);
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
