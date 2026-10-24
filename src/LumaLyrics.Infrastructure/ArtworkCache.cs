using System.Security.Cryptography;
using System.Text;
using LumaLyrics.Core;
using Microsoft.Data.Sqlite;
namespace LumaLyrics.Infrastructure;

public sealed record CachedArtwork(byte[] Bytes,int Width,int Height,string? SourceUrl)
{public bool HighResolution=>Math.Min(Width,Height)>=1000;}
public sealed record ArtworkProgress(int Total,int Cached,int HighResolution,int Unavailable,int Errors,bool Running,string Message)
{
    public int Pending=>Math.Max(0,Total-Cached-Unavailable-Errors);
    public override string ToString()=>$"封面 {Cached}/{Total} · 高清 {HighResolution} · 待处理 {Pending} · 待补齐 {Unavailable} · 网络待重试 {Errors}\n{Message}";
}
public sealed record ArtworkQueueItem(string Source,string PersistentId,Track Track,string ImageKey,Uri? RemoteUri,string? LocalPath);

/// <summary>All paths and metadata in this database belong to LumaLyrics, not Apple Music.</summary>
public sealed class ArtworkCache : IDisposable
{
    readonly SqliteConnection db;readonly string directory;
    public ArtworkCache(string root)
    {
        Directory.CreateDirectory(root);directory=Path.Combine(root,"cache","artwork");Directory.CreateDirectory(directory);
        db=new(new SqliteConnectionStringBuilder{DataSource=Path.Combine(root,"artwork.db")}.ToString());db.Open();
        Execute("""
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS ArtworkImages(ImageKey TEXT PRIMARY KEY,Width INTEGER,Height INTEGER,Hash TEXT,SourceUrl TEXT);
            CREATE TABLE IF NOT EXISTS LibraryArtwork(Source TEXT,PersistentId TEXT,Fingerprint TEXT,Title TEXT,Artist TEXT,Album TEXT,Duration REAL,ImageKey TEXT,RemoteUrl TEXT,LocalPath TEXT,State TEXT,Detail TEXT,RetryUtc TEXT,Active INTEGER,PRIMARY KEY(Source,PersistentId));
            CREATE INDEX IF NOT EXISTS IX_Artwork_Fingerprint ON LibraryArtwork(Fingerprint,Active);
            CREATE INDEX IF NOT EXISTS IX_Artwork_ImageKey ON LibraryArtwork(ImageKey);
            """);
    }
    SqliteCommand Command(string sql,params (string,object?)[] parameters)
    {var c=db.CreateCommand();c.CommandText=sql;foreach(var (name,value) in parameters)c.Parameters.AddWithValue(name,value??DBNull.Value);return c;}
    void Execute(string sql,params (string,object?)[] parameters){using var c=Command(sql,parameters);c.ExecuteNonQuery();}
    string ImagePath(string key)
    {
        if(key.Length!=64 || key.Any(x=>!Uri.IsHexDigit(x)))throw new InvalidDataException("封面缓存标识无效。");
        return Path.Combine(directory,key+".img");
    }
    public void Sync(LibrarySnapshot snapshot,IReadOnlyDictionary<string,AppleArtworkSource> sources)
    {
        // A metadata fallback is safe only when this exact artist/album has one cover.
        var albums=snapshot.Tracks.Where(t=>t.Track.Album.Length>0 && t.Track.Artist.Length>0 && sources.ContainsKey(t.PersistentId))
            .GroupBy(t=>MatchRules.Normalize(t.Track.Artist)+"|"+MatchRules.Normalize(t.Track.Album))
            .ToDictionary(g=>g.Key,g=>g.Select(t=>sources[t.PersistentId]).DistinctBy(s=>s.RemoteUri?.AbsoluteUri??s.ArtworkId).ToArray());
        using var tx=db.BeginTransaction();Execute("UPDATE LibraryArtwork SET Active=0");
        foreach(var item in snapshot.Tracks)
        {
            sources.TryGetValue(item.PersistentId,out var source);var track=item.Track;
            if(source is null && item.AlbumPersistentId is { } albumId && sources.TryGetValue(albumId,out var albumSource) && albumSource.SourceKind==45)source=albumSource;
            if(source is null && track.Artist.Length>0 && track.Album.Length>0 && albums.TryGetValue(MatchRules.Normalize(track.Artist)+"|"+MatchRules.Normalize(track.Album),out var candidates) && candidates.Length==1)source=candidates[0];
            string key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source?.RemoteUri?.AbsoluteUri??source?.LocalPath??"missing:"+snapshot.Path+":"+item.PersistentId)));
            Execute("""
                INSERT INTO LibraryArtwork(Source,PersistentId,Fingerprint,Title,Artist,Album,Duration,ImageKey,RemoteUrl,LocalPath,State,Active)
                VALUES($s,$id,$f,$t,$ar,$al,$d,$key,$url,$local,'Pending',1)
                ON CONFLICT(Source,PersistentId) DO UPDATE SET
                State=CASE WHEN LibraryArtwork.ImageKey<>excluded.ImageKey OR LibraryArtwork.Fingerprint<>excluded.Fingerprint THEN 'Pending' ELSE LibraryArtwork.State END,
                RetryUtc=CASE WHEN LibraryArtwork.ImageKey<>excluded.ImageKey OR LibraryArtwork.Fingerprint<>excluded.Fingerprint THEN NULL ELSE LibraryArtwork.RetryUtc END,
                Fingerprint=excluded.Fingerprint,Title=excluded.Title,Artist=excluded.Artist,Album=excluded.Album,Duration=excluded.Duration,ImageKey=excluded.ImageKey,RemoteUrl=excluded.RemoteUrl,LocalPath=excluded.LocalPath,Active=1
                """,("$s",snapshot.Path),("$id",item.PersistentId),("$f",track.Fingerprint),("$t",track.Title),("$ar",track.Artist),("$al",track.Album),("$d",track.DurationMs),("$key",key),("$url",source?.RemoteUri?.AbsoluteUri),("$local",source?.LocalPath));
        }
        tx.Commit();
        var missing=new List<string>();using(var c=Command("SELECT ImageKey FROM ArtworkImages")){using var r=c.ExecuteReader();while(r.Read()){var key=r.GetString(0);if(!File.Exists(ImagePath(key)))missing.Add(key);}}
        foreach(var key in missing){Execute("DELETE FROM ArtworkImages WHERE ImageKey=$key",("$key",key));Execute("UPDATE LibraryArtwork SET State='Pending',RetryUtc=NULL WHERE ImageKey=$key",("$key",key));}
    }
    public IReadOnlyList<ArtworkQueueItem> GetQueue()
    {
        using var c=Command("""
            SELECT a.Source,a.PersistentId,a.Title,a.Artist,a.Album,a.Duration,a.ImageKey,a.RemoteUrl,a.LocalPath
            FROM LibraryArtwork a LEFT JOIN ArtworkImages i USING(ImageKey)
            WHERE a.Active=1 AND (a.RetryUtc IS NULL OR a.RetryUtc<$now)
            AND (a.State<>'Cached' OR i.Width<1000 OR i.Height<1000)
            ORDER BY CASE a.State WHEN 'Pending' THEN 0 ELSE 1 END,a.Source,a.PersistentId
            """,("$now",DateTimeOffset.UtcNow.ToString("O")));
        using var r=c.ExecuteReader();var queue=new List<ArtworkQueueItem>();while(r.Read())queue.Add(new(r.GetString(0),r.GetString(1),new(r.GetString(2),r.GetString(3),r.GetString(4),r.IsDBNull(5)?null:r.GetDouble(5)),r.GetString(6),r.IsDBNull(7)?null:new(r.GetString(7)),r.IsDBNull(8)?null:r.GetString(8)));return queue;
    }
    public CachedArtwork? Find(Track input)
    {
        var track=TrackMetadata.FromAppleSession(input);
        using var c=Command("SELECT ImageKey FROM LibraryArtwork WHERE Active=1 AND Fingerprint=$f AND State='Cached' AND ($d IS NULL OR Duration IS NULL OR ABS(Duration-$d)<=5000) ORDER BY ABS(COALESCE(Duration,0)-COALESCE($d,0)) LIMIT 1",("$f",track.Fingerprint),("$d",track.DurationMs));
        if(c.ExecuteScalar() is not string key)return null;
        var artwork=Read(key);
        if(artwork is null)
        {
            Execute("UPDATE LibraryArtwork SET State='Pending',RetryUtc=NULL,Detail='本地封面校验失败，等待重新下载' WHERE ImageKey=$key",("$key",key));
            Execute("DELETE FROM ArtworkImages WHERE ImageKey=$key",("$key",key));
        }
        return artwork;
    }
    public CachedArtwork? Read(string key)
    {
        using var c=Command("SELECT Width,Height,Hash,SourceUrl FROM ArtworkImages WHERE ImageKey=$key",("$key",key));using var r=c.ExecuteReader();if(!r.Read())return null;
        try
        {
            var path=ImagePath(key);if(!File.Exists(path) || new FileInfo(path).Length>ImageDimensions.MaximumBytes)return null;
            var bytes=File.ReadAllBytes(path);var size=ImageDimensions.Read(bytes);
            if(Convert.ToHexString(SHA256.HashData(bytes))!=r.GetString(2) || size!=(r.GetInt32(0),r.GetInt32(1)))return null;
            return new(bytes,size.Width,size.Height,r.IsDBNull(3)?null:r.GetString(3));
        }
        catch(Exception e) when(e is IOException or InvalidDataException){return null;}
    }
    public void Save(ArtworkQueueItem item,byte[] bytes)
    {
        var (width,height)=ImageDimensions.Read(bytes);var path=ImagePath(item.ImageKey);var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllBytes(temp,bytes);File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}
        Execute("INSERT INTO ArtworkImages VALUES($key,$w,$h,$hash,$url) ON CONFLICT(ImageKey) DO UPDATE SET Width=excluded.Width,Height=excluded.Height,Hash=excluded.Hash,SourceUrl=excluded.SourceUrl",("$key",item.ImageKey),("$w",width),("$h",height),("$hash",Convert.ToHexString(SHA256.HashData(bytes))),("$url",item.RemoteUri?.AbsoluteUri));
        MarkCached(item.ImageKey,width>=1000&&height>=1000);
    }
    public void MarkCached(string key,bool highResolution)=>Execute("UPDATE LibraryArtwork SET State='Cached',Detail=$detail,RetryUtc=$retry WHERE ImageKey=$key",("$key",key),("$detail",highResolution?"Apple Music 封面 · 实际尺寸达到高清标准":"已保存可用封面；高清版本稍后重试"),("$retry",highResolution?null:DateTimeOffset.UtcNow.AddMinutes(15).ToString("O")));
    public void SetFailure(ArtworkQueueItem item,string state,string detail)=>Execute("UPDATE LibraryArtwork SET State=$state,Detail=$detail,RetryUtc=$retry WHERE ImageKey=$key AND State<>'Cached'",("$state",state),("$detail",detail),("$retry",DateTimeOffset.UtcNow.Add(state=="Error"?TimeSpan.FromMinutes(15):TimeSpan.FromDays(1)).ToString("O")),("$key",item.ImageKey));
    public void ResetRetries()=>Execute("UPDATE LibraryArtwork SET RetryUtc=NULL WHERE State<>'Cached' OR ImageKey IN (SELECT ImageKey FROM ArtworkImages WHERE Width<1000 OR Height<1000)");
    public ArtworkProgress GetProgress(string message="",bool running=false)
    {
        using var c=Command("SELECT a.State,COUNT(*),SUM(CASE WHEN i.Width>=1000 AND i.Height>=1000 THEN 1 ELSE 0 END) FROM LibraryArtwork a LEFT JOIN ArtworkImages i USING(ImageKey) WHERE a.Active=1 GROUP BY a.State");using var r=c.ExecuteReader();int total=0,cached=0,high=0,missing=0,errors=0;
        while(r.Read()){int count=r.GetInt32(1);total+=count;switch(r.GetString(0)){case "Cached":cached+=count;high+=r.GetInt32(2);break;case "Missing":missing+=count;break;case "Error":errors+=count;break;}}
        return new(total,cached,high,missing,errors,running,message);
    }
    public void Dispose()=>db.Dispose();
}
