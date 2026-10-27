using LumaLyrics.Core;

namespace LumaLyrics.Core.Tests;

public sealed class InstrumentCoverageTests
{
    [Fact]
    public void ReplayingAnObservedSectionDoesNotFillASeekGap()
    {
        var coverage = new InstrumentCoverage();
        coverage.Add(0, 10000);
        coverage.Add(20000, 30000);
        coverage.Add(0, 10000);
        Assert.False(coverage.IsComplete(30000));
        Assert.Equal(2, coverage.Ranges.Count);
        coverage.Add(10000, 20000);
        Assert.True(coverage.IsComplete(30000));
        Assert.Equal(new InstrumentSpan(0, 30000), Assert.Single(coverage.Ranges));
    }

    [Fact]
    public void BoundaryToleranceNeverClosesAnInternalGap()
    {
        var coverage = new InstrumentCoverage();
        coverage.Add(40, 5000);
        coverage.Add(5001, 9960);
        Assert.False(coverage.IsComplete(10000));
        coverage.Add(5000, 5001);
        Assert.True(coverage.IsComplete(10000));
        Assert.False(coverage.IsComplete(10000, 0));
    }

    [Fact]
    public void SortsAndMergesNestedAndAdjacentRanges()
    {
        var coverage = new InstrumentCoverage();
        coverage.Add(8000, 10000);
        coverage.Add(2000, 6000);
        coverage.Add(3000, 4000);
        coverage.Add(6000, 8000);
        Assert.Equal(new InstrumentSpan(2000, 10000), Assert.Single(coverage.Ranges));
        Assert.False(coverage.IsComplete(10000));
    }

    [Fact]
    public void EmptyAndInvalidInputsCannotBecomeComplete()
    {
        var coverage = new InstrumentCoverage();
        Assert.False(coverage.IsComplete(30000));
        Assert.Throws<ArgumentOutOfRangeException>(() => coverage.Add(-1, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => coverage.Add(10, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => coverage.Add(20, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => coverage.Add(0, double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => coverage.IsComplete(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => coverage.IsComplete(1000, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => coverage.IsComplete(1000, 1000));
    }
}
