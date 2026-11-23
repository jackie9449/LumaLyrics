using System.Text;
using System.Text.RegularExpressions;
namespace LumaLyrics.Core;
public static partial class MatchRules
{
    [GeneratedRegex(@"[\p{P}\p{Z}\s]+") ] private static partial Regex Punctuation();
    [GeneratedRegex(@"\b(live|remix|acoustic|instrumental|remaster(?:ed)?|rerecord(?:ed)?)\b|现场|現場|伴奏|重录|重錄", RegexOptions.IgnoreCase)] private static partial Regex Version();
    [GeneratedRegex(@"\b(?:feat(?:uring)?|ft)\.?\s+|\s*[,&;/、]\s*",RegexOptions.IgnoreCase)]private static partial Regex ArtistSeparator();
    public static string Normalize(string value) => Punctuation().Replace(value.Normalize(NormalizationForm.FormKC).ToLowerInvariant(), "");
    [GeneratedRegex(@"(?:[\(\[【]|[\s-])\s*(ライブ|ライヴ|raibu|リミックス|rimikkusu|アコースティック|akōsutikku|akosutikku|インストゥルメンタル|insutorumentaru|リマスター|rimasutā|rimasuta)(?=\s|[\)\]】]|$)",RegexOptions.IgnoreCase)]private static partial Regex JapaneseVersion();
    static string Versions(string value)
    {
        value=value.Normalize(NormalizationForm.FormKC);
        var labels=Version().Matches(value).Select(m=>m.Value.ToLowerInvariant()).Concat(JapaneseVersion().Matches(value).Select(m=>m.Groups[1].Value.ToLowerInvariant() switch
        {
            "ライブ" or "ライヴ" or "raibu"=>"live",
            "リミックス" or "rimikkusu"=>"remix",
            "アコースティック" or "akōsutikku" or "akosutikku"=>"acoustic",
            "インストゥルメンタル" or "insutorumentaru"=>"instrumental",
            _=>"remaster"
        }));
        return string.Join("|",labels.Distinct().Order());
    }
    [GeneratedRegex(@"\s+[-–—]\s*(EP|Single)\s*$",RegexOptions.IgnoreCase)]private static partial Regex ReleaseSuffix();
    static bool AlbumReadingMatches(string left,string right)
    {
        if(JapaneseKana.SameReading(left,right))return true;
        var first=ReleaseSuffix().Match(left);
        var second=ReleaseSuffix().Match(right);
        if(first.Success && second.Success && !first.Groups[1].Value.Equals(second.Groups[1].Value,StringComparison.OrdinalIgnoreCase))return false;
        return (first.Success || second.Success) && JapaneseKana.SameReading(ReleaseSuffix().Replace(left,""),ReleaseSuffix().Replace(right,""));
    }
    public static bool AlbumMatches(string left,string right)=>Normalize(left)==Normalize(right) || AlbumReadingMatches(left,right);

    public static bool CatalogAlbumMatches(string left,string right)
    {
        if(string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))return false;
        var first=ReleaseSuffix().Match(left);
        var second=ReleaseSuffix().Match(right);
        if(first.Success && second.Success && !first.Groups[1].Value.Equals(second.Groups[1].Value,StringComparison.OrdinalIgnoreCase))return false;
        return CatalogRecordingMatch.SameName(ReleaseSuffix().Replace(left,""),ReleaseSuffix().Replace(right,""));
    }
    public static bool SameRecordingVersion(Track left,Track right)
        =>Versions(left.Title+" "+left.Album)==Versions(right.Title+" "+right.Album);

    static bool ArtistReadingMatches(string left,string right)
        =>(JapaneseKana.HasKana(left) || JapaneseKana.HasKana(right)) && ArtistCreditsCover(left,right,0);

    public static bool ArtistCreditsCover(string expected,string actual,int maximumAdditional)
    {
        if(JapaneseKana.SameReading(expected,actual))return true;
        var first=ArtistSeparator().Split(expected).Select(Normalize).Where(name=>name.Length>0).Distinct().ToArray();
        var remaining=ArtistSeparator().Split(actual).Select(Normalize).Where(name=>name.Length>0).Distinct().ToList();
        if(first.Length==0 || first.Length>remaining.Count || remaining.Count-first.Length>maximumAdditional)return false;
        var readings=new List<string>();
        foreach(string artist in first)
        {
            if(!remaining.Remove(artist))readings.Add(artist);
        }
        foreach(string artist in readings)
        {
            var matches=remaining.Where(name=>JapaneseKana.SameReading(artist,name)).Take(2).ToArray();
            if(matches.Length!=1)return false;
            remaining.Remove(matches[0]);
        }
        return true;
    }
    public static ScoredCandidate Score(Track track, LyricsCandidate c)
    {
        if(c.CatalogMatch?.Confirms(track,c)==true)
        {
            double seconds=Math.Abs(track.DurationMs!.Value-c.Track.DurationMs!.Value)/1000;
            return new(c,1,true,$"发行曲目已核对 · 时长差 {seconds:0.0} 秒");
        }
        bool titleExact = Normalize(track.Title) == Normalize(c.Track.Title) && !string.IsNullOrWhiteSpace(track.Title);
        bool artistExact = (Normalize(track.Artist) == Normalize(c.Track.Artist) ||
            string.Join("|",ArtistSeparator().Split(track.Artist).Select(Normalize).Where(x=>x.Length>0).Order())==string.Join("|",ArtistSeparator().Split(c.Track.Artist).Select(Normalize).Where(x=>x.Length>0).Order())) && !string.IsNullOrWhiteSpace(track.Artist);
        bool titleReading=!titleExact && JapaneseKana.SameReading(track.Title,c.Track.Title);
        bool artistReading=!artistExact && ArtistReadingMatches(track.Artist,c.Track.Artist);
        bool albumExact=Normalize(track.Album)==Normalize(c.Track.Album) && !string.IsNullOrWhiteSpace(track.Album);
        bool albumReading=!albumExact && AlbumReadingMatches(track.Album,c.Track.Album);
        bool title = titleExact || titleReading;
        bool sameArtist = artistExact || artistReading;
        var (credits, sameRelease) = title && !sameArtist ? CompareCollaborativeCredits(track, c.Track) : default;
        bool corroboratedCredits = sameRelease && CatalogRecordingMatch.PlausibleDuration(track.DurationMs, c.Track.DurationMs);
        bool album = albumExact || albumReading || corroboratedCredits;
        bool differentVersion = !SameRecordingVersion(track, c.Track);
        double? delta = track.DurationMs.HasValue && c.Track.DurationMs.HasValue ? Math.Abs(track.DurationMs.Value - c.Track.DurationMs.Value) : null;
        bool partialCredits = corroboratedCredits && credits == CreditMatch.Partial;
        double artistWeight = sameArtist || (corroboratedCredits && credits == CreditMatch.Equivalent) ? .30 : partialCredits ? .22 : 0;
        double score = (title ? .4 : 0) + artistWeight + (delta <= 2000 ? .2 : delta <= 5000 ? .1 : 0) + (album ? .1 : 0);
        bool readingIdentity = titleReading || artistReading;
        bool needsReadingEvidence = readingIdentity && (!album || delta > 2000);
        bool eligible = title && artistWeight > 0 && !differentVersion && delta <= 5000 && !needsReadingEvidence
            && !(partialCredits && c.Instrumental);
        bool usesReading = readingIdentity || albumReading;
        string reason = !title ? "歌名不匹配"
            : !sameArtist && credits == CreditMatch.None ? "歌手信息不匹配"
            : differentVersion ? "录音版本不同"
            : delta is null ? "时长未知，请确认"
            : delta > 5000 ? "时长差超过 5 秒"
            : !sameArtist && !corroboratedCredits ? "合作署名有重合，但专辑或时长不足以确认录音"
            : partialCredits && c.Instrumental ? "部分歌手署名相符，纯音乐标记需手动确认"
            : needsReadingEvidence ? "假名读法相符，但专辑或时长不足以确认录音"
            : usesReading ? $"日文读法相符 · 时长差 {delta / 1000:0.0} 秒"
            : partialCredits ? $"合作歌手署名部分相符 · 时长差 {delta / 1000:0.0} 秒"
            : corroboratedCredits ? $"合作歌手署名相符 · 时长差 {delta / 1000:0.0} 秒"
            : $"时长差 {delta / 1000:0.0} 秒";
        return new(c, score, eligible, reason) { UsesJapaneseReading = usesReading };
    }
    public static LyricsCandidate? AutoSelect(Track track, IReadOnlyList<LyricsCandidate> candidates)
    {
        var ranked = candidates.Select(c => Score(track, c)).OrderByDescending(x => x.Score).ThenBy(x=>x.UsesJapaneseReading).ToArray();
        return ranked.Length > 0 && ranked[0].Eligible && ranked[0].Score >= .9 && (ranked.Length == 1 || ranked[0].Score - ranked[1].Score >= .08) ? ranked[0].Candidate : null;
    }
}
