using System.Diagnostics;
using System.Threading.Channels;
using LumaLyrics.Core;

namespace LumaLyrics.Infrastructure;

/// <summary>Bounded producer/consumer. Slow inference keeps recent windows and rejects expired epochs.</summary>
public sealed class InstrumentLiveAnalysis(InstrumentEngine engine, InstrumentEpochGuard guard)
{
    long dropped;
    int hasSignal;
    public long DroppedWindows => Interlocked.Read(ref dropped);
    public bool HasEffectiveSignal => Volatile.Read(ref hasSignal) != 0;

    public async Task RunAsync(IProcessAudioCapture capture, AudioTarget target, Action<InstrumentFrame> accept, CancellationToken token)
    {
        using var scope = CancellationTokenSource.CreateLinkedTokenSource(token);
        var queue = Channel.CreateBounded<InstrumentWindow>(new BoundedChannelOptions(2)
        { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.DropOldest }, _ => Interlocked.Increment(ref dropped));
        var producer = Task.Run(async () =>
        {
            Exception? failure = null;
            try
            {
                var epoch = guard.Current ?? throw new OperationCanceledException(scope.Token);
                var windows = new InstrumentWindowBuilder(engine.SampleRate, engine.WindowSamples, engine.HopSamples);
                InstrumentResampler? resampler = null;
                int rate = 0, channels = 0;
                long sourceFrames = 0, convertedFrames = 0, origin = 0;
                await foreach (var chunk in capture.ReadAsync(target, scope.Token).WithCancellation(scope.Token))
                {
                    scope.Token.ThrowIfCancellationRequested(); if (!guard.IsCurrent(epoch)) break;
                    if (chunk.Channels is < 1 or > 8 || chunk.SampleRate is < 8000 or > 192000 || chunk.Samples.Length == 0
                        || chunk.Samples.Length % chunk.Channels != 0 || chunk.Samples.Length > chunk.SampleRate * chunk.Channels * 10)
                        throw new InvalidDataException("Invalid capture audio format.");
                    bool gap = resampler is not null && Math.Abs(chunk.FirstSampleTicks - (origin + sourceFrames * (Stopwatch.Frequency / (double)rate))) > Stopwatch.Frequency / (double)rate + 1;
                    if (resampler is null || chunk.Discontinuous || gap || rate != chunk.SampleRate || channels != chunk.Channels)
                    {
                        if (resampler is not null)
                        {
                            var next = guard.AdvanceDiscontinuity(epoch); if (next is null) break; epoch = next;
                        }
                        rate = chunk.SampleRate; channels = chunk.Channels; resampler = new(rate, engine.SampleRate);
                        windows.Reset(); origin = chunk.FirstSampleTicks; sourceFrames = convertedFrames = 0;
                    }
                    double energy = 0; float peak = 0;
                    var mono = new float[chunk.Samples.Length / channels];
                    for (int index = 0; index < mono.Length; index++)
                    {
                        double sum = 0;
                        for (int channel = 0; channel < channels; channel++)
                        {
                            float value = chunk.Samples[index * channels + channel];
                            if (!float.IsFinite(value) || value is < -1 or > 1) throw new InvalidDataException("Invalid capture sample.");
                            sum += value; energy += value * value; peak = Math.Max(peak, Math.Abs(value));
                        }
                        mono[index] = (float)(sum / channels);
                    }
                    if (peak >= .002f && Math.Sqrt(energy / chunk.Samples.Length) >= .0005) Volatile.Write(ref hasSignal, 1);
                    sourceFrames += mono.Length;
                    var converted = resampler.Push(mono);
                    long first = origin + checked((long)(convertedFrames * (Stopwatch.Frequency / (double)engine.SampleRate)));
                    // Native music timestamps are not precise recording positions. Keep live results session-only.
                    var ready = windows.Push(converted, first, epoch, null); convertedFrames += converted.Length;
                    if (!HasEffectiveSignal) continue;
                    foreach (var window in ready) if (guard.IsCurrent(window.Epoch)) queue.Writer.TryWrite(window);
                }
            }
            catch (OperationCanceledException) when (scope.IsCancellationRequested) { }
            catch (Exception error) { failure = error; }
            finally { queue.Writer.TryComplete(failure); }
        }, CancellationToken.None);
        try
        {
            await foreach (var window in queue.Reader.ReadAllAsync(scope.Token))
            {
                long now = Stopwatch.GetTimestamp();
                if (!guard.IsCurrent(window.Epoch) || Stopwatch.GetElapsedTime(window.EndTicks, now).TotalMilliseconds is > 2000 or < -50) continue;
                var frame = await engine.AnalyzeAsync(window, scope.Token);
                scope.Token.ThrowIfCancellationRequested();
                if (guard.IsCurrent(frame.Epoch)) accept(frame);
            }
        }
        finally { scope.Cancel(); await producer; }
    }
}
