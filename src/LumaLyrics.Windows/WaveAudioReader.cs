using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using LumaLyrics.Core;

namespace LumaLyrics.Windows;

public sealed record WaveAudioInfo(int SampleRate, int Channels, int BitsPerSample, bool FloatingPoint,
    long SampleFrames, long DataOffset, long DataBytes)
{
    public double DurationMs => SampleFrames * (1000d / SampleRate);
}

/// <summary>Strict RIFF PCM16/24 and IEEE float32 reader; audio stays in bounded read-only chunks.</summary>
public static class WaveAudioReader
{
    public static WaveAudioInfo Inspect(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Inspect(stream);
    }
    static WaveAudioInfo Inspect(FileStream stream)
    {
        try
        {
            Span<byte> header = stackalloc byte[12]; stream.ReadExactly(header);
            if (!header[..4].SequenceEqual("RIFF"u8) || !header[8..].SequenceEqual("WAVE"u8))
                throw new InvalidDataException("Only RIFF WAV files are supported.");
            long end = 8L + BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
            if (end < 12 || end > stream.Length) throw new InvalidDataException("Truncated RIFF content.");
            int rate = 0, channels = 0, bits = 0, blockAlign = 0; bool floating = false, foundFormat = false, foundData = false;
            long offset = 0, dataBytes = 0; int chunks = 0;
            Span<byte> chunk = stackalloc byte[8]; Span<byte> format = stackalloc byte[40];
            while (stream.Position < end)
            {
                if (++chunks > 100000 || end - stream.Position < 8) throw new InvalidDataException("Invalid RIFF chunk layout.");
                stream.ReadExactly(chunk);
                uint size = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);
                long payload = stream.Position, next = checked(payload + size + (size & 1));
                // Some PCM24 writers omit the final data padding byte. Accept only a
                // complete terminal payload; frame alignment is still checked below.
                if (next == end + 1 && payload + size == end && chunk[..4].SequenceEqual("data"u8)) next = end;
                if (next > end) throw new InvalidDataException("WAV chunk exceeds the RIFF boundary.");
                if (chunk[..4].SequenceEqual("fmt "u8))
                {
                    if (foundFormat || size < 16) throw new InvalidDataException("Invalid or repeated WAV format.");
                    format.Clear(); stream.ReadExactly(format[..(int)Math.Min(40, size)]);
                    ushort tag = BinaryPrimitives.ReadUInt16LittleEndian(format);
                    channels = BinaryPrimitives.ReadUInt16LittleEndian(format[2..]);
                    uint sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(format[4..]);
                    uint byteRate = BinaryPrimitives.ReadUInt32LittleEndian(format[8..]);
                    blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(format[12..]); bits = BinaryPrimitives.ReadUInt16LittleEndian(format[14..]);
                    if (tag == 0xfffe)
                    {
                        if (size < 40 || BinaryPrimitives.ReadUInt16LittleEndian(format[16..]) < 22
                            || BinaryPrimitives.ReadUInt16LittleEndian(format[18..]) != bits)
                            throw new InvalidDataException("Unsupported extensible WAV format.");
                        var subformat = new Guid(format[24..]);
                        tag = subformat == new Guid("00000001-0000-0010-8000-00aa00389b71") ? (ushort)1
                            : subformat == new Guid("00000003-0000-0010-8000-00aa00389b71") ? (ushort)3 : (ushort)0;
                    }
                    floating = tag == 3;
                    if (channels is < 1 or > 8 || sampleRate is < 8000 or > 192000
                        || !(tag == 1 && bits is 16 or 24 || tag == 3 && bits == 32)
                        || blockAlign != channels * (bits / 8) || byteRate != sampleRate * blockAlign)
                        throw new InvalidDataException("WAV requires 1–8 channels, 8–192 kHz PCM16/24 or IEEE float32.");
                    rate = (int)sampleRate; foundFormat = true;
                }
                else if (chunk[..4].SequenceEqual("data"u8))
                {
                    if (foundData) throw new InvalidDataException("Multiple WAV data chunks are not supported.");
                    offset = payload; dataBytes = size; foundData = true;
                }
                stream.Position = next;
            }
            if (!foundFormat || !foundData || dataBytes == 0 || dataBytes % blockAlign != 0)
                throw new InvalidDataException("Missing, empty or misaligned WAV audio.");
            return new(rate, channels, bits, floating, dataBytes / blockAlign, offset, dataBytes);
        }
        catch (EndOfStreamException error) { throw new InvalidDataException("Truncated WAV header.", error); }
    }

    public static async IAsyncEnumerable<PcmChunk> ReadAsync(string path, [EnumeratorCancellation] CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        var info = Inspect(stream); stream.Position = info.DataOffset;
        int blockAlign = info.Channels * (info.BitsPerSample / 8);
        var buffer = new byte[Math.Max(1, info.SampleRate / 10) * blockAlign];
        long frames = 0, remaining = info.DataBytes;
        while (remaining > 0)
        {
            token.ThrowIfCancellationRequested();
            int bytes = (int)Math.Min(buffer.Length, remaining);
            try { await stream.ReadExactlyAsync(buffer.AsMemory(0, bytes), token); }
            catch (EndOfStreamException error) { throw new InvalidDataException("WAV audio was truncated during reading.", error); }
            var samples = PcmNormalizer.ToFloat(bytes == buffer.Length ? buffer : buffer[..bytes], info.BitsPerSample, info.FloatingPoint);
            token.ThrowIfCancellationRequested();
            yield return new(samples, info.SampleRate, info.Channels, checked((long)(frames * (Stopwatch.Frequency / (double)info.SampleRate))), false, samples.All(value => value == 0));
            frames += bytes / blockAlign; remaining -= bytes;
        }
    }
}
