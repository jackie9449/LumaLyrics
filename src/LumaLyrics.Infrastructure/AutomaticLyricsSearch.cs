using LumaLyrics.Core;

namespace LumaLyrics.Infrastructure;

public interface IAutomaticLyricsSearch
{
    Task<IReadOnlyList<LyricsCandidate>> SearchAutomaticAsync(Track track,IReadOnlyList<LyricsCandidate> known,CancellationToken token);
}

public static class AutomaticLyricsSearch
{
    public static bool CanExpand(Track input)
    {
        var track=TrackMetadata.FromAppleSession(input);
        return !string.IsNullOrWhiteSpace(track.Title) && !string.IsNullOrWhiteSpace(track.Artist)
            && track.DurationMs is >0 && double.IsFinite(track.DurationMs.Value);
    }
    // The ordinary lookup already searched the original fields. Supplementary
    // query cleanup improves discovery only; acceptance still uses the real track.
    public static IReadOnlyList<ManualLyricsQuery> Queries(Track track)=>CanExpand(track)?ManualLyricsSearch.Queries(track).Skip(1).Take(2).ToArray():[];
}
