using LumaLyrics.Infrastructure;
using LumaLyrics.Core;
namespace LumaLyrics.App;

internal sealed record LibraryPlayResult(bool Confirmed,string Message,bool ActivateWindow=true);

internal sealed class AppleMusicPlaybackBridge(PlaybackCoordinator coordinator,Action<string,object?>? trace=null,TimeSpan? confirmationDelay=null)
{
    int pending;
    readonly object selectionGate=new();
    CancellationTokenSource? selecting;
    internal bool IsBusy=>Volatile.Read(ref pending)>0;
    internal event Action? HandoffCompleted;
    internal async Task<(LumaLyrics.Core.LyricsOcrObservation Observation,NativeLyricsCapture Capture)> CaptureLyricsReviewAsync(Track track,LyricsDocument original,bool image,CancellationToken token)
    {
        Interlocked.Increment(ref pending);
        try
        {
            await coordinator.Source.RefreshAsync().WaitAsync(token);var before=coordinator.Source.Latest;
            if(before is null || !before.SessionId.Contains("AppleMusic",StringComparison.OrdinalIgnoreCase) || !MusicLibraryPlayback.IsRequestedRecording(track,before.Track))throw new InvalidOperationException("尚未确认 Apple Music 当前录音，请重新连接后重试。");
            string language=LumaLyrics.Windows.LocalLyricsOcr.PreferredLanguage(original);
            if(image && !LumaLyrics.Windows.LocalLyricsOcr.Supports(language))throw new InvalidOperationException($"本机尚未安装 {language} 的 Windows OCR。可使用“原生文字参考”；也可在 Windows 语言选项中安装该语言的光学字符识别组件后重试。");
            var capture=await AppleMusicLibraryHandoff.CaptureLyricsAsync(track,image,token,trace);
            var recognized=image?await LumaLyrics.Windows.LocalLyricsOcr.RecognizeAsync(capture.Image!,language,token):new LumaLyrics.Windows.LocalOcrResult("native-accessibility",capture.NativeLines);
            await coordinator.Source.RefreshAsync().WaitAsync(token);var after=coordinator.Source.Latest;
            if(after is null || !MusicLibraryPlayback.IsRequestedRecording(track,after.Track) || coordinator.Document is not { } current || TranslationAlignment.Hash(current.Raw)!=TranslationAlignment.Hash(original.Raw))throw new InvalidOperationException("采集期间录音或原词已变化，本次校对未保存。");
            if(recognized.Lines.Count==0)throw new InvalidOperationException("没有识别出可比较的文字，未保存空白记录。");
            return (new(track,TranslationAlignment.Hash(original.Raw),capture.CapturedUtc,before.PositionMs,recognized.Language,capture.Bounds,recognized.Lines),capture);
        }
        finally{Interlocked.Decrement(ref pending);HandoffCompleted?.Invoke();}
    }
    internal Task<MusicQueueSnapshot> ReadQueueAsync(CancellationToken token)=>AppleMusicLibraryHandoff.ReadQueueAsync(token,trace);
    internal Task<LibraryPlayResult> ReorderQueueAsync(MusicQueueMove move,CancellationToken token)=>RunSelectionAsync(async (context,requestToken)=>
    {
        await coordinator.Source.RefreshAsync().WaitAsync(requestToken);var before=coordinator.Source.Latest;
        if(before is null || !before.SessionId.Contains("AppleMusic",StringComparison.OrdinalIgnoreCase) || move.Snapshot.Current is null || !MusicLibraryPlayback.IsRequestedRecording(move.Snapshot.Current,before.Track))return new(false,"尚未确认 Apple Music 当前播放状态，请重新连接后重试。");
        var failure=await AppleMusicLibraryHandoff.ReorderQueueAsync(move,requestToken,context,trace);if(failure is not null)return new(false,failure);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(requestToken);timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            while(true)
            {
                var queue=await AppleMusicLibraryHandoff.ReadQueueAsync(timeout.Token,trace,context);await coordinator.Source.RefreshAsync().WaitAsync(timeout.Token);var current=coordinator.Source.Latest;
                if(MusicPlaybackQueue.ConfirmsMove(move,queue) && current is not null && current.Status==before.Status && MusicLibraryPlayback.IsRequestedRecording(before.Track,current.Track))return new(true,"已调整播放顺序，当前歌曲继续播放");
                await Task.Delay(250,timeout.Token);
            }
        }
        catch(OperationCanceledException) when(!requestToken.IsCancellationRequested){}
        return new(false,"已发送排序操作，尚未确认新顺序，请刷新播放列表查看。");
    },token);
    async Task<LibraryPlayResult> RunSelectionAsync(Func<AppleMusicLibraryHandoff.HandoffContext,CancellationToken,Task<LibraryPlayResult>> action,CancellationToken token)
    {
        using var current=CancellationTokenSource.CreateLinkedTokenSource(token);
        lock(selectionGate){selecting?.Cancel();selecting=current;}
        try{return await RunCommandAsync(context=>action(context,current.Token),current.Token);}
        finally{lock(selectionGate){if(ReferenceEquals(selecting,current))selecting=null;}}
    }
    async Task<LibraryPlayResult> RunCommandAsync(Func<AppleMusicLibraryHandoff.HandoffContext,Task<LibraryPlayResult>> action,CancellationToken token)
    {
        Interlocked.Increment(ref pending);var context=new AppleMusicLibraryHandoff.HandoffContext();LibraryPlayResult? result=null;bool minimized=false;
        try{result=await action(context);}
        finally
        {
            try{minimized=await AppleMusicLibraryHandoff.CompleteHandoffWindowAsync(context,result?.Confirmed==true && !token.IsCancellationRequested,trace);}
            finally{Interlocked.Decrement(ref pending);HandoffCompleted?.Invoke();}
        }
        token.ThrowIfCancellationRequested();return result with{ActivateWindow=result.Confirmed && minimized};
    }
    internal Task<LibraryPlayResult> InsertNextAsync(MusicLibrarySong song,CancellationToken token)=>RunCommandAsync(async context=>
    {
        var before=await AppleMusicLibraryHandoff.ReadQueueAsync(token,trace,context);
        string? failure=await AppleMusicLibraryHandoff.InsertNextAsync(song,token,context,trace);
        if(failure is not null)return new(false,failure);
        using var confirmation=CancellationTokenSource.CreateLinkedTokenSource(token);confirmation.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            while(true)
            {
                // The menu closes before the native manual-queue list finishes updating.
                // Retry reads only; never send the insertion a second time.
                await Task.Delay(250,confirmation.Token);var queue=await AppleMusicLibraryHandoff.ReadQueueAsync(confirmation.Token,trace,context);
                if(MusicPlaybackQueue.ConfirmsInsertion(song,before,queue))
                    return new(true,"已插播 · "+song.Title+" · 将在当前歌曲结束后播放");
            }
        }
        catch(OperationCanceledException) when(!token.IsCancellationRequested){}
        return new(false,"已发送插播请求，暂未确认下一首。请刷新播放列表查看。");
    },token);
    internal Task<LibraryPlayResult> PlayAsync(MusicLibrarySong song,CancellationToken token)=>RunSelectionAsync(async (context,requestToken)=>
    {
        string? failure=await AppleMusicLibraryHandoff.SelectAsync(song,requestToken,context,trace);
        if(failure is not null)return new(false,failure);
        return await ConfirmAsync(track=>MusicLibraryPlayback.IsRequestedRecording(song.Track,track),"正在播放 · "+song.Title,requestToken,context);
    },token);
    internal Task<LibraryPlayResult> PlayAlbumAsync(MusicAlbum album,CancellationToken token)=>RunSelectionAsync(async (context,requestToken)=>
    {
        var selection=await AppleMusicLibraryHandoff.SelectAlbumAsync(album,requestToken,context,trace);
        if(selection.Failure is not null)return new(false,selection.Failure);
        if(selection.FirstTrack is not { } first)return new(false,"尚无法确认 Apple Music 的专辑首曲，当前歌词继续跟随实际播放。");
        return await ConfirmAsync(track=>MusicLibraryPlayback.IsRequestedRecording(first,track),"正在播放专辑 · "+album.Title,requestToken,context);
    },token);
    internal Task<LibraryPlayResult> PlayQueueAsync(MusicQueueSelection selection,CancellationToken token)=>RunSelectionAsync(async (context,requestToken)=>
    {
        string? failure=await AppleMusicLibraryHandoff.SelectQueueAsync(selection,requestToken,context,trace);
        if(failure is not null)return new(false,failure);
        var track=selection.Snapshot.Upcoming[selection.Index].Track;
        var playback=await ConfirmAsync(actual=>MusicLibraryPlayback.IsRequestedRecording(track,actual),"正在播放 · "+track.Title,requestToken,context);
        if(!playback.Confirmed)return playback;
        using var confirmation=CancellationTokenSource.CreateLinkedTokenSource(requestToken);confirmation.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            while(true)
            {
                var after=await AppleMusicLibraryHandoff.ReadQueueAsync(confirmation.Token,trace,context);
                if(MusicPlaybackQueue.ConfirmsJump(selection,after))return playback;
                await Task.Delay(250,confirmation.Token);
            }
        }
        catch(OperationCanceledException) when(!requestToken.IsCancellationRequested){}
        return new(false,"已发送队列跳转请求，尚未确认目标位置。请刷新播放列表查看。");
    },token);
    async Task<LibraryPlayResult> ConfirmAsync(Func<Track,bool> matches,string message,CancellationToken token,AppleMusicLibraryHandoff.HandoffContext context)
    {
        var issued=DateTimeOffset.UtcNow;bool delayed=false;
        async Task<PlaybackSnapshot?> Read(CancellationToken readToken)
        {
            // Only the explicit private verifier supplies this artificial latency.
            if(!delayed && confirmationDelay is { } latency && latency>TimeSpan.Zero)
            {
                delayed=true;trace?.Invoke("confirmationDelayStarted",new{milliseconds=latency.TotalMilliseconds});
                await Task.Delay(latency/2,readToken);
                trace?.Invoke("confirmationStillPending",new{minimized=AppleMusicLibraryHandoff.HandoffWindowMinimized(context),actual=coordinator.Source.Latest});
                await Task.Delay(latency/2,readToken);
            }
            string? appleSession=coordinator.Source.SessionIds.FirstOrDefault(id=>id.Contains("AppleMusic",StringComparison.OrdinalIgnoreCase));
            if(appleSession is null){await coordinator.Source.ReconnectAsync().WaitAsync(readToken);appleSession=coordinator.Source.SessionIds.FirstOrDefault(id=>id.Contains("AppleMusic",StringComparison.OrdinalIgnoreCase));}
            if(appleSession is not null && coordinator.Source.SelectedId!=appleSession)await coordinator.Source.SelectAsync(appleSession).WaitAsync(readToken);
            await coordinator.Source.RefreshAsync().WaitAsync(readToken);return coordinator.Source.Latest;
        }
        bool confirmed=await MusicPlaybackConfirmation.WaitAsync(Read,matches,issued,token);
        trace?.Invoke("playbackConfirmation",new{confirmed,elapsed=(DateTimeOffset.UtcNow-issued).TotalSeconds});
        return confirmed?new(true,message):new(false,"已等待 Apple Music 确认播放，但尚未收到所选录音的有效状态。当前歌词继续跟随实际播放，可稍后重试。");
    }
}
