using System.Text;
using LumaLyrics.Core;

namespace LumaLyrics.Core.Tests;

public class AppleTimedLyricsTests
{
    static readonly string[] Lines=["Morning light is resting on my window", "A quiet road is waiting for our footsteps", "We carry every little spark together", "And leave a lantern shining by the ocean"];
    static string Ttml(string content)=>"<tt xmlns=\"http://www.w3.org/ns/ttml\"><body><div>"+content+"</div></body></tt>";
    static string Paragraphs(int shift=0)=>string.Concat(Lines.Select((line,i)=>$"<p begin=\"{i*30+shift}s\" end=\"{i*30+shift+10}s\">{line}</p>"));
    static AppleTimedLyrics Original(int shift=0)=>AppleTimedLyricsParser.Parse(Ttml(Paragraphs(shift)),140000);
    [Fact] public void PreservesRealLineEndsBlankIntervalsAndMilliseconds()
    {
        var parsed=AppleTimedLyricsParser.Parse(Ttml(Paragraphs().Replace("begin=\"0s\"","begin=\"00:00:01.125\"")),140000);
        var document=new LrcParser().Parse(parsed.Lrc);
        Assert.Equal(1125,document.Groups[0].TimeMs);Assert.Equal(Lines[0],document.Groups[0].Text);
        Assert.Equal("",document.Groups[document.FindIndex(15000)].Text);Assert.Equal(Lines[1],document.Groups[document.FindIndex(31000)].Text);
        Assert.Equal("",document.Groups[document.FindIndex(105000)].Text);Assert.Equal(0,document.FileOffsetMs);
    }
    [Fact] public void KeepsOverlappingVoicesAndDoesNotBorrowAnEnd()
    {
        var parsed=AppleTimedLyricsParser.Parse(Ttml(Paragraphs()+"<p begin=\"5s\" dur=\"10s\">Another voice answers softly</p>"),140000);
        var d=new LrcParser().Parse(parsed.Lrc);Assert.Equal(2,d.Groups[d.FindIndex(6000)].Lines.Length);
        Assert.Equal("Another voice answers softly",d.Groups[d.FindIndex(11000)].Text);Assert.Equal("",d.Groups[d.FindIndex(16000)].Text);
    }
    [Fact] public void PreservesWordSpacingAndOriginalLineBreaksExcludesTranslation()
    {
        string content=Paragraphs().Replace(Lines[0],"<span>Morning </span><span>light</span><br/>is resting <span xmlns:ttm=\"http://www.w3.org/ns/ttml#metadata\" ttm:role=\"x-translation\">译文</span>on my window");
        var parsed=AppleTimedLyricsParser.Parse(Ttml(content),140000);Assert.Equal("Morning light\nis resting on my window",parsed.Lines[0].Text);
    }
    [Theory]
    [InlineData("begin=\"0s\" end=\"10s\"","begin=\"0s\"")]
    [InlineData("begin=\"0s\"","begin=\"00:00:00:12\"")]
    [InlineData("begin=\"0s\"","begin=\"1t\"")]
    [InlineData("begin=\"0s\"","begin=\"-1s\"")]
    [InlineData("end=\"10s\"","end=\"0s\"")]
    [InlineData("end=\"10s\"","end=\"1000s\"")]
    [InlineData("end=\"10s\"","end=\"10s\" dur=\"12s\"")]
    [InlineData("begin=\"0s\"","begin=\"0.0001s\"")]
    public void RejectsMissingUnsupportedConflictingOrOutOfRangeTiming(string from,string to)=>Assert.Throws<InvalidDataException>(()=>AppleTimedLyricsParser.Parse(Ttml(Paragraphs().Replace(from,to)),140000));
    [Theory]
    [InlineData("<div>","<div timeContainer=\"seq\">")]
    [InlineData("<tt xmlns=", "<tt xmlns:ttp=\"http://www.w3.org/ns/ttml#parameter\" ttp:timeBase=\"smpte\" xmlns=")]
    public void RejectsUnsupportedContainerTimeAndTimeBase(string from,string to)=>Assert.Throws<InvalidDataException>(()=>AppleTimedLyricsParser.Parse(Ttml(Paragraphs()).Replace(from,to),140000));
    [Fact] public void ResolvesParallelParentOffsetsAndClipsToExplicitContainerEnd()
    {
        var parsed=AppleTimedLyricsParser.Parse(Ttml(Paragraphs()).Replace("<div>","<div begin=\"3s\" end=\"98s\">"),140000);
        Assert.Equal(3000,parsed.Lines[0].BeginMs);Assert.Equal(93000,parsed.Lines[^1].BeginMs);Assert.Equal(98000,parsed.Lines[^1].EndMs);
        var document=new LrcParser().Parse(parsed.Lrc);Assert.Equal("",document.Groups[document.FindIndex(99000)].Text);
    }
    [Fact] public void AppleMarkedSectionsUseDocumentClockOnlyWithExactBoundaryEvidence()
    {
        string xml="<tt xmlns=\"http://www.w3.org/ns/ttml\" xmlns:itunes=\"http://music.apple.com/lyric-ttml-internal\" itunes:timing=\"Line\"><body dur=\"140s\"><div begin=\"30s\" end=\"130s\" itunes:songPart=\"Verse\">"
            +string.Concat(Lines.Select((s,i)=>$"<p begin=\"{30+i*30}s\" end=\"{40+i*30}s\" itunes:key=\"L{i}\">{s}</p>"))+"</div></body></tt>";
        var parsed=AppleTimedLyricsParser.Parse(xml,140000);Assert.Equal("apple-absolute-section",parsed.TimingModel);
        Assert.Equal(30000,parsed.Lines[0].BeginMs);Assert.Equal(130000,parsed.Lines[^1].EndMs);
        Assert.Throws<InvalidDataException>(()=>AppleTimedLyricsParser.Parse(xml.Replace("itunes:timing=\"Line\"",""),140000));
        Assert.Throws<InvalidDataException>(()=>AppleTimedLyricsParser.Parse(xml.Replace("end=\"130s\" itunes:songPart","end=\"120s\" itunes:songPart"),140000));
    }
    [Fact] public void RejectsExternalEntitiesOversizeAndDeepDocuments()
    {
        Assert.Throws<System.Xml.XmlException>(()=>AppleTimedLyricsParser.Parse("<!DOCTYPE tt [<!ENTITY x SYSTEM 'file:///never-read'>]>"+Ttml(Paragraphs()),140000));
        Assert.Throws<InvalidDataException>(()=>AppleTimedLyricsParser.Parse(new string('x',AppleTimedLyricsParser.MaximumCharacters+1),140000));
        Assert.Throws<InvalidDataException>(()=>AppleTimedLyricsParser.Parse(Ttml(string.Concat(Enumerable.Repeat("<div>",60))+Paragraphs()+string.Concat(Enumerable.Repeat("</div>",60))),140000));
    }
    [Fact] public void RequiresDistinctiveOrderedEvidenceAndRejectsConflictingTimelines()
    {
        var a=Original();Assert.Same(a,AppleOriginalLyricsMatch.Select([a],Lines));
        Assert.Null(AppleOriginalLyricsMatch.Select([a],Lines.Reverse().ToArray()));
        Assert.Null(AppleOriginalLyricsMatch.Select([a],Lines.Take(3).ToArray()));
        Assert.Null(AppleOriginalLyricsMatch.Select([a],Enumerable.Repeat(Lines[0],5).ToArray()));
        Assert.Null(AppleOriginalLyricsMatch.Select([a,Original(2)],Lines));
        Assert.Same(a,AppleOriginalLyricsMatch.Select([a,Original()],Lines));
    }
    [Fact] public void RejectsRepeatedCandidateLinesAsRecordingEvidence()
    {
        var repeated=AppleTimedLyricsParser.Parse(Ttml(Paragraphs()+$"<p begin=\"110s\" end=\"120s\">{Lines[0]}</p>"),140000);
        Assert.Null(AppleOriginalLyricsMatch.Select([repeated],Lines));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void DecodesOnlyCompleteUtf8OrUtf16RootsWithoutSurroundingMemory(bool unicode)
    {
        var encoding=unicode?Encoding.Unicode:Encoding.UTF8;string xml=Ttml(Paragraphs());byte[] bytes=encoding.GetBytes("private-prefix\0"+xml+"\0private-suffix");
        var fragment=Assert.Single(TtmlMemoryFragments.Read(bytes,bytes.Length));Assert.Equal(xml,fragment.Xml);
        Assert.Equal(encoding.GetByteCount("private-prefix\0"),fragment.ByteOffset);
        Assert.Empty(TtmlMemoryFragments.Read(encoding.GetBytes(xml[..^5]),encoding.GetByteCount(xml[..^5])));
    }
    [Fact] public void RejectsLookalikeRootsAndAcceptsChunkOverlap()
    {
        var bytes=Encoding.UTF8.GetBytes("<ttwrong>not lyrics</tt>"+Ttml(Paragraphs()));
        Assert.Single(TtmlMemoryFragments.Read(bytes,bytes.Length));
        byte[] first=bytes[..70],second=bytes[70..];var joined=first.Concat(second).ToArray();Assert.Equal(Ttml(Paragraphs()),Assert.Single(TtmlMemoryFragments.Read(joined,joined.Length)).Xml);
    }
}
