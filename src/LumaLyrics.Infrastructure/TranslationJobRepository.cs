using System.Text.Json;
using Microsoft.Data.Sqlite;
namespace LumaLyrics.Infrastructure;

internal static class TranslationJobRepository
{
    internal static void Migrate(SqliteConnection db)
    {
        using var tx=db.BeginTransaction(deferred:false);
        var columns=new HashSet<string>();using(var c=Command(db,"PRAGMA table_info(Translations)")){using var r=c.ExecuteReader();while(r.Read())columns.Add(r.GetString(1));}
        if(!columns.Contains("Revision"))Execute(db,"ALTER TABLE Translations ADD COLUMN Revision TEXT DEFAULT ''");
        Execute(db,"""
            UPDATE Translations SET Revision=lower(hex(randomblob(16))) WHERE Revision='';
            CREATE TABLE IF NOT EXISTS TranslationHistory(Revision TEXT PRIMARY KEY,Fingerprint TEXT,SourceHash TEXT,Duration REAL,Title TEXT,Artist TEXT,Album TEXT,Content TEXT,Manual INTEGER,ReplacedUtc TEXT);
            CREATE TABLE IF NOT EXISTS TranslationJobs(Id TEXT PRIMARY KEY,Request TEXT NOT NULL,RequestHash TEXT NOT NULL,State TEXT NOT NULL,Receipt TEXT,ResultHash TEXT);
            CREATE TABLE IF NOT EXISTS TranslationBatches(Id TEXT PRIMARY KEY,Manifest TEXT NOT NULL);
            CREATE TRIGGER IF NOT EXISTS TranslationRevisionInsert AFTER INSERT ON Translations WHEN NEW.Revision='' BEGIN
              UPDATE Translations SET Revision=lower(hex(randomblob(16))) WHERE Fingerprint=NEW.Fingerprint AND SourceHash=NEW.SourceHash;
            END;
            CREATE TRIGGER IF NOT EXISTS TranslationHistoryUpdate BEFORE UPDATE ON Translations WHEN OLD.Content<>NEW.Content OR OLD.Manual<>NEW.Manual BEGIN
              INSERT OR IGNORE INTO TranslationHistory VALUES(OLD.Revision,OLD.Fingerprint,OLD.SourceHash,OLD.Duration,OLD.Title,OLD.Artist,OLD.Album,OLD.Content,OLD.Manual,strftime('%Y-%m-%dT%H:%M:%fZ','now'));
            END;
            CREATE TRIGGER IF NOT EXISTS TranslationRevisionUpdate AFTER UPDATE ON Translations WHEN NEW.Revision=OLD.Revision BEGIN
              UPDATE Translations SET Revision=lower(hex(randomblob(16))) WHERE Fingerprint=NEW.Fingerprint AND SourceHash=NEW.SourceHash;
            END;
            PRAGMA user_version=2;
            """);tx.Commit();
    }
    internal static SqliteConnection Open(string root,bool readOnly=false)
    {
        PersonalLibraryArchive.EnsureOutsideSoftware(root);
        if(!Directory.Exists(root)||!File.Exists(Path.Combine(root,"lyrics.db")))throw new DirectoryNotFoundException("原个人资料目录不可用，不会创建空库。");
        var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path.Combine(root,"translations.db"),Mode=readOnly?SqliteOpenMode.ReadOnly:SqliteOpenMode.ReadWrite,Pooling=false,DefaultTimeout=2}.ToString());db.Open();return db;
    }
    internal static SqliteCommand Command(SqliteConnection db,string sql,params (string,object?)[] args)
    {var c=db.CreateCommand();c.CommandText=sql;foreach(var (key,value) in args)c.Parameters.AddWithValue(key,value??DBNull.Value);return c;}
    internal static void Execute(SqliteConnection db,string sql,params (string,object?)[] args){using var c=Command(db,sql,args);c.ExecuteNonQuery();}
    internal static string? Scalar(SqliteConnection db,string sql,params (string,object?)[] args){using var c=Command(db,sql,args);return c.ExecuteScalar() as string;}
    internal static TranslationJobRequest Request(SqliteConnection db,string id)=>JsonSerializer.Deserialize<TranslationJobRequest>(Scalar(db,"SELECT Request FROM TranslationJobs WHERE Id=$id",("$id",id))??throw new InvalidDataException("未登记的任务。"),TranslationJobFiles.Json)!;
}
