using System.Text.Json;
using LumaLyrics.Core;
using Microsoft.Data.Sqlite;

namespace LumaLyrics.Infrastructure;

public sealed record InstrumentFileBinding(string FullPath, string ContentSha256, string Evidence);

/// <summary>Personal, recording/profile-scoped transactions. No playback-name lookup is provided.</summary>
public sealed class InstrumentRepository : IDisposable
{
    readonly SqliteConnection db;
    readonly object gate = new();
    bool disposed;
    public InstrumentRepository(string dataRoot, bool readOnly = false)
    {
        string root = InstrumentStoragePaths.CheckRoot(dataRoot);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("The selected instrument data directory is unavailable.");
        string path = Path.Combine(root, "instruments.db");
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Instrument database cannot be a link.");
        db = new(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false, Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate }.ToString());
        try
        {
            db.Open();
            using var version = Command("PRAGMA user_version");
            int current = Convert.ToInt32(version.ExecuteScalar());
            if (current is not (0 or 1) || readOnly && current != 1) throw new InvalidDataException("Unsupported instrument database version.");
            if (readOnly) return;
            using var schema = Command("""
                PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;
                CREATE TABLE IF NOT EXISTS Recordings(Id TEXT PRIMARY KEY, Source TEXT NOT NULL, PersistentId TEXT NOT NULL, IdentityJson TEXT NOT NULL, BindingJson TEXT);
                CREATE INDEX IF NOT EXISTS RecordingReferences ON Recordings(Source,PersistentId);
                CREATE TABLE IF NOT EXISTS Analyses(RecordingId TEXT NOT NULL REFERENCES Recordings(Id), ProfileId TEXT NOT NULL,
                  Revision INTEGER NOT NULL, Complete INTEGER NOT NULL, RosterJson TEXT NOT NULL, PRIMARY KEY(RecordingId,ProfileId));
                CREATE TABLE IF NOT EXISTS Coverage(RecordingId TEXT NOT NULL, ProfileId TEXT NOT NULL, StartMs REAL NOT NULL, EndMs REAL NOT NULL,
                  FOREIGN KEY(RecordingId,ProfileId) REFERENCES Analyses(RecordingId,ProfileId) ON DELETE CASCADE);
                CREATE TABLE IF NOT EXISTS Scores(RecordingId TEXT NOT NULL, ProfileId TEXT NOT NULL, StartMs REAL NOT NULL, EndMs REAL NOT NULL, ScoresJson TEXT NOT NULL,
                  FOREIGN KEY(RecordingId,ProfileId) REFERENCES Analyses(RecordingId,ProfileId) ON DELETE CASCADE);
                CREATE TABLE IF NOT EXISTS Intervals(RecordingId TEXT NOT NULL, ProfileId TEXT NOT NULL, Kind INTEGER NOT NULL, StartMs REAL NOT NULL, EndMs REAL NOT NULL,
                  FOREIGN KEY(RecordingId,ProfileId) REFERENCES Analyses(RecordingId,ProfileId) ON DELETE CASCADE);
                CREATE INDEX IF NOT EXISTS ScoresByTime ON Scores(RecordingId,ProfileId,StartMs);
                CREATE INDEX IF NOT EXISTS IntervalsByTime ON Intervals(RecordingId,ProfileId,StartMs);
                PRAGMA user_version=1;
                """);
            schema.ExecuteNonQuery();
        }
        catch { db.Dispose(); throw; }
    }

    SqliteCommand Command(string sql, SqliteTransaction? transaction = null, params (string Name, object Value)[] parameters)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var command = db.CreateCommand(); command.CommandText = sql; command.Transaction = transaction;
        foreach (var item in parameters) command.Parameters.AddWithValue(item.Name, item.Value);
        return command;
    }
    static T Read<T>(string text)
    {
        try { return JsonSerializer.Deserialize<T>(text) ?? throw new InvalidDataException("Empty instrument record."); }
        catch (JsonException error) { throw new InvalidDataException("Invalid instrument record JSON.", error); }
    }

    public IReadOnlyList<InstrumentRecordingKey> FindRecordings(string source, string persistentId)
    {
        lock (gate)
        {
            using var command = Command("SELECT Id,IdentityJson FROM Recordings WHERE Source=$source AND PersistentId=$id ORDER BY Id", null, ("$source", source), ("$id", persistentId));
            using var reader = command.ExecuteReader(); var keys = new List<InstrumentRecordingKey>();
            while (reader.Read())
            {
                var key = Read<InstrumentRecordingKey>(reader.GetString(1));
                if (InstrumentRecordingIdentity.KeyId(key) != reader.GetString(0) || key.Source != source || key.PersistentId != persistentId)
                    throw new InvalidDataException("Instrument recording identity was modified.");
                keys.Add(key);
            }
            return keys;
        }
    }

    public InstrumentTimeline? Load(InstrumentRecordingKey recording, string profileId)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            using var transaction = db.BeginTransaction(deferred: true);
            var value = LoadCore(recording, profileId, transaction);
            transaction.Commit(); return value;
        }
    }

    InstrumentTimeline? LoadCore(InstrumentRecordingKey recording, string profileId, SqliteTransaction? transaction)
    {
        string id = InstrumentRecordingIdentity.KeyId(recording);
        using var metadata = Command("SELECT a.Revision,a.Complete,a.RosterJson,r.IdentityJson FROM Analyses a JOIN Recordings r ON r.Id=a.RecordingId WHERE a.RecordingId=$id AND a.ProfileId=$profile", transaction, ("$id", id), ("$profile", profileId));
        long revision; bool complete; InstrumentKind[] roster;
        using (var reader = metadata.ExecuteReader())
        {
            if (!reader.Read()) return null;
            revision = reader.GetInt64(0); complete = reader.GetBoolean(1); roster = Read<InstrumentKind[]>(reader.GetString(2));
            recording = Read<InstrumentRecordingKey>(reader.GetString(3));
            if (InstrumentRecordingIdentity.KeyId(recording) != id) throw new InvalidDataException("Modified recording identity.");
        }
        var coverage = new List<InstrumentSpan>(); var scores = new List<InstrumentScorePoint>(); var intervals = new List<InstrumentInterval>();
        using (var command = Command("SELECT StartMs,EndMs FROM Coverage WHERE RecordingId=$id AND ProfileId=$profile ORDER BY StartMs", transaction, ("$id", id), ("$profile", profileId)))
        using (var reader = command.ExecuteReader()) while (reader.Read()) coverage.Add(new(reader.GetDouble(0), reader.GetDouble(1)));
        using (var command = Command("SELECT StartMs,EndMs,ScoresJson FROM Scores WHERE RecordingId=$id AND ProfileId=$profile ORDER BY StartMs,EndMs", transaction, ("$id", id), ("$profile", profileId)))
        using (var reader = command.ExecuteReader()) while (reader.Read()) scores.Add(new(reader.GetDouble(0), reader.GetDouble(1), Read<Dictionary<InstrumentKind, float>>(reader.GetString(2))));
        using (var command = Command("SELECT Kind,StartMs,EndMs FROM Intervals WHERE RecordingId=$id AND ProfileId=$profile ORDER BY Kind,StartMs", transaction, ("$id", id), ("$profile", profileId)))
        using (var reader = command.ExecuteReader()) while (reader.Read()) intervals.Add(new((InstrumentKind)reader.GetInt32(0), reader.GetDouble(1), reader.GetDouble(2)));
        try
        {
            var result = InstrumentTimelineValidation.Normalize(new(recording, profileId, coverage.ToArray(), scores.ToArray(), intervals.ToArray(), roster, complete, revision));
            if (result.Complete != complete) throw new InvalidDataException("Stored completeness does not match coverage.");
            return result;
        }
        catch (JsonException error) { throw new InvalidDataException("Invalid instrument JSON.", error); }
    }

    public void Save(InstrumentTimeline timeline) => Save(timeline, null);
    public void Save(InstrumentTimeline timeline, InstrumentFileBinding? binding, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var incoming = InstrumentTimelineValidation.Normalize(timeline);
        if (binding is not null) ValidateBinding(binding, incoming.Recording);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            token.ThrowIfCancellationRequested();
            using var transaction = db.BeginTransaction();
            var old = LoadCore(incoming.Recording, incoming.ProfileId, transaction);
            if (old is not null && old.Revision > incoming.Revision) return;
            if (old is not null && old.Revision == incoming.Revision)
            {
                if (JsonSerializer.Serialize(old) != JsonSerializer.Serialize(InstrumentTimelineValidation.Merge(old, incoming)))
                    throw new InvalidOperationException("An equal revision cannot replace different instrument results.");
                return;
            }
            var value = old is null ? incoming : InstrumentTimelineValidation.Merge(old, incoming);
            string id = InstrumentRecordingIdentity.KeyId(value.Recording), profile = value.ProfileId;
            using (var identity = Command("INSERT INTO Recordings(Id,Source,PersistentId,IdentityJson,BindingJson) VALUES($id,$source,$persistent,$identity,$binding) ON CONFLICT(Id) DO UPDATE SET BindingJson=COALESCE(excluded.BindingJson,Recordings.BindingJson)", transaction,
                ("$id", id), ("$source", value.Recording.Source), ("$persistent", value.Recording.PersistentId), ("$identity", JsonSerializer.Serialize(value.Recording)), ("$binding", binding is null ? DBNull.Value : JsonSerializer.Serialize(binding)))) identity.ExecuteNonQuery();
            using (var delete = Command("DELETE FROM Analyses WHERE RecordingId=$id AND ProfileId=$profile", transaction, ("$id", id), ("$profile", profile))) delete.ExecuteNonQuery();
            using (var analysis = Command("INSERT INTO Analyses VALUES($id,$profile,$revision,$complete,$roster)", transaction, ("$id", id), ("$profile", profile), ("$revision", value.Revision), ("$complete", value.Complete), ("$roster", JsonSerializer.Serialize(value.Roster)))) analysis.ExecuteNonQuery();
            foreach (var span in value.Coverage)
            {
                token.ThrowIfCancellationRequested();
                using var command = Command("INSERT INTO Coverage VALUES($id,$profile,$start,$end)", transaction, ("$id", id), ("$profile", profile), ("$start", span.StartMs), ("$end", span.EndMs)); command.ExecuteNonQuery();
            }
            foreach (var point in value.Scores)
            {
                token.ThrowIfCancellationRequested();
                using var command = Command("INSERT INTO Scores VALUES($id,$profile,$start,$end,$scores)", transaction, ("$id", id), ("$profile", profile), ("$start", point.StartMs), ("$end", point.EndMs), ("$scores", JsonSerializer.Serialize(point.Scores))); command.ExecuteNonQuery();
            }
            foreach (var interval in value.Intervals)
            {
                token.ThrowIfCancellationRequested();
                using var command = Command("INSERT INTO Intervals VALUES($id,$profile,$kind,$start,$end)", transaction, ("$id", id), ("$profile", profile), ("$kind", (int)interval.Kind), ("$start", interval.StartMs), ("$end", interval.EndMs)); command.ExecuteNonQuery();
            }
            token.ThrowIfCancellationRequested(); transaction.Commit();
        }
    }

    public InstrumentFileBinding? ReadFileBinding(InstrumentRecordingKey recording)
    {
        lock (gate)
        {
            using var command = Command("SELECT BindingJson FROM Recordings WHERE Id=$id", null, ("$id", InstrumentRecordingIdentity.KeyId(recording)));
            if (command.ExecuteScalar() is not string json) return null;
            var binding = Read<InstrumentFileBinding>(json); ValidateBinding(binding, recording); return binding;
        }
    }
    static void ValidateBinding(InstrumentFileBinding binding, InstrumentRecordingKey recording)
    {
        if (string.IsNullOrWhiteSpace(binding.FullPath) || binding.FullPath.Length > 32767 || !Path.IsPathFullyQualified(binding.FullPath)
            || string.IsNullOrWhiteSpace(binding.Evidence) || binding.Evidence.Length > 1024
            || !InstrumentInferenceProfile.IsHex(binding.ContentSha256, 64)
            || !string.Equals(binding.ContentSha256, recording.AudioSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("File binding does not match the analyzed content.");
    }
    public IReadOnlyList<InstrumentTimeline> ReadAll()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            using var transaction = db.BeginTransaction(deferred: true);
            var references = new List<(InstrumentRecordingKey Key, string Profile)>();
            using (var command = Command("SELECT r.Id,r.IdentityJson,a.ProfileId FROM Analyses a JOIN Recordings r ON r.Id=a.RecordingId ORDER BY r.Id,a.ProfileId", transaction))
            using (var reader = command.ExecuteReader()) while (reader.Read())
            {
                var key = Read<InstrumentRecordingKey>(reader.GetString(1));
                if (InstrumentRecordingIdentity.KeyId(key) != reader.GetString(0)) throw new InvalidDataException("Modified recording identity.");
                references.Add((key, reader.GetString(2)));
            }
            var values = references.Select(item => LoadCore(item.Key, item.Profile, transaction)!).ToArray();
            transaction.Commit(); return values;
        }
    }
    public void Clear()
    { lock (gate) { using var command = Command("DELETE FROM Analyses"); command.ExecuteNonQuery(); } }
    public void Dispose()
    { lock (gate) { if (!disposed) { disposed = true; db.Dispose(); } } }
}
