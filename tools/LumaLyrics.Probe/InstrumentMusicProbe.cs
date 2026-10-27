using System.Diagnostics;
using System.Text.Json;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
using LumaLyrics.Windows;

namespace LumaLyrics.Probe;

internal static class InstrumentMusicProbe
{
    internal static async Task RunAsync(string[] args)
    {
        string? Optional(string flag) { int i = Array.IndexOf(args, flag); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        string Required(string flag) => Optional(flag) ?? throw new ArgumentException("Missing " + flag);
        string root = Path.GetFullPath(Required("--data-dir")); PersonalLibraryArchive.EnsureOutsideSoftware(root);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Select an existing personal directory.");
        int seconds = int.TryParse(Optional("--seconds"), out int length) ? length : 30;
        if (seconds is < 3 or > 120) throw new ArgumentOutOfRangeException(nameof(seconds));
        var report = new Dictionary<string, object?> { ["rawAudioSaved"] = false, ["windowStateManipulated"] = false,
            ["appleMusicProcessPresent"] = Present("AppleMusic"), ["rendererProcessPresent"] = Present("AMPLibraryAgent"),
            ["createdUtc"] = DateTimeOffset.UtcNow, ["accuracyValidated"] = false };
        try
        {
            await using var source = new GsmtcPlaybackSource();
            await source.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
            var snapshot = source.Latest;
            report["mediaSessionPresent"] = snapshot is not null; report["recording"] = snapshot;
            if (snapshot is null) { report["status"] = "NoReadableAppleMusicSession"; return; }
            if (Optional("--expect-title") is { } title && MatchRules.Normalize(title) != MatchRules.Normalize(snapshot.Track.Title)
                || Optional("--expect-artist") is { } artist && MatchRules.Normalize(artist) != MatchRules.Normalize(snapshot.Track.Artist))
            { report["status"] = "RequestedRecordingMismatch"; return; }
            if (snapshot.Status != PlayState.Playing) { report["status"] = "NotPlaying"; return; }
            var option = InstrumentModelCatalog.Get(Optional("--model-id") ?? InstrumentModelCatalog.RockId);
            var models = new InstrumentModelStore(root);
            string model = await models.ImportAsync(Required("--model"), option.Profile, option.Bytes, default);
            var target = await AppleMusicAudioTargetResolver.ResolveAsync(snapshot, default);
            if (target is null) { report["status"] = "UnverifiedRenderer"; return; }
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
            var guard = new InstrumentEpochGuard(); var epoch = new InstrumentEpoch(snapshot.SessionId, snapshot.Generation, snapshot.Revision, 0); guard.Set(epoch);
            bool changed = false;
            void Observe(PlaybackSnapshot? next)
            {
                if (next?.SessionId == snapshot.SessionId && next.Generation == snapshot.Generation && next.Revision == snapshot.Revision && next.Status == PlayState.Playing) return;
                changed = true; guard.Clear(); stop.Cancel();
            }
            source.Changed += Observe;
            try
            {
                Observe(source.Latest);
                if (changed) { report["status"] = "RecordingChanged"; return; }
                string host = Environment.ProcessPath is { } path && Path.GetFileName(path).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase) ? path : throw new InvalidOperationException("Use the Microsoft dotnet host.");
                await using var engine = new InstrumentEngine(new OnnxInstrumentClassifier(model, option.Profile));
                await using var capture = new ProcessAudioCaptureBroker(host, typeof(InstrumentMusicProbe).Assembly.Location);
                var analysis = new InstrumentLiveAnalysis(engine, guard);
                var presentation = new InstrumentPresentation(option.Profile.Thresholds); presentation.Begin(epoch, false);
                var frames = new List<object>(); var delays = new List<double>(); InstrumentEpoch presented = epoch;
                try
                {
                    await analysis.RunAsync(capture, target, frame =>
                    {
                        if (stop.IsCancellationRequested || !guard.IsCurrent(frame.Epoch)) return;
                        if (presented != frame.Epoch) { presentation.Begin(frame.Epoch, true); presented = frame.Epoch; }
                        presentation.Apply(frame); delays.Add(Stopwatch.GetElapsedTime(frame.EndTicks).TotalMilliseconds);
                        frames.Add(new { endTicks = frame.EndTicks, frame.Scores, view = presentation.View(PlayState.Playing, Stopwatch.GetTimestamp()) });
                    }, stop.Token);
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
                delays.Sort();
                report["status"] = changed ? "RecordingChanged" : analysis.HasEffectiveSignal && frames.Count > 0 ? "ObservedInstrumentFrames" : "NoEffectiveAudio";
                report["profileId"] = engine.ProfileId; report["frames"] = frames; report["droppedWindows"] = analysis.DroppedWindows;
                report["windowEndToResultP95Ms"] = delays.Count == 0 ? null : delays[(int)Math.Ceiling(delays.Count * .95) - 1];
                report["observationWindowMs"] = engine.WindowSamples * (1000d / engine.SampleRate);
            }
            finally { source.Changed -= Observe; }
        }
        catch (Exception error) { report["status"] = "Unavailable"; report["errorType"] = error.GetType().Name; }
        finally
        {
            await File.WriteAllTextAsync(Path.Combine(root, "instrument-music-" + Guid.NewGuid().ToString("N") + ".json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(JsonSerializer.Serialize(new { status = report.GetValueOrDefault("status"), appleMusicProcessPresent = report["appleMusicProcessPresent"],
                rendererProcessPresent = report["rendererProcessPresent"], mediaSessionPresent = report.GetValueOrDefault("mediaSessionPresent"), accuracyValidated = false }));
        }
    }
    internal static bool Present(string name)
    {
        var processes = Process.GetProcessesByName(name);
        try { return processes.Length > 0; } finally { foreach (var process in processes) process.Dispose(); }
    }
}
