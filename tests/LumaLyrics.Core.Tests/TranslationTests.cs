using LumaLyrics.Core;
namespace LumaLyrics.Core.Tests;

public class TranslationTests
{
    const string Original="[00:01]We sail home\n[00:03]Across the quiet bay\n[00:05]The stars will guide us";
    const string Chinese="[00:01]我们驶向家乡\n[00:03]穿过宁静海湾\n[00:05]星光为我们引路";
    static LyricsDocument Parse(string text)=>new LrcParser().Parse(text);
    static ChineseTranslation? Align(string local=Original,string source=Original,string translated=Chinese)=>TranslationAlignment.Align(Parse(local),Parse(source),Parse(translated),"Original test translation","https://example.org/own-fixture");
    [Fact] public void AlignsByOriginalContentInsteadOfBorrowingAnotherTimeline()
    {
        var local="[00:10]We sail home\n[00:20]\n[00:30]Across the quiet bay\n[00:50]The stars will guide us";
        var result=Align(local)!;Assert.NotNull(result);Assert.Equal(TranslationAlignment.Hash(local),result.SourceHash);
        Assert.Equal(new[]{0,2,3},result.Lines.Select(l=>l.GroupIndex));Assert.Equal("穿过宁静海湾",result.TextAt(2));Assert.Equal("",result.TextAt(1));
    }
    [Theory][InlineData("[00:01]We sail away\n[00:03]Across the quiet bay\n[00:05]The stars will guide us")]
    [InlineData("[00:01]We sail home\n[00:03]The stars will guide us\n[00:05]Across the quiet bay")]
    public void RejectsDifferentWordsOrOrderEvenWhenTimestampsMatch(string local)=>Assert.Null(Align(local));
    [Fact] public void RejectsIncompleteAndUntimedTranslations(){Assert.Null(Align(translated:"[00:01]我们驶向家乡\n[00:03]穿过宁静海湾"));Assert.Null(Align(translated:"我们驶向家乡\n穿过宁静海湾\n星光为我们引路"));}
    [Fact] public void RejectsAmbiguousTimestampNeighbors()=>Assert.Null(Align(source:"[00:01]We sail home\n[00:01.20]Across the quiet bay\n[00:05]The stars will guide us",translated:"[00:01.10]我们驶向家乡\n[00:01.20]穿过宁静海湾\n[00:05]星光为我们引路"));
    [Fact] public void AppliesEachFileOffsetBeforePairing()=>Assert.NotNull(Align(translated:"[offset:1000]\n[00:02]我们驶向家乡\n[00:04]穿过宁静海湾\n[00:06]星光为我们引路"));
    [Fact] public void KeepsRepeatedRefrainsInOrder()
    {
        var r=Align("[00:01]We sail home\n[00:02]We sail home\n[00:03]Across the quiet bay","[00:11]We sail home\n[00:12]We sail home\n[00:13]Across the quiet bay","[00:11]我们驶向家乡\n[00:12]我们驶向家乡\n[00:13]穿过宁静海湾");Assert.Equal(3,r!.Lines.Count);
    }
    [Fact] public void SkipsExplicitCreditsButNeverTranslatesThem()=>Assert.NotNull(Align(source:"[00:00]作词 : Test\n"+Original,translated:"[00:00]翻译 : Test\n"+Chinese));
    [Fact] public void RejectsInstrumentalPlaceholderAndOriginalLanguageCopy(){Assert.Null(Align(translated:Original));Assert.Null(Align(translated:"[00:01]暂无翻译\n[00:03]暂无翻译\n[00:05]暂无翻译"));Assert.Null(TranslationAlignment.Align(Parse(Original) with{Instrumental=true},Parse(Original),Parse(Chinese),"test","https://example.org"));}
    [Theory][InlineData("山川与海洋\n此刻星光明亮\n我们继续远行",true)][InlineData("君の声が聞こえる\n夜空を見上げる",false)][InlineData("사랑해 영원히",false)][InlineData("We sail home across the quiet bay",false)][InlineData("",false)]
    public void ClassifiesFromLyricScriptConservatively(string text,bool chinese)=>Assert.Equal(chinese,TranslationAlignment.IsChinese(text));
    [Fact] public void SourceHashDetectsEvenAnOffsetEdit()=>Assert.NotEqual(TranslationAlignment.Hash(Original),TranslationAlignment.Hash("[offset:100]\n"+Original));
    [Fact] public void MixedChineseLyricsDoNotGetDuplicateChineseTranslationRows()
    {
        var mixed=Parse("[00:01]我们驶向家乡\n[00:03]We sail home\n[00:05]星光会指引我们");Assert.False(TranslationAlignment.NeedsChineseTranslation(mixed,"我们驶向家乡"));Assert.True(TranslationAlignment.NeedsChineseTranslation(mixed,"We sail home"));
        var japanese=Parse("[00:01]君の声\n[00:03]世界");Assert.True(TranslationAlignment.NeedsChineseTranslation(japanese,"世界"));
    }
    [Fact] public void InstrumentalRecordingCannotAutomaticallyAcquireVocalWords()=>Assert.False(AutomaticLyricsMatcher.Assess(new(42,new("Own song (Instrumental)","Own artist","Own album",10000),Original,null,false)).Accepted);
}
