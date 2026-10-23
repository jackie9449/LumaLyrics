using LumaLyrics.Infrastructure;
using LumaLyrics.Core;
namespace LumaLyrics.App;

internal sealed record LibraryPlayResult(bool Confirmed,string Message);

internal sealed class AppleMusicPlaybackBridge(PlaybackCoordinator coordinator,Action<string,object?>? trace=null)
{
    internal async Task<LibraryPlayResult> PlayAsync(MusicLibrarySong song,CancellationToken token)
    {
        string? failure=await AppleMusicLibraryHandoff.SelectAsync(song,token,trace);
        if(failure is not null)return new(false,failure);
        return await ConfirmAsync(track=>MusicLibraryPlayback.IsRequestedRecording(song.Track,track),"正在播放 · "+song.Title,token);
    }
    internal async Task<LibraryPlayResult> PlayAlbumAsync(MusicAlbum album,CancellationToken token)
    {
        var selection=await AppleMusicLibraryHandoff.SelectAlbumAsync(album,token,trace);
        if(selection.Failure is not null)return new(false,selection.Failure);
        if(selection.FirstTrack is not { } first)return new(false,"尚无法确认 Apple Music 的专辑首曲，当前歌词继续跟随实际播放。");
        return await ConfirmAsync(track=>MusicLibraryPlayback.IsRequestedRecording(first,track),"正在播放专辑 · "+album.Title,token);
    }
    async Task<LibraryPlayResult> ConfirmAsync(Func<Track,bool> matches,string message,CancellationToken token)
    {
        var issued=DateTimeOffset.UtcNow;
        string? appleSession=coordinator.Source.SessionIds.FirstOrDefault(id=>id.Contains("AppleMusic",StringComparison.OrdinalIgnoreCase));
        if(appleSession is null)
        {
            try{await coordinator.Source.ReconnectAsync().WaitAsync(TimeSpan.FromSeconds(3),token);}
            catch(TimeoutException){return new(false,"Apple Music 的播放连接暂未响应，请稍后重试。");}
            appleSession=coordinator.Source.SessionIds.FirstOrDefault(id=>id.Contains("AppleMusic",StringComparison.OrdinalIgnoreCase));
        }
        if(appleSession is not null && coordinator.Source.SelectedId!=appleSession)
            await coordinator.Source.SelectAsync(appleSession).WaitAsync(TimeSpan.FromSeconds(2),token);
        for(int i=0;i<36;i++)
        {
            token.ThrowIfCancellationRequested();
            try{await coordinator.Source.RefreshAsync().WaitAsync(TimeSpan.FromSeconds(2),token);}
            catch(TimeoutException){return new(false,"Apple Music 的播放状态暂未响应，请稍后重试。");}
            if(coordinator.Snapshot is {Status:PlayState.Playing} actual && actual.CapturedUtc>=issued && actual.SessionId.Contains("AppleMusic",StringComparison.OrdinalIgnoreCase) && matches(actual.Track))
                return new(true,message);
            await Task.Delay(250,token);
        }
        return new(false,"Apple Music 尚未确认所选录音的播放，可重试。当前歌词继续跟随实际播放内容。");
    }
}
