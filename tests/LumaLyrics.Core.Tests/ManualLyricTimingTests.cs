namespace LumaLyrics.Core.Tests;

public sealed class ManualLyricTimingTests
{
    [Fact]
    public void EnterAdvancesAndAnExplicitInstrumentalGapClearsTheDisplayedLyric()
    {
        var timing=new ManualLyricTiming("The quiet road\n\nWe follow the light\nThe quiet road",60000);
        timing.Mark(4100);
        timing.Mark(7200);
        timing.Mark(13000,leaveGap:true);
        Assert.True(timing.WaitingForStart);
        Assert.Equal(-1,timing.LineAt(18000));
        timing.Mark(42500);
        timing.Mark(48000);
        var parsed=new LrcParser().Parse(timing.MakeLrc());
        Assert.Equal(new double[]{4100,7200,13000,42500,48000},parsed.Groups.Select(g=>g.TimeMs));
        Assert.Equal(new[]{"The quiet road","We follow the light","","The quiet road",""},parsed.Groups.Select(g=>g.Text));
        Assert.Equal(-1,parsed.FindIndex(3999));
        Assert.Equal("",parsed.Groups[parsed.FindIndex(32000)].Text);
        Assert.Equal("The quiet road",parsed.Groups[parsed.FindIndex(42500)].Text);
    }

    [Fact]
    public void UndoReopensTheBoundaryAndEditsDoNotOverlapOrMoveOtherLines()
    {
        var timing=new ManualLyricTiming("One\nTwo",15000);
        timing.Mark(1500);timing.Mark(6000);
        timing.Undo();
        Assert.Equal(0,timing.ActiveIndex);
        Assert.Null(timing.Lines[1].StartMs);
        Assert.Throws<InvalidDataException>(()=>timing.Mark(1499));
        timing.Mark(6000,true);
        Assert.Throws<InvalidDataException>(()=>timing.Mark(6000));
        timing.Mark(8000);timing.Mark(11000);
        Assert.Throws<InvalidDataException>(()=>timing.Edit(0,1400,8100));
        Assert.Throws<InvalidDataException>(()=>timing.Edit(1,8000,15001));
        timing.Edit(0,1750,6200);
        Assert.Equal(1750,timing.Lines[0].StartMs);
        Assert.Equal(8000,timing.Lines[1].StartMs);
        timing.Undo();
        Assert.Equal(1500,timing.Lines[0].StartMs);
        Assert.True(timing.Complete);
    }

    [Fact]
    public void IncompleteDraftAndNonplainInputCannotBeSaved()
    {
        var timing=new ManualLyricTiming("One",4000);
        Assert.Throws<InvalidDataException>(()=>timing.Mark(double.NaN));
        Assert.Throws<InvalidDataException>(()=>timing.Mark(0,leaveGap:true));
        timing.Mark(1000);
        Assert.Throws<InvalidDataException>(()=>timing.MakeLrc());
        Assert.Throws<InvalidDataException>(()=>new ManualLyricTiming("[00:01]Previous timeline",4000));
        Assert.Throws<InvalidDataException>(()=>new ManualLyricTiming(" \n\r\n",4000));
    }

    [Theory]
    [InlineData("01:02.125",62125)]
    [InlineData("7.25",7250)]
    [InlineData("0:03",3000)]
    public void ManualTimeEntryRetainsMilliseconds(string text,int expected)
    {Assert.True(ManualLyricTiming.TryParseTime(text,out double result));Assert.Equal(expected,result);}
}
