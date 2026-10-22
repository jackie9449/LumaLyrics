using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
namespace LumaLyrics.Core;

public sealed record TranslationLine(int GroupIndex,string Text);
public sealed record ChineseTranslation(string SourceHash,IReadOnlyList<TranslationLine> Lines,string Source,string SourceUrl,string Review,DateTimeOffset PreparedUtc,bool Draft=false)
{
    public string TextAt(int groupIndex)=>Lines.FirstOrDefault(l=>l.GroupIndex==groupIndex)?.Text??"";
}

public static partial class TranslationAlignment
{
    [GeneratedRegex(@"(?i)^\s*(?:(?:作词|作詞|作曲|编曲|編曲|翻译|译者|翻譯|翻訳|詞|曲|词|制作人|制作|製作|监制|混音|母带|录音|吉他|贝斯|鼓|和声|歌手|专辑|演唱|작사|작곡|편곡|vocal|composer|lyricist|lyrics?(?:\s+by)?|written\s+by|produced\s+by|translated\s+by|translation)\s*[:：]|\[(?:verse|chorus|bridge|intro|outro|refrain)[^\]]*\]\s*$)")]
    private static partial Regex Credits();
    [GeneratedRegex(@"(?i)暂无|暫無|没有翻译|無翻譯|纯音乐|純音樂|lyrics?\s+(?:unavailable|not available)|translation\s+(?:unavailable|not available)|<html|<!doctype")]
    private static partial Regex Placeholder();
    public static string Hash(string raw)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    public static bool ContainsHan(string text)=>text.Any(c=>c is >= '\u3400' and <= '\u9fff');
    public static bool IsChinese(string text)
    {
        int han=0,latin=0;
        foreach(char c in text.Normalize(NormalizationForm.FormKC))
        {
            if(c is >= '\u3040' and <= '\u30ff' or >= '\uac00' and <= '\ud7af')return false;
            if(c is >= '\u3400' and <= '\u9fff')han++;
            else if(char.IsLetter(c))latin++;
        }
        return han>=5 && han>latin*2;
    }
    public static bool IsLyricText(string text)=>!string.IsNullOrWhiteSpace(text) && !Credits().IsMatch(text.Trim()) && text.Any(char.IsLetter);
    public static bool NeedsChineseTranslation(LyricsDocument document,string line)
    {
        if(!IsLyricText(line))return false;
        // Han-only Japanese lines still need translation when the original contains kana.
        if(document.Groups.Any(g=>g.Text.Any(c=>c is >= '\u3040' and <= '\u30ff' or >= '\uac00' and <= '\ud7af')))return true;
        int han=line.Count(c=>c is >= '\u3400' and <= '\u9fff');int other=line.Count(c=>char.IsLetter(c) && c is not (>= '\u3400' and <= '\u9fff'));
        return !(han>=2 && han>other*2);
    }
    public static ChineseTranslation? Align(LyricsDocument local,LyricsDocument original,LyricsDocument translated,string source,string url)
    {
        if(local.Instrumental || !local.IsSynced || !original.IsSynced || !translated.IsSynced || Placeholder().IsMatch(translated.Raw))return null;
        var a=local.Groups.Select((g,i)=>(Group:g,Index:i)).Where(x=>IsLyricText(x.Group.Text)).ToArray();
        var b=original.Groups.Where(g=>IsLyricText(g.Text)).ToArray();
        var t=translated.Groups.Where(g=>IsLyricText(g.Text)).ToArray();
        if(a.Length<3 || a.Length!=b.Length || t.Length!=b.Length || !ContainsHan(string.Join("\n",t.Select(g=>g.Text))))return null;
        var lines=new List<TranslationLine>();var used=new HashSet<int>();
        for(int i=0;i<a.Length;i++)
        {
            if(MatchRules.Normalize(a[i].Group.Text)!=MatchRules.Normalize(b[i].Text))return null;
            // The provider pair must agree on time before its translation can be mapped
            // onto the local original. Never borrow the other recording's timestamps.
            double time=b[i].TimeMs+original.FileOffsetMs;
            var matches=t.Select((g,j)=>(g,j)).Where(x=>Math.Abs(x.g.TimeMs+translated.FileOffsetMs-time)<=250).ToArray();
            if(matches.Length!=1 || !used.Add(matches[0].j))return null;
            var text=matches[0].g.Text.Trim();
            if(text.Length>4000 || !ContainsHan(text) && MatchRules.Normalize(text)!=MatchRules.Normalize(b[i].Text))return null;
            lines.Add(new(a[i].Index,text));
        }
        return new(Hash(local.Raw),lines,source,url,"社区译文 · 原文及逐行对应已检查 · 语义未人工逐首校对",DateTimeOffset.UtcNow);
    }
}
