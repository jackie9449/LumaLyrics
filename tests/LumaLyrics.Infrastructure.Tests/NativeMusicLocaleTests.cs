using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.Infrastructure.Tests;

public class NativeMusicLocaleTests
{
    [Theory]
    [InlineData("历史记录","待播清单","无待播歌曲。","在资料库中显示专辑")]
    [InlineData("記錄","待播清單","沒有即將播放的歌曲。","在資料庫中顯示專輯")]
    [InlineData("History","Playing Next","No upcoming songs.","Show Album in Library")]
    [InlineData("履歴","次に再生","次に再生される曲はありません。","アルバムをライブラリで表示")]
    public void NativeQueueAndAlbumLabelsAcceptInstalledClientLanguages(string history,string next,string empty,string album)
    {
        Assert.True(NativeMusicLabels.IsHistory(history));Assert.True(NativeMusicLabels.IsPlayingNext(next));
        Assert.True(NativeMusicLabels.IsEmptyQueue(empty));Assert.True(NativeMusicLabels.IsShowAlbumInLibrary(album));
        Assert.False(NativeMusicLabels.IsHistory(history+" extra"));Assert.False(NativeMusicLabels.IsPlayingNext(next+" extra"));
        Assert.False(NativeMusicLabels.IsEmptyQueue(empty+" extra"));Assert.False(NativeMusicLabels.IsShowAlbumInLibrary(album+" extra"));
    }
    [Theory]
    [InlineData("不要重複",MusicRepeatMode.None)]
    [InlineData("全部重複",MusicRepeatMode.List)]
    [InlineData("重複單首歌曲",MusicRepeatMode.One)]
    [InlineData("リピートしない",MusicRepeatMode.None)]
    [InlineData("全項目リピート",MusicRepeatMode.List)]
    [InlineData("1項目リピートする",MusicRepeatMode.One)]
    [InlineData("Do Not Repeat",MusicRepeatMode.None)]
    public void RepeatUsesExplicitInstalledStateNames(string name,MusicRepeatMode expected)=>Assert.Equal(expected,MusicPlaybackModeRules.ParseRepeat(name));

    [Theory]
    [InlineData("重複")][InlineData("リピート")][InlineData("Repeat")][InlineData("全項目リピート unknown")]
    public void AmbiguousRepeatLabelsRemainUnknown(string name)=>Assert.Null(MusicPlaybackModeRules.ParseRepeat(name));

    [Theory]
    [InlineData("音轨 1 原创曲",1)][InlineData("音軌 1 原創曲",1)]
    [InlineData("Track 10 Original song",10)][InlineData("トラック1 オリジナル曲",1)]
    [InlineData("トラック１２　オリジナル曲",12)][InlineData("Track 2",2)]
    [InlineData("Track 1x",null)][InlineData("音軌 0",null)][InlineData("トラック1曲",null)]
    [InlineData("Track 2147483648 song",null)][InlineData("My Track 1 song",null)]
    public void AlbumTrackNumbersRetainBoundariesAndDoNotGuess(string label,int? expected)=>Assert.Equal(expected,NativeMusicLabels.AlbumTrackNumber(label));

    [Theory]
    [InlineData("插播",true)][InlineData("Play Next",true)][InlineData("次に再生",true)]
    [InlineData("Play Last",false)][InlineData("あとで再生",false)][InlineData("插播這首",false)]
    public void PlayNextKeepsExactCommandProtection(string label,bool expected)=>Assert.Equal(expected,MusicPlaybackQueue.IsPlayNextCommand(label));
}
