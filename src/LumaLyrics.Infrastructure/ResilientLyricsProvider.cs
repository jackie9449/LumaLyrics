using System.Collections;
using LumaLyrics.Core;
namespace LumaLyrics.Infrastructure;

public sealed class LyricsLookupResult(IEnumerable<LyricsCandidate> candidates,IEnumerable<string>? unavailable=null):IReadOnlyList<LyricsCandidate>
{
    readonly LyricsCandidate[] entries=candidates.ToArray();
    public IReadOnlyList<string> UnavailableSources {get;}=(unavailable??[]).Distinct(StringComparer.Ordinal).ToArray();
    public int Count=>entries.Length;
    public LyricsCandidate this[int index]=>entries[index];
    public IEnumerator<LyricsCandidate> GetEnumerator()=>((IEnumerable<LyricsCandidate>)entries).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
}

public sealed class ResilientLyricsProvider:ILyricsProvider,IDisposable
{
    readonly ILyricsProvider primary;
    readonly ITranslationProvider secondary;
    public ResilientLyricsProvider(ILyricsProvider? primary=null,ITranslationProvider? secondary=null)
    {this.primary=primary??new LrclibProvider();this.secondary=secondary??new NetEaseTranslationProvider();}
    public async Task<IReadOnlyList<LyricsCandidate>> LookupAsync(Track track,CancellationToken token)
    {
        track=TrackMetadata.FromAppleSession(track);
        token.ThrowIfCancellationRequested();var found=new List<LyricsCandidate>();var unavailable=new List<string>();int reachable=0;
        try
        {
            var first=await primary.LookupAsync(track,token);token.ThrowIfCancellationRequested();found.AddRange(first);reachable++;
            if(first is LyricsLookupResult status)unavailable.AddRange(status.UnavailableSources);
            if(AutomaticLyricsMatcher.Select(track,found) is {Candidate.Instrumental:false})return new LyricsLookupResult(found,unavailable);
        }
        catch(Exception ex) when(ex is LyricsProviderException or HttpRequestException){unavailable.Add(Diagnostic("LRCLIB",ex));}
        token.ThrowIfCancellationRequested();
        try
        {
            var next=await secondary.LookupAsync(track,token);token.ThrowIfCancellationRequested();reachable++;
            found.AddRange(next.Where(c=>NetEaseTranslationProvider.RecordingMatches(track,c.Track)).Select(c=>c.OriginalCandidateFor(track)));
        }
        catch(Exception ex) when(ex is LyricsProviderException or HttpRequestException){unavailable.Add(Diagnostic("NetEase",ex));}
        if(reachable==0)throw new LyricsProviderException("歌词来源均暂不可用："+string.Join("、",unavailable)+"；已保留本地歌词，请稍后重试。");
        return new LyricsLookupResult(found.DistinctBy(c=>(c.ProviderName,c.Id)),unavailable);
    }
    static string Diagnostic(string source,Exception error)=>error is LyricsProviderException{StatusCode:{ } code}?$"{source}（{code}）":source;
    public void Dispose(){(primary as IDisposable)?.Dispose();(secondary as IDisposable)?.Dispose();}
}
