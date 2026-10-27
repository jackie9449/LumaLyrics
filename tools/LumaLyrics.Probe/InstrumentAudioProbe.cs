using System.Diagnostics;
using System.Text.Json;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
using LumaLyrics.Windows;

namespace LumaLyrics.Probe;

internal static class InstrumentAudioProbe
{
    internal static async Task RunAsync(string[] args)
    {
        int location = Array.IndexOf(args, "--data-dir");
        if (location < 0 || location + 1 == args.Length || args[location + 1].StartsWith("--"))
            throw new ArgumentException("Specify an external private --data-dir.");
        string root = Path.GetFullPath(args[location + 1]);
        PersonalLibraryArchive.EnsureOutsideSoftware(root);
        int length = Array.IndexOf(args, "--seconds");
        int seconds = length >= 0 && length + 1 < args.Length && int.TryParse(args[length + 1], out int requested) ? requested : 30;
        if (seconds is < 3 or > 120) throw new ArgumentOutOfRangeException(nameof(seconds));
        Directory.CreateDirectory(root);
        void Trace(string stage) => File.AppendAllText(Path.Combine(root, "instrument-audio-stages.log"), DateTimeOffset.UtcNow.ToString("O") + " " + stage + Environment.NewLine);
        Trace("probe-start");
        string reportPath = Path.Combine(root, "instrument-audio-probe.json");
        using var cancel = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, e) => { e.Cancel = true; cancel.Cancel(); };
        Console.CancelKeyPress += cancelHandler;
        var report = new Dictionary<string, object?>
        {
            ["windowsBuild"] = Environment.OSVersion.Version.Build,
            ["processLoopbackSupported"] = ProcessAudioCapture.IsSupported,
            ["requestedSeconds"] = seconds,
            ["rawAudioSaved"] = false,
            ["windowStateManipulated"] = false,
            ["createdUtc"] = DateTimeOffset.UtcNow
        };
        var elapsed = Stopwatch.StartNew();
        try
        {
            if (!ProcessAudioCapture.IsSupported) { report["status"] = "UnsupportedSystem"; return; }
            await using var source = new GsmtcPlaybackSource();
            Trace("media-start");
            await source.StartAsync(cancel.Token).WaitAsync(TimeSpan.FromSeconds(5), cancel.Token);
            var expected = source.Latest;
            Trace("media-ready");
            if (expected is null) { report["status"] = "NoAppleMusicSession"; return; }
            report["initialPlayState"] = expected.Status.ToString();
            if (expected.Status != PlayState.Playing && !args.Contains("--allow-paused")) { report["status"] = "NotPlaying"; return; }
            var target = await AppleMusicAudioTargetResolver.ResolveAsync(expected, cancel.Token);
            Trace("renderer-resolved");
            if (target is null) { report["status"] = "UnverifiedTarget"; return; }
            report["targetIdentityVerified"] = true;
            using var duration = CancellationTokenSource.CreateLinkedTokenSource(cancel.Token);
            long stopRequestedTicks = 0;
            using var stopRegistration = duration.Token.Register(() => Interlocked.CompareExchange(ref stopRequestedTicks, Stopwatch.GetTimestamp(), 0));
            duration.CancelAfter(TimeSpan.FromSeconds(seconds));
            string host = Environment.ProcessPath is { } executable && Path.GetFileName(executable).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase)
                ? executable : throw new InvalidOperationException("Run the native probe with the Microsoft dotnet host.");
            await using var capture = new ProcessAudioCaptureBroker(host, typeof(InstrumentAudioProbe).Assembly.Location);
            var levels = new List<double>();
            long frames = 0, discontinuities = 0, effectiveChunks = 0;
            bool recordingChanged = false;
            bool sourceReportedPlaying = expected.Status == PlayState.Playing;
            double? firstChunkMs = null;
            var started = Stopwatch.StartNew();
            try
            {
                await foreach (var chunk in capture.ReadAsync(target, duration.Token))
                {
                    firstChunkMs ??= started.Elapsed.TotalMilliseconds;
                    report["sampleRate"] = chunk.SampleRate;
                    report["channels"] = chunk.Channels;
                    if (chunk.Discontinuous) discontinuities++;
                    frames += chunk.Samples.Length / chunk.Channels;
                    var metrics = AudioSignalMetrics.Measure(chunk.Samples);
                    levels.Add(metrics.Rms);
                    if (metrics.HasEffectiveSignal) effectiveChunks++;
                    var current = source.Latest;
                    sourceReportedPlaying |= current?.Status == PlayState.Playing;
                    if (current is null || current.SessionId != expected.SessionId || current.Generation != expected.Generation
                        || current.Revision != expected.Revision) recordingChanged = true;
                }
            }
            catch (OperationCanceledException) when (duration.IsCancellationRequested) { }
            finally
            {
                await capture.DisposeAsync();
                report["forcedCaptureHelperStop"] = capture.ForcedTermination;
                report["helperDiagnostic"] = capture.HelperDiagnostic;
            }
            Trace("audio-enumeration-finished");
            var release = Stopwatch.StartNew();
            await capture.DisposeAsync();
            Trace("audio-disposed");
            report["disposeCallMs"] = release.Elapsed.TotalMilliseconds;
            report["stopToReleasedMs"] = stopRequestedTicks > 0 ? Stopwatch.GetElapsedTime(stopRequestedTicks).TotalMilliseconds : null;
            levels.Sort();
            report["packets"] = levels.Count;
            report["forcedCaptureHelperStop"] = capture.ForcedTermination;
            report["helperDiagnostic"] = capture.HelperDiagnostic;
            report["sampleFrames"] = frames;
            report["effectiveChunks"] = effectiveChunks;
            report["discontinuities"] = discontinuities;
            report["firstChunkMs"] = firstChunkMs;
            report["recordingChanged"] = recordingChanged;
            report["sourceReportedPlaying"] = sourceReportedPlaying;
            report["effectiveSignalThreshold"] = new { minimumRms = .0005, minimumPeak = .002 };
            report["rmsP50"] = levels.Count > 0 ? levels[(levels.Count - 1) / 2] : 0;
            report["rmsP95"] = levels.Count > 0 ? levels[(int)((levels.Count - 1) * .95)] : 0;
            report["status"] = cancel.IsCancellationRequested ? "Cancelled"
                : !sourceReportedPlaying ? "InterfaceReadyUnconfirmedPlayback"
                : effectiveChunks > 0 ? "ObservedAudio" : "NoEffectiveAudio";
            report["packageAudioRouting"] = await Task.Run(() => AppleMusicAudioRouting.Read(target.PackageFamily, expected.SessionId));
            Trace("routing-read");
        }
        catch (Exception error)
        {
            report["status"] = cancel.IsCancellationRequested ? "Cancelled" : "Unavailable";
            report["errorType"] = error.GetType().Name;
            report["hresult"] = $"0x{error.HResult:X8}";
        }
        finally
        {
            Trace("report-start");
            Console.CancelKeyPress -= cancelHandler;
            report["elapsedSeconds"] = elapsed.Elapsed.TotalSeconds;
            report["notVerified"] = new[] { "OtherApplicationsExcluded", "MinimizedPlayback", "OutputDeviceChanges", "PauseResume", "ProtectedContentVariants", "SustainedPlayback" };
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(JsonSerializer.Serialize(new { status = report.GetValueOrDefault("status"), packets = report.GetValueOrDefault("packets"), effectiveChunks = report.GetValueOrDefault("effectiveChunks"), rawAudioSaved = false }));
        }
    }
}
