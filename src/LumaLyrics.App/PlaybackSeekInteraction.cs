using LumaLyrics.Core;

namespace LumaLyrics.App;

public partial class MainWindow
{
    PlaybackSnapshot? seekRecording;
    CancellationTokenSource? seekCancellation;
    bool seekPending;double seekTarget;
    PlaybackSnapshot? nativeSeekRecording;
    bool nativeSeekAvailable;DateTimeOffset nextSeekProbe;
    CancellationTokenSource? seekProbe;
    bool CanSeek(PlaybackSnapshot? snapshot)=>PlaybackSeeking.Available(snapshot,nativeSeekAvailable && nativeSeekRecording is { } native && PlaybackSeeking.SameRecording(native,snapshot));
    async void UpdateSeekCapability(PlaybackSnapshot? snapshot)
    {
        if(quitting || !IsVisible || WindowState==System.Windows.WindowState.Minimized)return;
        if(snapshot is null || !PlaybackSeeking.Available(snapshot,true))return;
        bool same=nativeSeekRecording is { } previous && PlaybackSeeking.SameRecording(previous,snapshot);
        if(same && (nativeSeekAvailable || DateTimeOffset.UtcNow<nextSeekProbe))return;
        seekProbe?.Cancel();nativeSeekRecording=snapshot;nativeSeekAvailable=false;nextSeekProbe=DateTimeOffset.UtcNow.AddSeconds(15);
        if(snapshot.CanSeek || !snapshot.SessionId.Contains("AppleMusic",StringComparison.OrdinalIgnoreCase))return;
        using var current=new CancellationTokenSource();seekProbe=current;
        try
        {
            var available=await AppleMusicLibraryHandoff.SeekControlAsync(snapshot,()=>coordinator.Source.Latest,null,current.Token);
            if(!quitting && !current.IsCancellationRequested && ReferenceEquals(seekProbe,current) && PlaybackSeeking.SameRecording(snapshot,coordinator.Source.Latest)){nativeSeekAvailable=available;vm.CanSeek=CanSeek(coordinator.Snapshot);}
        }
        catch(OperationCanceledException){}
        finally{if(ReferenceEquals(seekProbe,current))seekProbe=null;}
    }
    void SeekStarted(object? sender,EventArgs e)
    {
        seekCancellation?.Cancel();seekCancellation=null;seekPending=false;
        seekRecording=coordinator.Source.Latest??coordinator.Snapshot;
    }
    void SeekCancelled(object? sender,EventArgs e){seekRecording=null;Tick();}
    void ValidateSeek(PlaybackSnapshot? actual)
    {
        if(seekRecording is not { } expected || PlaybackSeeking.SameRecording(expected,actual) && CanSeek(actual))return;
        seekCancellation?.Cancel();seekPending=false;SeekSlider.CancelEdit();seekRecording=null;
    }
    async void SeekCommitted(object? sender,EventArgs e)
    {
        var expected=seekRecording;if(expected is null)return;
        seekCancellation?.Cancel();using var current=new CancellationTokenSource(TimeSpan.FromSeconds(12));seekCancellation=current;
        seekTarget=SeekSlider.Value;seekPending=true;Tick();
        try
        {
            var result=await playbackBridge.SeekAsync(expected,seekTarget,nativeSeekAvailable,current.Token);
            if(!current.IsCancellationRequested && ReferenceEquals(seekCancellation,current) && PlaybackSeeking.SameRecording(expected,coordinator.Source.Latest))
                vm.Status=result.Confirmed?"已跳转至 "+TimeSpan.FromMilliseconds(seekTarget).ToString(@"mm\:ss"):result.Message;
        }
        catch(OperationCanceledException){}
        catch(Exception ex){store.Log("Seek: "+ex.GetType().Name);if(ReferenceEquals(seekCancellation,current))vm.Status="进度跳转暂不可用，请重试。";}
        finally
        {
            if(ReferenceEquals(seekCancellation,current)){seekCancellation=null;seekRecording=null;seekPending=false;Tick();}
        }
    }
}
