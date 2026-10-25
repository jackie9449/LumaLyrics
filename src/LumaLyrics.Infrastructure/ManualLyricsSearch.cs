using System.Text;
using System.Text.RegularExpressions;
using LumaLyrics.Core;

namespace LumaLyrics.Infrastructure;

public interface IManualLyricsSearch
{
    Task<IReadOnlyList<LyricsCandidate>> SearchManualAsync(Track query,CancellationToken token);
}

public sealed record ManualLyricsQuery(string Title,string Artist,bool Keywords=false)
{
    public string Text=>string.Join(" ",new[]{Title,Artist}.Where(s=>s.Length>0));
}

// Broader suggestions are for explicit manual choice only, never automatic imports.
public static partial class ManualLyricsSearch
{
    [GeneratedRegex(@"\([^)]*\)|\[[^\]]*\]|【[^】]*】")]private static partial Regex Brackets();
    [GeneratedRegex(@"\s+(?:feat(?:uring)?|ft)\.?\s+.*$",RegexOptions.IgnoreCase)]private static partial Regex Featured();
    [GeneratedRegex(@"\s*(?:原声|原聲|原唱|官方音源)\s*$")]private static partial Regex ArtistLabel();
    [GeneratedRegex(@"\s*[,&;/、]\s*|\s+(?:feat(?:uring)?|ft)\.?\s+",RegexOptions.IgnoreCase)]private static partial Regex ArtistSeparator();
    [GeneratedRegex(@"\s+")]private static partial Regex Spaces();
    static string Text(string value)=>Spaces().Replace(value.Normalize(NormalizationForm.FormKC)," ").Trim();
    public static string CleanTitle(string title)
    {
        var original=Text(title);var cleaned=Featured().Replace(Brackets().Replace(original," "),"").Trim();
        return cleaned.Length>0?Text(cleaned):original;
    }
    public static string CleanArtist(string artist)=>ArtistLabel().Replace(ArtistSeparator().Split(Brackets().Replace(Text(artist)," "))[0],"").Trim();
    public static IReadOnlyList<ManualLyricsQuery> Queries(Track input)
    {
        var track=TrackMetadata.FromAppleSession(input);var title=Text(track.Title);var artist=Text(track.Artist);
        if(title.Length==0)return [];
        return new[]{new ManualLyricsQuery(title,artist),new(CleanTitle(title),CleanArtist(artist),true),new(CleanTitle(title),"")}
            .Distinct().ToArray();
    }
    static int TitleRank(Track query,Track candidate)
    {
        var title=MatchRules.Normalize(query.Title);var other=MatchRules.Normalize(candidate.Title);
        var clean=MatchRules.Normalize(CleanTitle(query.Title));var otherClean=MatchRules.Normalize(CleanTitle(candidate.Title));
        if(title.Length==0 || other.Length==0)return 0;
        if(title==other)return 4;
        if(clean==otherClean)return 4;
        if(clean.Length>1 && otherClean.Contains(clean,StringComparison.Ordinal))return 2;
        return otherClean.Length>1 && clean.Contains(otherClean,StringComparison.Ordinal)?1:0;
    }
    static int ArtistRank(Track query,Track candidate)
    {
        var artist=MatchRules.Normalize(CleanArtist(query.Artist));var other=MatchRules.Normalize(CleanArtist(candidate.Artist));
        return artist.Length==0?0:artist==other?2:other.Contains(artist,StringComparison.Ordinal)
            || MatchRules.Normalize(candidate.Title+" "+candidate.Album).Contains(artist,StringComparison.Ordinal)?1:0;
    }
    public static IEnumerable<LyricsCandidate> Rank(Track query,IEnumerable<LyricsCandidate> candidates)=>candidates
        .OrderByDescending(c=>TitleRank(query,c.Track)).ThenByDescending(c=>ArtistRank(query,c.Track))
        .ThenByDescending(c=>MatchRules.Score(query,c).Score).ThenByDescending(c=>!string.IsNullOrWhiteSpace(c.SyncedLyrics));
    public static IReadOnlyList<LyricsCandidate> Candidates(Track query,IEnumerable<LyricsCandidate> candidates)=>Rank(query,candidates
        .Where(c=>c.Id>0 && (c.Instrumental || !string.IsNullOrWhiteSpace(c.Content)))
        .GroupBy(c=>(c.ProviderName,c.Id)).Select(g=>g.OrderByDescending(c=>!string.IsNullOrWhiteSpace(c.SyncedLyrics)).First())).Take(100).ToArray();
    internal static string Unavailable(string source,Exception error)=>error is LyricsProviderException{StatusCode:{ } status}?$"{source}（{status}）":source;
}
