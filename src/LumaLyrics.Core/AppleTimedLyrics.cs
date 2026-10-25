using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace LumaLyrics.Core;

public sealed record AppleLyricInterval(double BeginMs,double EndMs,string Text);
public sealed record AppleTimedLyrics(string Ttml,string Lrc,IReadOnlyList<AppleLyricInterval> Lines)
{
    public string TimingModel {get;init;}="ttml-par";
}

// Independently implemented TTML subset. Unsupported timing is rejected, never guessed.
public static class AppleTimedLyricsParser
{
    public const int MaximumCharacters=2*1024*1024;
    static readonly XNamespace Tt="http://www.w3.org/ns/ttml";
    static readonly XNamespace Itunes="http://music.apple.com/lyric-ttml-internal";
    public static AppleTimedLyrics Parse(string raw,double durationMs)
    {
        if(raw.Length>MaximumCharacters || !double.IsFinite(durationMs) || durationMs<=0)throw new InvalidDataException("原词大小或录音时长无效。");
        using var reader=XmlReader.Create(new StringReader(raw),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=MaximumCharacters});
        while(reader.Read())if(reader.Depth>48)throw new InvalidDataException("TTML 嵌套过深。");
        using var validated=XmlReader.Create(new StringReader(raw),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=MaximumCharacters});
        var xml=XDocument.Load(validated,LoadOptions.PreserveWhitespace);var root=xml.Root;
        if(root?.Name!=Tt+"tt" || root.Attributes().Any(a=>a.Name.LocalName=="timeBase" && a.Value!="media"))throw new InvalidDataException("不支持的 TTML 时间基准。");
        var body=root.Element(Tt+"body")??throw new InvalidDataException("TTML 缺少正文。");
        var paragraphs=body.Descendants(Tt+"p").ToArray();
        if(paragraphs.Length is <3 or >2000)throw new InvalidDataException("原词段落数量异常。");
        foreach(var parent in body.DescendantsAndSelf().Where(e=>e.Descendants(Tt+"p").Any()))
            if(parent.Attribute("timeContainer")?.Value is { } container && container!="par")throw new InvalidDataException("不支持顺序时间容器。");
        // The observed Apple Line/Word dialect repeats document-clock values on
        // songPart containers and paragraphs. Accept that dialect only when its
        // explicit markers AND every section's exact outer boundaries agree.
        var sections=body.Descendants(Tt+"div").Where(e=>e.Attribute("begin") is not null || e.Attribute("end") is not null).ToArray();
        bool appleAbsolute=root.Attribute(Itunes+"timing")?.Value is "Line" or "Word"
            && paragraphs.All(p=>p.Attribute(Itunes+"key") is not null && p.Attribute("begin") is not null && p.Attribute("end") is not null)
            && sections.Length>0 && sections.All(section=>section.Attribute(Itunes+"songPart") is not null && section.Attribute("begin") is { } begin && section.Attribute("end") is { } end
                && section.Descendants(Tt+"p").Any() && Time(begin.Value)==section.Descendants(Tt+"p").Min(p=>Time(p.Attribute("begin")!.Value))
                && Time(end.Value)==section.Descendants(Tt+"p").Max(p=>Time(p.Attribute("end")!.Value)))
            && (body.Attribute("begin") is null || Time(body.Attribute("begin")!.Value)==0);
        var lines=new List<AppleLyricInterval>();
        foreach(var p in paragraphs)
        {
            if(p.Ancestors(Tt+"p").Any())throw new InvalidDataException("不支持嵌套歌词段落。");
            double origin=0,limit=durationMs+1000;
            foreach(var parent in p.Ancestors().Reverse().Where(e=>e==body || e.Ancestors().Contains(body)))
            {
                double begin=(appleAbsolute?0:origin)+(parent.Attribute("begin") is { } offset?Time(offset.Value):0);
                if(parent.Attribute("end") is { } parentEnd)limit=Math.Min(limit,(appleAbsolute?0:origin)+Time(parentEnd.Value));
                if(parent.Attribute("dur") is { } parentDuration)limit=Math.Min(limit,begin+Time(parentDuration.Value));
                origin=appleAbsolute?0:begin;if(begin>=limit)throw new InvalidDataException("原词容器时间为空。");
            }
            double start=origin+Time(p.Attribute("begin")?.Value);
            double end=p.Attribute("end") is { } finish?origin+Time(finish.Value):p.Attribute("dur") is { } length?start+Time(length.Value):throw new InvalidDataException("原词缺少行结束时间。");
            if(start<0 || end<=start || end>durationMs+1000)throw new InvalidDataException("原词时间超出当前录音。");
            if(p.Attribute("dur") is { } dur && Math.Abs(start+Time(dur.Value)-end)>1)throw new InvalidDataException("原词结束时间冲突。");
            end=Math.Min(end,limit);if(start>=end)throw new InvalidDataException("原词超出容器时间。");
            string Text(XNode node)=>node switch
            {
                XText text=>text.Value,
                XElement element when element.Attributes().Any(a=>a.Name.LocalName=="role" && (a.Value.Contains("translation",StringComparison.OrdinalIgnoreCase) || a.Value.Contains("transliteration",StringComparison.OrdinalIgnoreCase)))=>"",
                XElement element when element.Name==Tt+"br"=>"\n",
                XElement element=>string.Concat(element.Nodes().Select(Text)),
                _=>""
            };
            string content=string.Concat(p.Nodes().Select(Text)).Replace("\r","");
            content=string.Join("\n",content.Split('\n').Select(s=>System.Text.RegularExpressions.Regex.Replace(s,@"[\t ]+"," ").Trim())).Trim();
            if(content.Length==0)continue;
            if(content.Length>8000 || content.Any(c=>char.IsControl(c) && c!='\n'))throw new InvalidDataException("原词文字异常。");
            lines.Add(new(start,end,content));
        }
        if(lines.Count<3)throw new InvalidDataException("原词有效段落不足。");
        // Sweep exact source boundaries: duets retain simultaneous lines and gaps become
        // explicit empty LRC groups. There is no invented four-second line duration.
        var output=new StringBuilder();
        foreach(double time in lines.SelectMany(l=>new[]{l.BeginMs,l.EndMs}).Distinct().Order())
        {
            var active=lines.Where(l=>l.BeginMs<=time && l.EndMs>time).Select(l=>l.Text).ToArray();
            long ms=(long)Math.Round(time,MidpointRounding.AwayFromZero);
            string stamp=string.Create(CultureInfo.InvariantCulture,$"[{ms/60000:00}:{ms/1000%60:00}.{ms%1000:000}]");
            foreach(string line in active.Length==0?[""]:active.SelectMany(s=>s.Split('\n')))output.Append(stamp).AppendLine(line);
        }
        return new(raw,output.ToString().Replace("\r\n","\n"),lines.OrderBy(l=>l.BeginMs).ToArray()){TimingModel=appleAbsolute?"apple-absolute-section":"ttml-par"};
    }
    static double Time(string? value)
    {
        if(string.IsNullOrWhiteSpace(value))throw new InvalidDataException("原词缺少时间。");
        double ms;
        if(value.EndsWith("ms",StringComparison.Ordinal))ms=Number(value[..^2]);
        else if(value.EndsWith('s'))ms=Number(value[..^1])*1000;
        else
        {
            var parts=value.Split(':');
            if(parts.Length is not (2 or 3))throw new InvalidDataException("不支持帧、节拍或相对时间。");
            double seconds=Number(parts[^1]),minutes=Number(parts[^2]),hours=parts.Length==3?Number(parts[0]):0;
            if(seconds>=60 || minutes>=60 || minutes%1!=0 || hours%1!=0)throw new InvalidDataException("原词时钟格式无效。");
            ms=(hours*3600+minutes*60+seconds)*1000;
        }
        if(!double.IsFinite(ms) || ms<0 || ms>86400000 || Math.Abs(ms-Math.Round(ms))>.00001)throw new InvalidDataException("原词时间精度或范围不支持。");
        return Math.Round(ms);
    }
    static double Number(string value)=>double.TryParse(value,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out double n)?n:throw new InvalidDataException("原词时间格式无效。");
}

public static class AppleOriginalLyricsMatch
{
    // Accessibility supplies identity and ordered text, never timestamps. Common repeated
    // lines are excluded as evidence; competing timelines cannot be chosen by score.
    public static AppleTimedLyrics? Select(IEnumerable<AppleTimedLyrics> candidates,IReadOnlyList<string> nativeLines)
    {
        string[] anchors=nativeLines.Select(MatchRules.Normalize).Where(s=>s.Length>=8).ToArray();
        anchors=anchors.Where(a=>anchors.Count(b=>b==a)==1).ToArray();
        if(anchors.Length<4 || anchors.Sum(a=>a.Length)<60)return null;
        var accepted=new List<AppleTimedLyrics>();
        foreach(var candidate in candidates)
        {
            string[] lines=candidate.Lines.Select(l=>MatchRules.Normalize(l.Text)).ToArray();int previous=-1,matched=0;
            foreach(string anchor in anchors)
            {
                int index=Array.IndexOf(lines,anchor);
                if(index<0)continue;
                if(index<=previous || Array.LastIndexOf(lines,anchor)!=index){matched=-1;break;}
                previous=index;matched++;
            }
            if(matched>=4 && matched>=Math.Ceiling(anchors.Length*.8))accepted.Add(candidate);
        }
        var distinct=accepted.DistinctBy(c=>c.Lrc).Take(2).ToArray();
        return distinct.Length==1?distinct[0]:null;
    }
}
