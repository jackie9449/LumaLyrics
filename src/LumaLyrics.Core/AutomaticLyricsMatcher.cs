using System.Text.RegularExpressions;
namespace LumaLyrics.Core;

public static class TrackMetadata
{
    public static Track FromAppleSession(Track track)
    {
        if(!string.IsNullOrWhiteSpace(track.Album))return track;
        var parts=track.Artist.Split(" — ",StringSplitOptions.TrimEntries);
        return parts.Length==2 && parts.All(x=>x.Length>0)?track with{Artist=parts[0],Album=parts[1]}:track;
    }
}
public sealed record LyricsAssessment(bool Accepted, bool Synced, double Quality, string Reason, double? TextCoverage=null);
public sealed record AutomaticLyricsChoice(LyricsCandidate Candidate, LyricsAssessment Assessment, double MatchScore);

public static partial class AutomaticLyricsMatcher
{
    [GeneratedRegex(@"(?im)^\s*\[(?:verse|chorus|bridge|intro|outro|instrumental|refrain)[^\]]*\]\s*$")]private static partial Regex Sections();
    [GeneratedRegex(@"(?i)<html|<!doctype|暂无歌词|暫無歌詞|仅供试听|僅供試聽|lyrics not available|lyrics unavailable|sample lyrics|preview only")]private static partial Regex Placeholder();
    static string Canonical(string value)=>MatchRules.Normalize(Sections().Replace(value,""));
    public static LyricsAssessment Assess(LyricsCandidate candidate)
    {
        if(candidate.Instrumental)return new(true,false,1,"来源标记为纯音乐");
        if(string.IsNullOrWhiteSpace(candidate.Content)||Placeholder().IsMatch(candidate.Content))return new(false,false,0,"空内容或占位歌词");
        LyricsDocument document;
        try{document=new LrcParser().Parse(candidate.Content);}catch(InvalidDataException){return new(false,false,0,"歌词大小异常");}
        var plain=candidate.PlainLyrics??"";
        if(!document.IsSynced)
        {
            var text=Canonical(candidate.Content);var lineCount=candidate.Content.Split('\n').Count(x=>!string.IsNullOrWhiteSpace(x));
            bool substantial=text.Length>=60 && lineCount>=4;
            return new(substantial,false,substantial?.65:0,substantial?"静态全文通过结构检查，完整性无法单独证明":"静态内容过短，疑似片段");
        }
        var vocal=document.Groups.Where(g=>!string.IsNullOrWhiteSpace(g.Text)).ToArray();
        if(vocal.Length<3||document.Warnings.Count>Math.Max(2,document.Groups.Count/10))return new(false,true,0,"有效时间点不足或格式损坏");
        var duration=candidate.Track.DurationMs;
        if(duration.HasValue && vocal.Any(x=>x.TimeMs+document.FileOffsetMs>duration+5000))return new(false,true,0,"时间戳超出歌曲时长");
        double last=vocal[^1].TimeMs;
        if(duration>=120000 && last<duration*.55)return new(false,true,0,"时间戳只覆盖歌曲前半段，疑似截断");
        double? coverage=null;
        if(!string.IsNullOrWhiteSpace(plain))
        {
            var expected=Canonical(plain);var actual=Canonical(string.Join("\n",vocal.Select(g=>g.Text)));
            // Multiset of short character sequences also works for unspaced Chinese/Japanese.
            static Dictionary<string,int> Grams(string s){var d=new Dictionary<string,int>();for(int i=0;i+2<s.Length;i++){var g=s.Substring(i,3);d[g]=d.GetValueOrDefault(g)+1;}return d;}
            var a=Grams(actual);var b=Grams(expected);coverage=b.Count==0?1:b.Sum(x=>Math.Min(x.Value,a.GetValueOrDefault(x.Key)))/(double)b.Sum(x=>x.Value);
            if(coverage<.82)return new(false,true,coverage.Value,"同步歌词缺少纯文本中的较多内容",coverage);
        }
        return new(true,true,.8+(coverage??.5)*.2,coverage.HasValue?"时间戳范围及同步/纯文本覆盖检查通过":"时间戳范围及文本结构检查通过；来源无全文对照",coverage);
    }
    public static AutomaticLyricsChoice? Select(Track input,IReadOnlyList<LyricsCandidate> candidates)
    {
        var track=TrackMetadata.FromAppleSession(input);
        var ranked=new List<AutomaticLyricsChoice>();
        foreach(var candidate in candidates)
        {
            var score=MatchRules.Score(track,candidate);
            if(!score.Eligible || score.Score<.80)continue;
            var quality=Assess(candidate);if(!quality.Accepted)continue;
            ranked.Add(new(candidate,quality,score.Score));
        }
        // Duplicate catalog records are common. Rank acceptable versions by metadata,
        // then content checks; identical recordings no longer require a user decision.
        return ranked.OrderByDescending(x=>x.MatchScore).ThenByDescending(x=>x.Assessment.Synced)
            .ThenByDescending(x=>x.Assessment.Quality).ThenBy(x=>Math.Abs((x.Candidate.Track.DurationMs??0)-(track.DurationMs??0)))
            .ThenBy(x=>x.Candidate.Id).FirstOrDefault();
    }
}
