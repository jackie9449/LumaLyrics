using System.Text.RegularExpressions;
using LumaLyrics.Core;
namespace LumaLyrics.Infrastructure;

internal static partial class NetEaseRecording
{
    [GeneratedRegex(@"\b(?:feat(?:uring)?|ft)\.?\s+|\s*[,&;/、]\s*",RegexOptions.IgnoreCase)]private static partial Regex Separator();
    [GeneratedRegex(@"\s*-\s*(?:EP|Single)\s*$",RegexOptions.IgnoreCase)]private static partial Regex AlbumType();
    [GeneratedRegex(@"\[(?:\d+:\d+(?:[.:]\d+)?|(?:ar|ti|al|by|offset|length):[^\]]*)\]",RegexOptions.IgnoreCase)]private static partial Regex Tags();
    static HashSet<string> Artists(string value)=>Separator().Split(value).Select(MatchRules.Normalize).Where(x=>x.Length>0).ToHashSet(StringComparer.Ordinal);
    internal static bool AdditionalCreditsMatch(Track local,Track catalog)
    {
        if(local.Album.Length==0 || catalog.Album.Length==0 || local.DurationMs is not >0 || catalog.DurationMs is not >0 || Math.Abs(local.DurationMs.Value-catalog.DurationMs.Value)>1000)return false;
        if(MatchRules.Normalize(AlbumType().Replace(local.Album,""))!=MatchRules.Normalize(AlbumType().Replace(catalog.Album,"")))return false;
        var a=Artists(local.Artist);var b=Artists(catalog.Artist);
        if(a.Count==0 || !a.IsSubsetOf(b) || b.Count>a.Count+3)return false;
        // The title, edition markers and real duration must still pass the core gate.
        return MatchRules.Score(local,new(0,catalog with{Artist=local.Artist},null,null,false)).Eligible;
    }
    internal static bool IsExplicitInstrumental(string raw)
    {
        string text=MatchRules.Normalize(Tags().Replace(raw,""));
        return text is "纯音乐请欣赏" or "純音樂請欣賞" or "此歌曲为没有填词的纯音乐请您欣赏" or "此歌曲為沒有填詞的純音樂請您欣賞" or "instrumental";
    }
}
