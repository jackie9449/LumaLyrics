using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using LumaLyrics.Core;

namespace LumaLyrics.Windows;

/// <summary>Isolates uninterruptible driver calls. Only the owned capture helper may be terminated.</summary>
public sealed class ProcessAudioCaptureBroker : IProcessAudioCapture
{
    readonly string host, probeDll;
    readonly object gate = new();
    readonly SemaphoreSlim stopping = new(1);
    readonly CancellationTokenSource lifetime = new();
    Process? child;
    bool disposed, started, lifetimeDisposed;
    public bool ForcedTermination { get; private set; }
    public string HelperDiagnostic { get; private set; } = "";

    public ProcessAudioCaptureBroker(string dotnetHost, string probeDll)
    {
        host = Path.GetFullPath(dotnetHost); this.probeDll = Path.GetFullPath(probeDll);
        if (!File.Exists(host) || !Path.GetFileName(host).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(this.probeDll) || Path.GetFileName(this.probeDll) != "LumaLyrics.Probe.dll")
            throw new ArgumentException("A known Microsoft host and packaged capture helper are required.");
    }

    public async IAsyncEnumerable<PcmChunk> ReadAsync(AudioTarget target, [EnumeratorCancellation] CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        CancellationTokenSource linked;
        Process process;
        Stream output;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (started) throw new InvalidOperationException("A broker can only be read once.");
            if (!AppleMusicAudioTargetResolver.IsStillValid(target)) throw new InvalidOperationException("Unverified Apple Music renderer.");
            var start = new ProcessStartInfo(host)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(this.probeDll)!
            };
            foreach (var argument in new[] { this.probeDll, "--stream-instrument-audio", "--target-pid", target.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--target-start", target.StartTimeUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture), "--target-family", target.PackageFamily,
                "--target-source", target.SourceAppId! }) start.ArgumentList.Add(argument);
            process = new() { StartInfo = start };
            linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, token);
            try { if (!process.Start()) throw new IOException("Could not start the audio helper."); }
            catch { process.Dispose(); linked.Dispose(); throw; }
            output = process.StandardOutput.BaseStream;
            child = process; started = true;
        }
        // Drain trusted helper diagnostics without retaining private metadata or allowing pipe backpressure.
        var errors = DrainErrorsAsync(process.StandardError);
        try
        {
            var hello = new byte[8];
            using (var startup = CancellationTokenSource.CreateLinkedTokenSource(linked.Token))
            {
                startup.CancelAfter(TimeSpan.FromSeconds(7));
                await ReadPipeAsync(output, hello, startup.Token);
            }
            if (BinaryPrimitives.ReadUInt32LittleEndian(hello) != InstrumentAudioProtocol.Magic
                || BinaryPrimitives.ReadInt32LittleEndian(hello.AsSpan(4)) != InstrumentAudioProtocol.Version)
                throw new InvalidDataException("Invalid audio helper handshake.");
            var header = new byte[InstrumentAudioProtocol.HeaderBytes];
            while (true)
            {
                linked.Token.ThrowIfCancellationRequested();
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
                idle.CancelAfter(TimeSpan.FromSeconds(15));
                await ReadPipeAsync(output, header, idle.Token);
                var frame = InstrumentAudioProtocol.Validate(header);
                var bytes = new byte[checked(frame.Samples * sizeof(float))];
                await ReadPipeAsync(output, bytes, idle.Token);
                var samples = PcmNormalizer.ToFloat(bytes, 32, true);
                linked.Token.ThrowIfCancellationRequested();
                yield return new(samples, 44100, 2, frame.Ticks, (frame.Flags & 1) != 0, (frame.Flags & 2) != 0);
            }
        }
        finally
        {
            linked.Cancel();
            await StopAsync();
            await errors;
            linked.Dispose();
        }
    }

    static async ValueTask ReadPipeAsync(Stream stream, Memory<byte> buffer, CancellationToken token)
    {
        try { await stream.ReadExactlyAsync(buffer, token); }
        catch (Exception error) when (token.IsCancellationRequested && error is IOException or ObjectDisposedException)
        { throw new OperationCanceledException(token); }
    }

    async Task DrainErrorsAsync(StreamReader reader)
    {
        var retained = new System.Text.StringBuilder();
        try
        {
            var buffer = new char[1024];
            int count;
            while ((count = await reader.ReadAsync(buffer)) != 0)
            {
                retained.Append(buffer, 0, count);
                if (retained.Length > 4096) retained.Remove(0, retained.Length - 4096);
            }
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException) { }
        finally { HelperDiagnostic = retained.ToString(); }
    }

    async Task StopAsync()
    {
        await stopping.WaitAsync();
        try
        {
            Process? process;
            lock (gate) process = child;
            if (process is null) return;
            try
            {
                if (!process.HasExited)
                {
                    using var request = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
                    try { await process.StandardInput.WriteLineAsync("stop".AsMemory(), request.Token); await process.StandardInput.FlushAsync(request.Token); }
                    catch (Exception error) when (error is IOException or OperationCanceledException) { }
                    process.StandardInput.Close();
                    using var grace = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    try { await process.WaitForExitAsync(grace.Token); }
                    catch (OperationCanceledException)
                    {
                        ForcedTermination = true;
                        // This process was created here; Apple Music is never a child of it.
                        try { process.Kill(true); }
                        catch (InvalidOperationException) when (process.HasExited) { }
                        using var killed = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                        await process.WaitForExitAsync(killed.Token);
                    }
                }
            }
            finally
            {
                lock (gate) child = null;
                process.Dispose();
            }
        }
        finally { stopping.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        lock (gate) { if (!disposed) { disposed = true; lifetime.Cancel(); } }
        await StopAsync();
        lock (gate) { if (!lifetimeDisposed) { lifetimeDisposed = true; lifetime.Dispose(); } }
    }
}
