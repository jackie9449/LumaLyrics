using LumaLyrics.Core;
namespace LumaLyrics.Infrastructure;

public sealed record TranslationAlbumSong(string Source,string PersistentId,Track Track,string? SourceHash,
    IReadOnlyList<TranslationJobGroup> Groups,IReadOnlyList<TranslationLine>? ExistingTranslation,
    string? TranslationSource,bool? AiDraft,bool? Manual,string? UnavailableReason);
public sealed record TranslationAlbumContext(int Version,string AlbumKey,string Title,string Artist,
    string OrderNote,IReadOnlyList<TranslationAlbumSong> Songs);

// One builder per export batch: all selected songs in an album see the same reference snapshot.
internal sealed class TranslationAlbumContextBuilder
{
    readonly IReadOnlyList<MusicAlbum> albums;
    readonly Dictionary<string,TranslationAlbumContext> contexts=new(StringComparer.Ordinal);
    readonly LyricsRepository lyrics;readonly TranslationRepository translations;
    internal TranslationAlbumContextBuilder(string root,LyricsRepository lyrics,TranslationRepository translations)
    {
        this.lyrics=lyrics;this.translations=translations;
        var members=lyrics.GetLibraryQueue(true);
        var songs=members.Select(m=>new MusicLibrarySong(m.Source,m.PersistentId,m.Track)).ToList();
        foreach(var source in members.Select(m=>m.Source).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if(!File.Exists(source))continue;
            try
            {
                var snapshot=new AppleMusicLibraryReader().Read(source);
                var references=new AppleMusicArtworkReader().Read(source);
                var native=snapshot.Tracks.ToDictionary(t=>t.PersistentId,StringComparer.OrdinalIgnoreCase);
                for(int i=0;i<songs.Count;i++)if(songs[i].Source.Equals(source,StringComparison.OrdinalIgnoreCase) && native.TryGetValue(songs[i].PersistentId,out var track) && track.Track==songs[i].Track)
                    songs[i]=songs[i] with{AlbumArtist=track.AlbumArtist,AlbumPersistentId=track.AlbumPersistentId is { } id && references.TryGetValue(id,out var reference) && reference.SourceKind==45?id:null};
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidDataException or Microsoft.Data.Sqlite.SqliteException){}
        }
        albums=MusicLibraryAlbums.Group(songs);
    }
    internal TranslationAlbumContext Get(Track track,string? source,string? id)
    {
        var matches=albums.Where(a=>a.Songs.Any(s=>s.Track==track && (source is null || s.Source.Equals(source,StringComparison.OrdinalIgnoreCase)&&s.PersistentId==id))).ToArray();
        var album=matches.Length==1?matches[0]:new MusicAlbum("single:"+track.Fingerprint,track.Album,track.Artist,[new(source??"",id??"",track)]);
        if(contexts.TryGetValue(album.Key,out var saved))return saved;
        var songs=album.Songs.Select(s=>
        {
            var binding=lyrics.FindReadOnly(s.Track);
            var doc=binding is null?null:new LrcParser().Parse(binding.Raw,binding.Plain) with{Instrumental=binding.Instrumental};
            bool usable=doc is not null && doc.IsSynced && !doc.Instrumental && (binding!.Manual || LibraryPrefetcher.QualityOf(binding,s.Track));
            var translation=usable?translations.Find(s.Track,doc!):null;
            var active=usable?translations.FindActive(s.Track,doc!):null;
            return new TranslationAlbumSong(s.Source,s.PersistentId,s.Track,usable?TranslationAlignment.Hash(doc!.Raw):null,
                usable?doc!.Groups.Select((g,i)=>new TranslationJobGroup(i,g.Text,TranslationAlignment.NeedsChineseTranslation(doc,g.Text))).ToArray():[],
                translation?.Lines,translation?.Source,translation?.Draft,active?.Record.Manual,
                usable?null:doc?.Instrumental==true?"已确认纯音乐，无演唱原词":"未取得可确认的动态原词；不得补写或猜测");
        }).ToArray();
        return contexts[album.Key]=new(1,album.Key,album.Title,album.Artist,"仅包含当前已确认资料库成员；列表按标题排列，不是已核实的专辑曲序，可能不完整。",songs);
    }
}
