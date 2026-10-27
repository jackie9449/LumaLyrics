using LumaLyrics.Core;

namespace LumaLyrics.Core.Tests;

public sealed class InstrumentEpochTests
{
    [Fact]
    public void SeekAndRevisitedRecordingInvalidateEarlierEpochs()
    {
        var guard = new InstrumentEpochGuard();
        var first = new InstrumentEpoch("AppleMusic", 1, 1, 0);
        Assert.False(guard.IsCurrent(first));
        guard.Set(first);
        Assert.True(guard.IsCurrent(first));
        guard.Set(first with { Discontinuity = 1 });
        Assert.False(guard.IsCurrent(first));
        guard.Set(first with { Revision = 3 });
        Assert.False(guard.IsCurrent(first));
        guard.Clear();
        Assert.False(guard.IsCurrent(first with { Revision = 3 }));
    }
}
