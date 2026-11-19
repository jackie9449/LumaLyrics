using System.Text.Json;
using LumaLyrics.Core;

namespace LumaLyrics.Infrastructure;

public sealed partial class NetEaseTranslationProvider
{
    public Task<IReadOnlyList<LyricsCandidate>> SearchManualAsync(Track query,CancellationToken token)=>SearchExpandedAsync(query,[],false,token);
    public Task<IReadOnlyList<LyricsCandidate>> SearchAutomaticAsync(Track track,IReadOnlyList<LyricsCandidate> known,CancellationToken token)=>SearchExpandedAsync(track,known,true,token);
    async Task<IReadOnlyList<LyricsCandidate>> SearchExpandedAsync(Track query,IReadOnlyList<LyricsCandidate> known,bool automatic,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();var catalog=new List<CatalogSong>();var found=new List<LyricsCandidate>();var unavailable=new List<string>();
        if(automatic && !AutomaticLyricsSearch.CanExpand(query))return [];
        var catalogStatus=LyricsCatalogStatus.None;
        // Ordinary NetEase lookup already tried the kana keyword variant if needed.
        // A verified ID can bypass that keyword search, so include it on expansion.
        bool searchedKeywords=automatic && NetEaseVerifiedRelease.FindId(query) is null;
        var queries=automatic?AutomaticLyricsSearch.Queries(query,includeKana:!searchedKeywords):ManualLyricsSearch.Queries(query);
        var originalQuery=searchedKeywords?ManualLyricsSearch.Queries(query,includeKana:false).FirstOrDefault()?.Text:null;
        var knownIds=known.Where(c=>c.ProviderName=="NetEase").Select(c=>c.Id).ToHashSet();
        using var budget=CancellationTokenSource.CreateLinkedTokenSource(token);budget.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            if(!automatic)
            {
                try
                {
                    if(await FindVerifiedSongAsync(query,budget.Token) is {} verified)catalog.Add(verified);
                }
                catch(Exception ex) when(ex is LyricsProviderException or JsonException)
                {
                    unavailable.Add(ManualLyricsSearch.Unavailable("NetEase",ex));
                    if(ex is LyricsProviderException{StatusCode:429 or 405})return new LyricsLookupResult([],unavailable);
                }
            }
            foreach(var text in queries.Select(q=>q.Text).Where(text=>text!=originalQuery).Distinct(StringComparer.Ordinal))
            {
                try
                {
                    catalog.AddRange(await SearchSongsAsync(text,budget.Token));
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
            var eligible=catalog.Where(song=>!automatic || !knownIds.Contains(song.Candidate.Id) && RecordingMatches(query,song.Candidate))
                .DistinctBy(song=>song.Candidate.Id).ToDictionary(song=>song.Candidate.Id);
            if(automatic && catalog.Any(song=>!knownIds.Contains(song.Candidate.Id)) && eligible.Count==0)
                catalogStatus=LyricsCatalogStatus.Unconfirmed;
            foreach(var item in ManualLyricsSearch.Rank(query,eligible.Values.Select(song=>song.Candidate)).Take(automatic?3:8))
            {
                try
                {
                    if(await FetchLyricsAsync(eligible[item.Id],budget.Token) is not {} lyrics)
                    {
                        if(automatic)catalogStatus=LyricsCatalogStatus.MatchedWithoutLyrics;
                        continue;
                    }
                    var candidate=automatic?lyrics.OriginalCandidateFor(query):lyrics.OriginalCandidate;
                    if(!candidate.Instrumental && !new LrcParser().Parse(lyrics.Original).IsSynced)
                        candidate=candidate with{SyncedLyrics=null,PlainLyrics=lyrics.Original};
                    found.Add(candidate);
                }
                catch(LyricsProviderException ex)
                {
                    unavailable.Add(ManualLyricsSearch.Unavailable("NetEase",ex));
                    break;
                }
            }
        }
        catch(OperationCanceledException) when(!token.IsCancellationRequested){unavailable.Add("NetEase（搜索超时）");}
        catch(Exception ex) when(ex is LyricsProviderException or HttpRequestException or JsonException or InvalidOperationException)
        {unavailable.Add(ManualLyricsSearch.Unavailable("NetEase",ex));}
        token.ThrowIfCancellationRequested();
        return new LyricsLookupResult(ManualLyricsSearch.Candidates(query,found),unavailable,catalogStatus:catalogStatus);
    }
}
