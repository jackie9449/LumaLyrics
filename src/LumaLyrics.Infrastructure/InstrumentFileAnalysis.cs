using System.Diagnostics;
using System.Security.Cryptography;
using LumaLyrics.Core;

namespace LumaLyrics.Infrastructure;

/// <summary>Explicit local-file experiment. Successful inference is not an accuracy qualification.</summary>
public sealed class InstrumentFileAnalysis(InstrumentEngine engine, InstrumentRepository repository,
    Func<string, CancellationToken, IAsyncEnumerable<PcmChunk>> readAudio)
{
    readonly SemaphoreSlim gate = new(1);

    public Task<InstrumentTimeline> AnalyzeAsync(string path, InstrumentRecordingKey recording,
        IProgress<double>? progress, CancellationToken token)
        => Task.Run(() => AnalyzeCoreAsync(path, recording, progress, token), token);

    async Task<InstrumentTimeline> AnalyzeCoreAsync(string path, InstrumentRecordingKey recording,
        IProgress<double>? progress, CancellationToken token)
    {
        InstrumentRecordingIdentity.Validate(recording); token.ThrowIfCancellationRequested();
        if (engine.ActivityParameters.Count == 0) throw new InvalidOperationException("File analysis requires explicit activity parameters.");
        await gate.WaitAsync(token);
        try
        {
            string full = Path.GetFullPath(path);
            var before = new FileInfo(full); before.Refresh();
            if (!before.Exists) throw new FileNotFoundException("The selected audio file is unavailable.");
            long length = before.Length; var modified = before.LastWriteTimeUtc; var created = before.CreationTimeUtc;
            // Deny ordinary writes, replacement and rename throughout hashing, reading and commit.
            await using var lease = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            string hash = Convert.ToHexString(await SHA256.HashDataAsync(lease, token)).ToLowerInvariant();
            if (recording.AudioSha256 is { } expected && !hash.Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Selected audio does not match the confirmed content hash.");
            var epoch = new InstrumentEpoch("local-file:" + Guid.NewGuid().ToString("N"), 1, 1, 0);
            var windows = new InstrumentWindowBuilder(engine.SampleRate, engine.WindowSamples, engine.HopSamples);
            var filters = engine.ActivityParameters.ToDictionary(pair => pair.Key, pair => new InstrumentActivityFilter(pair.Value));
            var results = new List<(InstrumentFrame Frame, InstrumentInterval[] Intervals)>();
            InstrumentResampler? resampler = null;
            int sourceRate = 0, channels = 0, lastPercent = -1;
            long sourceFrames = 0, convertedFrames = 0;
            double lastObservation = -1;
            async Task AnalyzeWindow(InstrumentWindow window)
            {
                token.ThrowIfCancellationRequested();
                if (window.EndMs!.Value <= lastObservation) return;
                var frame = await engine.AnalyzeAsync(window, token);
                var active = new List<InstrumentInterval>();
                double center = window.StartMs!.Value + engine.WindowSamples * (500d / engine.SampleRate);
                double begin = Math.Max(window.StartMs.Value, center - engine.HopSamples * (500d / engine.SampleRate));
                double end = Math.Min(window.EndMs.Value, center + engine.HopSamples * (500d / engine.SampleRate));
                foreach (var score in frame.Scores)
                {
                    if (!filters.TryGetValue(score.Key, out var filter)) throw new InvalidDataException("Classifier family has no configured activity rule.");
                    if (filter.Update(score.Value, window.EndMs.Value) && end > begin) active.Add(new(score.Key, begin, end));
                }
                if (results.Count >= 500000) throw new InvalidDataException("Audio exceeds the bounded analysis length.");
                results.Add((frame, active.ToArray())); lastObservation = window.EndMs.Value;
            }
            async Task PushMono(float[] mono)
            {
                if (mono.Length == 0) return;
                long ticks = checked((long)(convertedFrames * (Stopwatch.Frequency / (double)engine.SampleRate)));
                var ready = windows.Push(mono, ticks, epoch, convertedFrames * (1000d / engine.SampleRate));
                convertedFrames = checked(convertedFrames + mono.Length);
                foreach (var window in ready) await AnalyzeWindow(window);
            }
            await foreach (var chunk in readAudio(full, token).WithCancellation(token))
            {
                token.ThrowIfCancellationRequested();
                if (chunk.Discontinuous || chunk.Channels is < 1 or > 8 || chunk.SampleRate is < 8000 or > 192000
                    || chunk.Samples.Length == 0 || chunk.Samples.Length % chunk.Channels != 0 || chunk.Samples.Length > chunk.SampleRate * chunk.Channels * 10)
                    throw new InvalidDataException("Invalid or discontinuous source audio.");
                if (resampler is null)
                { sourceRate = chunk.SampleRate; channels = chunk.Channels; resampler = new(sourceRate, engine.SampleRate); }
                if (sourceRate != chunk.SampleRate || channels != chunk.Channels) throw new InvalidDataException("Source audio format changed.");
                var mono = new float[chunk.Samples.Length / channels];
                for (int frame = 0; frame < mono.Length; frame++)
                {
                    double sum = 0;
                    for (int channel = 0; channel < channels; channel++)
                    {
                        float sample = chunk.Samples[frame * channels + channel];
                        if (!float.IsFinite(sample) || sample is < -1 or > 1) throw new InvalidDataException("Invalid source sample.");
                        sum += sample;
                    }
                    mono[frame] = (float)(sum / channels);
                }
                sourceFrames = checked(sourceFrames + mono.Length);
                if (sourceFrames * (1000d / sourceRate) > recording.DurationMs + 1)
                    throw new InvalidDataException("Selected file exceeds the confirmed recording duration.");
                await PushMono(resampler.Push(mono));
                int percent = Math.Min(99, (int)(sourceFrames * (100000d / sourceRate) / recording.DurationMs));
                if (percent > lastPercent) { lastPercent = percent; progress?.Report(percent / 100d); }
            }
            if (resampler is null) throw new InvalidDataException("Source audio is empty.");
            await PushMono(resampler.Finish());
            if (convertedFrames < engine.WindowSamples) throw new InvalidDataException("Audio is too short for one valid model context.");
            var tail = windows.Finish(); if (tail is not null) await AnalyzeWindow(tail);
            long duration = checked((long)Math.Ceiling(sourceFrames * (1000d / sourceRate)));
            if (Math.Abs(duration - recording.DurationMs) > 1) throw new InvalidDataException("Selected file does not match the confirmed duration.");
            var after = new FileInfo(full); after.Refresh();
            if (!after.Exists || after.Length != length || after.LastWriteTimeUtc != modified || after.CreationTimeUtc != created)
                throw new InvalidDataException("Source audio changed during analysis.");
            lease.Position = 0;
            string afterHash = Convert.ToHexString(await SHA256.HashDataAsync(lease, token)).ToLowerInvariant();
            if (hash != afterHash) throw new InvalidDataException("Source content changed during analysis.");
            var actual = recording with { DurationMs = duration, AudioSha256 = hash };
            var builder = new InstrumentTimelineBuilder(actual, engine.ProfileId);
            foreach (var result in results) { token.ThrowIfCancellationRequested(); builder.Add(result.Frame, result.Intervals); }
            var timeline = builder.Build();
            long revision = checked((repository.Load(actual, engine.ProfileId)?.Revision ?? 0) + 1);
            timeline = timeline with { Revision = revision };
            token.ThrowIfCancellationRequested();
            repository.Save(timeline, new(full, hash, "explicit-local-file-analysis"), token);
            progress?.Report(1); return repository.Load(actual, engine.ProfileId)!;
        }
        finally { gate.Release(); }
    }
}
