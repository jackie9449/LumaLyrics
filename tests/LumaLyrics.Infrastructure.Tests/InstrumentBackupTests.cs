using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using LumaLyrics.Core;

namespace LumaLyrics.Infrastructure.Tests;

public sealed class InstrumentBackupTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "Luma-instrument-backup-" + Guid.NewGuid().ToString("N"));
    static readonly InstrumentRecordingKey Key = new("fixture", "confirmed-recording", 30000, new('a', 64));
    string At(string name) => Path.Combine(root, name);
    public InstrumentBackupTests() { Directory.CreateDirectory(root); Directory.CreateDirectory(At("source")); }
    void Seed()
    {
        using var lyrics = new LyricsRepository(At("source/lyrics.db"));
        using var repository = new InstrumentRepository(At("source"));
        repository.Save(new(Key, "fixture-profile", [new(0, 10000)], [], [], [InstrumentKind.Piano], false, 1),
            new(At("unavailable-original.wav"), Key.AudioSha256!, "explicit-test-file-binding"));
        File.WriteAllBytes(At("source/private-model.onnx"), [1, 2, 3]);
        File.WriteAllBytes(At("source/private-source.wav"), [1, 2, 3]);
    }
    [Fact]
    public void WalSnapshotReadableJsonAndRestoreRemainConsistentWithoutSourceAudioOrModel()
    {
        Seed(); using var live = new InstrumentRepository(At("source"));
        var manifest = PersonalLibraryArchive.Export(At("source"), At("backup"));
        Assert.Contains(manifest.Files, file => file.Path == "state/instruments.db");
        var readable = Assert.Single(manifest.Files, file => file.Kind == "instrument");
        Assert.Contains("fixture-profile", File.ReadAllText(At("backup/" + readable.Path)));
        live.Save(new(Key, "fixture-profile", [new(20000, 30000)], [], [], [], false, 2));
        PersonalLibraryArchive.Restore(At("backup"), At("restored"));
        using var restored = new InstrumentRepository(At("restored"));
        Assert.False(restored.Load(Key, "fixture-profile")!.Complete); Assert.Equal(1, restored.Load(Key, "fixture-profile")!.Revision);
        Assert.Equal(At("unavailable-original.wav"), restored.ReadFileBinding(Key)!.FullPath);
        Assert.DoesNotContain(manifest.Files, file => file.Path.EndsWith(".wav") || file.Path.EndsWith(".onnx"));
        Assert.Throws<IOException>(() => PersonalLibraryArchive.Restore(At("backup"), At("restored")));
    }
    [Fact]
    public void UpdatingJsonAndItsFileHashCannotForgeTheDatabaseEvidence()
    {
        Seed(); var manifest = PersonalLibraryArchive.Export(At("source"), At("backup"));
        var readable = Assert.Single(manifest.Files, file => file.Kind == "instrument");
        string target = At("backup/" + readable.Path);
        var record = JsonNode.Parse(File.ReadAllText(target))!; record["timeline"]!["revision"] = 99;
        File.WriteAllText(target, record.ToJsonString());
        string metadata = At("backup/personal-library.json"); var index = JsonNode.Parse(File.ReadAllText(metadata))!;
        var entry = index["files"]!.AsArray().Single(file => file!["path"]!.GetValue<string>() == readable.Path)!;
        entry["bytes"] = new FileInfo(target).Length; entry["sha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target))).ToLowerInvariant();
        File.WriteAllText(metadata, index.ToJsonString());
        Assert.Throws<InvalidDataException>(() => PersonalLibraryArchive.Validate(At("backup")));
    }
    [Fact]
    public void OldBackupsWithoutInstrumentsRemainAccepted()
    {
        using (var lyrics = new LyricsRepository(At("source/lyrics.db"))) { }
        var manifest = PersonalLibraryArchive.Export(At("source"), At("old-backup"));
        Assert.DoesNotContain(manifest.Files, file => file.Kind == "instrument");
        PersonalLibraryArchive.Restore(At("old-backup"), At("restored"));
        Assert.False(File.Exists(At("restored/instruments.db")));
    }
    public void Dispose()
    { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
}
