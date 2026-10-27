namespace LumaLyrics.Windows;

/// <summary>Signal strength only: neither a music classifier nor proof of unprotected content.</summary>
public readonly record struct AudioSignalMetrics(double Rms, float Peak)
{
    // PCM16 output can contain one-LSB dither even when no music is playing.
    public bool HasEffectiveSignal => Rms >= .0005 && Peak >= .002f;

    public static AudioSignalMetrics Measure(ReadOnlySpan<float> samples)
    {
        double energy = 0;
        float peak = 0;
        foreach (float sample in samples)
        {
            if (!float.IsFinite(sample)) throw new InvalidDataException("Non-finite audio sample.");
            energy += (double)sample * sample;
            peak = Math.Max(peak, Math.Abs(sample));
        }
        return new(samples.Length > 0 ? Math.Sqrt(energy / samples.Length) : 0, peak);
    }
}
