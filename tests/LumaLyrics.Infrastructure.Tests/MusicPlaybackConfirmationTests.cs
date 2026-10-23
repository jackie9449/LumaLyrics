using System.Diagnostics;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.Infrastructure.Tests;

public sealed class MusicPlaybackConfirmationTests
{
    static readonly Track requested=new("微光之间","浮光工作室","原创测试",180000);
    static readonly DateTimeOffset issued=DateTimeOffset.UtcNow;
    static readonly PlaybackConfirmationOptions fast=new(TimeSpan.FromSeconds(2),TimeSpan.FromMilliseconds(15),TimeSpan.FromMilliseconds(5));
    static PlaybackSnapshot Snapshot()=>new("AppleInc.AppleMusicWin!App",1,1,requested,PlayState.Playing,1,0,issued,issued.AddMilliseconds(1),0,PositionQuality.Reliable);
    static bool Match(Track track)=>MusicLibraryPlayback.IsRequestedRecording(requested,track);

    [Fact] public async Task KeepsWaitingForTheSameInFlightReadAfterItsSoftTimeout()
    {
        int calls=0;var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var pending=new TaskCompletionSource<PlaybackSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var wait=MusicPlaybackConfirmation.WaitAsync(_=>{calls++;entered.TrySetResult();return pending.Task;},Match,issued,CancellationToken.None,fast);
        await entered.Task;await Task.Delay(60);Assert.False(wait.IsCompleted);Assert.Equal(1,calls);
        pending.SetResult(Snapshot());Assert.True(await wait);Assert.Equal(1,calls);
    }
    [Fact] public async Task RetriesTransientTimeoutsAndDoesNotReissueAPlaybackCommand()
    {
        int reads=0;
        Task<PlaybackSnapshot?> Probe(CancellationToken token){if(++reads<=2)throw new TimeoutException("temporary native read delay");return Task.FromResult<PlaybackSnapshot?>(Snapshot());}
        Assert.True(await MusicPlaybackConfirmation.WaitAsync(Probe,Match,issued,CancellationToken.None,fast));Assert.Equal(3,reads);
    }
    [Theory] [InlineData("stale")] [InlineData("other-recording")] [InlineData("paused")] [InlineData("other-player")] [InlineData("missing")]
    public async Task IneligibleSnapshotsWaitForAnActualFreshRequestedRecording(string kind)
    {
        var invalid=kind switch{"stale"=>Snapshot() with{CapturedUtc=issued.AddSeconds(-1)},"other-recording"=>Snapshot() with{Track=requested with{Title="微光之间 (Live)"}},"paused"=>Snapshot() with{Status=PlayState.Paused},"other-player"=>Snapshot() with{SessionId="Other.Player"},_=>null};
        int reads=0;
        Assert.True(await MusicPlaybackConfirmation.WaitAsync(_=>Task.FromResult(++reads<=2?invalid:Snapshot()),Match,issued,CancellationToken.None,fast));Assert.Equal(3,reads);
    }
    [Fact] public async Task TotalDeadlineBoundsANativeReadThatNeverReturns()
    {
        int reads=0;var pending=new TaskCompletionSource<PlaybackSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);var watch=Stopwatch.StartNew();
        bool result=await MusicPlaybackConfirmation.WaitAsync(_=>{reads++;return pending.Task;},Match,issued,CancellationToken.None,fast with{Timeout=TimeSpan.FromMilliseconds(80)});
        Assert.False(result);Assert.Equal(1,reads);Assert.True(watch.Elapsed<TimeSpan.FromSeconds(1));pending.SetException(new TimeoutException("late read failure is observed"));
    }
    [Fact] public async Task WrongRecordingCannotBecomeSuccessAtTheFinalDeadline()
    {
        int reads=0;var wrong=Snapshot() with{Track=requested with{Album="原创测试 (Live)"}};
        Assert.False(await MusicPlaybackConfirmation.WaitAsync(_=>{reads++;return Task.FromResult<PlaybackSnapshot?>(wrong);},Match,issued,CancellationToken.None,fast with{Timeout=TimeSpan.FromMilliseconds(60)}));Assert.True(reads>1);
    }
    [Fact] public async Task CancellationRejectsLateSuccessAndDoesNotAffectTheNextRequest()
    {
        using var cancellation=new CancellationTokenSource();var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var pending=new TaskCompletionSource<PlaybackSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var old=MusicPlaybackConfirmation.WaitAsync(_=>{entered.TrySetResult();return pending.Task;},Match,issued,cancellation.Token,fast);await entered.Task;cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>old);pending.SetResult(Snapshot());
        Assert.True(await MusicPlaybackConfirmation.WaitAsync(_=>Task.FromResult<PlaybackSnapshot?>(Snapshot()),Match,issued,CancellationToken.None,fast));
    }
}
