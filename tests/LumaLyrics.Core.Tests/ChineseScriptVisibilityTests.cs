using LumaLyrics.Core;
namespace LumaLyrics.Core.Tests;
public sealed class ChineseScriptVisibilityTests
{
    [Theory]
    [InlineData("[00:00]風中的音樂",false,false,true)]
    [InlineData("[00:00]Chinese dreams\n[00:12]让光照亮远方",false,false,true)]
    [InlineData("[00:00]A quiet night",false,false,false)]
    [InlineData("[00:00]风の歌を聞く\n[00:12]音樂",false,false,false)]
    [InlineData("[00:00]바람의 노래\n[00:12]音樂",false,false,false)]
    [InlineData("[00:00]作词：中文名字\n[00:12]A quiet night",false,false,false)]
    [InlineData("[00:00]A quiet night",true,false,true)]
    [InlineData("[00:00]A quiet night",false,true,true)]
    public void VisibilityUsesLyricLanguageOrEitherTranslationSwitch(string raw,bool main,bool floating,bool visible)=>Assert.Equal(visible,ChineseScriptVisibility.ShouldShow(new LrcParser().Parse(raw),main,floating));
    [Fact] public void EmptyPlaybackRemainsHiddenUnlessTranslationsAreEnabled(){Assert.False(ChineseScriptVisibility.ShouldShow(null,false,false));Assert.True(ChineseScriptVisibility.ShouldShow(null,true,false));}
}
