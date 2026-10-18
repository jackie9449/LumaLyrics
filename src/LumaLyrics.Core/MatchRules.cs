using System.Text;
using System.Text.RegularExpressions;
namespace LumaLyrics.Core;
public static partial class MatchRules
{
    [GeneratedRegex(@"[\p{P}\p{Z}\s]+") ] private static partial Regex Punctuation();
    [GeneratedRegex(@"\b(live|remix|acoustic|instrumental|remaster(?:ed)?|rerecord(?:ed)?)\b|现场|現場|伴奏|重录|重錄", RegexOptions.IgnoreCase)] private static partial Regex Version();
    [GeneratedRegex(@"\b(?:feat(?:uring)?|ft)\.?\s+|\s*[,&;/、]\s*",RegexOptions.IgnoreCase)]private static partial Regex ArtistSeparator();
    public static string Normalize(string value) => Punctuation().Replace(value.Normalize(NormalizationForm.FormKC).ToLowerInvariant(), "");
    static string Versions(string value) => string.Join("|", Version().Matches(value).Select(m => m.Value.ToLowerInvariant()).Distinct().Order());
    public static ScoredCandidate Score(Track track, LyricsCandidate c)
    {
        bool title = Normalize(track.Title) == Normalize(c.Track.Title) && !string.IsNullOrWhiteSpace(track.Title);
        bool artist = (Normalize(track.Artist) == Normalize(c.Track.Artist) ||
            string.Join("|",ArtistSeparator().Split(track.Artist).Select(Normalize).Where(x=>x.Length>0).Order())==string.Join("|",ArtistSeparator().Split(c.Track.Artist).Select(Normalize).Where(x=>x.Length>0).Order())) && !string.IsNullOrWhiteSpace(track.Artist);
        bool version = Versions(track.Title+" "+track.Album) != Versions(c.Track.Title+" "+c.Track.Album);
        double? delta = track.DurationMs.HasValue && c.Track.DurationMs.HasValue ? Math.Abs(track.DurationMs.Value - c.Track.DurationMs.Value) : null;
        double score = (title ? .4 : 0) + (artist ? .3 : 0) + (delta <= 2000 ? .2 : delta <= 5000 ? .1 : 0) + (Normalize(track.Album) == Normalize(c.Track.Album) && track.Album.Length > 0 ? .1 : 0);
        bool eligible = title && artist && !version && delta <= 5000;
        return new(c, score, eligible, version ? "录音版本不同" : delta is null ? "时长未知，请确认" : delta > 5000 ? "时长差超过 5 秒" : $"时长差 {delta / 1000:0.0} 秒");
    }
    public static LyricsCandidate? AutoSelect(Track track, IReadOnlyList<LyricsCandidate> candidates)
    {
        var ranked = candidates.Select(c => Score(track, c)).OrderByDescending(x => x.Score).ToArray();
        return ranked.Length > 0 && ranked[0].Eligible && ranked[0].Score >= .9 && (ranked.Length == 1 || ranked[0].Score - ranked[1].Score >= .08) ? ranked[0].Candidate : null;
    }
}
