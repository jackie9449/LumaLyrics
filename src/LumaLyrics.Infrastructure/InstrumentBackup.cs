using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LumaLyrics.Core;

namespace LumaLyrics.Infrastructure;

public sealed record InstrumentBackupRecord(InstrumentTimeline Timeline, InstrumentFileBinding? Binding);

internal static class InstrumentBackup
{
    static readonly JsonSerializerOptions Json = new()
    { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter() } };
    static string Relative(InstrumentTimeline timeline) => "instruments/" + InstrumentRecordingIdentity.KeyId(timeline.Recording) + "/"
        + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(timeline.ProfileId))).ToLowerInvariant() + ".json";

    internal static void Export(string dataRoot, string staging, PersonalLibraryManifest manifest, CancellationToken token)
    {
        string source = PersonalLibraryArchive.AssetPath(dataRoot, "instruments.db");
        if (!File.Exists(source)) return;
        string state = Path.Combine(staging, "state"), snapshot = Path.Combine(state, "instruments.db");
        PersonalLibrarySnapshot.Backup(source, snapshot);
        PersonalLibrarySnapshot.CheckDatabase(snapshot, false);
        using var repository = new InstrumentRepository(state, true);
        // Every readable record is generated from the completed snapshot, never live tables.
        foreach (var timeline in repository.ReadAll())
        {
            token.ThrowIfCancellationRequested();
            string relative = Relative(timeline), target = PersonalLibraryArchive.AssetPath(staging, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, JsonSerializer.Serialize(new InstrumentBackupRecord(timeline, repository.ReadFileBinding(timeline.Recording)), Json), new UTF8Encoding(false));
            PersonalLibraryArchive.AddFile(staging, relative, "instrument", manifest);
        }
        PersonalLibraryArchive.AddFile(staging, "state/instruments.db", "state", manifest);
    }

    internal static void Validate(string root, PersonalLibraryManifest manifest, CancellationToken token = default)
    {
        var files = manifest.Files.Where(file => file.Kind == "instrument").ToArray();
        bool hasDatabase = manifest.Files.Any(file => file.Path == "state/instruments.db" && file.Kind == "state");
        if (!hasDatabase)
        { if (files.Length > 0) throw new InvalidDataException("Instrument readable records require their database snapshot."); return; }
        string state = Path.Combine(root, "state");
        PersonalLibrarySnapshot.CheckDatabase(PersonalLibraryArchive.AssetPath(root, "state/instruments.db"), false);
        using var repository = new InstrumentRepository(state, true);
        var expected = repository.ReadAll().ToDictionary(Relative);
        if (files.Length != expected.Count) throw new InvalidDataException("Instrument snapshot and readable record counts differ.");
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            if (file.Bytes > 256L * 1024 * 1024 || !expected.TryGetValue(file.Path, out var timeline))
                throw new InvalidDataException("Unexpected instrument backup record.");
            InstrumentBackupRecord record;
            try { record = JsonSerializer.Deserialize<InstrumentBackupRecord>(File.ReadAllText(PersonalLibraryArchive.AssetPath(root, file.Path)), Json) ?? throw new InvalidDataException("Empty instrument backup record."); }
            catch (JsonException error) { throw new InvalidDataException("Invalid instrument backup JSON.", error); }
            var normalized = InstrumentTimelineValidation.Normalize(record.Timeline);
            var authoritative = new InstrumentBackupRecord(timeline, repository.ReadFileBinding(timeline.Recording));
            if (Relative(normalized) != file.Path || JsonSerializer.Serialize(record, Json) != JsonSerializer.Serialize(authoritative, Json))
                throw new InvalidDataException("Instrument readable data differs from its database snapshot.");
        }
    }
}
