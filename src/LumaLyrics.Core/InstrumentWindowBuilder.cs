using System.Diagnostics;

namespace LumaLyrics.Core;

/// <summary>Single-owner sample windows. Gaps, clock loss and epoch changes discard context.</summary>
public sealed class InstrumentWindowBuilder
{
    readonly int sampleRate, windowSamples, hopSamples;
    readonly List<float> buffer = [];
    InstrumentEpoch? epoch;
    long originTicks, receivedSamples, consumedSamples;
    double? originMs;

    public InstrumentWindowBuilder(int sampleRate, int windowSamples, int hopSamples)
    {
        if (sampleRate is < 1 or > 192000 || windowSamples < 1 || windowSamples > sampleRate * 30
            || hopSamples < 1 || hopSamples > windowSamples) throw new ArgumentOutOfRangeException(nameof(windowSamples));
        this.sampleRate = sampleRate; this.windowSamples = windowSamples; this.hopSamples = hopSamples;
    }

    public IReadOnlyList<InstrumentWindow> Push(float[] mono, long firstSampleTicks, InstrumentEpoch value, double? firstSampleMs)
    {
        ArgumentNullException.ThrowIfNull(mono);
        ArgumentNullException.ThrowIfNull(value);
        if (mono.Length > sampleRate * 10) throw new ArgumentOutOfRangeException(nameof(mono), "Audio chunk exceeds ten seconds.");
        if (firstSampleTicks < 0 || firstSampleMs.HasValue && (!double.IsFinite(firstSampleMs.Value) || firstSampleMs < 0))
            throw new ArgumentOutOfRangeException(nameof(firstSampleMs));
        foreach (float sample in mono)
            if (!float.IsFinite(sample) || sample is < -1 or > 1) throw new ArgumentException("Invalid normalized sample.", nameof(mono));
        if (mono.Length == 0) return [];
        double expectedTicks = originTicks + receivedSamples * (Stopwatch.Frequency / (double)sampleRate);
        double expectedMs = (originMs ?? 0) + receivedSamples * (1000d / sampleRate);
        bool gap = epoch is not null && Math.Abs(firstSampleTicks - expectedTicks) > Stopwatch.Frequency / (double)sampleRate + 1;
        bool changedClock = originMs.HasValue != firstSampleMs.HasValue
            || firstSampleMs.HasValue && Math.Abs(firstSampleMs.Value - expectedMs) > 50;
        if (epoch != value || gap || changedClock)
        {
            Reset(); epoch = value; originTicks = firstSampleTicks; originMs = firstSampleMs;
        }
        buffer.AddRange(mono);
        receivedSamples = checked(receivedSamples + mono.Length);
        var windows = new List<InstrumentWindow>();
        while (buffer.Count >= windowSamples)
        {
            var samples = new float[windowSamples]; buffer.CopyTo(0, samples, 0, samples.Length);
            long start = originTicks + checked((long)(consumedSamples * (Stopwatch.Frequency / (double)sampleRate)));
            long end = originTicks + checked((long)((consumedSamples + windowSamples) * (Stopwatch.Frequency / (double)sampleRate)));
            windows.Add(new(value, start, end,
                originMs.HasValue ? originMs + consumedSamples * (1000d / sampleRate) : null,
                originMs.HasValue ? originMs + (consumedSamples + windowSamples) * (1000d / sampleRate) : null,
                sampleRate, samples));
            buffer.RemoveRange(0, hopSamples);
            consumedSamples = checked(consumedSamples + hopSamples);
        }
        return windows;
    }

    public void Reset()
    {
        buffer.Clear(); epoch = null; originMs = null; originTicks = receivedSamples = consumedSamples = 0;
    }
}
