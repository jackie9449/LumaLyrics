using System.Text.Json;
using System.Text.RegularExpressions;
using LumaLyrics.Core;

namespace LumaLyrics.Infrastructure;

public sealed partial record NetEaseReleaseEvidence(long AppleReleaseId,long AppleSongId,long SourceAlbumId,long SourceSongId,
    int TrackCount,Track Published,Track Japanese,Track Source)
{
    internal CatalogRecordingMatch? Confirm(LyricsCandidate candidate,long? albumId)
    {
        if(AppleReleaseId<=0 || AppleSongId<=0 || SourceAlbumId<=0 || SourceSongId<=0 || TrackCount<=0
            || SourceAlbumId!=albumId || candidate.Id!=SourceSongId || candidate.Track!=Source
            || !ValidNames(Published) || !ValidNames(Japanese) || !ValidNames(Source))return null;
        if(!SameReleasedTrack(Japanese,Source)
            || !MatchRules.SameRecordingVersion(Published,Japanese)
            || !CatalogRecordingMatch.PlausibleDuration(Published.DurationMs,Japanese.DurationMs)
            || !CatalogRecordingMatch.PlausibleDuration(Japanese.DurationMs,Source.DurationMs)
            || TrackCount==1 && !MatchRules.ArtistCreditsCover(Japanese.Artist,Source.Artist,0)
                && !MatchRules.ArtistCreditsCover(Published.Artist,Source.Artist,0))return null;
        return new(candidate.ProviderName,candidate.Id,Source,Published,Japanese);
    }

    static bool ValidNames(Track? track)=>track is not null
        && !string.IsNullOrWhiteSpace(track.Title) && !string.IsNullOrWhiteSpace(track.Artist) && !string.IsNullOrWhiteSpace(track.Album);

    internal static bool SameReleasedTrack(Track localized,Track source)
        =>MatchRules.CatalogAlbumMatches(localized.Album,source.Album) && MatchRules.SameRecordingVersion(localized,source)
            && (CatalogRecordingMatch.SameName(localized.Title,source.Title)
                || TitleCreditMatches(localized,source) || TitleCreditMatches(source,localized));

    [GeneratedRegex(@"^(?<title>.+?)\s*\(\s*(?:with|feat\.?|featuring|ft\.?)\s+(?<artist>[^()]+)\)\s*$",
        RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)]
    private static partial Regex TitleCredit();

    static bool TitleCreditMatches(Track labelled,Track credited)
    {
        var match=TitleCredit().Match(labelled.Title);
        return match.Success && CatalogRecordingMatch.SameName(match.Groups["title"].Value,credited.Title)
            && MatchRules.ArtistCreditsCover(labelled.Artist+" & "+match.Groups["artist"].Value,credited.Artist,0);
    }
}

public sealed partial class NetEaseTranslationProvider
{
    const int AlbumSearchLimit=24;
    const int ReleaseCacheCapacity=64;
    readonly AppleReleaseCatalog? appleCatalog;
    readonly SemaphoreSlim releaseGate=new(1,1);

    sealed record CachedRelease(DateTimeOffset Expires,IReadOnlyList<CatalogSong> Songs);
    sealed record CatalogAlbum(long Id,string Name,string Artist,int Count,DateOnly? Released);
    readonly record struct ReleaseLookup(CatalogSong? Song,bool Unavailable);

    async Task<ReleaseLookup> FindLocalizedSongAsync(Track track,IReadOnlyList<CatalogSong> hints,CancellationToken token)
    {
        if(appleCatalog is null || !MayNeedLocalizedRelease(track,hints))return default;
        var checkedReleases=Volatile.Read(ref sessionCache).CheckedReleases;
        try
        {
            await releaseGate.WaitAsync(token);
            try
            {
                AppleLocalizedRelease? apple;
                using(var appleBudget=CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    appleBudget.CancelAfter(TimeSpan.FromSeconds(25));
                    apple=await appleCatalog.FindAsync(track,appleBudget.Token);
                    appleBudget.Token.ThrowIfCancellationRequested();
                }
                if(apple is null)return default;

                // Reuse evidence for the verified Apple edition. A track absent from the
                // cached Apple album can still resolve through the normal catalog search.
                if(checkedReleases.TryGetValue(apple,out var cached) && cached.Expires>DateTimeOffset.UtcNow)
                    return new(FindSong(track,cached.Songs),false);

                using var neteaseBudget=CancellationTokenSource.CreateLinkedTokenSource(token);
                neteaseBudget.CancelAfter(TimeSpan.FromSeconds(20));
                var songs=await MatchReleaseAsync(apple,neteaseBudget.Token);
                neteaseBudget.Token.ThrowIfCancellationRequested();
                if(!checkedReleases.ContainsKey(apple) && checkedReleases.Count>=ReleaseCacheCapacity)
                    checkedReleases.Remove(checkedReleases.MinBy(pair=>pair.Value.Expires).Key);
                checkedReleases[apple]=new(DateTimeOffset.UtcNow+(songs.Count>0?TimeSpan.FromHours(1):TimeSpan.FromMinutes(5)),songs);
                return new(FindSong(track,songs),false);
            }
            finally{releaseGate.Release();}
        }
        catch(OperationCanceledException) when(!token.IsCancellationRequested){return new(null,true);}
        catch(Exception ex) when(ex is HttpRequestException or JsonException or LyricsProviderException)
        {
            token.ThrowIfCancellationRequested();
            return new(null,true);
        }
    }

    static bool MayNeedLocalizedRelease(Track track,IReadOnlyList<CatalogSong> hints)
    {
        if(track.DurationMs is not >0 || !double.IsFinite(track.DurationMs.Value)
            || new[]{track.Title,track.Artist,track.Album}.Any(name=>string.IsNullOrWhiteSpace(name) || name.Length>250))return false;
        return new[]{track.Title,track.Artist,track.Album}.Any(HasJapaneseCue)
            || hints.Any(song=>CatalogRecordingMatch.PlausibleDuration(track.DurationMs,song.Candidate.Track.DurationMs)
                && (AppleReleaseCatalog.HasJapaneseText(song.Candidate.Track.Title)
                    || AppleReleaseCatalog.HasJapaneseText(song.Candidate.Track.Album)
                    || MatchRules.CatalogAlbumMatches(track.Album,song.Candidate.Track.Album)));
    }

    static bool HasJapaneseCue(string value)
        =>AppleReleaseCatalog.HasJapaneseText(value) || JapaneseKana.HasMarkedVowel(value)
            || value.Split([' ','-','\t','/','(',')'],StringSplitOptions.RemoveEmptyEntries)
                .Any(word=>word.Length>=5 && JapaneseKana.SearchKatakana(word) is not null);

    static CatalogSong? FindSong(Track track,IReadOnlyList<CatalogSong> songs)
    {
        var matches=songs.Where(song=>song.Candidate.CatalogMatch?.Confirms(track,song.Candidate)==true).Take(2).ToArray();
        return matches.Length==1?matches[0]:null;
    }

    async Task<IReadOnlyList<CatalogSong>> MatchReleaseAsync(AppleLocalizedRelease apple,CancellationToken token)
    {
        var japanese=apple.Japanese;
        var choices=await SearchReleasesAsync(apple,japanese.Name,token);
        if(choices is null or {Length:0})
            choices=await SearchReleasesAsync(apple,japanese.Name+" "+japanese.Artist,token);
        if(choices is null || choices.Length is 0 or >3)return [];
        IReadOnlyList<CatalogSong> verified=[];
        foreach(var choice in choices)
        {
            using var detail=await GetAsync($"api/album/{choice.Id}",token);
            if(!detail.RootElement.TryGetProperty("album",out var node) || ReadAlbum(node) is not {} album
                || album.Id!=choice.Id || !SameRelease(apple,album))continue;
            var parent=AlbumSongsParent(detail.RootElement,node);
            if(!parent.TryGetProperty("songs",out var rows) || rows.ValueKind!=JsonValueKind.Array || rows.GetArrayLength()!=japanese.Count)
                continue;
            var songs=ParseSongs(parent,japanese.Count);
            var matches=ConfirmAlbum(apple,album,songs);
            if(matches.Count==0)continue;
            if(verified.Count>0)return [];
            verified=matches;
        }
        return verified;
    }

    async Task<CatalogAlbum[]?> SearchReleasesAsync(AppleLocalizedRelease apple,string term,CancellationToken token)
    {
        string query=Uri.EscapeDataString(term);
        using var search=await GetAsync($"api/search/get?s={query}&type=10&limit={AlbumSearchLimit}",token);
        if(!search.RootElement.TryGetProperty("result",out var result) || result.ValueKind!=JsonValueKind.Object
            || !result.TryGetProperty("albums",out var albums) || albums.ValueKind!=JsonValueKind.Array)return [];
        if(Integer(result,"albumCount")>AlbumSearchLimit)return null;
        return albums.EnumerateArray().Select(ReadAlbum).OfType<CatalogAlbum>()
            .Where(album=>SameRelease(apple,album)).DistinctBy(album=>album.Id).Take(4).ToArray();
    }

    static bool SameRelease(AppleLocalizedRelease apple,CatalogAlbum other)
        =>other.Count==apple.Japanese.Count && MatchRules.CatalogAlbumMatches(apple.Japanese.Name,other.Name)
            && (MatchRules.ArtistCreditsCover(apple.Japanese.Artist,other.Artist,0)
                || MatchRules.ArtistCreditsCover(apple.Published.Artist,other.Artist,0))
            && (apple.Japanese.Released is not {} date || other.Released is not {} issued || Math.Abs(date.DayNumber-issued.DayNumber)<=1);

    static IReadOnlyList<CatalogSong> ConfirmAlbum(AppleLocalizedRelease apple,CatalogAlbum album,IReadOnlyList<CatalogSong> songs)
    {
        if(songs.Count!=album.Count || songs.Select(s=>s.Candidate.Id).Distinct().Count()!=songs.Count)return [];
        var ordered=songs.OrderBy(s=>s.Disc).ThenBy(s=>s.Number).ToArray();
        var verified=new List<CatalogSong>();
        for(int i=0;i<ordered.Length;i++)
        {
            var source=ordered[i];
            var japanese=apple.Japanese.Songs[i];
            var recording=source.Candidate.Track;
            if(source.AlbumId!=album.Id || source.Disc!=japanese.Disc || source.Number!=japanese.Number
                || string.IsNullOrWhiteSpace(recording.Artist) || !NetEaseReleaseEvidence.SameReleasedTrack(japanese.Track,recording)
                || !(recording.DurationMs>0 && Math.Abs(japanese.Track.DurationMs!.Value-recording.DurationMs.Value)<=2000))return [];
            var proof=new NetEaseReleaseEvidence(apple.Japanese.Id,japanese.Id,album.Id,source.Candidate.Id,
                album.Count,apple.Published.Songs[i].Track,japanese.Track,recording);
            // Edition comparison allows rounding and fades on neighbouring tracks.
            // A track can only be used when its own stricter duration and credit checks pass.
            if(proof.Confirm(source.Candidate,source.AlbumId) is {} match)
                verified.Add(source with{Candidate=source.Candidate with{CatalogMatch=match},ReleaseEvidence=proof});
        }
        return verified;
    }

    static JsonElement AlbumSongsParent(JsonElement root,JsonElement album)
        =>album.TryGetProperty("songs",out var songs) && songs.ValueKind==JsonValueKind.Array?album:root;

    static CatalogAlbum? ReadAlbum(JsonElement node)
    {
        if(Id(node) is not {} id || Integer(node,"size") is not >0 || !node.TryGetProperty("artist",out var artist))return null;
        var name=Text(node,"name");
        var credits=Text(artist,"name");
        if(name.Length==0 || credits.Length==0)return null;
        DateOnly? released=null;
        if(node.TryGetProperty("publishTime",out var time) && time.ValueKind==JsonValueKind.Number
            && time.TryGetInt64(out long millis) && millis is >0 and <=253402300799999)
            released=DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(millis).UtcDateTime);
        return new(id,name,credits,Integer(node,"size"),released);
    }
}
