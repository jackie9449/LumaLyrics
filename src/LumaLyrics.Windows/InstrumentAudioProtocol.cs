using System.Buffers.Binary;
using LumaLyrics.Core;

namespace LumaLyrics.Windows;

internal static class InstrumentAudioProtocol
{
    internal const uint Magic = 0x414D554C; // LUMA, little endian
    internal const int Version = 1, HeaderBytes = 24;

    internal static byte[] Header(PcmChunk chunk)
    {
        var header = new byte[HeaderBytes];
        BinaryPrimitives.WriteInt32LittleEndian(header, chunk.Samples.Length);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), chunk.SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), chunk.Channels);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(12), chunk.FirstSampleTicks);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(20), (chunk.Discontinuous ? 1 : 0) | (chunk.Silent ? 2 : 0));
        Validate(header);
        return header;
    }

    internal static (int Samples, long Ticks, int Flags) Validate(byte[] header)
    {
        if (header.Length != HeaderBytes) throw new InvalidDataException("Invalid audio header length.");
        int count = BinaryPrimitives.ReadInt32LittleEndian(header);
        int rate = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
        int channels = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(8));
        long ticks = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(12));
        int flags = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(20));
        if (count is <= 0 or > 8820 || count % 2 != 0 || rate != 44100 || channels != 2 || ticks < 0 || (flags & ~3) != 0)
            throw new InvalidDataException("Invalid audio stream frame.");
        return (count, ticks, flags);
    }
}
