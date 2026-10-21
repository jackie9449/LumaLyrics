using LumaLyrics.Infrastructure;
using LumaLyrics.Core;
namespace LumaLyrics.App;

internal sealed record LibraryPlayResult(bool Confirmed,string Message);

internal sealed class AppleMusicPlaybackBridge(PlaybackCoordinator coordinator)
{
    internal async Task<LibraryPlayResult> PlayAsync(MusicLibrarySong song,CancellationToken token)
    {
        string? failure=await AppleMusicLibraryHandoff.SelectAsync(song,token);
        if(failure is not null)return new(false,failure);
        string? appleSession=coordinator.Source.SessionIds.FirstOrDefault(id=>id.Contains("AppleMusic",StringComparison.OrdinalIgnoreCase));
        if(appleSession is not null && coordinator.Source.SelectedId!=appleSession)
            await coordinator.Source.SelectAsync(appleSession).WaitAsync(TimeSpan.FromSeconds(2),token);
        for(int i=0;i<36;i++)
        {
            token.ThrowIfCancellationRequested();
            try{await coordinator.Source.RefreshAsync().WaitAsync(TimeSpan.FromSeconds(2),token);}
            catch(TimeoutException){return new(false,"Apple Music 的播放状态暂未响应，请稍后重试。");}
            if(coordinator.Snapshot is {Status:PlayState.Playing} actual && actual.SessionId.Contains("AppleMusic",StringComparison.OrdinalIgnoreCase) && MusicLibraryPlayback.IsRequestedRecording(song.Track,actual.Track))
                return new(true,"正在播放 · "+song.Title);
            await Task.Delay(250,token);
        }
        return new(false,"Apple Music 尚未确认播放这首歌，可重试。当前歌词继续跟随实际播放内容。");
    }
}
