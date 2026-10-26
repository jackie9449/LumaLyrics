using LumaLyrics.Core;
namespace LumaLyrics.Core.Tests;

public class SeekingTests
{
    [Fact] public void NativeScrollingLabelRequiresExactArtistAndAlbumIncludingRepeatedCopies()
    {
        var track=new Track("原创录音","演示歌手","演示专辑",180000);
        Assert.True(PlaybackSeeking.MatchesNativeLabels(track,track.Title,"演示歌手 — 演示专辑"));Assert.True(PlaybackSeeking.MatchesNativeLabels(track,track.Title,"演示歌手 — 演示专辑 演示歌手 — 演示专辑"));Assert.False(PlaybackSeeking.MatchesNativeLabels(track,track.Title,"演示歌手 — 其他专辑"));Assert.False(PlaybackSeeking.MatchesNativeLabels(track,track.Title,"演示歌手"));Assert.False(PlaybackSeeking.MatchesNativeLabels(track,"另一个录音","演示歌手 — 演示专辑"));
    }
    static PlaybackSnapshot Snapshot()=>new("AppleMusic",1,2,new("原创录音","演示","示例专辑",180000),PlayState.Playing,1,10000,DateTimeOffset.UtcNow.AddSeconds(-1),DateTimeOffset.UtcNow,0,PositionQuality.Estimated,CanSeek:true);
    [Theory][InlineData(-100,0)][InlineData(250000,180000)][InlineData(32500,32500)]
    public void TargetIsClamped(double requested,double expected)=>Assert.Equal(expected,PlaybackSeeking.Target(Snapshot(),requested));
    [Theory][InlineData(double.NaN)][InlineData(double.PositiveInfinity)][InlineData(double.NegativeInfinity)]
    public void RejectsNonFiniteTargets(double target)=>Assert.Null(PlaybackSeeking.Target(Snapshot(),target));
    [Fact] public void RejectsUnavailableDurationAndCapability()
    {
        var s=Snapshot();Assert.False(PlaybackSeeking.Available(s with{CanSeek=false}));Assert.False(PlaybackSeeking.Available(s with{Track=s.Track with{DurationMs=double.NaN}}));Assert.False(PlaybackSeeking.Available(s with{Track=s.Track with{DurationMs=null}}));Assert.False(PlaybackSeeking.Available(s with{Status=PlayState.Stopped}));Assert.True(PlaybackSeeking.Available(s with{Status=PlayState.Paused}));
    }
    [Fact] public void ConfirmationRequiresFreshTimelineAndExactSession()
    {
        var s=Snapshot();var issued=DateTimeOffset.UtcNow;var after=s with{PositionMs=60000,TimelineUtc=issued,CapturedUtc=issued};
        Assert.True(PlaybackSeeking.Confirmed(s,after,60000,issued));Assert.False(PlaybackSeeking.Confirmed(s,after with{TimelineUtc=s.TimelineUtc},60000,issued));Assert.False(PlaybackSeeking.Confirmed(s,after with{Generation=2},60000,issued));Assert.False(PlaybackSeeking.Confirmed(s,after with{Revision=3},60000,issued));Assert.False(PlaybackSeeking.Confirmed(s,after with{SessionId="Other"},60000,issued));Assert.False(PlaybackSeeking.Confirmed(s,after with{PositionMs=10000},60000,issued));Assert.False(PlaybackSeeking.Confirmed(s,after with{PositionMs=double.NaN},60000,issued));
    }
    [Fact] public async Task ChangedRecordingNeverReceivesCommand()
    {
        var s=Snapshot();int commands=0;var result=await PlaybackSeeking.ExecuteAsync(s,40000,_=>Task.FromResult<PlaybackSnapshot?>(s with{Revision=3}),(_,_,_)=>{commands++;return Task.FromResult(true);},default);
        Assert.False(result.Confirmed);Assert.Equal(0,commands);
    }
    [Fact] public async Task SendsExactlyOnceAndConfirmsAfterAnOldRead()
    {
        var s=Snapshot();int commands=0,reads=0;
        var result=await PlaybackSeeking.ExecuteAsync(s,40000,_=>Task.FromResult<PlaybackSnapshot?>(++reads<3?s:s with{PositionMs=40000,TimelineUtc=DateTimeOffset.UtcNow,CapturedUtc=DateTimeOffset.UtcNow}),(_,_,_)=>{commands++;return Task.FromResult(true);},default);
        Assert.True(result.Confirmed);Assert.Equal(1,commands);Assert.Equal(3,reads);
    }
    [Fact] public async Task RejectionIsNotRetried()
    {
        var s=Snapshot();int commands=0;var result=await PlaybackSeeking.ExecuteAsync(s,40000,_=>Task.FromResult<PlaybackSnapshot?>(s),(_,_,_)=>{commands++;return Task.FromResult(false);},default);Assert.False(result.Confirmed);Assert.Equal(1,commands);
    }
    [Fact] public async Task CancellationDuringReadPreventsCommandEvenWhenProviderIgnoresIt()
    {
        var s=Snapshot();using var cts=new CancellationTokenSource();int commands=0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>PlaybackSeeking.ExecuteAsync(s,40000,_=>{cts.Cancel();return Task.FromResult<PlaybackSnapshot?>(s);},(_,_,_)=>{commands++;return Task.FromResult(true);},cts.Token));Assert.Equal(0,commands);
    }
    [Fact] public async Task TrackChangeAfterCommandCannotConfirmOrRepeat()
    {
        var s=Snapshot();int commands=0;var result=await PlaybackSeeking.ExecuteAsync(s,40000,_=>Task.FromResult<PlaybackSnapshot?>(commands==0?s:s with{Revision=3,PositionMs=40000,TimelineUtc=DateTimeOffset.UtcNow,CapturedUtc=DateTimeOffset.UtcNow}),(_,_,_)=>{commands++;return Task.FromResult(true);},default);Assert.False(result.Confirmed);Assert.Equal(1,commands);
    }
}
