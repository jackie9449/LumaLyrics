using System.Text;
namespace LumaLyrics.Infrastructure;

public sealed record MusicAlbum(string Key,string Title,string Artist,IReadOnlyList<MusicLibrarySong> Songs)
{
    public string Summary=>$"{Songs.Count:N0} 首歌曲";
    public bool CanPlay=>MusicLibraryPlayback.AlbumAnchor(this) is not null;
    public IReadOnlyList<string> CoverPaths=>Songs.Select(s=>s.CoverPath).OfType<string>().Where(p=>p.Length>0).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray();
    public bool Matches(string query)
    {
        if(string.IsNullOrWhiteSpace(query))return true;
        string text=LumaLyrics.Core.MatchRules.Normalize(Title+" "+Artist+" "+string.Join(" ",Songs.Select(s=>s.Title+" "+s.Artist)));
        return query.Split((char[]?)null,StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).Select(LumaLyrics.Core.MatchRules.Normalize).All(t=>text.Contains(t,StringComparison.Ordinal));
    }
}

public static class MusicLibraryAlbums
{
    // Keep punctuation and edition labels. A title with missing metadata is never
    // enough to merge unrelated songs into a fabricated album.
    static string Identity(string text)=>text.Trim().Normalize(NormalizationForm.FormC).ToUpperInvariant();
    static string Key(MusicLibrarySong song)
    {
        if(string.IsNullOrWhiteSpace(song.Album))return song.Key;
        // A native artwork reference can be shared across differently named albums
        // on Windows. It must never override explicit album/album-artist metadata.
        string album=Identity(song.Source)+"\0album:"+Identity(song.Album);
        if(!string.IsNullOrWhiteSpace(song.AlbumArtist))album+="\0artist:"+Identity(song.AlbumArtist);
        if(song.AlbumPersistentId is {Length:>0} id && id.Any(c=>c!='0'))return album+"\0id:"+Identity(id);
        return string.IsNullOrWhiteSpace(song.AlbumArtist)?album+"\0artist:"+Identity(song.Artist):album;
    }
    public static IReadOnlyList<MusicAlbum> Group(IReadOnlyList<MusicLibrarySong> songs)=>songs.GroupBy(Key,StringComparer.Ordinal).Select(group=>
    {
        var tracks=group.OrderBy(s=>s.Title,StringComparer.CurrentCultureIgnoreCase).ThenBy(s=>s.Key,StringComparer.Ordinal).ToArray();
        var albumArtists=tracks.Select(s=>s.AlbumArtist.Trim()).Where(s=>s.Length>0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var artists=tracks.Select(s=>s.Artist.Trim()).Where(s=>s.Length>0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        string title=tracks.Select(s=>s.Album.Trim()).FirstOrDefault(s=>s.Length>0)??"未命名专辑";
        string artist=albumArtists.Length==1?albumArtists[0]:artists.Length==1?artists[0]:artists.Length>1?"群星":"未知艺人";
        return new MusicAlbum(group.Key,title,artist,tracks);
    }).OrderBy(a=>a.Title,StringComparer.CurrentCultureIgnoreCase).ThenBy(a=>a.Artist,StringComparer.CurrentCultureIgnoreCase).ThenBy(a=>a.Key,StringComparer.Ordinal).ToArray();
}
