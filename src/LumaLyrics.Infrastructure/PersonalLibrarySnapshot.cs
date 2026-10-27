using System.Security.Cryptography;
using System.Text;
using LumaLyrics.Core;
using Microsoft.Data.Sqlite;
namespace LumaLyrics.Infrastructure;

internal static class PersonalLibrarySnapshot
{
    internal static SqliteConnection Open(string path,bool readOnly=true)
    {
        var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=readOnly?SqliteOpenMode.ReadOnly:SqliteOpenMode.ReadWrite,Pooling=false}.ToString());
        try{db.Open();return db;}catch{db.Dispose();throw;}
    }
    internal static void Backup(string source,string destination)
    {
        using var input=Open(source);
        using var output=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=destination,Pooling=false}.ToString());output.Open();input.BackupDatabase(output);
        using var command=output.CreateCommand();command.CommandText="PRAGMA journal_mode=DELETE";command.ExecuteNonQuery();
    }
    internal static void CheckDatabase(string path,bool lyrics)
    {
        using var db=Open(path);using var command=db.CreateCommand();command.CommandText="PRAGMA quick_check";
        if(command.ExecuteScalar() is not "ok")throw new InvalidDataException("个人资料数据库校验失败。");
        if(lyrics){command.CommandText="PRAGMA user_version";if(Convert.ToInt32(command.ExecuteScalar())!=5)throw new InvalidDataException("歌词数据库版本不兼容，请先使用对应版本软件完成升级。");}
    }
    record LibraryRow(string Source,string PersistentId,Track Track);
    record BoundRow(Track Track,LyricsBinding Binding);
    record ArtworkRow(string Source,string PersistentId,Track Track,string ImageKey);
    static string Text(SqliteDataReader reader,int index)=>reader.IsDBNull(index)?"":reader.GetString(index);
    static Track ReadTrack(SqliteDataReader reader,int index)=>new(Text(reader,index),Text(reader,index+1),Text(reader,index+2),reader.IsDBNull(index+3)?null:reader.GetDouble(index+3));
    static bool Matches(Track a,Track b)=>a.Fingerprint==b.Fingerprint && (!a.DurationMs.HasValue || !b.DurationMs.HasValue || Math.Abs(a.DurationMs.Value-b.DurationMs.Value)<=5000);
    internal static void Export(string dataRoot,string staging,PersonalLibraryManifest manifest,CancellationToken token,IProgress<string>? progress)
    {
        string state=Path.Combine(staging,"state");Directory.CreateDirectory(state);
        var lyricDb=Path.Combine(state,"lyrics.db");Backup(Path.Combine(dataRoot,"lyrics.db"),lyricDb);CheckDatabase(lyricDb,true);
        var rows=new List<LibraryRow>();var bindings=new List<BoundRow>();var artworkRows=new List<ArtworkRow>();
        using(var db=Open(lyricDb))
        {
            using var command=db.CreateCommand();command.CommandText="SELECT Source,PersistentId,Title,Artist,Album,Duration FROM LibraryTracks WHERE Active=1 ORDER BY Source,PersistentId";
            using(var reader=command.ExecuteReader())while(reader.Read())rows.Add(new(Text(reader,0),Text(reader,1),ReadTrack(reader,2)));
            command.CommandText="SELECT t.Title,t.Artist,t.Album,t.Duration,b.Id,b.TrackId,d.Raw,d.Source,b.Manual,COALESCE(o.Offset,0),d.Instrumental,d.Plain,d.ProviderJson FROM Tracks t JOIN TrackLyricsBindings b ON b.TrackId=t.Id JOIN LyricsDocuments d ON d.Id=b.DocumentId LEFT JOIN SyncOffsets o ON o.BindingId=b.Id ORDER BY b.Manual DESC,b.Id";
            using var bound=command.ExecuteReader();while(bound.Read())bindings.Add(new(ReadTrack(bound,0),new(Text(bound,4),Text(bound,5),Text(bound,6),Text(bound,7),bound.GetBoolean(8),bound.GetDouble(9),bound.GetBoolean(10),bound.GetBoolean(11),bound.IsDBNull(12)?null:bound.GetString(12))));
        }
        foreach(var bound in bindings)if(!rows.Any(r=>Matches(r.Track,bound.Track)))rows.Add(new("local-binding",bound.Binding.TrackId,bound.Track));
        var artDb=Path.Combine(state,"artwork.db");
        if(File.Exists(Path.Combine(dataRoot,"artwork.db")))
        {
            Backup(Path.Combine(dataRoot,"artwork.db"),artDb);CheckDatabase(artDb,false);
            var imageRows=new List<(string Key,string Hash,int Width,int Height)>();
            using(var db=Open(artDb))
            {
                using var command=db.CreateCommand();command.CommandText="SELECT ImageKey,Hash,Width,Height FROM ArtworkImages";
                using(var reader=command.ExecuteReader())while(reader.Read())imageRows.Add(new(Text(reader,0),Text(reader,1),reader.GetInt32(2),reader.GetInt32(3)));
                command.CommandText="SELECT Source,PersistentId,Title,Artist,Album,Duration,ImageKey FROM LibraryArtwork WHERE Active=1";
                using var images=command.ExecuteReader();while(images.Read())artworkRows.Add(new(Text(images,0),Text(images,1),ReadTrack(images,2),Text(images,6)));
            }
            var invalid=new List<string>();int processed=0;
            foreach(var item in imageRows)
            {
                token.ThrowIfCancellationRequested();
                if(!PersonalLibraryArchive.IsHash(item.Key)){invalid.Add(item.Key);continue;}
                var source=Path.Combine(dataRoot,"cache","artwork",item.Key+".img");
                try
                {
                    if(!File.Exists(source) || new FileInfo(source).Length>ImageDimensions.MaximumBytes)throw new InvalidDataException();
                    var bytes=File.ReadAllBytes(source);var dimensions=ImageDimensions.Read(bytes);string hash=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                    if(!hash.Equals(item.Hash,StringComparison.OrdinalIgnoreCase) || dimensions!=(item.Width,item.Height))throw new InvalidDataException();
                    string path="covers/"+hash+(bytes[0]==137?".png":".jpg");string destination=PersonalLibraryArchive.AssetPath(staging,path);
                    if(!File.Exists(destination)){Directory.CreateDirectory(Path.GetDirectoryName(destination)!);File.WriteAllBytes(destination,bytes);PersonalLibraryArchive.AddFile(staging,path,"cover",manifest);}
                    manifest.Images.Add(new(item.Key,path,dimensions.Width,dimensions.Height));
                }
                catch(Exception error) when(error is IOException or InvalidDataException){invalid.Add(item.Key);manifest.Warnings.Add("封面缺失、损坏或在快照时发生变化："+item.Key+"；恢复后保持待补齐。");}
                if(++processed%100==0)progress?.Report($"已整理 {processed}/{imageRows.Count} 张封面");
            }
            // Only repair the new snapshot. The live cache and its workers are untouched.
            using var output=Open(artDb,false);using var tx=output.BeginTransaction();
            foreach(string key in invalid)
            {
                using var command=output.CreateCommand();command.CommandText="DELETE FROM ArtworkImages WHERE ImageKey=$key; UPDATE LibraryArtwork SET State='Pending',RetryUtc=NULL,Detail='个人备份未包含可用封面，等待补齐' WHERE ImageKey=$key";command.Parameters.AddWithValue("$key",key);command.ExecuteNonQuery();
            }
            using(var command=output.CreateCommand()){command.CommandText="UPDATE LibraryArtwork SET State='Pending',RetryUtc=NULL WHERE State='Cached' AND ImageKey NOT IN (SELECT ImageKey FROM ArtworkImages)";command.ExecuteNonQuery();}
            tx.Commit();
        }
        int count=0;
        foreach(var row in rows)
        {
            token.ThrowIfCancellationRequested();var bound=bindings.FirstOrDefault(b=>Matches(b.Track,row.Track))?.Binding;
            string id=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(row.Source+"\0"+row.PersistentId))).ToLowerInvariant()[..24];
            bool ready=bound is not null && (bound.Manual || LibraryPrefetcher.QualityOf(bound,row.Track));
            string kind=bound is null?"Missing":!ready?"NeedsReview":bound.Instrumental?"Instrumental":bound.Manual?"Manual":"Synced";
            string? lyric=null;
            if(ready && !bound!.Instrumental && !string.IsNullOrWhiteSpace(bound.Raw))
            {
                string name=row.Track.Artist+" - "+row.Track.Title;
                name=string.Concat(name.Select(c=>char.IsControl(c) || Path.GetInvalidFileNameChars().Contains(c)?'_':c));name=name[..Math.Min(80,name.Length)].Trim(' ','.');
                bool plain=bound.Plain || !new LrcParser().Parse(bound.Raw).IsSynced;
                lyric=$"lyrics/{name}-{id}."+(plain?"txt":"lrc");Directory.CreateDirectory(Path.Combine(staging,"lyrics"));
                File.WriteAllText(PersonalLibraryArchive.AssetPath(staging,lyric),bound.Raw,new UTF8Encoding(false));PersonalLibraryArchive.AddFile(staging,lyric,"lyric",manifest);
            }
            if(bound?.ProviderJson is { } provenance && bound.Source.StartsWith("Apple Music · 本机 TTML",StringComparison.Ordinal))
            {
                var original=AppleOriginalProvenance.Validate(provenance,bound.Raw);
                string relative="originals/"+AppleOriginalProvenance.Key(original)+".json";
                string destination=PersonalLibraryArchive.AssetPath(staging,relative);
                if(!File.Exists(destination))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);File.WriteAllText(destination,provenance,new UTF8Encoding(false));
                    PersonalLibraryArchive.AddFile(staging,relative,"original",manifest);
                }
            }
            var exact=artworkRows.FirstOrDefault(a=>a.Source==row.Source && a.PersistentId==row.PersistentId);
            var possible=exact is not null?[exact.ImageKey]:artworkRows.Where(a=>Matches(a.Track,row.Track)).Select(a=>a.ImageKey).Distinct().ToArray();
            string? cover=possible.Length==1?manifest.Images.FirstOrDefault(i=>i.ImageKey==possible[0])?.Path:null;
            manifest.Tracks.Add(new(id,row.Track,kind,lyric,cover,bound?.Manual==true,bound?.OffsetMs??0,bound?.Source,row.PersistentId));
            if(++count%200==0)progress?.Report($"已整理 {count}/{rows.Count} 首曲目");
        }
        var settings=Path.Combine(dataRoot,"settings.json");if(File.Exists(settings)){File.Copy(settings,Path.Combine(state,"settings.json"));PersonalLibraryArchive.AddFile(staging,"state/settings.json","state",manifest);}
        var audit=Path.Combine(dataRoot,"cache","apple-reference-audit.json");if(File.Exists(audit)){File.Copy(audit,Path.Combine(state,"apple-reference-audit.json"));PersonalLibraryArchive.AddFile(staging,"state/apple-reference-audit.json","state",manifest);}
        PersonalLibraryArchive.AddFile(staging,"state/lyrics.db","state",manifest);
        if(File.Exists(artDb))PersonalLibraryArchive.AddFile(staging,"state/artwork.db","state",manifest);
        if(File.Exists(Path.Combine(dataRoot,"translations.db")))
        {
            string target=Path.Combine(state,"translations.db");Backup(Path.Combine(dataRoot,"translations.db"),target);TranslationRepository.CheckDatabase(target);
            using var translations=new TranslationRepository(state,true);
            var json=new System.Text.Json.JsonSerializerOptions{WriteIndented=true,PropertyNamingPolicy=System.Text.Json.JsonNamingPolicy.CamelCase};
            foreach(var record in translations.ReadAll())
            {
                token.ThrowIfCancellationRequested();string id=TranslationAlignment.Hash(record.Track.Fingerprint+"\0"+record.Translation.SourceHash);
                string relative="translations/"+id+".json";Directory.CreateDirectory(Path.Combine(staging,"translations"));
                File.WriteAllText(PersonalLibraryArchive.AssetPath(staging,relative),System.Text.Json.JsonSerializer.Serialize(record,json),new UTF8Encoding(false));
                PersonalLibraryArchive.AddFile(staging,relative,"translation",manifest);
            }
            PersonalLibraryArchive.AddFile(staging,"state/translations.db","state",manifest);
        }
        InstrumentBackup.Export(dataRoot,staging,manifest,token);
        string reviews=Path.Combine(dataRoot,"lyrics-review");
        if(Directory.Exists(reviews))
        {
            foreach(string folder in Directory.EnumerateDirectories(reviews))
            {
                token.ThrowIfCancellationRequested();string source=PersonalLibraryArchive.AssetPath(dataRoot,"lyrics-review/"+Path.GetFileName(folder)+"/review.json");
                if(!File.Exists(source))continue;var review=LyricsReviewStore.Validate(source);
                if(review.Id!=Path.GetFileName(folder))throw new InvalidDataException("校对记录目录与身份不一致。");
                foreach(string name in review.ImageSha256 is null?new[]{"review.json"}:new[]{"review.json","capture.png"})
                {
                    string relative="lyrics-review/"+review.Id+"/"+name;
                    string target=PersonalLibraryArchive.AssetPath(staging,relative);Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(PersonalLibraryArchive.AssetPath(dataRoot,relative),target);PersonalLibraryArchive.AddFile(staging,relative,"review",manifest);
                }
            }
        }
    }
}
