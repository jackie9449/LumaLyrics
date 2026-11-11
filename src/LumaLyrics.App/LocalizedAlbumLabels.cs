using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

internal static class LocalizedAlbumLabels
{
    internal static string Title(MusicAlbum album)=>album.Songs.All(s=>string.IsNullOrWhiteSpace(s.Album))?UiText.T("未命名专辑"):album.Title;
    internal static string Artist(MusicAlbum album)
    {
        var albumArtists=album.Songs.Select(s=>s.AlbumArtist.Trim()).Where(s=>s.Length>0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var artists=album.Songs.Select(s=>s.Artist.Trim()).Where(s=>s.Length>0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return albumArtists.Length==1 || artists.Length==1?album.Artist:UiText.T(artists.Length>1?"群星":"未知艺人");
    }
}
