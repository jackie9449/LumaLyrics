using System.Text.Json;
using LumaLyrics.Core;

namespace LumaLyrics.Infrastructure;

public sealed partial class LrclibProvider
{
    public Task<IReadOnlyList<LyricsCandidate>> SearchManualAsync(Track query,CancellationToken token)=>SearchExpandedAsync(query,ManualLyricsSearch.Queries(query),token);
    public Task<IReadOnlyList<LyricsCandidate>> SearchAutomaticAsync(Track track,IReadOnlyList<LyricsCandidate> known,CancellationToken token)=>SearchExpandedAsync(track,AutomaticLyricsSearch.Queries(track),token);
    async Task<IReadOnlyList<LyricsCandidate>> SearchExpandedAsync(Track query,IReadOnlyList<ManualLyricsQuery> queries,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();var found=new List<LyricsCandidate>();var unavailable=new List<string>();
        using var budget=CancellationTokenSource.CreateLinkedTokenSource(token);budget.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            foreach(var search in queries)
            {
                var url=search.Keywords?"search?q="+Uri.EscapeDataString(search.Text)
                    :"search?track_name="+Uri.EscapeDataString(search.Title)+(search.Artist.Length>0?"&artist_name="+Uri.EscapeDataString(search.Artist):"");
                var raw=await GetAsync(url,budget.Token);
                if(raw is null)continue;
                using var json=JsonDocument.Parse(raw);
                if(json.RootElement.ValueKind!=JsonValueKind.Array)throw new LyricsProviderException("LRCLIB 搜索返回格式异常。");
                found.AddRange(json.RootElement.EnumerateArray().Where(e=>e.ValueKind==JsonValueKind.Object).Select(Parse));
            }
        }
        catch(OperationCanceledException) when(!token.IsCancellationRequested){unavailable.Add("LRCLIB（搜索超时）");}
        catch(Exception ex) when(ex is LyricsProviderException or HttpRequestException or JsonException or InvalidOperationException)
        {unavailable.Add(ManualLyricsSearch.Unavailable("LRCLIB",ex));}
        token.ThrowIfCancellationRequested();
        return new LyricsLookupResult(ManualLyricsSearch.Candidates(query,found),unavailable);
    }
}
