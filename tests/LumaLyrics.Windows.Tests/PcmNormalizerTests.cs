using System.Buffers.Binary;
using LumaLyrics.Windows;

namespace LumaLyrics.Windows.Tests;

public sealed class PcmNormalizerTests
{
    [Fact]
    public void SignedPcm16KeepsZeroAndExtremes()
    {
        var samples = PcmNormalizer.ToFloat([0, 128, 0, 0, 255, 127], 16, false);
        Assert.Equal(-1f, samples[0]);
        Assert.Equal(0f, samples[1]);
        Assert.InRange(samples[2], .9999f, 1f);
    }

    [Fact]
    public void PackedPcm24IsSignExtended()
    {
        var samples = PcmNormalizer.ToFloat([0, 0, 128, 0, 0, 0, 255, 255, 127], 24, false);
        Assert.Equal(-1f, samples[0]);
        Assert.Equal(0f, samples[1]);
        Assert.InRange(samples[2], .99999f, 1f);
    }

    [Fact]
    public void Pcm32AndExtensibleValidBitsAreNormalized()
    {
        var samples = PcmNormalizer.ToFloat([0, 0, 0, 128, 0, 0, 0, 64], 32, false);
        Assert.Equal(new[] { -1f, .5f }, samples);
        Assert.Equal(new[] { -1f, .5f }, PcmNormalizer.ToFloat([0, 0, 0, 128, 0, 0, 0, 64], 32, false, 24));
    }

    [Fact]
    public void FloatRequiresFiniteSamplesAndClipsOverload()
    {
        var bytes = new byte[12];
        BinaryPrimitives.WriteSingleLittleEndian(bytes, -.5f);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(4), 2f);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(8), -2f);
        Assert.Equal(new[] { -.5f, 1f, -1f }, PcmNormalizer.ToFloat(bytes, 32, true));
        BinaryPrimitives.WriteSingleLittleEndian(bytes, float.NaN);
        Assert.Throws<InvalidDataException>(() => PcmNormalizer.ToFloat(bytes, 32, true));
    }

    [Fact]
    public void RejectsUnsupportedAndTruncatedFormats()
    {
        Assert.Throws<NotSupportedException>(() => PcmNormalizer.ToFloat([0], 8, false));
        Assert.Throws<InvalidDataException>(() => PcmNormalizer.ToFloat([0], 16, false));
        Assert.Throws<NotSupportedException>(() => PcmNormalizer.ToFloat([0, 0], 16, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => PcmNormalizer.ToFloat([0, 0], 16, false, 24));
    }
}
