using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
namespace LumaLyrics.Infrastructure;

/// <summary>Resolves current native identities to verified local images without downloads or database writes.</summary>
public sealed class MusicLibraryArtworkCatalog(string root)
{
    sealed record ImageRecord(int Width,int Height,string Hash);
    public IReadOnlyDictionary<string,string> Load(LibrarySnapshot? snapshot,IReadOnlyDictionary<string,AppleArtworkSource>? sources,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();
        var images=new Dictionary<string,ImageRecord>(StringComparer.OrdinalIgnoreCase);
        var saved=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        string database=Path.Combine(root,"artwork.db");
        if(File.Exists(database))
        {
            using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=database,Mode=SqliteOpenMode.ReadOnly,Pooling=false,DefaultTimeout=3}.ToString());db.Open();
            using var cmd=db.CreateCommand();cmd.CommandText="SELECT ImageKey,Width,Height,Hash FROM ArtworkImages";
            using(var rows=cmd.ExecuteReader())while(rows.Read()){token.ThrowIfCancellationRequested();images[rows.GetString(0)]=new(rows.GetInt32(1),rows.GetInt32(2),rows.GetString(3));}
            cmd.CommandText="SELECT Source,PersistentId,ImageKey FROM LibraryArtwork WHERE Active=1";
            using(var rows=cmd.ExecuteReader())while(rows.Read()){token.ThrowIfCancellationRequested();saved[rows.GetString(0)+"\0"+rows.GetString(1)]=rows.GetString(2);}
        }
        var verified=new Dictionary<string,string?>(StringComparer.OrdinalIgnoreCase);
        string? Verify(string path,ImageRecord? expected)
        {
            token.ThrowIfCancellationRequested();if(verified.TryGetValue(path,out var cached))return cached;
            string? result=null;
            try
            {
                if(File.Exists(path) && new FileInfo(path).Length<=ImageDimensions.MaximumBytes)
                {
                    var bytes=File.ReadAllBytes(path);token.ThrowIfCancellationRequested();var size=ImageDimensions.Read(bytes);
                    if(expected is null || size==(expected.Width,expected.Height) && Convert.ToHexString(SHA256.HashData(bytes)).Equals(expected.Hash,StringComparison.OrdinalIgnoreCase))result=path;
                }
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidDataException){}
            verified[path]=result;return result;
        }
        string? Cached(string key)=>key.Length==64 && key.All(Uri.IsHexDigit) && images.TryGetValue(key,out var image)?Verify(Path.Combine(root,"cache","artwork",key+".img"),image):null;
        var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var pair in saved){token.ThrowIfCancellationRequested();if(Cached(pair.Value) is { } path)result[pair.Key]=path;}
        if(snapshot is null || sources is null)return result;
        foreach(var track in snapshot.Tracks.Concat(snapshot.Playlists.SelectMany(p=>p.Tracks)))
        {
            token.ThrowIfCancellationRequested();string identity=snapshot.Path+"\0"+track.PersistentId;
            sources.TryGetValue(track.PersistentId,out var source);
            if(source is null && track.AlbumPersistentId is { } album && sources.TryGetValue(album,out var reference) && reference.SourceKind==45)source=reference;
            if(source is null)continue;
            string? location=source.RemoteUri?.AbsoluteUri??source.LocalPath;
            string? path=location is null?null:Cached(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(location))));
            if(path is null && source.LocalPath is { } local)path=Verify(local,null);
            // A current native mapping supersedes an old song mapping, even when its image is missing.
            result.Remove(identity);if(path is not null)result[identity]=path;
        }
        return result;
    }
}
