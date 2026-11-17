using LumaLyrics.Core;
namespace LumaLyrics.Infrastructure;

public static class LyricsLookupDiagnosis
{
    public static bool Unavailable(IReadOnlyList<LyricsCandidate> candidates)=>candidates is LyricsLookupResult{UnavailableSources.Count:>0};
    public static string Describe(Track track,IReadOnlyList<LyricsCandidate> candidates)
    {
        string reason=candidates.Count==0&&Unavailable(candidates)?"来源暂不可用，尚未完成查询":AutomaticLyricsMatcher.RejectionReason(track,candidates);
        if(candidates is LyricsLookupResult{UnavailableSources.Count:>0} partial)reason+="\n部分来源暂不可用："+string.Join("、",partial.UnavailableSources);
        return reason;
    }
}
