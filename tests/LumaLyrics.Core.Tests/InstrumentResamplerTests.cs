using LumaLyrics.Core;

namespace LumaLyrics.Core.Tests;

public sealed class InstrumentResamplerTests
{
    static float[] Tone(int rate, int frequency, int count) => Enumerable.Range(0, count)
        .Select(index => (float)(.5 * Math.Sin(2 * Math.PI * frequency * index / rate))).ToArray();
    static double Rms(float[] samples) => Math.Sqrt(samples.Skip(300).Take(samples.Length - 600).Average(value => value * value));
    [Fact]
    public void ArbitraryPacketsDoNotChangePhaseOrSampleCount()
    {
        var input = Tone(44100, 1000, 44100);
        var whole = new InstrumentResampler(44100, 16000);
        var expected = whole.Push(input).Concat(whole.Finish()).ToArray();
        var split = new InstrumentResampler(44100, 16000); var output = new List<float>();
        for (int index = 0; index < input.Length; index += 137) output.AddRange(split.Push(input[index..Math.Min(index + 137, input.Length)]));
        output.AddRange(split.Finish());
        Assert.Equal(16000, output.Count); Assert.Equal(expected, output.ToArray());
    }
    [Fact]
    public void DownsamplingRejectsHighFrequencyAliasingAndRetainsThePassband()
    {
        float[] Convert(int frequency)
        {
            var resampler = new InstrumentResampler(48000, 16000);
            return resampler.Push(Tone(48000, frequency, 48000)).Concat(resampler.Finish()).ToArray();
        }
        Assert.InRange(Rms(Convert(1000)), .34, .36);
        Assert.True(Rms(Convert(12000)) < .003);
    }
    [Fact]
    public void ResetAndInvalidSamplesCannotBridgeOldContext()
    {
        var resampler = new InstrumentResampler(44100, 16000);
        resampler.Push(Tone(44100, 1000, 4410)); resampler.Reset();
        Assert.Throws<ArgumentException>(() => resampler.Push([float.NaN]));
        var silence = resampler.Push(new float[4410]).Concat(resampler.Finish()).ToArray();
        Assert.Equal(1600, silence.Length); Assert.All(silence, value => Assert.Equal(0f, value));
        Assert.Throws<InvalidOperationException>(() => resampler.Push([0f]));
    }
}
