using LumaLyrics.Core;
namespace LumaLyrics.Core.Tests;
public sealed class ChineseScriptVisibilityTests
{
    [Theory]
    [InlineData("[00:00]風中的音樂",false,true)]
    [InlineData("[00:00]Chinese dreams\n[00:12]让光照亮远方",false,true)]
    [InlineData("[00:00]A quiet night",false,false)]
    [InlineData("[00:00]风の歌を聞く\n[00:12]音樂",false,false)]
    [InlineData("[00:00]바람의 노래\n[00:12]音樂",false,false)]
    [InlineData("[00:00]作词：中文名字\n[00:12]A quiet night",false,false)]
    [InlineData("[00:00]A quiet night",true,true)]
    [InlineData("[00:00]風中的音樂",true,true)]
    public void VisibilityUsesLyricLanguageOrMainTranslationSwitch(string raw,bool main,bool visible)=>Assert.Equal(visible,ChineseScriptVisibility.ShouldShow(new LrcParser().Parse(raw),main));
    [Fact] public void EmptyPlaybackRemainsHiddenUnlessMainTranslationIsEnabled(){Assert.False(ChineseScriptVisibility.ShouldShow(null,false));Assert.True(ChineseScriptVisibility.ShouldShow(null,true));}
    [Theory]
    [InlineData("風中的音樂",true)]
    [InlineData("A quiet night",false)]
    [InlineData("[ar:中文歌手]\n作词：中文名字\nA quiet night",false)]
    [InlineData("風の歌を聞く\n音樂",false)]
    public void StaticLyricsFollowTheSameVisibilityRule(string raw,bool expected)=>Assert.Equal(expected,ChineseScriptVisibility.ShouldShow(new LrcParser().Parse(raw,true),false));
    [Fact] public void CreditsDoNotHideRealLyricsAtTheSameTimestamp()=>Assert.True(ChineseScriptVisibility.ShouldShow(new LrcParser().Parse("[00:00]作词：名字\n[00:00]让光照亮远方"),false));
}
