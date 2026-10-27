using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
using LumaLyrics.Windows;

namespace LumaLyrics.Probe;

internal static class InstrumentBenchmarkCommands
{
    internal static async Task RunAsync(string directory)
    {
        string root = Path.GetFullPath(directory);
        PersonalLibraryArchive.EnsureOutsideSoftware(root);
        var profile = LoadProfile(root);
        await using var classifier = new OnnxInstrumentClassifier(ModelPath(root), profile);
        float[] inputs = ReadFloats(Path.Combine(root, "parity-inputs.f32"));
        float[] expected = ReadFloats(Path.Combine(root, "parity-scores.f32"));
        int count = inputs.Length / profile.WindowSamples;
        if (count != 20 || inputs.Length != count * profile.WindowSamples || expected.Length != count * profile.OutputClassCount)
            throw new InvalidDataException("Numerical check requires exactly twenty self-generated reference windows.");
        var timings = new List<double>();
        double maximumError = 0;
        for (int sample = 0; sample < count; sample++)
        {
            var waveform = inputs.AsSpan(sample * profile.WindowSamples, profile.WindowSamples).ToArray();
            var watch = Stopwatch.StartNew();
            float[] actual = classifier.Scores(waveform, default);
            timings.Add(watch.Elapsed.TotalMilliseconds);
            for (int label = 0; label < actual.Length; label++)
                maximumError = Math.Max(maximumError, Math.Abs(actual[label] - expected[sample * profile.OutputClassCount + label]));
        }
        timings.Sort();
        var report = new
        {
            profileId = classifier.ProfileId, runtime = "Microsoft.ML.OnnxRuntime 1.31.0 CPU", samples = count,
            maximumAbsoluteError = maximumError, numericParityPassed = maximumError <= 1e-4,
            inferenceP95Ms = timings[(int)Math.Ceiling(count * .95) - 1], accuracyValidated = false, productionReady = false
        };
        await File.WriteAllTextAsync(Path.Combine(root, "instrument-csharp-parity.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(report));
        if (!report.numericParityPassed) throw new InvalidDataException("C# numerical parity gate failed.");
    }

    static InstrumentInferenceProfile LoadProfile(string root)
    {
        using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "instrument-model-export.json")));
        var data = metadata.RootElement;
        var json = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        var labels = data.GetProperty("labelMap").Deserialize<Dictionary<int, InstrumentKind>>(json)!;
        return new InstrumentInferenceProfile
        {
            Id = "waveform-experimental", Sha256 = data.GetProperty("modelSha256").GetString()!,
            SampleRate = data.GetProperty("sampleRate").GetInt32(), WindowSamples = data.GetProperty("windowSamples").GetInt32(),
            HopSamples = data.GetProperty("hopSamples").GetInt32(), InputName = data.GetProperty("inputName").GetString()!,
            OutputName = data.GetProperty("outputName").GetString()!, OutputClassCount = data.GetProperty("outputClassCount").GetInt32(),
            PreprocessingVersion = data.GetProperty("preprocessingVersion").GetString()!, LabelMap = labels,
            Thresholds = labels.Values.Distinct().ToDictionary(kind => kind, _ => new ActivityParameters(.65f, .4f, 2, 750))
        };
    }

    static string ModelPath(string root)
    {
        using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "instrument-model-export.json")));
        string name = metadata.RootElement.TryGetProperty("modelFile", out var file) ? file.GetString()! : "yamnet-waveform.onnx";
        if (Path.GetFileName(name) != name || Path.GetExtension(name) != ".onnx") throw new InvalidDataException("Invalid model filename.");
        return Path.Combine(root, name);
    }

    internal static async Task ScoreDatasetAsync(string[] args)
    {
        string Value(string flag)
        { int index = Array.IndexOf(args, flag); return index >= 0 && index + 1 < args.Length ? args[index + 1] : throw new ArgumentException("Missing " + flag); }
        string root = Path.GetFullPath(Value("--data-dir")), model = Path.GetFullPath(Value("--model-dir"));
        PersonalLibraryArchive.EnsureOutsideSoftware(root);
        using var dataset = JsonDocument.Parse(await File.ReadAllTextAsync(Value("--dataset")));
        using var modelMetadata = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(model, "instrument-model-export.json")));
        if (!modelMetadata.RootElement.GetProperty("numericParityPassed").GetBoolean()) throw new InvalidDataException("Model numerical gate did not pass.");
        var profile = SelectedProfile(model, args);
        await using var engine = new InstrumentEngine(new OnnxInstrumentClassifier(ModelPath(model), profile));
        using var repository = new InstrumentRepository(root);
        var items = new List<object>(); var watch = Stopwatch.StartNew();
        foreach (var record in dataset.RootElement.GetProperty("recordings").EnumerateArray())
        {
            string path = Path.GetFullPath(record.GetProperty("audioPath").GetString()!);
            var info = WaveAudioReader.Inspect(path);
            if (record.GetProperty("startMs").GetDouble() != 0 || Math.Abs(record.GetProperty("durationMs").GetDouble() - info.DurationMs) > 1)
                throw new InvalidDataException("Dataset scoring requires exact whole-file ranges.");
            string id = record.GetProperty("recordingId").GetString()!;
            var key = new InstrumentRecordingKey("public-evaluation", id, (long)Math.Ceiling(info.DurationMs), null);
            var analysis = new InstrumentFileAnalysis(engine, repository, WaveAudioReader.ReadAsync);
            var result = await analysis.AnalyzeAsync(path, key, null, default);
            double span = engine.WindowSamples * (1000d / engine.SampleRate);
            var points = result.Scores.Where(point => point.EndMs - point.StartMs >= span - .001).ToArray();
            items.Add(new { recordingId = id, audioSha256 = result.Recording.AudioSha256,
                completeCoverage = result.Complete, roster = InstrumentVocabulary.VisibleKinds(result.Roster).Select(kind => kind.ToString()).ToArray(),
                centersMs = points.Select(point => point.StartMs + span / 2).ToArray(), endsMs = points.Select(point => point.EndMs).ToArray(),
                scores = profile.LabelMap.Values.Distinct().ToDictionary(kind => kind.ToString(), kind => points.Select(point => point.Scores[kind]).ToArray()) });
        }
        string output = Path.Combine(root, "instrument-csharp-dataset-scores.json");
        await using var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write);
        await JsonSerializer.SerializeAsync(file, new { modelSha256 = profile.Sha256, sourcePreprocessing = InstrumentResampler.Version,
            profileId = engine.ProfileId, elapsedMs = watch.Elapsed.TotalMilliseconds, recordings = items });
        Console.WriteLine(JsonSerializer.Serialize(new { status = "ScoredExperimental", recordings = items.Count, elapsedMs = watch.Elapsed.TotalMilliseconds, accuracyValidated = false }));
    }

    internal static async Task RunFileAsync(string[] args)
    {
        string Value(string flag)
        { int index = Array.IndexOf(args, flag); return index >= 0 && index + 1 < args.Length && !args[index + 1].StartsWith("--") ? args[index + 1] : throw new ArgumentException("Missing " + flag); }
        if (!args.Contains("--experimental")) throw new ArgumentException("Uncalibrated file analysis requires the explicit --experimental flag.");
        string root = Path.GetFullPath(Value("--data-dir")), modelRoot = Path.GetFullPath(Value("--model-dir")), path = Path.GetFullPath(Value("--file"));
        PersonalLibraryArchive.EnsureOutsideSoftware(root); PersonalLibraryArchive.EnsureOutsideSoftware(modelRoot);
        using var repository = new InstrumentRepository(root);
        using var cancel = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; cancel.Cancel(); };
        Console.CancelKeyPress += handler;
        var watch = Stopwatch.StartNew();
        object report;
        try
        {
            using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(modelRoot, "instrument-model-export.json"), cancel.Token));
            if (!metadata.RootElement.GetProperty("numericParityPassed").GetBoolean()) throw new InvalidDataException("Model export did not pass the numerical gate.");
            var classifier = new OnnxInstrumentClassifier(ModelPath(modelRoot), SelectedProfile(modelRoot, args));
            await using var engine = new InstrumentEngine(classifier);
            var info = WaveAudioReader.Inspect(path);
            string localId = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(path))).ToLowerInvariant();
            var key = new InstrumentRecordingKey("explicit-local-file", localId, checked((long)Math.Ceiling(info.DurationMs)), null);
            var analysis = new InstrumentFileAnalysis(engine, repository, WaveAudioReader.ReadAsync);
            var result = await analysis.AnalyzeAsync(path, key, null, cancel.Token);
            report = new { status = "AnalyzedExperimental", completeCoverage = result.Complete, windows = result.Scores.Length,
                roster = InstrumentVocabulary.VisibleKinds(result.Roster).Select(InstrumentVocabulary.Name).ToArray(),
                durationMs = result.Recording.DurationMs, elapsedMs = watch.Elapsed.TotalMilliseconds,
                profileId = result.ProfileId, sampleRate = engine.SampleRate, windowSamples = engine.WindowSamples, hopSamples = engine.HopSamples,
                timingConvention = "window-center-estimate", windowCenterUncertaintyMs = engine.WindowSamples * (500d / engine.SampleRate),
                sourcePreprocessing = InstrumentResampler.Version, accuracyValidated = false, productionReady = false };
        }
        catch (Exception error)
        {
            Environment.ExitCode = 2;
            report = new { status = cancel.IsCancellationRequested ? "Cancelled" : "Failed", errorType = error.GetType().Name,
                hresult = $"0x{error.HResult:X8}", accuracyValidated = false, productionReady = false };
        }
        finally { Console.CancelKeyPress -= handler; }
        string output = Path.Combine(root, "instrument-file-analysis-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(report));
    }

    static InstrumentInferenceProfile SelectedProfile(string root, string[] args)
    {
        int index = Array.IndexOf(args, "--model-id");
        return index < 0 ? LoadProfile(root) : index + 1 < args.Length
            ? InstrumentModelCatalog.Get(args[index + 1]).Profile : throw new ArgumentException("Missing --model-id");
    }

    static float[] ReadFloats(string path)
    {
        var file = new FileInfo(path);
        if (file.Length > 16 * 1024 * 1024 || file.Length % sizeof(float) != 0) throw new InvalidDataException("Invalid numerical reference size.");
        var bytes = File.ReadAllBytes(path); var values = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
        return values;
    }
}
