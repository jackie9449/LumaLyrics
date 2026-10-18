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
    [Fact] public async Task SlowOldRequestCannotReplaceNewTrackEvenIfItIgnoresCancellation()
    {
        var version=new RequestVersion();var slow=new TaskCompletionSource<string>();var a=version.Next();string? applied=null;
        async Task FinishA(){var result=await slow.Task;if(version.IsCurrent(a))applied=result;}
        var task=FinishA();var b=version.Next();if(version.IsCurrent(b))applied="B";slow.SetResult("A");await task;Assert.Equal("B",applied);version.Next();Assert.False(version.IsCurrent(b));
    }
}
