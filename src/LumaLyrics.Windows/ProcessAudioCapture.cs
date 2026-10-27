using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using LumaLyrics.Core;

namespace LumaLyrics.Windows;

/// <summary>Single-use process loopback. Audio never leaves bounded, owned memory buffers.</summary>
public sealed class ProcessAudioCapture : IProcessAudioCapture
{
    readonly object gate = new();
    readonly CancellationTokenSource lifetime = new();
    readonly Action<string>? diagnostic;
    Task? worker;
    bool disposed, lifetimeDisposed;
    long packets, droppedChunks;
    int droppedSinceRead;
    public static bool IsSupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348) && IntPtr.Size == 8;
    public long Packets => Interlocked.Read(ref packets);
    public long DroppedChunks => Interlocked.Read(ref droppedChunks);

    public ProcessAudioCapture() : this(null) { }
    internal ProcessAudioCapture(Action<string>? diagnostic) => this.diagnostic = diagnostic;

    public async IAsyncEnumerable<PcmChunk> ReadAsync(AudioTarget target, [EnumeratorCancellation] CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        CancellationTokenSource linked;
        var channel = Channel.CreateBounded<PcmChunk>(new BoundedChannelOptions(32)
        {
            SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.DropOldest
        }, _ => { Interlocked.Increment(ref droppedChunks); Interlocked.Exchange(ref droppedSinceRead, 1); });
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (worker is not null) throw new InvalidOperationException("A capture instance can only be read once.");
            if (!IsSupported) throw new PlatformNotSupportedException("Process audio requires Windows build 20348 or later on x64.");
            if (!AppleMusicAudioTargetResolver.IsStillValid(target)) throw new InvalidOperationException("Apple Music audio target could not be verified.");
            linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, token);
            worker = Task.Factory.StartNew(() => Capture(target, channel.Writer, linked.Token), CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
        try
        {
            await foreach (var chunk in channel.Reader.ReadAllAsync(linked.Token).ConfigureAwait(false))
            {
                linked.Token.ThrowIfCancellationRequested();
                yield return Interlocked.Exchange(ref droppedSinceRead, 0) != 0 ? chunk with { Discontinuous = true } : chunk;
            }
        }
        finally
        {
            linked.Cancel();
            await worker.ConfigureAwait(false);
            linked.Dispose();
        }
    }

    void Capture(AudioTarget target, ChannelWriter<PcmChunk> output, CancellationToken token)
    {
        ProcessAudioInterop.IAudioClient? client = null;
        ProcessAudioInterop.IAudioCaptureClient? capture = null;
        bool comInitialized = false, started = false;
        Exception? failure = null;
        try
        {
            Trace("worker-start");
            token.ThrowIfCancellationRequested();
            Marshal.ThrowExceptionForHR(ProcessAudioInterop.CoInitializeEx(0, 0));
            comInitialized = true;
            Trace("activation-start");
            client = new ProcessAudioActivationCompletion(target.ProcessId).Activate(token);
            Trace("activation-complete");
            token.ThrowIfCancellationRequested();
            if (!AppleMusicAudioTargetResolver.IsStillValid(target)) throw new InvalidOperationException("Audio target changed during activation.");
            // A fixed supported conversion format follows Microsoft's ApplicationLoopback sample.
            var format = new ProcessAudioInterop.WaveFormat
            {
                Tag = 1, Channels = 2, SampleRate = 44100, BitsPerSample = 16,
                BlockAlign = 4, BytesPerSecond = 176400, ExtraSize = 0
            };
            Marshal.ThrowExceptionForHR(client.Initialize(0, 0x80060000, 0, 0, format, 0));
            Trace("initialized");
            using var ready = new AutoResetEvent(false);
            Marshal.ThrowExceptionForHR(client.SetEventHandle(ready.SafeWaitHandle.DangerousGetHandle()));
            Marshal.ThrowExceptionForHR(client.GetService(typeof(ProcessAudioInterop.IAudioCaptureClient).GUID, out object service));
            capture = (ProcessAudioInterop.IAudioCaptureClient)service;
            token.ThrowIfCancellationRequested();
            Marshal.ThrowExceptionForHR(client.Start());
            started = true;
            Trace("started");
            var handles = new[] { token.WaitHandle, ready };
            long targetChecked = Stopwatch.GetTimestamp();
            while (true)
            {
                int signaled = WaitHandle.WaitAny(handles, 1000);
                token.ThrowIfCancellationRequested();
                long now = Stopwatch.GetTimestamp();
                if (now - targetChecked >= Stopwatch.Frequency)
                {
                    if (!AppleMusicAudioTargetResolver.IsStillValid(target)) throw new InvalidOperationException("Apple Music audio target exited.");
                    targetChecked = now;
                }
                if (signaled == WaitHandle.WaitTimeout) continue;
                Marshal.ThrowExceptionForHR(capture.GetNextPacketSize(out uint next));
                while (next > 0)
                {
                    token.ThrowIfCancellationRequested();
                    Marshal.ThrowExceptionForHR(capture.GetBuffer(out nint data, out uint frames, out uint flags, out _, out ulong qpc));
                    try
                    {
                        if (frames > 441000) throw new InvalidDataException("Audio packet exceeds the capture bound.");
                        Interlocked.Increment(ref packets);
                        for (int offset = 0; offset < frames; offset += 4410)
                        {
                            token.ThrowIfCancellationRequested();
                            int count = (int)Math.Min(4410, frames - offset);
                            bool silent = (flags & 2) != 0;
                            var bytes = new byte[checked(count * 4)];
                            if (!silent)
                            {
                                if (data == 0) throw new InvalidDataException("Audio packet has no sample buffer.");
                                Marshal.Copy(data + offset * 4, bytes, 0, bytes.Length);
                            }
                            long timestamp = (flags & 4) == 0
                                ? checked((long)(qpc * (Stopwatch.Frequency / 10000000d) + offset * (Stopwatch.Frequency / 44100d)))
                                : Stopwatch.GetTimestamp() - (long)((frames - offset) * (Stopwatch.Frequency / 44100d));
                            output.TryWrite(new(PcmNormalizer.ToFloat(bytes, 16, false), 44100, 2, timestamp,
                                (flags & 5) != 0 && offset == 0, silent));
                        }
                    }
                    finally { Marshal.ThrowExceptionForHR(capture.ReleaseBuffer(frames)); }
                    Marshal.ThrowExceptionForHR(capture.GetNextPacketSize(out next));
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { failure = error; }
        finally
        {
            Trace("stopping");
            if (started && client is not null) client.Stop();
            Trace("stopped");
            ProcessAudioInterop.Release(capture);
            ProcessAudioInterop.Release(client);
            if (comInitialized) ProcessAudioInterop.CoUninitialize();
            output.TryComplete(failure);
            Trace("released");
        }
    }

    void Trace(string stage)
    {
        try { diagnostic?.Invoke(stage); } catch (Exception) { /* Diagnostics must never affect native cleanup. */ }
    }

    public async ValueTask DisposeAsync()
    {
        Task? pending;
        lock (gate)
        {
            if (!disposed) { disposed = true; lifetime.Cancel(); }
            pending = worker;
        }
        if (pending is not null) await pending.ConfigureAwait(false);
        lock (gate)
        {
            if (!lifetimeDisposed) { lifetimeDisposed = true; lifetime.Dispose(); }
        }
    }
}
