using LumaLyrics.Core;
namespace LumaLyrics.Core.Tests;
public sealed class TestTime : TimeProvider
{
    public long Milliseconds; public DateTimeOffset Utc=new(2026,10,18,4,0,0,TimeSpan.Zero);
    public override long TimestampFrequency=>1000;public override long GetTimestamp()=>Milliseconds;public override DateTimeOffset GetUtcNow()=>Utc;
    public void Advance(int ms){Milliseconds+=ms;Utc=Utc.AddMilliseconds(ms);}
}
public class ClockTests
{
    [Theory]
    [InlineData(PlayState.Playing,11000)]
    [InlineData(PlayState.Paused,10000)]
    public void DelayedSnapshotIncludesDeliveryTimeOnlyWhilePlaying(PlayState state,double expected)
    {
        var time=new TestTime();var clock=new PlaybackClock(time);
        var snapshot=Snapshot(time,10000,state);time.Advance(1000);clock.Update(snapshot);
        Assert.Equal(expected,clock.PositionMs);
    }
    static PlaybackSnapshot Snapshot(TestTime t,double pos,PlayState state=PlayState.Playing,DateTimeOffset? timeline=null,long revision=1,double? rate=1)=>new("AppleMusic.exe",1,revision,new("A","B","C",240000),state,rate,pos,timeline??t.Utc,t.Utc,t.Milliseconds,PositionQuality.Estimated);
    [Fact] public void StartsMidSongAndUsesMonotonicTime()
    {
        var t=new TestTime();var clock=new PlaybackClock(t);clock.Update(Snapshot(t,60000,timeline:t.Utc.AddSeconds(-1)));t.Advance(1000);t.Utc=t.Utc.AddHours(4);Assert.Equal(62000,clock.PositionMs);
    }
    [Fact] public void PauseFreezesAndResumeDoesNotProjectAcrossPause()
    {
        var t=new TestTime();var clock=new PlaybackClock(t);var start=t.Utc;clock.Update(Snapshot(t,10000));t.Advance(2000);clock.Update(Snapshot(t,10000,PlayState.Paused,start));var paused=clock.PositionMs;t.Advance(10000);Assert.Equal(paused,clock.PositionMs);clock.Update(Snapshot(t,10000,PlayState.Playing,start));Assert.InRange(clock.PositionMs!.Value,10000,12001);t.Advance(1000);Assert.InRange(clock.PositionMs!.Value,11000,13001);
    }
    [Fact] public void RepeatedTimelineDoesNotResetClock()
    {
        var t=new TestTime();var c=new PlaybackClock(t);var s=Snapshot(t,30000);c.Update(s);t.Advance(1000);c.Update(s with{CapturedUtc=t.Utc});t.Advance(1000);Assert.Equal(32000,c.PositionMs);
    }
    [Fact] public void ForwardSeekBackwardSeekAndLoopReanchorImmediately()
    {
        var t=new TestTime();var c=new PlaybackClock(t);c.Update(Snapshot(t,40000));t.Advance(1000);c.Update(Snapshot(t,90000));Assert.Equal(90000,c.PositionMs);t.Advance(1000);c.Update(Snapshot(t,15000));Assert.Equal(15000,c.PositionMs);t.Advance(1000);c.Update(Snapshot(t,0));Assert.Equal(0,c.PositionMs);
    }
    [Fact] public void NewTrackCannotKeepOldAnchor(){var t=new TestTime();var c=new PlaybackClock(t);c.Update(Snapshot(t,90000));c.Update(Snapshot(t,0,revision:2) with{Quality=PositionQuality.Unavailable});Assert.Null(c.PositionMs);}
    [Fact] public void OldAndFutureTimelineAreRejected(){var t=new TestTime();var c=new PlaybackClock(t);c.Update(Snapshot(t,1000,timeline:t.Utc.AddMinutes(-3)));Assert.Null(c.PositionMs);c.Update(Snapshot(t,1000,timeline:t.Utc.AddMinutes(1)));Assert.Null(c.PositionMs);}
    [Fact] public void StalePlaybackStopsExtrapolating(){var t=new TestTime();var c=new PlaybackClock(t);c.Update(Snapshot(t,1000));t.Advance(91000);Assert.Null(c.PositionMs);}
    [Fact] public void UnknownRateUsesOneAndInvalidationRequiresNewEvidence(){var t=new TestTime();var c=new PlaybackClock(t);c.Update(Snapshot(t,5000,rate:null));t.Advance(1000);Assert.Equal(6000,c.PositionMs);Assert.Equal(PositionQuality.Estimated,c.Quality);c.Invalidate();Assert.Null(c.PositionMs);}
    [Fact] public void QuantizedRepeatsDoNotCreateSawtooth(){var t=new TestTime();var c=new PlaybackClock(t);c.Update(Snapshot(t,5000));t.Advance(500);c.Update(Snapshot(t,5000));Assert.Equal(5500,c.PositionMs);}
    [Fact] public void PausedMidSongStartupAcceptsOldTimestampWithoutAdvancing(){var t=new TestTime();var c=new PlaybackClock(t);c.Update(Snapshot(t,40000,PlayState.Paused,t.Utc.AddHours(-1)));Assert.Equal(40000,c.PositionMs);t.Advance(30000);Assert.Equal(40000,c.PositionMs);}
    [Fact] public void ResumeDeliveryMustNotBeLost()
    {
        var t=new TestTime();var c=new PlaybackClock(t);var stamp=t.Utc;
        c.Update(Snapshot(t,10000,PlayState.Paused));t.Advance(5000);
        var resume=Snapshot(t,10000,timeline:stamp);t.Advance(2000);c.Update(resume);
        Assert.Equal(12000,c.PositionMs);
    }
    [Fact] public void ResumeMustNotProjectAcrossShortPause()
    {
        var t=new TestTime();var c=new PlaybackClock(t);c.Update(Snapshot(t,10000));t.Advance(500);
        c.Update(Snapshot(t,10500,PlayState.Paused));t.Advance(100);var stamp=t.Utc;t.Advance(1800);
        c.Update(Snapshot(t,10500,timeline:stamp));Assert.Equal(10500,c.PositionMs);
        t.Advance(1000);c.Update(Snapshot(t,10500,timeline:stamp));Assert.Equal(11500,c.PositionMs);
    }
    [Fact] public void DelayedPauseWithoutNewTimelineFreezesAtCaptureNotDelivery()
    {
        var t=new TestTime();var c=new PlaybackClock(t);var stamp=t.Utc;c.Update(Snapshot(t,10000));
        t.Advance(1000);var pause=Snapshot(t,10000,PlayState.Paused,stamp);t.Advance(2000);c.Update(pause);
        Assert.Equal(11000,c.PositionMs);t.Advance(5000);Assert.Equal(11000,c.PositionMs);
    }
    [Fact] public void FreshSmallCorrectionUsesSourceInsteadOfAccumulatedHistory()
    {
        var t=new TestTime();var c=new PlaybackClock(t);c.Update(Snapshot(t,10000));t.Advance(1000);
        c.Update(Snapshot(t,11200));Assert.Equal(11200,c.PositionMs);
        t.Advance(1000);c.Update(Snapshot(t,12000));Assert.Equal(12000,c.PositionMs);
    }
    [Fact] public void ImmediateAndDelayedConsumersConvergeOnSameFreshEvidence()
    {
        var t=new TestTime();var direct=new PlaybackClock(t);var delayed=new PlaybackClock(t);
        var first=Snapshot(t,10000);direct.Update(first);t.Advance(300);delayed.Update(first);
        t.Advance(700);var next=Snapshot(t,11200);direct.Update(next);t.Advance(450);delayed.Update(next);
        Assert.Equal(11650,direct.PositionMs);Assert.Equal(direct.PositionMs,delayed.PositionMs);
    }
    [Fact] public void RateOnlyChangePreservesPositionAtCaptureAndUsesNewRateAfterward()
    {
        var t=new TestTime();var c=new PlaybackClock(t);c.Update(Snapshot(t,10000));t.Advance(100);
        var faster=Snapshot(t,10000,rate:2);t.Advance(400);c.Update(faster);Assert.Equal(10900,c.PositionMs);
    }
    [Fact] public void PreResumeEvidenceRemainsFencedOnLaterPolls()
    {
        var t=new TestTime();var c=new PlaybackClock(t);c.Update(Snapshot(t,10000,PlayState.Paused));
        t.Advance(1000);var pausedStamp=t.Utc;t.Advance(9000);
        c.Update(Snapshot(t,10000,timeline:pausedStamp));t.Advance(1000);
        c.Update(Snapshot(t,10000,timeline:pausedStamp.AddMilliseconds(100)));Assert.Equal(11000,c.PositionMs);
        t.Advance(1000);c.Update(Snapshot(t,12000));Assert.Equal(12000,c.PositionMs);
    }
    [Fact] public void PausedSeekSeenAtResumeDoesNotProjectAcrossPausedAge()
    {
        var t=new TestTime();var c=new PlaybackClock(t);c.Update(Snapshot(t,10000,PlayState.Paused));
        t.Advance(1000);var seekStamp=t.Utc;t.Advance(9000);
        var resume=Snapshot(t,30000,timeline:seekStamp);t.Advance(500);c.Update(resume);
        Assert.Equal(30500,c.PositionMs);
    }
    [Fact] public void OldTimestampCannotUndoFreshSeek()
    {
        var t=new TestTime();var c=new PlaybackClock(t);var old=t.Utc;c.Update(Snapshot(t,10000));
        t.Advance(1000);c.Update(Snapshot(t,40000));t.Advance(100);
        c.Update(Snapshot(t,10000,timeline:old));Assert.Equal(40100,c.PositionMs);
    }
    [Fact] public void OutOfOrderCaptureCannotUndoPauseOrNewTrack()
    {
        var t=new TestTime();var c=new PlaybackClock(t);var playing=Snapshot(t,10000);c.Update(playing);
        t.Advance(1000);c.Update(Snapshot(t,11000,PlayState.Paused));t.Advance(500);c.Update(playing);
        Assert.Equal(11000,c.PositionMs);t.Advance(1000);Assert.Equal(11000,c.PositionMs);
        c.Update(Snapshot(t,0,revision:2));c.Update(playing);Assert.Equal(0,c.PositionMs);
    }
    [Fact] public void ExpiredEvidenceCannotBeRevivedBySameRawWithNewTimestamp()
    {
        var t=new TestTime();var c=new PlaybackClock(t);c.Update(Snapshot(t,10000));
        for(int i=0;i<92;i++){t.Advance(1000);c.Update(Snapshot(t,10000));}
        Assert.Null(c.PositionMs);
        t.Advance(1000);c.Update(Snapshot(t,11000));Assert.Equal(11000,c.PositionMs);
    }
    [Theory]
    [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(double.NegativeInfinity)]
    public void NonFiniteSourcePositionIsUnavailable(double position)
    {
        var t=new TestTime();var c=new PlaybackClock(t);c.Update(Snapshot(t,position));Assert.Null(c.PositionMs);
    }
    [Fact] public void QueuedPauseResumeSequenceMatchesImmediateConsumer()
    {
        var t=new TestTime();var immediate=new PlaybackClock(t);var queued=new PlaybackClock(t);
        var samples=new List<PlaybackSnapshot>();
        void Publish(PlaybackSnapshot s){samples.Add(s);immediate.Update(s);}
        Publish(Snapshot(t,10000));t.Advance(500);
        Publish(Snapshot(t,10500,PlayState.Paused));var pausedStamp=t.Utc;t.Advance(3000);
        Publish(Snapshot(t,10500,timeline:pausedStamp));t.Advance(200);
        Publish(Snapshot(t,10500,timeline:pausedStamp));t.Advance(700);
        foreach(var s in samples)queued.Update(s);
        Assert.Equal(11400,immediate.PositionMs);Assert.Equal(immediate.PositionMs,queued.PositionMs);
    }
    [Fact] public void FreshReliableStationaryPositionOverridesPrediction()
    {
        var t=new TestTime();var c=new PlaybackClock(t);c.Update(Snapshot(t,10000) with{Quality=PositionQuality.Reliable});
        t.Advance(500);c.Update(Snapshot(t,10000) with{Quality=PositionQuality.Reliable});Assert.Equal(10000,c.PositionMs);
    }
    [Fact] public void SourceAgeAndDeliveryBothCountTowardExpiry()
    {
        var t=new TestTime();var c=new PlaybackClock(t);var s=Snapshot(t,10000,timeline:t.Utc.AddSeconds(-89));
        t.Advance(2000);c.Update(s);Assert.Null(c.PositionMs);Assert.Equal(PositionQuality.Unavailable,c.Quality);
    }
    [Fact] public void SubsecondBackwardSeekUsesExactFreshSource()
    {
        var t=new TestTime();var c=new PlaybackClock(t);c.Update(Snapshot(t,10000));t.Advance(200);
        c.Update(Snapshot(t,9950));Assert.Equal(9950,c.PositionMs);
    }
    [Fact] public async Task SlowOldRequestCannotReplaceNewTrackEvenIfItIgnoresCancellation()
    {
        var version=new RequestVersion();var slow=new TaskCompletionSource<string>();var a=version.Next();string? applied=null;
        async Task FinishA(){var result=await slow.Task;if(version.IsCurrent(a))applied=result;}
        var task=FinishA();var b=version.Next();if(version.IsCurrent(b))applied="B";slow.SetResult("A");await task;Assert.Equal("B",applied);version.Next();Assert.False(version.IsCurrent(b));
    }
}
