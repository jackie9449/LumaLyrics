using System.Text.Json;
using LumaLyrics.Core;
namespace LumaLyrics.Infrastructure;
public sealed class LibraryWorkspaceCatalog(string root)
{
    readonly SemaphoreSlim readGate=new(1,1);
    Dictionary<LibrarySongKey,(AssessmentKey Key,LibraryManagedSong Song)> assessments=new(LibrarySongKey.Comparer);
    sealed record AssessmentKey(Track Track,LyricsBinding? Binding,string? Revision,bool Older,LibraryCoverState Cover,string? Reason);
    public async Task<LibraryWorkspaceSnapshot> ReadAsync(string? source,long generation,CancellationToken token,bool confirmedOnly=false)
    {
        await readGate.WaitAsync(token).ConfigureAwait(false);
        try{return await Task.Run(()=>Read(source,generation,token,confirmedOnly),token).ConfigureAwait(false);}finally{readGate.Release();}
    }
    LibraryWorkspaceSnapshot Read(string? source,long generation,CancellationToken token,bool confirmedOnly)
    {
        var library=new MusicLibraryCatalog(root,source);
        var cached=confirmedOnly?library.LoadConfirmedSongs(token):[];
        var catalog=cached.Count>0?new MusicLibraryResult(cached,""):library.Load(token);
        if(catalog.Unavailable)throw new IOException(catalog.Message);
        var sources=catalog.Songs.Select(s=>s.Source).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(sources.Length>1)throw new InvalidDataException("存在多个资料库来源，请明确选择本机资料库。");
        var bindings=new Dictionary<string,List<(Track Track,LyricsBinding Binding)>>();
        return Build();
        LibraryWorkspaceSnapshot Build()
        {
            using var db=PersonalLibrarySnapshot.Open(Path.Combine(root,"lyrics.db"));using var c=db.CreateCommand();
            c.CommandText="SELECT t.Title,t.Artist,t.Album,t.Duration,b.Id,b.TrackId,d.Raw,d.Source,b.Manual,COALESCE(o.Offset,0),d.Instrumental,d.Plain,d.ProviderJson FROM Tracks t JOIN TrackLyricsBindings b ON b.TrackId=t.Id JOIN LyricsDocuments d ON d.Id=b.DocumentId LEFT JOIN SyncOffsets o ON o.BindingId=b.Id";
            using(var r=c.ExecuteReader())while(r.Read()){var track=new Track(r.GetString(0),r.GetString(1),r.GetString(2),r.IsDBNull(3)?null:r.GetDouble(3));var b=new LyricsBinding(r.GetString(4),r.GetString(5),r.GetString(6),r.GetString(7),r.GetBoolean(8),r.GetDouble(9),r.GetBoolean(10),r.GetBoolean(11),r.IsDBNull(12)?null:r.GetString(12));if(!bindings.TryGetValue(track.Fingerprint,out var rows))bindings[track.Fingerprint]=rows=[];rows.Add((track,b));}
            var reasons=new Dictionary<LibrarySongKey,string>(LibrarySongKey.Comparer);
            using(var q=db.CreateCommand()){q.CommandText="SELECT Source,PersistentId,Detail FROM LibraryTracks WHERE Active=1 AND State NOT IN ('Cached','Manual') AND Detail IS NOT NULL";using var r=q.ExecuteReader();while(r.Read())reasons[new(r.GetString(0),r.GetString(1))]=r.GetString(2);}
            var translations=new Dictionary<string,List<(StoredTranslation Record,string Revision)>>();
            if(File.Exists(Path.Combine(root,"translations.db"))){using var tr=PersonalLibrarySnapshot.Open(Path.Combine(root,"translations.db"));using var q=tr.CreateCommand();q.CommandText="SELECT Title,Artist,Album,Duration,Content,Manual,Revision FROM Translations";using var r=q.ExecuteReader();while(r.Read()){var t=new Track(r.GetString(0),r.GetString(1),r.GetString(2),r.IsDBNull(3)?null:r.GetDouble(3));var value=new StoredTranslation(t,JsonSerializer.Deserialize<ChineseTranslation>(r.GetString(4))!,r.GetBoolean(5));if(!translations.TryGetValue(t.Fingerprint,out var rows))translations[t.Fingerprint]=rows=[];rows.Add((value,r.GetString(6)));}}
            var covers=new Dictionary<string,LibraryCoverState>();
            if(File.Exists(Path.Combine(root,"artwork.db"))){using var art=PersonalLibrarySnapshot.Open(Path.Combine(root,"artwork.db"));using var q=art.CreateCommand();q.CommandText="SELECT a.Source,a.PersistentId,a.ImageKey,i.Width,i.Height FROM LibraryArtwork a JOIN ArtworkImages i USING(ImageKey) WHERE a.Active=1";using var r=q.ExecuteReader();while(r.Read())covers[r.GetString(0)+"\0"+r.GetString(1)]=!File.Exists(Path.Combine(root,"cache","artwork",r.GetString(2)+".img"))?LibraryCoverState.Unreadable:Math.Min(r.GetInt32(3),r.GetInt32(4))>=1000?LibraryCoverState.HighResolution:LibraryCoverState.Ready;}
            bool Matches(Track a,Track b)=>!a.DurationMs.HasValue||!b.DurationMs.HasValue||Math.Abs(a.DurationMs.Value-b.DurationMs.Value)<=5000;
            var result=new List<LibraryManagedSong>();var parser=new LrcParser();
            var next=new Dictionary<LibrarySongKey,(AssessmentKey Key,LibraryManagedSong Song)>(LibrarySongKey.Comparer);
            foreach(var song in catalog.Songs)
            {
                token.ThrowIfCancellationRequested();
                var key=new LibrarySongKey(song.Source,song.PersistentId);
                var binding=bindings.GetValueOrDefault(song.Track.Fingerprint)?.FirstOrDefault(x=>Matches(x.Track,song.Track)).Binding;
                string? hash=binding is null?null:TranslationAlignment.Hash(binding.Raw);
                var stored=translations.GetValueOrDefault(song.Track.Fingerprint)?.Where(t=>Matches(t.Record.Track,song.Track)).ToArray()??[];
                var active=stored.FirstOrDefault(t=>t.Record.Translation.SourceHash==hash);
                var assessment=new AssessmentKey(song.Track,binding,active.Revision,stored.Length>0,covers.GetValueOrDefault(song.Key),reasons.GetValueOrDefault(key));
                if(assessments.TryGetValue(key,out var previous)&&previous.Key==assessment){next[key]=previous;result.Add(previous.Song);continue;}
                var doc=binding is null?null:parser.Parse(binding.Raw,binding.Plain) with{Instrumental=binding.Instrumental};
                var status=LibraryWorkspaceProjection.Assess(new(doc,binding is not null&&(binding.Manual||binding.StaticFallback||LibraryPrefetcher.QualityOf(binding,song.Track)),binding?.Manual==true,active.Record?.Translation,active.Record?.Manual==true,stored.Length>0,active.Revision,assessment.Cover));
                if(status.Original is not (LibraryOriginalState.Ready or LibraryOriginalState.Instrumental))status=status with{Reason=assessment.Reason??(status.Original switch
                {
                    LibraryOriginalState.Unconfirmed=>"当前缓存未通过检查，请重新准备以查看具体原因。",
                    LibraryOriginalState.ManualPlain=>"当前为手动静态原词，需动态时间轴。",
                    LibraryOriginalState.Static=>"已保留静态歌词，可重新查找动态原词。",
                    _=>null
                })};
                var row=new LibraryManagedSong(key,song.Track,binding?.Id,status);next[key]=(assessment,row);result.Add(row);
            }
            assessments=next;
            return new(root,sources.FirstOrDefault()??source??"",generation,false,result,catalog.Message);
        }
    }
}
