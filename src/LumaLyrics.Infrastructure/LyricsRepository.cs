using LumaLyrics.Core;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;
namespace LumaLyrics.Infrastructure;

public sealed class LyricsRepository : ILyricsRepository, IDisposable
{
    readonly SqliteConnection db;
    public LyricsRepository(string path)
    {
        db = new($"Data Source={path}"); db.Open();
        using(var version=db.CreateCommand())
        {
            version.CommandText="PRAGMA user_version";var old=Convert.ToInt32(version.ExecuteScalar());
            if(old is >0 and <5)
            {
                var backup=path+".pre-v5.backup";
                if(!File.Exists(backup)){using var destination=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=backup,Pooling=false}.ToString());destination.Open();db.BackupDatabase(destination);}
            }
        }
        int schemaVersion;using(var version=db.CreateCommand()){version.CommandText="PRAGMA user_version";schemaVersion=Convert.ToInt32(version.ExecuteScalar());if(schemaVersion>5)throw new InvalidDataException("歌词数据库来自更新版本，请升级浮光歌词。");}
        Execute("""
            PRAGMA journal_mode=WAL;
            PRAGMA foreign_keys=ON;
            CREATE TABLE IF NOT EXISTS Tracks(Id TEXT PRIMARY KEY, Fingerprint TEXT NOT NULL, Title TEXT, Artist TEXT, Album TEXT, Duration REAL);
            CREATE INDEX IF NOT EXISTS IX_Tracks_Fingerprint ON Tracks(Fingerprint);
            CREATE TABLE IF NOT EXISTS LyricsDocuments(Id TEXT PRIMARY KEY, Raw TEXT NOT NULL, Source TEXT, Hash TEXT, FetchedUtc TEXT, Instrumental INTEGER, ParserVersion INTEGER DEFAULT 1, Plain INTEGER DEFAULT 0, ProviderJson TEXT);
            CREATE TABLE IF NOT EXISTS TrackLyricsBindings(Id TEXT PRIMARY KEY, TrackId TEXT REFERENCES Tracks(Id), DocumentId TEXT REFERENCES LyricsDocuments(Id), Manual INTEGER, UNIQUE(TrackId));
            CREATE TABLE IF NOT EXISTS SyncOffsets(BindingId TEXT PRIMARY KEY REFERENCES TrackLyricsBindings(Id) ON DELETE CASCADE, Offset REAL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS LookupFailures(Fingerprint TEXT PRIMARY KEY, ExpiresUtc TEXT);
            CREATE TABLE IF NOT EXISTS LibraryTracks(Source TEXT, PersistentId TEXT, Title TEXT, Artist TEXT, Album TEXT, Duration REAL, Fingerprint TEXT, State TEXT DEFAULT 'Pending', Detail TEXT, RetryUtc TEXT, Active INTEGER DEFAULT 1, LyricsKind TEXT DEFAULT '', PRIMARY KEY(Source,PersistentId));
            CREATE INDEX IF NOT EXISTS IX_LibraryTracks_State ON LibraryTracks(Active,State);
            """);
        var columns=new HashSet<string>();using(var c=Command("PRAGMA table_info(LyricsDocuments)")){using var r=c.ExecuteReader();while(r.Read())columns.Add(r.GetString(1));}
        var libraryColumns=new HashSet<string>();using(var c=Command("PRAGMA table_info(LibraryTracks)")){using var r=c.ExecuteReader();while(r.Read())libraryColumns.Add(r.GetString(1));}
        using(var migration=db.BeginTransaction())
        {
            if(!columns.Contains("Plain"))Execute("ALTER TABLE LyricsDocuments ADD COLUMN Plain INTEGER DEFAULT 0");
            if(!columns.Contains("ProviderJson"))Execute("ALTER TABLE LyricsDocuments ADD COLUMN ProviderJson TEXT");
            if(!libraryColumns.Contains("LyricsKind"))Execute("ALTER TABLE LibraryTracks ADD COLUMN LyricsKind TEXT DEFAULT ''");
            if(schemaVersion<5)Execute("UPDATE LibraryTracks SET State='Pending',RetryUtc=NULL,LyricsKind='' WHERE State='Cached'");
            Execute("PRAGMA user_version=5");migration.Commit();
        }
    }
    SqliteCommand Command(string sql, params (string, object?)[] parameters)
    {
        var c = db.CreateCommand(); c.CommandText = sql;
        foreach (var (key, value) in parameters) c.Parameters.AddWithValue(key, value ?? DBNull.Value);
        return c;
    }
    void Execute(string sql, params (string, object?)[] p) { using var c = Command(sql, p); c.ExecuteNonQuery(); }
    string? FindTrack(Track track)
    {
        using var c = Command("SELECT Id, Duration FROM Tracks WHERE Fingerprint=$f", ("$f", track.Fingerprint));
        using var r = c.ExecuteReader();
        while (r.Read()) if (!track.DurationMs.HasValue || r.IsDBNull(1) || Math.Abs(r.GetDouble(1) - track.DurationMs.Value) <= 5000) return r.GetString(0);
        return null;
    }
    public LyricsBinding? Find(Track track)
    {
        track=TrackMetadata.FromAppleSession(track);
        var id = FindTrack(track);
        // Keep bindings created by 0.1's combined GSMTC artist field usable.
        if(id is null)id=FindTrack(track with{Artist=track.Artist+" — "+track.Album,Album=""});
        if (id is null) return null;
        if (track.DurationMs.HasValue) Execute("UPDATE Tracks SET Duration=$d WHERE Id=$id", ("$d", track.DurationMs), ("$id", id));
        using var c = Command("SELECT b.Id,b.TrackId,d.Raw,d.Source,b.Manual,COALESCE(o.Offset,0),d.Instrumental,d.Plain,d.ProviderJson FROM TrackLyricsBindings b JOIN LyricsDocuments d ON b.DocumentId=d.Id LEFT JOIN SyncOffsets o ON o.BindingId=b.Id WHERE b.TrackId=$id", ("$id", id));
        using var r = c.ExecuteReader();
        return r.Read() ? new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetBoolean(4), r.GetDouble(5), r.GetBoolean(6), r.GetBoolean(7),r.IsDBNull(8)?null:r.GetString(8)) : null;
    }
    public LyricsBinding Save(Track track, string raw, string source, bool manual, bool instrumental = false, bool plain = false, string? providerJson = null)
    {
        track=TrackMetadata.FromAppleSession(track);
        var current = Find(track); if (current?.Manual == true && !manual) return current;
        using var tx = db.BeginTransaction();
        string trackId = FindTrack(track) ?? Guid.NewGuid().ToString("N");
        Execute("INSERT INTO Tracks VALUES($id,$f,$t,$a,$al,$d) ON CONFLICT(Id) DO UPDATE SET Duration=COALESCE(excluded.Duration,Tracks.Duration)", ("$id", trackId), ("$f", track.Fingerprint), ("$t", track.Title), ("$a", track.Artist), ("$al", track.Album), ("$d", track.DurationMs));
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))); string docId = Guid.NewGuid().ToString("N");
        Execute("INSERT INTO LyricsDocuments(Id,Raw,Source,Hash,FetchedUtc,Instrumental,Plain,ProviderJson) VALUES($id,$raw,$s,$h,$utc,$i,$p,$json)", ("$id", docId), ("$raw", raw), ("$s", source), ("$h", hash), ("$utc", DateTimeOffset.UtcNow.ToString("O")), ("$i", instrumental), ("$p", plain), ("$json", providerJson));
        // A different document gets a fresh offset. Never carry calibration across lyric versions.
        Execute("DELETE FROM TrackLyricsBindings WHERE TrackId=$t", ("$t", trackId));
        string bindingId = Guid.NewGuid().ToString("N");
        Execute("INSERT INTO TrackLyricsBindings VALUES($id,$t,$d,$m)", ("$id", bindingId), ("$t", trackId), ("$d", docId), ("$m", manual));
        bool automaticReady=LibraryPrefetcher.QualityOf(new(bindingId,trackId,raw,source,manual,0,instrumental,plain,providerJson),track);
        Execute("UPDATE LibraryTracks SET State=$state,LyricsKind=$kind,RetryUtc=NULL WHERE Fingerprint=$f AND ($d IS NULL OR Duration IS NULL OR ABS(Duration-$d)<=5000)",("$state",manual?"Manual":automaticReady?"Cached":"Incomplete"),("$kind",automaticReady?instrumental?"Instrumental":"Synced":""),("$f",track.Fingerprint),("$d",track.DurationMs));
        tx.Commit(); ClearFailure(track);
        return new(bindingId, trackId, raw, source, manual, 0, instrumental, plain,providerJson);
    }
    public void SetOffset(string id, double ms) => Execute("INSERT INTO SyncOffsets VALUES($id,$o) ON CONFLICT(BindingId) DO UPDATE SET Offset=excluded.Offset", ("$id", id), ("$o", ms));
    public void RestoreAutomatic(Track track) { track=TrackMetadata.FromAppleSession(track);var id = FindTrack(track); Execute("DELETE FROM TrackLyricsBindings WHERE TrackId=$id", ("$id", id));Execute("UPDATE LibraryTracks SET State='Pending',LyricsKind='',RetryUtc=NULL WHERE Fingerprint=$f",("$f",track.Fingerprint));ClearFailure(track); }
    public bool HasFailure(Track track)
    {
        using var c = Command("SELECT ExpiresUtc FROM LookupFailures WHERE Fingerprint=$f", ("$f", track.Fingerprint));
        return c.ExecuteScalar() is string s && DateTimeOffset.TryParse(s, out var t) && t > DateTimeOffset.UtcNow;
    }
    public void RememberFailure(Track track) => Execute("INSERT INTO LookupFailures VALUES($f,$e) ON CONFLICT(Fingerprint) DO UPDATE SET ExpiresUtc=excluded.ExpiresUtc", ("$f", track.Fingerprint), ("$e", DateTimeOffset.UtcNow.AddHours(6).ToString("O")));
    public void ClearFailure(Track track) => Execute("DELETE FROM LookupFailures WHERE Fingerprint=$f", ("$f", track.Fingerprint));
    public void ClearNetworkCache()
    {
        Execute("DELETE FROM TrackLyricsBindings WHERE Manual=0; DELETE FROM LyricsDocuments WHERE Source LIKE 'LRCLIB%' AND Id NOT IN (SELECT DocumentId FROM TrackLyricsBindings); DELETE FROM LookupFailures; UPDATE LibraryTracks SET State='Pending',RetryUtc=NULL,LyricsKind='' WHERE State='Cached';");
    }
    public void SyncLibrary(LibrarySnapshot snapshot)
    {
        using var tx=db.BeginTransaction();Execute("UPDATE LibraryTracks SET Active=0 WHERE Source=$s",("$s",snapshot.Path));
        foreach(var item in snapshot.Tracks)
        {
            var t=item.Track;
            Execute("""
                INSERT INTO LibraryTracks(Source,PersistentId,Title,Artist,Album,Duration,Fingerprint,Active)
                VALUES($s,$id,$t,$ar,$al,$d,$f,1)
                ON CONFLICT(Source,PersistentId) DO UPDATE SET
                State=CASE WHEN LibraryTracks.Fingerprint<>excluded.Fingerprint OR ABS(COALESCE(LibraryTracks.Duration,0)-COALESCE(excluded.Duration,0))>5000 THEN 'Pending' ELSE LibraryTracks.State END,
                RetryUtc=CASE WHEN LibraryTracks.Fingerprint<>excluded.Fingerprint OR ABS(COALESCE(LibraryTracks.Duration,0)-COALESCE(excluded.Duration,0))>5000 THEN NULL ELSE LibraryTracks.RetryUtc END,
                Title=excluded.Title,Artist=excluded.Artist,Album=excluded.Album,Duration=excluded.Duration,Fingerprint=excluded.Fingerprint,Active=1
                """,("$s",snapshot.Path),("$id",item.PersistentId),("$t",t.Title),("$ar",t.Artist),("$al",t.Album),("$d",t.DurationMs),("$f",t.Fingerprint));
        }
        tx.Commit();
    }
    public IReadOnlyList<LibraryQueueItem> GetLibraryQueue(bool includeDeferred=false,bool cachedOnly=false)
    {
        using var c=Command("SELECT Source,PersistentId,Title,Artist,Album,Duration FROM LibraryTracks WHERE Active=1 AND (State NOT IN ('Cached','Manual') OR $all=1 OR $cached=1) AND ($cached=0 OR State='Cached') AND ($all=1 OR RetryUtc IS NULL OR RetryUtc<$now) ORDER BY CASE State WHEN 'Pending' THEN 0 WHEN 'Error' THEN 2 ELSE 1 END,Source,PersistentId",("$all",includeDeferred),("$cached",cachedOnly),("$now",DateTimeOffset.UtcNow.ToString("O")));
        using var r=c.ExecuteReader();var result=new List<LibraryQueueItem>();
        while(r.Read())result.Add(new(r.GetString(0),r.GetString(1),new(r.GetString(2),r.GetString(3),r.GetString(4),r.IsDBNull(5)?null:r.GetDouble(5))));return result;
    }
    public LibraryProgress GetLibraryProgress(string message="",bool running=false)
    {
        using var c=Command("SELECT State,LyricsKind,COUNT(*) FROM LibraryTracks WHERE Active=1 GROUP BY State,LyricsKind");using var r=c.ExecuteReader();var counts=new Dictionary<string,int>();int synced=0,instrumental=0;
        while(r.Read()){var state=r.GetString(0);int count=r.GetInt32(2);counts[state]=counts.GetValueOrDefault(state)+count;if(state=="Cached"){if(r.GetString(1)=="Instrumental")instrumental+=count;else if(r.GetString(1)=="Synced")synced+=count;}}
        return new(counts.Values.Sum(),counts.GetValueOrDefault("Cached"),counts.GetValueOrDefault("Missing")+counts.GetValueOrDefault("Incomplete"),counts.GetValueOrDefault("Error"),running,message){Synced=synced,Instrumental=instrumental,Manual=counts.GetValueOrDefault("Manual")};
    }
    public void SetLibraryState(LibraryQueueItem item,string state,string detail,string kind="")
    {
        Execute("UPDATE LibraryTracks SET State=$state,Detail=$detail,LyricsKind=$kind,RetryUtc=$retry WHERE Source=$s AND PersistentId=$id",("$state",state),("$detail",detail),("$kind",kind),("$retry",state is "Cached" or "Manual" or "Pending"?null:DateTimeOffset.UtcNow.Add(state=="Error"?TimeSpan.FromMinutes(15):TimeSpan.FromDays(1)).ToString("O")),("$s",item.Source),("$id",item.PersistentId));
    }
    public void ResetLibraryRetries()=>Execute("UPDATE LibraryTracks SET RetryUtc=NULL WHERE State<>'Cached'");
    public void Dispose() => db.Dispose();
}
