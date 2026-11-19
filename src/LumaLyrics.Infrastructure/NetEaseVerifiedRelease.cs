using LumaLyrics.Core;

namespace LumaLyrics.Infrastructure;

internal static class NetEaseVerifiedRelease
{
    const string Artist="Ariabl'eyeS";
    const string AppleAlbum="Kegarenaki Ruriiro No Rosenkreuz";
    const string JapaneseAlbum="穢れなき瑠璃色のローゼンクロイツ";
    const long NetEaseAlbumId=38636065;

    // Apple Music US/JP album 1489281445 has the same song IDs in both stores.
    // Each NetEase recording was checked against the localized title, disc, track number and duration.
    static readonly CatalogRecordingMatch[] Recordings=
    [
        Recording(558560453,"Prelude -Rosenkreuz-","Prelude-Rosenkreuz-",Artist,66000),
        Recording(558560454,"Ruriiro Soiree","瑠璃色ソワレ","Rena",310345),
        Recording(558560455,"Ruriiro No Himegimi","瑠璃色の姫君",Artist,246269),
        Recording(558560456,"Itoshiki Rufenburg","愛しきルーフェンブルク","LUNA & Rena",248308),
        Recording(558560457,"Symmetric Rose","Symmetric Rose",Artist,401143),
        Recording(558560458,"Kuroki Inbou","黒き陰謀",Artist,220929),
        Recording(558560459,"Keikan No Senritsu","荊冠の旋律","LUNA",362400),
        Recording(558560460,"Kegarenaki Barajuuji","穢れなき薔薇十字","Rena",352872),
        Recording(558560461,"Fine","Fine","LUNA",291983),
        Recording(558560462,"...Postlude","...Postlude",Artist,44000)
    ];

    static CatalogRecordingMatch Recording(long id,string title,string localizedTitle,string credits,int duration)
        =>new("NetEase",id,new(localizedTitle,credits,JapaneseAlbum,duration),
            new(title,Artist,AppleAlbum,duration),localizedTitle,JapaneseAlbum);

    internal static long? FindId(Track track)=>Recordings.FirstOrDefault(recording=>recording.MatchesLocal(track))?.Id;

    internal static LyricsCandidate Attach(LyricsCandidate candidate,long? albumId)
    {
        if(albumId!=NetEaseAlbumId || candidate.ProviderName!="NetEase")return candidate;
        var recording=Array.Find(Recordings,r=>r.Id==candidate.Id);
        if(recording is null)return candidate;

        var source=candidate.Track;
        var expected=recording.SourceTrack;
        if(MatchRules.Normalize(source.Title)!=MatchRules.Normalize(expected.Title)
            || MatchRules.Normalize(source.Album)!=MatchRules.Normalize(expected.Album)
            || !MatchRules.ArtistCreditsCover(expected.Artist,source.Artist,0)
            || !(source.DurationMs>0 && Math.Abs(source.DurationMs.Value-expected.DurationMs!.Value)<=1000))
            return candidate;

        return candidate with{CatalogMatch=recording with{SourceTrack=source}};
    }
}
