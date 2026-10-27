using LumaLyrics.Core;

namespace LumaLyrics.Core.Tests;

public sealed class InstrumentActivityTests
{
    [Fact]
    public void RequiresConsecutiveAttackAndRetainsActivityThroughBriefGaps()
    {
        var filter = new InstrumentActivityFilter(new(.65f, .40f, 2, 750));
        Assert.False(filter.Update(.8f, 0));
        Assert.True(filter.Update(.8f, 480));
        Assert.True(filter.Update(.2f, 960));
        Assert.False(filter.Update(.2f, 1440));
        filter.Reset();
        Assert.False(filter.Update(.9f, 2000));
    }

    [Fact]
    public void MidrangeBreaksAttackButMaintainsAnAlreadyActiveInstrument()
    {
        var filter = new InstrumentActivityFilter(new(.65f, .40f, 2, 750));
        Assert.False(filter.Update(.9f, 0));
        Assert.False(filter.Update(.5f, 480));
        Assert.False(filter.Update(.9f, 960));
        Assert.True(filter.Update(.9f, 1440));
        Assert.True(filter.Update(.5f, 3000));
        Assert.True(filter.Update(.1f, 3400));
        Assert.False(filter.Update(.1f, 3750));
    }

    [Fact]
    public void InstrumentsDoNotExcludeEachOther()
    {
        var piano = new InstrumentActivityFilter(new(.65f, .4f, 1, 750));
        var drums = new InstrumentActivityFilter(new(.65f, .4f, 1, 750));
        Assert.True(piano.Update(.9f, 0));
        Assert.True(drums.Update(.9f, 0));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(-.1f)]
    [InlineData(1.1f)]
    public void RejectsInvalidScoresWithoutChangingAttack(float score)
    {
        var filter = new InstrumentActivityFilter(new(.65f, .4f, 2, 750));
        Assert.False(filter.Update(.9f, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => filter.Update(score, 480));
        Assert.True(filter.Update(.9f, 480));
    }

    [Fact]
    public void DuplicateAndBackwardsTimesCannotCreateAttackEvidence()
    {
        var filter = new InstrumentActivityFilter(new(.65f, .4f, 2, 750));
        filter.Update(.9f, 480);
        Assert.Throws<ArgumentOutOfRangeException>(() => filter.Update(.9f, 480));
        Assert.Throws<ArgumentOutOfRangeException>(() => filter.Update(.9f, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => filter.Update(.9f, double.NaN));
    }

    [Fact]
    public void RejectsInvalidParameters()
    {
        Assert.Throws<ArgumentException>(() => new InstrumentActivityFilter(new(.4f, .65f, 2, 750)));
        Assert.Throws<ArgumentException>(() => new InstrumentActivityFilter(new(.6f, .6f, 2, 750)));
        Assert.Throws<ArgumentException>(() => new InstrumentActivityFilter(new(.65f, .4f, 0, 750)));
        Assert.Throws<ArgumentException>(() => new InstrumentActivityFilter(new(.65f, .4f, 2, -1)));
        Assert.Throws<ArgumentException>(() => new InstrumentActivityFilter(new(float.NaN, .4f, 2, 750)));
    }
}
