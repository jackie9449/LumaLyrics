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
public sealed record AutomaticLyricsChoice(LyricsCandidate Candidate, LyricsAssessment Assessment, double MatchScore)
{
    internal bool UsesJapaneseReading {get;init;}
}

public sealed record AutomaticLyricsDecision(bool Accepted,double MatchScore,LyricsAssessment? Assessment,string Reason)
{
    internal bool UsesJapaneseReading {get;init;}
}

public static partial class AutomaticLyricsMatcher
{
    public const int PolicyVersion=6;
    [GeneratedRegex(@"(?im)^\s*\[(?:verse|chorus|bridge|intro|outro|instrumental|refrain)[^\]]*\]\s*$")]private static partial Regex Sections();
    [GeneratedRegex(@"(?i)<html|<!doctype|暂无歌词|暫無歌詞|仅供试听|僅供試聽|lyrics not available|lyrics unavailable|sample lyrics|preview only")]private static partial Regex Placeholder();
    [GeneratedRegex(@"(?i)\binstrumental\b|伴奏")]private static partial Regex InstrumentalTitle();
    [GeneratedRegex(@"(?i)^\s*(?:instrumental(?: only)?|no lyrics|纯音乐(?:[，,]\s*请欣赏)?|純音樂(?:[，,]\s*請欣賞)?|无歌词|無歌詞)[。.!！]?\s*$")]private static partial Regex StaticNotice();
    static string Canonical(string value)=>MatchRules.Normalize(Sections().Replace(value,""));
    public static LyricsAssessment Assess(LyricsCandidate candidate)
    {
        if(candidate.Instrumental)return string.IsNullOrWhiteSpace(candidate.SyncedLyrics) && string.IsNullOrWhiteSpace(candidate.PlainLyrics)
            ?new(true,false,1,"匹配来源明确标记为纯音乐")
            :new(false,false,0,"纯音乐标记与歌词内容矛盾，需确认");
        if(InstrumentalTitle().IsMatch(candidate.Track.Title))return new(false,false,0,"伴奏录音标记与人声原词矛盾，需确认");
        if(string.IsNullOrWhiteSpace(candidate.Content)||Placeholder().IsMatch(candidate.Content))return new(false,false,0,"空内容或占位歌词");
        LyricsDocument document;
        try{document=new LrcParser().Parse(candidate.Content);}catch(InvalidDataException){return new(false,false,0,"歌词大小异常");}
        var plain=candidate.PlainLyrics??"";
        if(!document.IsSynced)
        {
            return new(false,false,0,document.Warnings.Count>0?"歌词时间标签无效，需手动确认":"只有静态文字，未提供可靠时间戳");
        }
        var vocal=document.Groups.Where(g=>Canonical(g.Text).Length>0).ToArray();
        if(vocal.Length<3||document.Warnings.Count>Math.Max(2,document.Groups.Count/10))return new(false,true,0,"有效时间点不足或格式损坏");
        var duration=candidate.Track.DurationMs;
        if(duration.HasValue && vocal.Any(x=>x.TimeMs+document.FileOffsetMs>duration+5000 || x.TimeMs+document.FileOffsetMs< -1000))return new(false,true,0,"时间戳超出歌曲时长");
        double last=vocal[^1].TimeMs+document.FileOffsetMs;
        double? coverage=null;
        if(!string.IsNullOrWhiteSpace(plain))
        {
            var expected=Canonical(plain);var actual=Canonical(string.Join("\n",vocal.Select(g=>g.Text)));
            // Multiset of short character sequences also works for unspaced Chinese/Japanese.
            static Dictionary<string,int> Grams(string s){var d=new Dictionary<string,int>();for(int i=0;i+2<s.Length;i++){var g=s.Substring(i,3);d[g]=d.GetValueOrDefault(g)+1;}return d;}
            var a=Grams(actual);var b=Grams(expected);coverage=b.Count==0?1:b.Sum(x=>Math.Min(x.Value,a.GetValueOrDefault(x.Key)))/(double)b.Sum(x=>x.Value);
            if(coverage<.90)return new(false,true,coverage.Value,"同步歌词缺少纯文本中的较多内容",coverage);
        }
        if(duration>=120000 && last<duration*.55)
        {
            if(string.IsNullOrWhiteSpace(plain))return new(false,true,0,"尾段较长且来源缺少全文对照，需手动确认");
            string actual=Canonical(string.Join("\n",vocal.Select(g=>g.Text))),expected=Canonical(plain);
            if(actual!=expected)return new(false,true,coverage??0,"尾段较长，同步正文与来源全文顺序或内容不一致",coverage);
            if(vocal.Length<6 || actual.Length<80 || vocal[^1].TimeMs-vocal[0].TimeMs<60000 || vocal.Select(g=>Canonical(g.Text)).Distinct().Count()<3)
                return new(false,true,coverage??0,"歌词时间点过于集中或有效正文不足，需手动确认",coverage);
            return new(true,true,1,"长尾段：来源全文顺序与同步正文一致，时间范围检查通过",coverage);
        }
        return new(true,true,.8+(coverage??.5)*.2,coverage.HasValue?"时间戳范围及同步/纯文本覆盖检查通过":"时间戳范围及文本结构检查通过；来源无全文对照",coverage);
    }
    static bool HasVocalEvidence(Track track,IReadOnlyList<LyricsCandidate> candidates)
        =>candidates.Any(c=>!c.Instrumental && !string.IsNullOrWhiteSpace(c.Content) && !Placeholder().IsMatch(c.Content)
            && MatchRules.Score(track,c).Eligible && (Canonical(c.Content).Length>10 || StaticText(c) is not null));
    public static AutomaticLyricsDecision Decide(Track input,LyricsCandidate candidate,IReadOnlyList<LyricsCandidate>? context=null)
    {
        var track=TrackMetadata.FromAppleSession(input);return DecideCore(track,candidate,HasVocalEvidence(track,context??[candidate]));
    }
    static AutomaticLyricsDecision DecideCore(Track track,LyricsCandidate candidate,bool vocalEvidence)
    {
        var score=MatchRules.Score(track,candidate);
        if(RecordingRejection(track,score) is {} rejection)return new(false,score.Score,null,rejection);
        if(candidate.Instrumental&&vocalEvidence)return new(false,score.Score,null,"纯音乐标记与同录音的人声歌词冲突，需手动确认");
        var quality=Assess(candidate);return new(quality.Accepted,score.Score,quality,quality.Reason){UsesJapaneseReading=score.UsesJapaneseReading};
    }
    static string? RecordingRejection(Track track,ScoredCandidate score)
    {
        if(!score.Eligible)return score.Reason;
        if(score.Score+1e-9<.80)return "歌曲信息匹配度不足，需手动确认";
        var candidate=score.Candidate.Track;
        if(track.Album.Length>0 && candidate.Album.Length>0 && !MatchRules.AlbumMatches(track.Album,candidate.Album)
            && Math.Abs((track.DurationMs??0)-(candidate.DurationMs??0))>2000)return "专辑不同且时长差超过 2 秒，需手动确认";
        return null;
    }
    public static string RejectionReason(Track input,IReadOnlyList<LyricsCandidate> candidates)
    {
        if(candidates.Count==0)return "来源未返回歌词候选";
        var track=TrackMetadata.FromAppleSession(input);bool vocals=HasVocalEvidence(track,candidates);
        return candidates.Select(c=>DecideCore(track,c,vocals)).OrderByDescending(d=>d.MatchScore).First().Reason;
    }
    public static AutomaticLyricsChoice? Select(Track input,IReadOnlyList<LyricsCandidate> candidates)
    {
        var track=TrackMetadata.FromAppleSession(input);bool vocals=HasVocalEvidence(track,candidates);var ranked=new List<AutomaticLyricsChoice>();
        foreach(var candidate in candidates){var decision=DecideCore(track,candidate,vocals);if(decision.Accepted)ranked.Add(new(candidate,decision.Assessment!,decision.MatchScore){UsesJapaneseReading=decision.UsesJapaneseReading});}
        return ranked.OrderByDescending(x=>x.MatchScore).ThenByDescending(x=>x.Assessment.Synced)
            .ThenBy(x=>x.UsesJapaneseReading).ThenByDescending(x=>x.Assessment.Quality).ThenBy(x=>Math.Abs((x.Candidate.Track.DurationMs??0)-(track.DurationMs??0)))
            .ThenBy(x=>x.Candidate.Id).FirstOrDefault();
    }

    // Called after the timed search chain. Evaluate the source text without modifying its lyrics.
    public static LyricsCandidate? SelectStatic(Track input,IReadOnlyList<LyricsCandidate> candidates)
    {
        var track=TrackMetadata.FromAppleSession(input);
        foreach(var score in candidates.Where(c=>!c.Instrumental && !InstrumentalTitle().IsMatch(c.Track.Title))
            .Select(c=>MatchRules.Score(track,c)).Where(score=>RecordingRejection(track,score) is null)
            .OrderByDescending(score=>score.Score).ThenBy(score=>score.UsesJapaneseReading)
            .ThenBy(score=>Math.Abs(score.Candidate.Track.DurationMs!.Value-track.DurationMs!.Value)).ThenBy(score=>score.Candidate.Id))
        {
            var candidate=score.Candidate;
            if(StaticText(candidate) is {} raw)return candidate with{PlainLyrics=raw,SyncedLyrics=null};
        }
        return null;
    }
    static string? StaticText(LyricsCandidate candidate)
    {
        var raw=string.IsNullOrWhiteSpace(candidate.PlainLyrics)?candidate.SyncedLyrics:candidate.PlainLyrics;
        if(string.IsNullOrWhiteSpace(raw) || raw.Length>2*1024*1024 || Placeholder().IsMatch(raw))return null;
        var body=Sections().Replace(raw,"").TrimStart('\uFEFF');
        if(StaticNotice().IsMatch(body) || !body.Split('\n').Any(line=>!line.TrimStart().StartsWith('[') && line.Any(char.IsLetter)))return null;
        var document=new LrcParser().Parse(body);
        return document.IsSynced || document.Warnings.Count>0 || raw.Contains("[offset:",StringComparison.OrdinalIgnoreCase)?null:raw;
    }
}
