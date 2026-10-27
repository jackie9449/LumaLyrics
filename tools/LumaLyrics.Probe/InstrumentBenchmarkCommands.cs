using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;

namespace LumaLyrics.Probe;

internal static class InstrumentBenchmarkCommands
{
    internal static async Task RunAsync(string directory)
    {
        string root = Path.GetFullPath(directory);
        PersonalLibraryArchive.EnsureOutsideSoftware(root);
        using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "instrument-model-export.json")));
        var data = metadata.RootElement;
        var json = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        var labels = data.GetProperty("labelMap").Deserialize<Dictionary<int, InstrumentKind>>(json)!;
        var profile = new InstrumentInferenceProfile
        {
            Id = "yamnet-waveform-experimental", Sha256 = data.GetProperty("modelSha256").GetString()!,
            SampleRate = data.GetProperty("sampleRate").GetInt32(), WindowSamples = data.GetProperty("windowSamples").GetInt32(),
            HopSamples = data.GetProperty("hopSamples").GetInt32(), InputName = data.GetProperty("inputName").GetString()!,
            OutputName = data.GetProperty("outputName").GetString()!, OutputClassCount = data.GetProperty("outputClassCount").GetInt32(),
            PreprocessingVersion = data.GetProperty("preprocessingVersion").GetString()!, LabelMap = labels,
            Thresholds = labels.Values.Distinct().ToDictionary(kind => kind, _ => new ActivityParameters(.65f, .4f, 2, 750))
        };
        await using var classifier = new OnnxInstrumentClassifier(Path.Combine(root, "yamnet-waveform.onnx"), profile);
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

    static float[] ReadFloats(string path)
    {
        var file = new FileInfo(path);
        if (file.Length > 16 * 1024 * 1024 || file.Length % sizeof(float) != 0) throw new InvalidDataException("Invalid numerical reference size.");
        var bytes = File.ReadAllBytes(path); var values = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
        return values;
    }
}
