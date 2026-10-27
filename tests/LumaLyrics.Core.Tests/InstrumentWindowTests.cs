using System.Diagnostics;
using LumaLyrics.Core;

namespace LumaLyrics.Core.Tests;

public sealed class InstrumentWindowTests
{
    static long T(double ms) => (long)(ms * Stopwatch.Frequency / 1000);
    static readonly InstrumentEpoch A = new("AppleMusic", 1, 1, 0);

    [Fact]
    public void ArbitraryPacketsProduceTheSameOwnedOverlappingWindows()
    {
        float[] input = Enumerable.Range(0, 2000).Select(i => i / 2000f).ToArray();
        var whole = new InstrumentWindowBuilder(1000, 1000, 500).Push(input, T(0), A, 10000);
        var chunked = new InstrumentWindowBuilder(1000, 1000, 500);
        var first = chunked.Push(input[..600], T(0), A, 10000);
        var rest = chunked.Push(input[600..], T(600), A, 10600);
        Assert.Empty(first);
        Assert.Equal(3, rest.Count);
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(whole[i].Mono, rest[i].Mono);
            Assert.Equal(10000 + i * 500, rest[i].StartMs);
            Assert.Equal(11000 + i * 500, rest[i].EndMs);
        }
        input[0] = -1;
        Assert.Equal(0f, whole[0].Mono[0]);
    }

    [Fact]
    public void ANewRecordingOrSeekNeverCombinesOldAndNewSamples()
    {
        var builder = new InstrumentWindowBuilder(1000, 1000, 500);
        Assert.Empty(builder.Push(new float[600], T(0), A, 0));
        Assert.Empty(builder.Push(new float[600], T(600), A with { Revision = 2 }, 0));
        builder.Reset();
        Assert.Empty(builder.Push(new float[600], T(1200), A, 5000));
    }

    [Fact]
    public void ARealSampleGapResetsContextRatherThanBridgingCoverage()
    {
        var builder = new InstrumentWindowBuilder(1000, 1000, 500);
        Assert.Empty(builder.Push(new float[600], T(0), A, 0));
        Assert.Empty(builder.Push(new float[600], T(610), A, 610));
        var window = Assert.Single(builder.Push(new float[400], T(1210), A, 1210));
        Assert.Equal(610, window.StartMs);
    }

    [Fact]
    public void LosingTheTimelineClearsReusableMusicTimes()
    {
        var builder = new InstrumentWindowBuilder(1000, 1000, 500);
        builder.Push(new float[600], T(0), A, 10000);
        Assert.Empty(builder.Push(new float[600], T(600), A, null));
        var window = Assert.Single(builder.Push(new float[400], T(1200), A, null));
        Assert.Null(window.StartMs);
        Assert.Null(window.EndMs);
    }

    [Fact]
    public void InvalidAudioCannotPartiallyChangeTheBuffer()
    {
        var builder = new InstrumentWindowBuilder(1000, 1000, 500);
        builder.Push(new float[600], T(0), A, 0);
        Assert.Throws<ArgumentException>(() => builder.Push([float.NaN], T(600), A, 600));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Push(new float[10001], T(600), A, 600));
        Assert.Single(builder.Push(new float[400], T(600), A, 600));
    }
    [Fact]
    public void OfflinePaddingDoesNotExtendTheObservedTail()
    {
        var builder = new InstrumentWindowBuilder(1000, 1000, 500);
        builder.Push(Enumerable.Repeat(.5f, 1300).ToArray(), T(0), A, 0);
        var tail = builder.Finish()!;
        Assert.Equal(1000, tail.Mono.Length); Assert.Equal(1300, tail.EndMs); Assert.Equal(500, tail.StartMs);
        Assert.Equal(0f, tail.Mono[^1]); Assert.Null(builder.Finish());
        Assert.Throws<InvalidOperationException>(() => builder.Push([0f], T(1300), A, 1300));
    }
}
