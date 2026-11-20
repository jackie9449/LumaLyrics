using System.Text.Json;
using LumaLyrics.Core;

namespace LumaLyrics.Infrastructure;

public sealed partial class NetEaseTranslationProvider
{
    public Task<IReadOnlyList<LyricsCandidate>> SearchManualAsync(Track query,CancellationToken token)=>SearchExpandedAsync(query,[],false,token);
    public Task<IReadOnlyList<LyricsCandidate>> SearchAutomaticAsync(Track track,IReadOnlyList<LyricsCandidate> known,CancellationToken token)=>SearchExpandedAsync(track,known,true,token);
    async Task<IReadOnlyList<LyricsCandidate>> SearchExpandedAsync(Track query,IReadOnlyList<LyricsCandidate> known,bool automatic,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();var catalog=new List<CatalogSong>();var found=new List<TranslationCandidate>();var unavailable=new List<string>();
        query=TrackMetadata.FromAppleSession(query);
        if(automatic && !AutomaticLyricsSearch.CanExpand(query))return [];
        var catalogStatus=LyricsCatalogStatus.None;
        // Ordinary NetEase lookup already tried the kana keyword variant if needed.
        var queries=automatic?AutomaticLyricsSearch.Queries(query,includeKana:false):ManualLyricsSearch.Queries(query);
        var originalQuery=automatic?ManualLyricsSearch.Queries(query,includeKana:false).FirstOrDefault()?.Text:null;
        var knownIds=known.Where(c=>c.ProviderName=="NetEase").Select(c=>c.Id).ToHashSet();
        using var budget=CancellationTokenSource.CreateLinkedTokenSource(token);budget.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
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
                    found.Add(lyrics);
                }
                catch(LyricsProviderException ex)
                {
                    unavailable.Add(ManualLyricsSearch.Unavailable("NetEase",ex));
                    break;
                }
            }
            // Complete ordinary suggestions first so catalogue failures cannot hide them.
            if(!automatic && unavailable.Count==0 && !found.Any(lyric=>RecordingMatches(query,lyric)))
            {
                var release=await FindLocalizedSongAsync(query,catalog,budget.Token);
                if(release.Unavailable)unavailable.Add("Apple Music / NetEase");
                if(release.Song is {} verified)
                {
                    int index=found.FindIndex(lyric=>lyric.Id==verified.Candidate.Id
                        && lyric.Track==verified.Candidate.Track && lyric.CatalogAlbumId==verified.AlbumId);
                    if(index>=0)found[index]=found[index] with{ReleaseEvidence=verified.ReleaseEvidence};
                    else if(await FetchLyricsAsync(verified,budget.Token) is {} lyrics)
                    {
                        found.RemoveAll(item=>item.Id==lyrics.Id);
                        found.Add(lyrics);
                    }
                }
            }
        }
        catch(OperationCanceledException) when(!token.IsCancellationRequested){unavailable.Add("NetEase（搜索超时）");}
        catch(Exception ex) when(ex is LyricsProviderException or HttpRequestException or JsonException or InvalidOperationException)
        {unavailable.Add(ManualLyricsSearch.Unavailable("NetEase",ex));}
        token.ThrowIfCancellationRequested();
        return new LyricsLookupResult(ManualLyricsSearch.Candidates(query,found.Select(ToCandidate)),unavailable,catalogStatus:catalogStatus);

        LyricsCandidate ToCandidate(TranslationCandidate lyrics)
        {
            var candidate=automatic?lyrics.OriginalCandidateFor(query):lyrics.OriginalCandidate;
            return !candidate.Instrumental && !new LrcParser().Parse(lyrics.Original).IsSynced
                ?candidate with{SyncedLyrics=null,PlainLyrics=lyrics.Original}:candidate;
        }
    }
}
