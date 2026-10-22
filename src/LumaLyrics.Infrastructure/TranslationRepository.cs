using System.Globalization;
using System.Text.Json;
using LumaLyrics.Core;
using Microsoft.Data.Sqlite;
namespace LumaLyrics.Infrastructure;

public sealed record TranslationQueueItem(string Key,Track Track,string OriginalHash,int Priority);
public sealed record TranslationProgress(int Total,int Prepared,int Chinese,int Instrumental,int MissingOriginal,int MissingTranslation,int Mismatch,int Errors,int Pending,bool Running,string Message)
{
    public int Drafts {get;init;}
    public override string ToString()=>$"资料库 {Total} 首 · 来源译文 {Prepared} · AI 译稿 {Drafts}（待校对）\n中文原词 {Chinese} · 纯音乐 {Instrumental} · 待处理 {Pending}\n原词待确认 {MissingOriginal} · 暂无完整译文 {MissingTranslation} · 对应待核 {Mismatch} · 网络待重试 {Errors}\n{Message}";
}
public sealed record StoredTranslation(Track Track,ChineseTranslation Translation,bool Manual);

public sealed class TranslationRepository : IDisposable
{
    readonly SqliteConnection db;
    public TranslationRepository(string root,bool readOnly=false)
    {
        if(!readOnly){PersonalLibraryArchive.EnsureOutsideSoftware(root);Directory.CreateDirectory(root);DataDirectoryConfiguration.ProtectFromGit(root);}
        db=new(new SqliteConnectionStringBuilder{DataSource=Path.Combine(root,"translations.db"),Mode=readOnly?SqliteOpenMode.ReadOnly:SqliteOpenMode.ReadWriteCreate,Pooling=false}.ToString());
        db.Open();using(var version=Command("PRAGMA user_version")){int v=Convert.ToInt32(version.ExecuteScalar());if(v>1 || readOnly&&v!=1){db.Dispose();throw new InvalidDataException("中文译文资料库版本不兼容，请升级浮光歌词。");}}
        if(!readOnly)Execute("""
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS Translations(Fingerprint TEXT,SourceHash TEXT,Duration REAL,Title TEXT,Artist TEXT,Album TEXT,Content TEXT NOT NULL,Manual INTEGER DEFAULT 0,PRIMARY KEY(Fingerprint,SourceHash));
            CREATE TABLE IF NOT EXISTS PreparationQueue(Id TEXT PRIMARY KEY,Fingerprint TEXT,Title TEXT,Artist TEXT,Album TEXT,Duration REAL,OriginalHash TEXT,Priority INTEGER,State TEXT DEFAULT 'Pending',Detail TEXT DEFAULT '',RetryUtc TEXT,Active INTEGER DEFAULT 1);
            CREATE INDEX IF NOT EXISTS IX_TranslationQueue ON PreparationQueue(Active,State,Priority);
            PRAGMA user_version=1;
            """);
    }
    SqliteCommand Command(string sql,params (string,object?)[] parameters)
    {var c=db.CreateCommand();c.CommandText=sql;foreach(var (key,value) in parameters)c.Parameters.AddWithValue(key,value??DBNull.Value);return c;}
    void Execute(string sql,params (string,object?)[] p){using var c=Command(sql,p);c.ExecuteNonQuery();}
    public static void Validate(ChineseTranslation translation)
    {
        if(!PersonalLibraryArchive.IsHash(translation.SourceHash) || translation.Lines is null || translation.Lines.Count is <1 or >20000 || string.IsNullOrWhiteSpace(translation.Source) || translation.Source.Length>2000 || translation.SourceUrl is null || translation.SourceUrl.Length>4000 || string.IsNullOrWhiteSpace(translation.Review))throw new InvalidDataException("中文译文记录不完整。");
        var indexes=new HashSet<int>();
        foreach(var line in translation.Lines)if(line is null || line.GroupIndex<0 || line.GroupIndex>20000 || !indexes.Add(line.GroupIndex) || string.IsNullOrWhiteSpace(line.Text) || line.Text.Length>4000)throw new InvalidDataException("中文译文的逐行对应无效。");
    }
    public ChineseTranslation? Find(Track input,LyricsDocument document)
    {
        if(document.Instrumental || !document.IsSynced)return null;
        var track=TrackMetadata.FromAppleSession(input);
        using var c=Command("SELECT Content FROM Translations WHERE Fingerprint=$f AND SourceHash=$h AND ($d IS NULL OR Duration IS NULL OR ABS(Duration-$d)<=5000)",("$f",track.Fingerprint),("$h",TranslationAlignment.Hash(document.Raw)),("$d",track.DurationMs));
        if(c.ExecuteScalar() is not string content)return null;
        var translation=Read(content);
        return translation.Lines.All(l=>l.GroupIndex<document.Groups.Count && TranslationAlignment.IsLyricText(document.Groups[l.GroupIndex].Text))?translation:null;
    }
    static ChineseTranslation Read(string content)
    {if(content.Length>4*1024*1024)throw new InvalidDataException("中文译文记录过大。");var t=JsonSerializer.Deserialize<ChineseTranslation>(content)??throw new InvalidDataException("中文译文为空。");Validate(t);return t;}
    public void Save(Track input,ChineseTranslation translation,bool manual=false)
    {
        Validate(translation);var track=TrackMetadata.FromAppleSession(input);
        Execute("""
            INSERT INTO Translations(Fingerprint,SourceHash,Duration,Title,Artist,Album,Content,Manual) VALUES($f,$h,$d,$t,$a,$al,$c,$m)
            ON CONFLICT(Fingerprint,SourceHash) DO UPDATE SET Content=excluded.Content,Manual=excluded.Manual,Duration=excluded.Duration WHERE Translations.Manual=0 OR excluded.Manual=1
            """,("$f",track.Fingerprint),("$h",translation.SourceHash),("$d",track.DurationMs),("$t",track.Title),("$a",track.Artist),("$al",track.Album),("$c",JsonSerializer.Serialize(translation)),("$m",manual));
    }
    public IReadOnlyList<StoredTranslation> ReadAll()
    {
        using var c=Command("SELECT Title,Artist,Album,Duration,Content,Manual FROM Translations ORDER BY Fingerprint,SourceHash");using var r=c.ExecuteReader();var records=new List<StoredTranslation>();
        while(r.Read())records.Add(new(new(r.GetString(0),r.GetString(1),r.GetString(2),r.IsDBNull(3)?null:r.GetDouble(3)),Read(r.GetString(4)),r.GetBoolean(5)));return records;
    }
    public void Sync(IReadOnlyList<(string Key,Track Track,string Hash,int Priority)> rows)
    {
        using var tx=db.BeginTransaction();Execute("UPDATE PreparationQueue SET Active=0");
        foreach(var (key,t,hash,priority) in rows)
            Execute("""
                INSERT INTO PreparationQueue(Id,Fingerprint,Title,Artist,Album,Duration,OriginalHash,Priority,Active) VALUES($id,$f,$t,$a,$al,$d,$h,$p,1)
                ON CONFLICT(Id) DO UPDATE SET
                State=CASE WHEN PreparationQueue.Fingerprint<>excluded.Fingerprint OR PreparationQueue.OriginalHash<>excluded.OriginalHash OR ABS(COALESCE(PreparationQueue.Duration,0)-COALESCE(excluded.Duration,0))>5000 THEN 'Pending' ELSE PreparationQueue.State END,
                RetryUtc=CASE WHEN PreparationQueue.Fingerprint<>excluded.Fingerprint OR PreparationQueue.OriginalHash<>excluded.OriginalHash THEN NULL ELSE PreparationQueue.RetryUtc END,
                Fingerprint=excluded.Fingerprint,Title=excluded.Title,Artist=excluded.Artist,Album=excluded.Album,Duration=excluded.Duration,OriginalHash=excluded.OriginalHash,Priority=excluded.Priority,Active=1
                """,("$id",key),("$f",t.Fingerprint),("$t",t.Title),("$a",t.Artist),("$al",t.Album),("$d",t.DurationMs),("$h",hash),("$p",priority));
        tx.Commit();
    }
    public IReadOnlyList<TranslationQueueItem> Queue()
    {
        using var c=Command("SELECT Id,Title,Artist,Album,Duration,OriginalHash,Priority FROM PreparationQueue WHERE Active=1 AND State NOT IN ('Prepared','Draft','Chinese','Instrumental') AND (RetryUtc IS NULL OR RetryUtc<$now) ORDER BY Priority,Id",("$now",DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture)));using var r=c.ExecuteReader();var list=new List<TranslationQueueItem>();
        while(r.Read())list.Add(new(r.GetString(0),new(r.GetString(1),r.GetString(2),r.GetString(3),r.IsDBNull(4)?null:r.GetDouble(4)),r.GetString(5),r.GetInt32(6)));return list;
    }
    public void SetState(TranslationQueueItem item,string state,string detail,string? hash=null)
    {
        if(state is not ("Prepared" or "Draft" or "Chinese" or "Instrumental" or "MissingOriginal" or "MissingTranslation" or "Mismatch" or "Error" or "Pending"))throw new ArgumentException("Unknown translation preparation state",nameof(state));
        Execute("UPDATE PreparationQueue SET State=$s,Detail=$detail,OriginalHash=COALESCE($h,OriginalHash),RetryUtc=$retry WHERE Id=$id",("$s",state),("$detail",detail),("$h",hash),("$retry",state is "Prepared" or "Draft" or "Chinese" or "Instrumental" or "Pending"?null:DateTimeOffset.UtcNow.Add(state=="Error"?TimeSpan.FromMinutes(15):TimeSpan.FromDays(7)).ToString("O",CultureInfo.InvariantCulture)),("$id",item.Key));
    }
    public void MarkDraft(Track track,string hash)=>Execute("UPDATE PreparationQueue SET State='Draft',Detail='AI 译稿 · 待校对',OriginalHash=$h,RetryUtc=NULL WHERE Active=1 AND Fingerprint=$f AND ($d IS NULL OR Duration IS NULL OR ABS(Duration-$d)<=5000)",("$f",track.Fingerprint),("$h",hash),("$d",track.DurationMs));
    public void ResetRetries()=>Execute("UPDATE PreparationQueue SET RetryUtc=NULL WHERE State NOT IN ('Prepared','Draft','Chinese','Instrumental')");
    public TranslationProgress Progress(string message="",bool running=false)
    {
        using var c=Command("SELECT State,COUNT(*) FROM PreparationQueue WHERE Active=1 GROUP BY State");using var r=c.ExecuteReader();var counts=new Dictionary<string,int>();while(r.Read())counts[r.GetString(0)]=r.GetInt32(1);
        return new(counts.Values.Sum(),counts.GetValueOrDefault("Prepared"),counts.GetValueOrDefault("Chinese"),counts.GetValueOrDefault("Instrumental"),counts.GetValueOrDefault("MissingOriginal"),counts.GetValueOrDefault("MissingTranslation"),counts.GetValueOrDefault("Mismatch"),counts.GetValueOrDefault("Error"),counts.GetValueOrDefault("Pending"),running,message){Drafts=counts.GetValueOrDefault("Draft")};
    }
    public static void CheckDatabase(string path)
    {
        using var db=PersonalLibrarySnapshot.Open(path);using var c=db.CreateCommand();c.CommandText="PRAGMA quick_check";if(c.ExecuteScalar() is not "ok")throw new InvalidDataException("中文译文数据库校验失败。");
        c.CommandText="PRAGMA user_version";if(Convert.ToInt32(c.ExecuteScalar())!=1)throw new InvalidDataException("中文译文数据库版本不受支持。");
        c.CommandText="SELECT Content FROM Translations";using var r=c.ExecuteReader();while(r.Read())Read(r.GetString(0));
    }
    public void Dispose()=>db.Dispose();
}
