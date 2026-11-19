using System.Diagnostics;

namespace LumaLyrics.Core.Tests;

public sealed class OfflineLyricTimingTests
{
    [Fact]
    public void SparseDraftCannotSaveAndCompletingItRetainsTheSecondChorusAndGaps()
    {
        var edit=new ManualLyricTiming("Same refrain\nMiddle line\nSame refrain",60000);
        edit.LoadDraft([new(1200,5000),null,new(31000,35000)]);
        Assert.Equal(2,edit.MarkedCount);
        Assert.Equal(1,edit.ActiveIndex);
        Assert.Throws<InvalidDataException>(()=>edit.MakeLrc());
        Assert.Throws<InvalidDataException>(()=>edit.Edit(1,1000,7000));
        Assert.Throws<InvalidDataException>(()=>edit.Edit(1,6500,32000));
        edit.Mark(6500);edit.Mark(9000);
        Assert.True(edit.Complete);
        var doc=new LrcParser().Parse(edit.MakeLrc());
        Assert.Equal(new[]{"Same refrain","","Middle line","","Same refrain",""},doc.Groups.Select(l=>l.Text));
        Assert.Equal("",doc.Groups[doc.FindIndex(15000)].Text);
        edit.Undo();
        Assert.Equal(1,edit.ActiveIndex);
        Assert.Equal(31000,edit.Lines[2].StartMs);
        Assert.False(edit.Complete);
    }

    [Fact]
    public void MalformedDraftCannotOverwriteAnyLineWithImpossibleTimes()
    {
        var edit=new ManualLyricTiming("One\nTwo\nThree",10000);
        Assert.Throws<InvalidDataException>(()=>edit.LoadDraft([new(10,20)]));
        edit.LoadDraft([new(-1,1000),new(6000,6500),new(7000,12000)]);
        Assert.Null(edit.Lines[0].StartMs);
        Assert.Equal(6000,edit.Lines[1].StartMs);
        Assert.Null(edit.Lines[2].StartMs);
        Assert.Throws<InvalidDataException>(()=>edit.Edit(0,7000,8000));
        edit.Edit(0,1000,2000);
        Assert.Equal(2,edit.ActiveIndex);
        Assert.Throws<InvalidDataException>(()=>edit.MakeLrc());
    }

    [Fact]
    public void WholeDraftCalibrationPreservesGapsAndMissingEndsAndCanBeUndone()
    {
        var edit=new ManualLyricTiming("A\nB\nA",20000);
        edit.LoadDraft([new(1400,5000),null,new(12000,null)]);
        Assert.Throws<InvalidDataException>(()=>edit.ShiftDraft(-1600));
        edit.ShiftDraft(1000);
        Assert.Equal(2400,edit.Lines[0].StartMs);
        Assert.Equal(6000,edit.Lines[0].EndMs);
        Assert.Null(edit.Lines[1].StartMs);
        Assert.Equal(13000,edit.Lines[2].StartMs);
        Assert.Null(edit.Lines[2].EndMs);
        edit.Undo();
        Assert.Equal(1400,edit.Lines[0].StartMs);
        Assert.Equal(12000,edit.Lines[2].StartMs);
    }

    [Fact]
    public void CaptureClockRejectsSeekAndRecordingChangeDespiteUsingTheSameTitle()
    {
        var t=new Track("Own Title","Own Artist","Own Album",60000);
        var time=DateTimeOffset.UnixEpoch.AddDays(10000);
        long baseTicks=15*Stopwatch.Frequency;
        PlaybackSnapshot At(int seconds,int position,long revision=1)=>new("fixture",1,revision,t,PlayState.Playing,1,position,
            time.AddSeconds(seconds),time.AddSeconds(seconds),baseTicks+seconds*Stopwatch.Frequency,PositionQuality.Estimated);
        var clock=new LyricCaptureTimeline(At(0,0));
        for(int i=0;i<=5;i++)Assert.True(clock.Observe(At(i,i*1000),15025));
        Assert.InRange(clock.StartMs!.Value,24.99,25.01);
        Assert.True(clock.Observe(At(6,27000),15025));
        Assert.False(clock.Observe(At(7,28000),15025));
        Assert.False(clock.Observe(At(7,7000,revision:2),15025));
        Assert.False(clock.Observe(At(7,7000) with{Status=PlayState.Paused},15025));
    }

    [Fact]
    public void CaptureNeverTrimsAnAudibleIntroToFitThePlayerClock()
    {
        var t=new Track("Song","Singer","Album",240000);
        var utc=DateTimeOffset.UnixEpoch.AddDays(10000);
        PlaybackSnapshot At(int n)=>new("fixture",1,1,t,PlayState.Playing,1,n*1000,
            utc.AddMilliseconds(n*1025),utc.AddMilliseconds(n*1025),(long)((10+n*1.025)*Stopwatch.Frequency),PositionQuality.Estimated);
        var clock=new LyricCaptureTimeline(At(0));
        for(int i=0;i<170;i++)Assert.True(clock.Observe(At(i),8800));
        Assert.InRange(clock.StartMs!.Value,-1280,-1270);
        clock.ObserveSignal(430);
        Assert.Equal(-430,clock.StartMs);
    }

    [Fact]
    public void WaveReaderMakesExactMonoSampleCountsAndRejectsOtherDurationsAndSilence()
    {
        using var wave=Build(1200);
        var audio=LyricTimingAudio.ReadWave(wave,2000);
        Assert.Equal(32000,audio.Count);
        Assert.Equal(600,audio.Samples[0]);
        wave.Position=0;
        Assert.Throws<InvalidDataException>(()=>LyricTimingAudio.ReadWave(wave,4500));
        using var silence=Build(0);
        Assert.Throws<InvalidDataException>(()=>LyricTimingAudio.ReadWave(silence,2000));

        static MemoryStream Build(short volume)
        {
            const int inputRate=44100,frames=88200;
            var buffer=new MemoryStream();
            using var writer=new BinaryWriter(buffer,System.Text.Encoding.ASCII,leaveOpen:true);
            writer.Write("RIFF"u8);writer.Write(36+frames*4);writer.Write("WAVEfmt "u8);writer.Write(16);
            writer.Write((short)1);writer.Write((short)2);writer.Write(inputRate);writer.Write(inputRate*4);
            writer.Write((short)4);writer.Write((short)16);writer.Write("data"u8);writer.Write(frames*4);
            for(int i=0;i<frames;i++){writer.Write(volume);writer.Write((short)0);}
            buffer.Position=0;return buffer;
        }
    }
}
