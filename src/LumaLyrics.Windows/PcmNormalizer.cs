using System.Buffers.Binary;

namespace LumaLyrics.Windows;

public static class PcmNormalizer
{
    public static float[] ToFloat(byte[] bytes, int bitsPerSample, bool ieeeFloat, int validBitsPerSample = 0)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bitsPerSample is not (16 or 24 or 32) || ieeeFloat && bitsPerSample != 32)
            throw new NotSupportedException("Unsupported audio sample format.");
        int valid = validBitsPerSample == 0 ? bitsPerSample : validBitsPerSample;
        if (valid < 1 || valid > bitsPerSample || ieeeFloat && valid != 32)
            throw new ArgumentOutOfRangeException(nameof(validBitsPerSample));
        int width = bitsPerSample / 8;
        if (bytes.Length % width != 0) throw new InvalidDataException("Truncated PCM sample.");
        var samples = new float[bytes.Length / width];
        double scale = 1d / (1L << (valid - 1));
        for (int i = 0; i < samples.Length; i++)
        {
            var sample = bytes.AsSpan(i * width, width);
            if (ieeeFloat)
            {
                float value = BinaryPrimitives.ReadSingleLittleEndian(sample);
                if (!float.IsFinite(value)) throw new InvalidDataException("Non-finite audio sample.");
                samples[i] = Math.Clamp(value, -1, 1);
            }
            else
            {
                int value = bitsPerSample switch
                {
                    16 => BinaryPrimitives.ReadInt16LittleEndian(sample),
                    24 => (sample[0] | sample[1] << 8 | sample[2] << 16) << 8 >> 8,
                    _ => BinaryPrimitives.ReadInt32LittleEndian(sample)
                };
                samples[i] = (float)((value >> (bitsPerSample - valid)) * scale);
            }
        }
        return samples;
    }
}
