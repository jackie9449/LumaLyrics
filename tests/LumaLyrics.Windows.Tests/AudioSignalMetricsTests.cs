namespace LumaLyrics.Windows.Tests;

public sealed class AudioSignalMetricsTests
{
    [Fact]
    public void OneLsbDitherDoesNotCountAsEffectiveMusicSignal()
    {
        var samples = Enumerable.Range(0, 1000).Select(i => i % 2 == 0 ? 1f / 32768 : -1f / 32768).ToArray();
        Assert.False(AudioSignalMetrics.Measure(samples).HasEffectiveSignal);
        Assert.False(AudioSignalMetrics.Measure(new float[1000]).HasEffectiveSignal);
    }

    [Fact]
    public void AMeaningfulSignalPassesButAnIsolatedQuietClickDoesNot()
    {
        var samples = Enumerable.Range(0, 1000).Select(i => (float)(.1 * Math.Sin(i * .1))).ToArray();
        Assert.True(AudioSignalMetrics.Measure(samples).HasEffectiveSignal);
        var click = new float[1000]; click[0] = .002f;
        Assert.False(AudioSignalMetrics.Measure(click).HasEffectiveSignal);
    }
}
