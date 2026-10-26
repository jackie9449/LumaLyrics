using System.Text.Json;
using LumaLyrics.Core;

namespace LumaLyrics.Infrastructure;

public sealed partial class NetEaseTranslationProvider
{
    public Task<IReadOnlyList<LyricsCandidate>> SearchManualAsync(Track query,CancellationToken token)=>SearchExpandedAsync(query,[],false,token);
    public Task<IReadOnlyList<LyricsCandidate>> SearchAutomaticAsync(Track track,IReadOnlyList<LyricsCandidate> known,CancellationToken token)=>SearchExpandedAsync(track,known,true,token);
    async Task<IReadOnlyList<LyricsCandidate>> SearchExpandedAsync(Track query,IReadOnlyList<LyricsCandidate> known,bool automatic,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();var catalog=new List<LyricsCandidate>();var found=new List<LyricsCandidate>();var unavailable=new List<string>();
        if(automatic && !AutomaticLyricsSearch.CanExpand(query))return [];
        var queries=automatic?AutomaticLyricsSearch.Queries(query):ManualLyricsSearch.Queries(query);
        var originalQuery=ManualLyricsSearch.Queries(query).FirstOrDefault()?.Text;
        var knownIds=known.Where(c=>c.ProviderName=="NetEase").Select(c=>c.Id).ToHashSet();
        using var budget=CancellationTokenSource.CreateLinkedTokenSource(token);budget.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            foreach(var text in queries.Select(q=>q.Text).Where(text=>!automatic || text!=originalQuery).Distinct(StringComparer.Ordinal))
            {
                try
                {
                    var raw=await GetAsync("api/search/get?s="+Uri.EscapeDataString(text)+"&type=1&limit=30",budget.Token);
                    using var json=JsonDocument.Parse(raw);CheckCode(json.RootElement);
                    if(!json.RootElement.TryGetProperty("result",out var result) || result.ValueKind!=JsonValueKind.Object
                        || !result.TryGetProperty("songs",out var songs) || songs.ValueKind!=JsonValueKind.Array)continue;
                    foreach(var song in songs.EnumerateArray().Take(30))
                    {
                        if(song.ValueKind!=JsonValueKind.Object || !song.TryGetProperty("id",out var id) || id.ValueKind!=JsonValueKind.Number || !id.TryGetInt64(out var number) || number<=0)continue;
                        var title=Text(song,"name");if(title.Length==0)continue;
                        var artist=song.TryGetProperty("artists",out var ar) && ar.ValueKind==JsonValueKind.Array?string.Join(" & ",ar.EnumerateArray().Select(a=>Text(a,"name"))):"";
                        var album=song.TryGetProperty("album",out var al)?Text(al,"name"):"";
                        double? duration=song.TryGetProperty("duration",out var d) && d.ValueKind==JsonValueKind.Number && d.TryGetDouble(out var ms)?ms:null;
                        catalog.Add(new(number,new(title,artist,album,duration),null,null,false){ProviderName="NetEase"});
                    }
                }
                catch(Exception ex) when(ex is LyricsProviderException or HttpRequestException or JsonException or InvalidOperationException)
                {
                    unavailable.Add(ManualLyricsSearch.Unavailable("NetEase",ex));
                    if(ex is LyricsProviderException{StatusCode:429 or 405})return new LyricsLookupResult([],unavailable);
                    break; // A later search failure must not discard catalog results already found.
                }
            }
            // Manual suggestions may have incomplete/different metadata. Automatic
            // fallback must verify the recording before spending a lyric request.
            var eligible=catalog.DistinctBy(c=>c.Id).Where(c=>!automatic || !knownIds.Contains(c.Id) && RecordingMatches(query,c.Track));
            foreach(var item in ManualLyricsSearch.Rank(query,eligible).Take(automatic?3:8))
            {
                try
                {
                    var raw=await GetAsync($"api/song/lyric?id={item.Id}&lv=-1&kv=-1&tv=-1",budget.Token);
                    using var json=JsonDocument.Parse(raw);CheckCode(json.RootElement);
                    var original=Nested(json.RootElement,"lrc");if(string.IsNullOrWhiteSpace(original))continue;
                    bool instrumental=NetEaseRecording.IsExplicitInstrumental(original);
                    var document=new LrcParser().Parse(original);bool synced=document.IsSynced;
                    var provenance=JsonSerializer.Serialize(new NetEaseOriginal("NetEase",item.Id,item.Track,instrumental?"":original,
                        $"https://music.163.com/#/song?id={item.Id}",instrumental,instrumental?original:null));
                    // Match existing automatic credit handling only after RecordingMatches;
                    // the source's original credits remain in the provenance above.
                    var candidateTrack=automatic?item.Track with{Artist=TrackMetadata.FromAppleSession(query).Artist}:item.Track;
                    found.Add(new(item.Id,candidateTrack,!instrumental && synced?original:null,!instrumental && !synced?original:null,instrumental,provenance){ProviderName="NetEase"});
                }
                catch(LyricsProviderException ex)
                {
                    if(ex.StatusCode==404)continue;
                    unavailable.Add(ManualLyricsSearch.Unavailable("NetEase",ex));
                    break;
                }
            }
        }
        catch(OperationCanceledException) when(!token.IsCancellationRequested){unavailable.Add("NetEase（搜索超时）");}
        catch(Exception ex) when(ex is LyricsProviderException or HttpRequestException or JsonException or InvalidOperationException)
        {unavailable.Add(ManualLyricsSearch.Unavailable("NetEase",ex));}
        token.ThrowIfCancellationRequested();
        return new LyricsLookupResult(ManualLyricsSearch.Candidates(query,found),unavailable);
    }
}
