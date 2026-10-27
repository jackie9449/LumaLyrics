namespace LumaLyrics.Core;

/// <summary>Phase-continuous band-limited mono conversion. Reset across every discontinuity.</summary>
public sealed class InstrumentResampler
{
    public const string Version = "mono-blackman-sinc-24-r094-v1";
    readonly int sourceRate, targetRate, divisor, radius;
    readonly double[][] kernels;
    readonly List<float> buffer = [];
    long received, bufferStart, nextOutput;
    bool finished;

    public InstrumentResampler(int sourceRate, int targetRate)
    {
        if (sourceRate is < 8000 or > 192000 || targetRate is < 8000 or > 192000) throw new ArgumentOutOfRangeException(nameof(sourceRate));
        this.sourceRate = sourceRate; this.targetRate = targetRate;
        int a = sourceRate, b = targetRate; while (b != 0) (a, b) = (b, a % b); divisor = a;
        int phases = targetRate / divisor;
        if (phases > 2048) throw new NotSupportedException("This unusual sample-rate ratio exceeds the bounded resampler profile.");
        if (sourceRate == targetRate) { kernels = []; return; }
        double ratio = Math.Min(1, targetRate / (double)sourceRate), cutoff = ratio * .94;
        radius = (int)Math.Ceiling(24 / ratio); kernels = new double[phases][];
        for (int phase = 0; phase < phases; phase++)
        {
            double fraction = phase / (double)phases, sum = 0;
            var kernel = new double[radius * 2];
            for (int tap = 0; tap < kernel.Length; tap++)
            {
                double distance = tap - radius + 1 - fraction;
                double argument = Math.PI * cutoff * distance;
                double sinc = Math.Abs(argument) < 1e-12 ? 1 : Math.Sin(argument) / argument;
                double window = Math.Abs(distance) > radius ? 0 : .42 + .5 * Math.Cos(Math.PI * distance / radius) + .08 * Math.Cos(2 * Math.PI * distance / radius);
                kernel[tap] = cutoff * sinc * window; sum += kernel[tap];
            }
            for (int tap = 0; tap < kernel.Length; tap++) kernel[tap] /= sum;
            kernels[phase] = kernel;
        }
    }

    public float[] Push(float[] mono)
    {
        if (finished) throw new InvalidOperationException("The resampler has finished.");
        if (mono.Length > sourceRate * 10) throw new ArgumentOutOfRangeException(nameof(mono));
        foreach (float sample in mono) if (!float.IsFinite(sample) || sample is < -1 or > 1) throw new ArgumentException("Invalid normalized source samples.", nameof(mono));
        if (sourceRate == targetRate) { received = checked(received + mono.Length); return mono.ToArray(); }
        buffer.AddRange(mono); received = checked(received + mono.Length); return Drain(false);
    }
    public float[] Finish()
    {
        if (finished) return [];
        finished = true; return sourceRate == targetRate ? [] : Drain(true);
    }
    float[] Drain(bool final)
    {
        var result = new List<float>();
        long finalCount = checked(received * targetRate) / sourceRate;
        while (nextOutput < finalCount)
        {
            long position = checked(nextOutput * sourceRate), center = position / targetRate;
            if (!final && center + radius >= received) break;
            int phase = (int)(position % targetRate) / divisor;
            var kernel = kernels[phase]; double value = 0;
            for (int tap = 0; tap < kernel.Length; tap++)
            {
                long index = center - radius + 1 + tap;
                if (index >= 0 && index < received) value += buffer[checked((int)(index - bufferStart))] * kernel[tap];
            }
            result.Add((float)Math.Clamp(value, -1, 1)); nextOutput++;
        }
        long keep = Math.Max(0, checked(nextOutput * sourceRate) / targetRate - radius + 1);
        int remove = (int)Math.Min(buffer.Count, Math.Max(0, keep - bufferStart));
        if (remove > 0) { buffer.RemoveRange(0, remove); bufferStart += remove; }
        return result.ToArray();
    }
    public void Reset() { buffer.Clear(); received = bufferStart = nextOutput = 0; finished = false; }
}
