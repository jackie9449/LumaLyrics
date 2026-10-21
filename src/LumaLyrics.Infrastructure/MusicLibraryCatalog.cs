using System.Globalization;
using LumaLyrics.Core;
using Microsoft.Data.Sqlite;
namespace LumaLyrics.Infrastructure;

public sealed record MusicLibrarySong(string Source,string PersistentId,Track Track,string? CoverPath=null,string LyricsKind="",string AlbumArtist="",string? AlbumPersistentId=null)
{
    public string Key=>Source+"\0"+PersistentId;
    public string Title=>Track.Title;
    public string Artist=>Track.Artist;
    public string Album=>Track.Album;
    public string Duration=>Track.DurationMs is >0?$"{(int)(Track.DurationMs.Value/60000)}:{(int)(Track.DurationMs.Value/1000)%60:00}":"—";
    public string LyricsLabel=>LyricsKind switch{"Synced"=>"动态歌词","Instrumental"=>"纯音乐","Manual"=>"个人歌词",_=>""};
    public bool CanPlay=>!string.IsNullOrWhiteSpace(Track.Title) && !string.IsNullOrWhiteSpace(Track.Artist);
}
public sealed record MusicLibraryResult(IReadOnlyList<MusicLibrarySong> Songs,string Message);

// Read-only connections: browsing never starts downloads, changes bindings or edits Apple's library.
public sealed class MusicLibraryCatalog(string root,string? libraryPath=null)
{
    public MusicLibraryResult Load(CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();
        var cached=ReadCached(token);string? path=libraryPath??AppleMusicLibraryReader.Discover();LibrarySnapshot? snapshot=null;string message="";
        if(path is not null)
        {
            try{token.ThrowIfCancellationRequested();snapshot=new AppleMusicLibraryReader().Read(path);}
            catch(Exception ex) when(ex is IOException or InvalidDataException or UnauthorizedAccessException){message="本机 Apple Music 资料库暂不可读，显示已导入的歌曲。可在缓存管理中选择资料库后刷新。";}
        }
        if(snapshot is null && cached.Count==0)return new([],"尚未找到音乐库。请打开 Apple Music 完成资料库同步，或在缓存管理中选择本机资料库。");
        var covers=ReadCovers(token);
        IReadOnlyDictionary<string,AppleArtworkSource>? albumReferences=null;
        if(snapshot is not null)try{albumReferences=new AppleMusicArtworkReader().Read(snapshot.Path);}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or SqliteException){}
        var items=snapshot?.Tracks.Select(t=>new MusicLibrarySong(snapshot.Path,t.PersistentId,t.Track,AlbumArtist:t.AlbumArtist,
            AlbumPersistentId:t.AlbumPersistentId is { } id && albumReferences?.TryGetValue(id,out var reference)==true && reference.SourceKind==45?id:null)).ToArray()??cached.Values.ToArray();
        var songs=new List<MusicLibrarySong>(items.Length);
        foreach(var item in items)
        {
            token.ThrowIfCancellationRequested();cached.TryGetValue(item.Key,out var saved);covers.TryGetValue(item.Key,out var cover);
            songs.Add(item with{CoverPath=cover,LyricsKind=saved?.LyricsKind??""});
        }
        return new(songs.OrderBy(s=>s.Title,StringComparer.CurrentCultureIgnoreCase).ThenBy(s=>s.Artist,StringComparer.CurrentCultureIgnoreCase).ThenBy(s=>s.Key,StringComparer.Ordinal).ToArray(),message);
    }
    Dictionary<string,MusicLibrarySong> ReadCached(CancellationToken token)
    {
        var result=new Dictionary<string,MusicLibrarySong>(StringComparer.OrdinalIgnoreCase);string path=Path.Combine(root,"lyrics.db");if(!File.Exists(path))return result;
        using var db=Open(path);using var command=db.CreateCommand();
        command.CommandText="SELECT Source,PersistentId,Title,Artist,Album,Duration,State,LyricsKind FROM LibraryTracks WHERE Active=1";
        using var rows=command.ExecuteReader();while(rows.Read())
        {
            token.ThrowIfCancellationRequested();string state=rows.GetString(6),kind=state=="Manual"?"Manual":state=="Cached"?rows.GetString(7):"";
            var song=new MusicLibrarySong(rows.GetString(0),rows.GetString(1),new(rows.GetString(2),rows.GetString(3),rows.GetString(4),rows.IsDBNull(5)?null:rows.GetDouble(5)),LyricsKind:kind);result[song.Key]=song;
        }
        return result;
    }
    Dictionary<string,string> ReadCovers(CancellationToken token)
    {
        var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);string path=Path.Combine(root,"artwork.db");if(!File.Exists(path))return result;
        using var db=Open(path);using var command=db.CreateCommand();
        command.CommandText="SELECT a.Source,a.PersistentId,a.ImageKey FROM LibraryArtwork a JOIN ArtworkImages i USING(ImageKey) WHERE a.Active=1";
        using var rows=command.ExecuteReader();while(rows.Read())
        {
            token.ThrowIfCancellationRequested();string key=rows.GetString(2);if(key.Length!=64 || key.Any(c=>!Uri.IsHexDigit(c)))continue;
            result[rows.GetString(0)+"\0"+rows.GetString(1)]=Path.Combine(root,"cache","artwork",key+".img");
        }
        return result;
    }
    static SqliteConnection Open(string path)
    {
        var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=SqliteOpenMode.ReadOnly,Pooling=false,DefaultTimeout=3}.ToString());connection.Open();return connection;
    }
    public static bool Matches(MusicLibrarySong song,string query)
    {
        string text=MatchRules.Normalize(song.Title+" "+song.Artist+" "+song.Album);
        return query.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Select(MatchRules.Normalize).All(term=>term.Length==0 || text.Contains(term,StringComparison.Ordinal));
    }
}

public static class MusicLibraryPlayback
{
    public static bool NamesMatch(Track track,IReadOnlyList<string> names)
    {
        if(string.IsNullOrWhiteSpace(track.Title) || string.IsNullOrWhiteSpace(track.Artist))return false;
        var normalized=names.Select(MatchRules.Normalize).ToHashSet(StringComparer.Ordinal);
        if(!normalized.Contains(MatchRules.Normalize(track.Title)) || !normalized.Contains(MatchRules.Normalize(track.Artist)))return false;
        if(track.Album.Length>0 && !normalized.Contains(MatchRules.Normalize(track.Album)))return false;
        var duration=names.Select(n=>TimeSpan.TryParseExact(n,new[]{@"m\:ss",@"mm\:ss",@"h\:mm\:ss"},CultureInfo.InvariantCulture,out var value)?(double?)value.TotalMilliseconds:null).FirstOrDefault(v=>v.HasValue);
        return !duration.HasValue || !track.DurationMs.HasValue || Math.Abs(duration.Value-track.DurationMs.Value)<=4000;
    }
    public static bool IsPlayCommand(string command,string title)=>!string.IsNullOrWhiteSpace(title) && new[]{"播放","Play","再生"}.Any(verb=>MatchRules.Normalize(command)==MatchRules.Normalize(verb+title));
    public static bool IsRequestedRecording(Track expected,Track actual)
    {
        actual=TrackMetadata.FromAppleSession(actual);
        if(string.IsNullOrWhiteSpace(expected.Title) || string.IsNullOrWhiteSpace(actual.Title))return false;
        if(MatchRules.Normalize(expected.Title)!=MatchRules.Normalize(actual.Title) || MatchRules.Normalize(expected.Artist)!=MatchRules.Normalize(actual.Artist))return false;
        if(expected.Album.Length>0 && MatchRules.Normalize(expected.Album)!=MatchRules.Normalize(actual.Album))return false;
        return !expected.DurationMs.HasValue || !actual.DurationMs.HasValue || Math.Abs(expected.DurationMs.Value-actual.DurationMs.Value)<=4000;
    }
}
