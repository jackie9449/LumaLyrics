using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
namespace LumaLyrics.Core;

// Native labels are independent of LumaLyrics' selected interface language.
// These values are present in Apple Music for Windows 1.1792.39682.0 resources;
// IDs, control types and recording/context checks remain the primary selectors.
public static partial class NativeMusicLabels
{
    static bool Matches(string value,params string[] labels)=>labels.Contains(value.Trim().Normalize(NormalizationForm.FormKC),StringComparer.OrdinalIgnoreCase);
    public static bool IsSongs(string value)=>Matches(value,"歌曲","Songs","曲");
    public static bool IsHistory(string value)=>Matches(value,"历史记录","記錄","History","履歴");
    public static bool IsPlayingNext(string value)=>Matches(value,"待播清单","待播清單","Playing Next","次に再生");
    public static bool IsEmptyQueue(string value)=>Matches(value,"无待播歌曲。","沒有即將播放的歌曲。","No upcoming songs.","次に再生される曲はありません。");
    public static bool IsShowAlbumInLibrary(string value)=>Matches(value,"在资料库中显示专辑","在資料庫中顯示專輯","Show Album in Library","アルバムをライブラリで表示");
    public static bool IsPlayNext(string value)=>Matches(value,"插播","Play Next","次に再生");

    // Parse a native album row number only, never a playlist position. Japanese
    // resources omit the space between トラック and its number. Keep a strict
    // trailing boundary so "Track 1x" cannot be mistaken for the first track.
    public static int? AlbumTrackNumber(string value)
    {
        var match=AlbumTrack().Match(value.Normalize(NormalizationForm.FormKC));
        return match.Success && int.TryParse(match.Groups[1].Value,NumberStyles.None,CultureInfo.InvariantCulture,out int number) && number>0?number:null;
    }
    [GeneratedRegex(@"^(?:(?:音轨|音軌|Track)\s+|トラック\s*)([0-9]+)(?=\s|$)",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)]
    private static partial Regex AlbumTrack();
}
