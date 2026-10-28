using System.Text.Json;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;

namespace LumaLyrics.Probe;

/// <summary>Controlled whole-sound stop/resume timing, never a semantic accuracy result.</summary>
internal static class InstrumentTransitionProbe
{
    internal static async Task RunAsync(string[] args)
    {
        string Value(string flag)
        { int i = Array.IndexOf(args, flag); return i >= 0 && i + 1 < args.Length ? args[i + 1] : throw new ArgumentException("Missing " + flag); }
        string root = Path.GetFullPath(Value("--data-dir")); PersonalLibraryArchive.EnsureOutsideSoftware(root);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Select an existing personal report directory.");
        var profile = InstrumentModelCatalog.Get(Value("--model-id")).Profile;
        using var scores = JsonDocument.Parse(await File.ReadAllTextAsync(Value("--scores")));
        using var events = JsonDocument.Parse(await File.ReadAllTextAsync(Value("--boundaries")));
        if (scores.RootElement.GetProperty("modelSha256").GetString() != profile.Sha256) throw new InvalidDataException("Score model mismatch.");
        var expected = events.RootElement;
        var record = scores.RootElement.GetProperty("recordings").EnumerateArray().Single(r => r.GetProperty("recordingId").GetString() == expected.GetProperty("recordingId").GetString());
        if (record.GetProperty("audioSha256").GetString() != expected.GetProperty("audioSha256").GetString()) throw new InvalidDataException("Controlled recording mismatch.");
        double[] ends = record.GetProperty("endsMs").Deserialize<double[]>()!;
        if (ends.Length > 500000 || ends.Any(t => !double.IsFinite(t) || t < 0) || !ends.SequenceEqual(ends.Distinct().Order())) throw new InvalidDataException("Invalid score times.");
        var boundaries = expected.GetProperty("boundaries").EnumerateArray().Select(e => (At: e.GetProperty("atMs").GetDouble(), Active: e.GetProperty("active").GetBoolean())).ToArray();
        if (boundaries.Any(b => !double.IsFinite(b.At) || b.At < 0) || !boundaries.Select(b => b.At).SequenceEqual(boundaries.Select(b => b.At).Distinct().Order())) throw new InvalidDataException("Invalid transition times.");
        var results = new List<object>();
        foreach (string name in expected.GetProperty("kinds").Deserialize<string[]>()!)
        {
            var kind = Enum.Parse<InstrumentKind>(name);
            var filter = new InstrumentActivityFilter(profile.Thresholds[kind]);
            float[] values = record.GetProperty("scores").GetProperty(name).Deserialize<float[]>()!;
            if (values.Length != ends.Length) throw new InvalidDataException("Score count mismatch.");
            bool[] active = values.Select((score, index) => filter.Update(score, ends[index])).ToArray();
            for (int b = 0; b < boundaries.Length; b++)
            {
                var boundary = boundaries[b]; double until = b + 1 < boundaries.Length ? boundaries[b + 1].At : double.PositiveInfinity;
                int before = Array.FindLastIndex(ends, t => t < boundary.At);
                int after = -1;
                for (int i = 0; i < ends.Length; i++)
                    if (ends[i] >= boundary.At && ends[i] < until && active[i] == boundary.Active) { after = i; break; }
                bool valid = before >= 0 && active[before] != boundary.Active && after >= 0;
                results.Add(new { kind = name, atMs = boundary.At, targetActive = boundary.Active,
                    status = valid ? "ObservedTransition" : before < 0 || active[before] == boundary.Active ? "NoOppositeStateBeforeBoundary" : "NoMatchingTransition",
                    delayMs = valid ? (double?)(ends[after] - boundary.At) : null });
            }
        }
        string output = Path.Combine(root, "instrument-transitions-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { modelId = profile.Id, scope = "controlled whole-sound stop/resume; window/filter delay only",
            results, accuracyValidated = false }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(new { modelId = profile.Id, results, accuracyValidated = false }));
    }
}
