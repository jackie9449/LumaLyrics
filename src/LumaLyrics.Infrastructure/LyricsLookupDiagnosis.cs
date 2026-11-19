using LumaLyrics.Core;
namespace LumaLyrics.Infrastructure;

public static class LyricsLookupDiagnosis
{
    public static bool Unavailable(IReadOnlyList<LyricsCandidate> candidates)=>candidates is LyricsLookupResult{UnavailableSources.Count:>0};
    public static string Describe(Track track,IReadOnlyList<LyricsCandidate> candidates)
    {
        string reason=candidates.Count>0?AutomaticLyricsMatcher.RejectionReason(track,candidates)
            :Unavailable(candidates)?"来源暂不可用，尚未完成查询"
            :(candidates as LyricsLookupResult)?.CatalogStatus switch
            {
                LyricsCatalogStatus.Unconfirmed=>"来源返回了曲目，但未能确认相同录音",
                LyricsCatalogStatus.MatchedWithoutLyrics=>"已找到对应曲目，但来源未提供原词",
                _=>AutomaticLyricsMatcher.RejectionReason(track,candidates)
            };
        if(candidates is LyricsLookupResult{UnavailableSources.Count:>0} partial)reason+="\n部分来源暂不可用："+string.Join("、",partial.UnavailableSources);
        return reason;
    }
}
