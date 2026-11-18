using System.Text.RegularExpressions;
using LumaLyrics.Core;
namespace LumaLyrics.Infrastructure;

internal static partial class NetEaseRecording
{
    [GeneratedRegex(@"\s*-\s*(?:EP|Single)\s*$",RegexOptions.IgnoreCase)]private static partial Regex AlbumType();
    [GeneratedRegex(@"\[(?:\d+:\d+(?:[.:]\d+)?|(?:ar|ti|al|by|offset|length):[^\]]*)\]",RegexOptions.IgnoreCase)]private static partial Regex Tags();
    internal static bool AdditionalCreditsMatch(Track local,Track catalog)
    {
        if(local.Album.Length==0 || catalog.Album.Length==0 || local.DurationMs is not >0 || catalog.DurationMs is not >0 || Math.Abs(local.DurationMs.Value-catalog.DurationMs.Value)>1000)return false;
        if(!MatchRules.AlbumMatches(AlbumType().Replace(local.Album,""),AlbumType().Replace(catalog.Album,"")))return false;
        if(!MatchRules.ArtistCreditsCover(local.Artist,catalog.Artist,3))return false;
        // The title, edition markers and real duration must still pass the core gate.
        return MatchRules.Score(local,new(0,catalog with{Artist=local.Artist},null,null,false)).Eligible;
    }
    internal static bool IsExplicitInstrumental(string raw)
    {
        string text=MatchRules.Normalize(Tags().Replace(raw,""));
        return text is "纯音乐请欣赏" or "純音樂請欣賞" or "此歌曲为没有填词的纯音乐请您欣赏" or "此歌曲為沒有填詞的純音樂請您欣賞" or "instrumental";
    }
}
