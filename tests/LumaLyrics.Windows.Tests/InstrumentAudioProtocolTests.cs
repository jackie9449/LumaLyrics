using System.Buffers.Binary;
using LumaLyrics.Core;

namespace LumaLyrics.Windows.Tests;

public sealed class InstrumentAudioProtocolTests
{
    [Fact]
    public void ProtocolPreservesOwnedFloatFrameMetadata()
    {
        var chunk = new PcmChunk(new float[882], 44100, 2, 12345, true, false);
        var value = InstrumentAudioProtocol.Validate(InstrumentAudioProtocol.Header(chunk));
        Assert.Equal(882, value.Samples); Assert.Equal(12345, value.Ticks); Assert.Equal(1, value.Flags);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(8822)]
    public void InvalidSizesCannotAllocateAudioBuffers(int count)
    {
        var header = InstrumentAudioProtocol.Header(new(new float[882], 44100, 2, 0, false, false));
        BinaryPrimitives.WriteInt32LittleEndian(header, count);
        Assert.Throws<InvalidDataException>(() => InstrumentAudioProtocol.Validate(header));
    }
}
