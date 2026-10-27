using LumaLyrics.Core;
using Microsoft.Data.Sqlite;

namespace LumaLyrics.Infrastructure.Tests;

public sealed class InstrumentStorageTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "Luma-instrument-storage-" + Guid.NewGuid().ToString("N"));
    static readonly InstrumentRecordingKey A = new("fixture-library", "studio-id", 30000, null);
    public InstrumentStorageTests() => Directory.CreateDirectory(root);
    static InstrumentTimeline Partial(InstrumentRecordingKey? key = null, long revision = 1) => new(key ?? A, "profile-a",
        [new(0, 10000)], [new(0, 10000, new() { [InstrumentKind.Piano] = .9f })],
        [new(InstrumentKind.Piano, 1000, 9000)], [InstrumentKind.Piano], false, revision);

    [Fact]
    public void RecordingVersionsAndProfilesNeverShareResults()
    {
        using var repository = new InstrumentRepository(root);
        repository.Save(Partial());
        Assert.False(repository.Load(A, "profile-a")!.Complete);
        Assert.Null(repository.Load(A with { PersistentId = "live-id" }, "profile-a"));
        Assert.Null(repository.Load(A, "profile-b"));
        var firstFile = A with { AudioSha256 = new('a', 64) };
        var otherFile = A with { AudioSha256 = new('b', 64) };
        repository.Save(Partial(firstFile)); repository.Save(Partial(otherFile));
        Assert.Equal(3, repository.FindRecordings(A.Source, A.PersistentId).Count);
        Assert.Null(InstrumentRecordingIdentity.Resolve(repository.FindRecordings(A.Source, A.PersistentId), 30000, null));
        Assert.Equal(firstFile, InstrumentRecordingIdentity.Resolve(repository.FindRecordings(A.Source, A.PersistentId), 30001, firstFile.AudioSha256, 1));
        Assert.Null(InstrumentRecordingIdentity.Resolve([firstFile], 30002, firstFile.AudioSha256, 1));
    }

    [Fact]
    public void ReplaysReplaceScoresWithoutDuplicatingOrFillingSeekGaps()
    {
        using var repository = new InstrumentRepository(root);
        repository.Save(Partial()); repository.Save(Partial());
        var later = new InstrumentTimeline(A, "profile-a", [new(20000, 30000)],
            [new(20000, 30000, new() { [InstrumentKind.Strings] = .8f })],
            [new(InstrumentKind.Strings, 21000, 29000)], [InstrumentKind.Strings], false, 2);
        repository.Save(later);
        var saved = repository.Load(A, "profile-a")!;
        Assert.False(saved.Complete);
        Assert.Equal(2, saved.Coverage.Length); Assert.Equal(2, saved.Scores.Length);
        Assert.Equal(new[] { InstrumentKind.Piano, InstrumentKind.Strings }, saved.Roster);
        repository.Save(Partial(revision: 3));
        Assert.Equal(2, repository.Load(A, "profile-a")!.Scores.Length);
        repository.Save(later with { Revision = 1 });
        Assert.Equal(3, repository.Load(A, "profile-a")!.Revision);
    }

    [Fact]
    public void InvalidAndForgedCompleteWritesLeavePreviousTransactionIntact()
    {
        using var repository = new InstrumentRepository(root);
        repository.Save(Partial());
        Assert.Throws<InvalidDataException>(() => repository.Save(Partial(revision: 2) with { Complete = true }));
        Assert.Throws<InvalidDataException>(() => repository.Save(Partial(revision: 2) with
        { Scores = [new(0, 10000, new() { [InstrumentKind.Piano] = float.NaN })] }));
        Assert.Throws<InvalidDataException>(() => repository.Save(Partial(revision: 2) with
        { Intervals = [new(InstrumentKind.Piano, 1000, 20000)] }));
        Assert.Equal(1, repository.Load(A, "profile-a")!.Revision);
        repository.Save(new(A, "profile-a", [new(10000, 30000)], [], [], [], false, 2));
        Assert.True(repository.Load(A, "profile-a")!.Complete);
    }

    [Fact]
    public void ClearOnlyDeletesAnalysisAndRetainsTheUserFile()
    {
        string userFile = Path.Combine(root, "authored-source.wav"); File.WriteAllBytes(userFile, [1, 2, 3]);
        using var repository = new InstrumentRepository(root);
        repository.Save(Partial()); repository.Clear();
        Assert.Null(repository.Load(A, "profile-a"));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(userFile));
    }

    [Fact]
    public void TamperedCompletenessAndFutureSchemaAreRejected()
    {
        using (var repository = new InstrumentRepository(root)) repository.Save(Partial());
        using (var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(root, "instruments.db"), Pooling = false }.ToString()))
        {
            db.Open(); using var command = db.CreateCommand();
            command.CommandText = "UPDATE Analyses SET Complete=1"; command.ExecuteNonQuery();
        }
        using (var repository = new InstrumentRepository(root))
            Assert.Throws<InvalidDataException>(() => repository.Load(A, "profile-a"));
        using (var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(root, "instruments.db"), Pooling = false }.ToString()))
        {
            db.Open(); using var command = db.CreateCommand(); command.CommandText = "PRAGMA user_version=99"; command.ExecuteNonQuery();
        }
        Assert.Throws<InvalidDataException>(() => new InstrumentRepository(root));
    }

    [Fact]
    public void TimelinesRejectUntimedAndOldRecordingFramesWithoutChangingValidCoverage()
    {
        var builder = new InstrumentTimelineBuilder(A, "profile-a");
        var epoch = new InstrumentEpoch("fixture-session", 1, 1, 0);
        var first = new InstrumentFrame(epoch, 100, 0, 10000, "profile-a", new Dictionary<InstrumentKind, float> { [InstrumentKind.Piano] = .9f });
        builder.Add(first, [new(InstrumentKind.Piano, 1000, 9000)]);
        Assert.Throws<InvalidDataException>(() => builder.Add(first with { StartMs = null, EndMs = null }, []));
        Assert.Throws<InvalidDataException>(() => builder.Add(first with { Epoch = epoch with { Revision = 2 } }, []));
        builder.Add(first with { Epoch = epoch with { Discontinuity = 1 }, StartMs = 20000, EndMs = 30000 }, []);
        var timeline = builder.Build();
        Assert.False(timeline.Complete); Assert.Equal(2, timeline.Coverage.Length);
        Assert.Single(timeline.Roster); Assert.Equal(InstrumentKind.Piano, timeline.Roster[0]);
    }

    [Fact]
    public void AFailedDatabaseInsertRollsBackEveryChangedTable()
    {
        using var repository = new InstrumentRepository(root); repository.Save(Partial());
        using (var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(root, "instruments.db"), Pooling = false }.ToString()))
        {
            db.Open(); using var trigger = db.CreateCommand();
            trigger.CommandText = "CREATE TRIGGER fail_scores BEFORE INSERT ON Scores BEGIN SELECT RAISE(ABORT,'authored interruption'); END"; trigger.ExecuteNonQuery();
        }
        Assert.Throws<SqliteException>(() => repository.Save(Partial(revision: 2) with
        { Scores = [new(0, 10000, new() { [InstrumentKind.Piano] = .3f })] }));
        var retained = repository.Load(A, "profile-a")!;
        Assert.Equal(1, retained.Revision); Assert.Equal(.9f, Assert.Single(retained.Scores).Scores[InstrumentKind.Piano]);
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
