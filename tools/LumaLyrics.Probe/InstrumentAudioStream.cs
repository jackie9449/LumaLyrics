using System.Buffers.Binary;
using LumaLyrics.Core;
using LumaLyrics.Windows;

namespace LumaLyrics.Probe;

internal static class InstrumentAudioStream
{
    internal static async Task RunAsync(string[] args)
    {
        string Value(string flag)
        {
            int index = Array.IndexOf(args, flag);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : throw new ArgumentException("Incomplete audio helper arguments.");
        }
        using var cancel = new CancellationTokenSource();
        var target = new AudioTarget(uint.Parse(Value("--target-pid"), System.Globalization.CultureInfo.InvariantCulture),
            new DateTime(long.Parse(Value("--target-start"), System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc),
            Value("--target-family"), Value("--target-source"));
        _ = Task.Factory.StartNew(() => { try { Console.In.ReadLine(); } finally { cancel.Cancel(); } }, CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
        await using var capture = new ProcessAudioCapture(stage => Console.Error.WriteLine("capture-stage:" + stage));
        await using var output = Console.OpenStandardOutput();
        bool sentHello = false;
        try
        {
            await foreach (var chunk in capture.ReadAsync(target, cancel.Token))
            {
                if (!sentHello)
                {
                    var hello = new byte[8];
                    BinaryPrimitives.WriteUInt32LittleEndian(hello, InstrumentAudioProtocol.Magic);
                    BinaryPrimitives.WriteInt32LittleEndian(hello.AsSpan(4), InstrumentAudioProtocol.Version);
                    await output.WriteAsync(hello, cancel.Token); sentHello = true;
                }
                var header = InstrumentAudioProtocol.Header(chunk);
                var bytes = new byte[checked(chunk.Samples.Length * sizeof(float))];
                Buffer.BlockCopy(chunk.Samples, 0, bytes, 0, bytes.Length);
                await output.WriteAsync(header, cancel.Token);
                await output.WriteAsync(bytes, cancel.Token);
                await output.FlushAsync(cancel.Token);
            }
        }
        catch (Exception error) when (error is OperationCanceledException or IOException) { }
        catch (Exception error) { await Console.Error.WriteLineAsync($"Audio helper failed: {error.GetType().Name} 0x{error.HResult:X8}"); }
    }
}
