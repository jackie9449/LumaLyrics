using System.Text;
using LumaLyrics.Windows;

namespace LumaLyrics.Windows.Tests;

public sealed class WaveAudioReaderTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "Luma-wav-reader-" + Guid.NewGuid().ToString("N"));
    public WaveAudioReaderTests() => Directory.CreateDirectory(root);
    string WriteWave(bool floating = false, bool junk = false, int channels = 1, int rate = 16000)
    {
        string path = Path.Combine(root, Guid.NewGuid().ToString("N") + ".wav");
        using var stream = File.Create(path); using var writer = new BinaryWriter(stream, Encoding.ASCII);
        writer.Write("RIFF"u8); writer.Write(0); writer.Write("WAVE"u8);
        if (junk) { writer.Write("JUNK"u8); writer.Write(3); writer.Write(new byte[] { 1, 2, 3, 0 }); }
        writer.Write("fmt "u8); writer.Write(16); writer.Write((short)(floating ? 3 : 1)); writer.Write((short)channels);
        writer.Write(rate); writer.Write(rate * channels * (floating ? 4 : 2)); writer.Write((short)(channels * (floating ? 4 : 2))); writer.Write((short)(floating ? 32 : 16));
        writer.Write("data"u8); writer.Write(floating ? 8 : 4);
        if (floating) { writer.Write(0f); writer.Write(.5f); } else { writer.Write((short)0); writer.Write((short)16384); }
        writer.Flush(); stream.Position = 4; writer.Write(checked((int)stream.Length - 8)); return path;
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)]
    public async Task ReadsRealChunkLayoutRatherThanAssumingAFortyFourByteHeader(bool floating, bool junk)
    {
        string path = WriteWave(floating, junk);
        var chunks = new List<float>();
        await foreach (var chunk in WaveAudioReader.ReadAsync(path, default)) chunks.AddRange(chunk.Samples);
        Assert.Equal(new[] { 0f, .5f }, chunks);
        var info = WaveAudioReader.Inspect(path); Assert.Equal(2, info.SampleFrames); Assert.Equal(16000, info.SampleRate);
    }

    [Fact]
    public void RejectsTruncatedAndOversizedDataBeforeYieldingAudio()
    {
        string path = WriteWave();
        using (var file = new FileStream(path, FileMode.Open, FileAccess.Write)) file.SetLength(file.Length - 1);
        Assert.Throws<InvalidDataException>(() => WaveAudioReader.Inspect(path));
        path = WriteWave();
        using (var file = new FileStream(path, FileMode.Open, FileAccess.Write)) { file.Position = 40; using var writer = new BinaryWriter(file); writer.Write(uint.MaxValue); }
        Assert.Throws<InvalidDataException>(() => WaveAudioReader.Inspect(path));
    }

    [Fact]
    public async Task RejectsNonfiniteFloatAndUnsupportedChannelOrEncodingMetadata()
    {
        string path = WriteWave(true);
        using (var file = new FileStream(path, FileMode.Open, FileAccess.Write)) { file.Position = 44; using var writer = new BinaryWriter(file); writer.Write(float.NaN); }
        await Assert.ThrowsAsync<InvalidDataException>(async () => { await foreach (var _ in WaveAudioReader.ReadAsync(path, default)) { } });
        Assert.Throws<InvalidDataException>(() => WaveAudioReader.Inspect(WriteWave(channels: 0)));
        path = WriteWave();
        using (var file = new FileStream(path, FileMode.Open, FileAccess.Write)) { file.Position = 20; using var writer = new BinaryWriter(file); writer.Write((short)6); }
        Assert.Throws<InvalidDataException>(() => WaveAudioReader.Inspect(path));
    }

    [Fact]
    public async Task AlreadyCancelledReadNeverOpensTheSource()
    {
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        { await foreach (var _ in WaveAudioReader.ReadAsync(Path.Combine(root, "missing.wav"), cancel.Token)) { } });
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
