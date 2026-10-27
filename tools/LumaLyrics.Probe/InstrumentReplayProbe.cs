using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
using LumaLyrics.Windows;

namespace LumaLyrics.Probe;

/// <summary>Real-time file-paced pipeline measurement. Never substitutes for native-capture verification.</summary>
internal static class InstrumentReplayProbe
{
    internal static async Task RunAsync(string[] args)
    {
        string Value(string flag) { int i = Array.IndexOf(args, flag); return i >= 0 && i + 1 < args.Length ? args[i + 1] : throw new ArgumentException("Missing " + flag); }
        string root = Path.GetFullPath(Value("--data-dir")), file = Path.GetFullPath(Value("--file"));
        PersonalLibraryArchive.EnsureOutsideSoftware(root);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Select an existing personal directory.");
        int seconds = int.Parse(Value("--seconds")); if (seconds is < 5 or > 1800) throw new ArgumentOutOfRangeException(nameof(seconds));
        var option = InstrumentModelCatalog.Get(args.Contains("--model-id") ? Value("--model-id") : null);
        var profile = option.Profile;
        var store = new InstrumentModelStore(root);
        await store.ImportAsync(Value("--model"), profile, option.Bytes, default);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        var epoch = new InstrumentEpoch("file-paced-fixture", 1, 1, 0); var guard = new InstrumentEpochGuard(); guard.Set(epoch);
        await using var engine = new InstrumentEngine(new OnnxInstrumentClassifier(store.LocalPath(profile), profile));
        await using var capture = new PacedCapture(file);
        var analysis = new InstrumentLiveAnalysis(engine, guard);
        using var process = Process.GetCurrentProcess(); process.Refresh();
        var cpuStart = process.TotalProcessorTime; var watch = Stopwatch.StartNew();
        var latency = new List<double>(); long accepted = 0, peakWorking = process.WorkingSet64, peakPrivate = process.PrivateMemorySize64;
        long warmWorking = 0, warmPrivate = 0; int handles = process.HandleCount, peakHandles = handles;
        try
        {
            await analysis.RunAsync(capture, new(1, DateTime.UnixEpoch, "self-owned-file-replay"), frame =>
            {
                accepted++; latency.Add(Stopwatch.GetElapsedTime(frame.EndTicks).TotalMilliseconds); process.Refresh();
                peakWorking = Math.Max(peakWorking, process.WorkingSet64); peakPrivate = Math.Max(peakPrivate, process.PrivateMemorySize64); peakHandles = Math.Max(peakHandles, process.HandleCount);
                if (warmWorking == 0 && watch.Elapsed.TotalSeconds >= 10) { warmWorking = process.WorkingSet64; warmPrivate = process.PrivateMemorySize64; }
            }, stop.Token);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        process.Refresh(); latency.Sort();
        peakWorking = Math.Max(peakWorking, process.WorkingSet64); peakPrivate = Math.Max(peakPrivate, process.PrivateMemorySize64); peakHandles = Math.Max(peakHandles, process.HandleCount);
        await using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        var report = new
        {
            scope = "Real-time file-paced C# decoder/FIR/ONNX pipeline, no sound output or native capture",
            elapsedSeconds = watch.Elapsed.TotalSeconds, acceptedFrames = accepted, droppedWindows = analysis.DroppedWindows,
            effectiveSignal = analysis.HasEffectiveSignal, rawAudioSaved = false,
            sourceSha256 = Convert.ToHexString(await SHA256.HashDataAsync(input)).ToLowerInvariant(),
            afterWindowEndP50Ms = latency.Count == 0 ? (double?)null : latency[(latency.Count - 1) / 2],
            afterWindowEndP95Ms = latency.Count == 0 ? (double?)null : latency[(int)Math.Ceiling(latency.Count * .95) - 1],
            observationWindowMs = profile.WindowSamples * (1000d / profile.SampleRate),
            cpuPercentOfOneCore = (process.TotalProcessorTime - cpuStart).TotalMilliseconds / watch.Elapsed.TotalMilliseconds * 100,
            warmWorkingSetMiB = warmWorking / 1048576d, endWorkingSetMiB = process.WorkingSet64 / 1048576d, peakWorkingSetMiB = peakWorking / 1048576d,
            warmPrivateMiB = warmPrivate / 1048576d, endPrivateMiB = process.PrivateMemorySize64 / 1048576d, peakPrivateMiB = peakPrivate / 1048576d,
            initialHandles = handles, endHandles = process.HandleCount, peakHandles,
            accuracyValidated = false, nativeCaptureValidated = false
        };
        string output = Path.Combine(root, "instrument-replay-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(report));
        if (!analysis.HasEffectiveSignal || accepted == 0) throw new InvalidDataException("No effective replay frames.");
    }
    sealed class PacedCapture(string path) : IProcessAudioCapture
    {
        public async IAsyncEnumerable<PcmChunk> ReadAsync(AudioTarget target, [EnumeratorCancellation] CancellationToken token)
        {
            long origin = Stopwatch.GetTimestamp(), frames = 0;
            while (true)
            {
                bool first = true;
                await foreach (var chunk in WaveAudioReader.ReadAsync(path, token))
                {
                    int count = chunk.Samples.Length / chunk.Channels;
                    long start = origin + checked((long)(frames * (Stopwatch.Frequency / (double)chunk.SampleRate)));
                    frames += count;
                    long end = origin + checked((long)(frames * (Stopwatch.Frequency / (double)chunk.SampleRate)));
                    double delay = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), end).TotalMilliseconds;
                    if (delay > 0) await Task.Delay(TimeSpan.FromMilliseconds(delay), token);
                    token.ThrowIfCancellationRequested();
                    yield return chunk with { FirstSampleTicks = start, Discontinuous = first };
                    first = false;
                }
            }
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
