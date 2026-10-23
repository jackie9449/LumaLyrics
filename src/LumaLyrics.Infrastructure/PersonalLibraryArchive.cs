using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
namespace LumaLyrics.Infrastructure;

public static class PersonalLibraryArchive
{
    static readonly JsonSerializerOptions Json=new(){WriteIndented=true,PropertyNamingPolicy=JsonNamingPolicy.CamelCase};
    static readonly HashSet<string> StateFiles=["state/lyrics.db","state/artwork.db","state/translations.db","state/settings.json","state/apple-reference-audit.json"];
    internal static bool IsHash(string? value)=>value is {Length:64} && value.All(Uri.IsHexDigit);
    static string Hash(string path){using var stream=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();}
    internal static void AddFile(string root,string relative,string kind,PersonalLibraryManifest manifest)
    {string path=AssetPath(root,relative);manifest.Files.Add(new(relative,Hash(path),new FileInfo(path).Length,kind));}
    static bool Inside(string child,string parent)=>Path.GetFullPath(child).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    public static void EnsureOutsideSoftware(string path)
    {
        string full=Path.GetFullPath(path),software=Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
        if(full.Equals(software,StringComparison.OrdinalIgnoreCase)||Inside(full,software))throw new InvalidDataException("个人文件请保存在软件目录之外，以便独立备份和更新。");
        for(var directory=new DirectoryInfo(Directory.Exists(full)?full:Path.GetDirectoryName(full)!);directory is not null;directory=directory.Parent)
            if(File.Exists(Path.Combine(directory.FullName,"LumaLyrics.dll"))&&File.Exists(Path.Combine(directory.FullName,"LumaLyrics.runtimeconfig.json")))throw new InvalidDataException("个人文件不能放在当前或旧版的软件目录中，请选择独立位置。");
    }
    static void Separate(string source,string destination)
    {if(Path.GetFullPath(source).Equals(Path.GetFullPath(destination),StringComparison.OrdinalIgnoreCase)||Inside(destination,source)||Inside(source,destination))throw new InvalidDataException("来源与目标目录不能相同或互相包含。");}
    internal static string AssetPath(string root,string relative)
    {
        if(string.IsNullOrWhiteSpace(relative)||relative.Contains('\\')||Path.IsPathRooted(relative))throw new InvalidDataException("个人文件路径无效。");
        var segments=relative.Split('/');
        if(segments.Any(s=>s is "" or "." or ".." || s.EndsWith(' ') || s.EndsWith('.') || s.Any(c=>char.IsControl(c)||Path.GetInvalidFileNameChars().Contains(c))))throw new InvalidDataException("个人文件包含不安全路径。");
        var path=Path.GetFullPath(Path.Combine(root,Path.Combine(segments)));
        if(!Inside(path,root))throw new InvalidDataException("个人文件超出资料目录。");
        var current=Path.GetFullPath(root);
        foreach(var segment in new[]{""}.Concat(segments))
        {
            if(segment.Length>0)current=Path.Combine(current,segment);
            if((File.Exists(current)||Directory.Exists(current)) && (File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("个人资料中不接受链接或重定向文件。");
        }
        return path;
    }
    static T NewDirectory<T>(string destination,Func<string,T> build,CancellationToken token)
    {
        destination=Path.GetFullPath(destination);EnsureOutsideSoftware(destination);
        if(File.Exists(destination)||Directory.Exists(destination))throw new IOException("目标已存在，请选择一个新的资料目录。");
        string parent=Path.GetDirectoryName(destination)??throw new InvalidDataException("请选择有效目录。");
        if(!Directory.Exists(parent))throw new DirectoryNotFoundException("目标的父目录不存在。");
        string stage=Path.Combine(parent,".luma-personal-"+Guid.NewGuid().ToString("N")+".partial");Directory.CreateDirectory(stage);
        try{token.ThrowIfCancellationRequested();var result=build(stage);token.ThrowIfCancellationRequested();Directory.Move(stage,destination);return result;}
        finally{DeleteStage(stage,parent);}
    }
    static void DeleteStage(string stage,string parent)
    {
        // Only a newly created sibling with our unpredictable staging name is owned by this operation.
        string full=Path.GetFullPath(stage);
        if(!Path.GetDirectoryName(full)!.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent)),StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(full).StartsWith(".luma-personal-",StringComparison.Ordinal))throw new InvalidOperationException("临时目录范围校验失败。");
        if(Directory.Exists(full))
        {if((File.GetAttributes(full)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("临时目录被重定向。");Directory.Delete(full,true);}
    }

    public static PersonalLibraryManifest Export(string dataRoot,string destination,CancellationToken token=default,IProgress<string>? progress=null)
    {
        dataRoot=Path.GetFullPath(dataRoot);Separate(dataRoot,destination);
        if(!File.Exists(Path.Combine(dataRoot,"lyrics.db")))throw new FileNotFoundException("此目录中还没有个人歌词资料库。");
        return NewDirectory(destination,staging=>{
            progress?.Report("正在创建一致的资料库快照，原资料保持可用…");var manifest=new PersonalLibraryManifest();
            PersonalLibrarySnapshot.Export(dataRoot,staging,manifest,token,progress);
            File.WriteAllText(Path.Combine(staging,"README.md"),"# 浮光歌词个人资料\n\n这是私人备份，与软件安装包分别保存。lyrics/ 中是已通过自动检查或手动保留的歌词，covers/ 中是原始图片，translations/ 中是可阅读的中文译文 JSON（包含原词哈希、逐行编号与来源）；曲目对应关系、来源和校准记录见 personal-library.json。纯音乐没有歌词文件，缺失或待核歌词不生成文件。state/ 保留数据库快照、手动绑定、设置和待导入队列，其中的历史候选不代表全部准确或已由 Apple 核对。译文的原文及逐行对应检查不等于语义已经人工逐首校对。\n\n在新版浮光歌词“设置 → 个人资料”中恢复到一个新目录；不会覆盖已有资料。备份后新增的下载需要再次导出。包含 translations.db 的备份请使用 0.10.0 或更新版本恢复。\n",new UTF8Encoding(false));
            AddFile(staging,"README.md","readme",manifest);
            DataDirectoryConfiguration.ProtectFromGit(staging);AddFile(staging,".gitignore","metadata",manifest);
            File.WriteAllText(Path.Combine(staging,PersonalLibraryManifest.FileName),JsonSerializer.Serialize(manifest,Json),new UTF8Encoding(false));
            ValidateFolder(staging,token);progress?.Report("个人资料已导出并通过校验。");return manifest;
        },token);
    }
    static PersonalLibraryManifest ReadManifest(Stream stream)
    {
        var manifest=JsonSerializer.Deserialize<PersonalLibraryManifest>(stream,Json)??throw new InvalidDataException("个人资料清单为空。");
        if(manifest.Format!="LumaLyrics.PersonalLibrary"||manifest.Version!=1)throw new InvalidDataException("个人资料格式或版本不受支持，请使用对应版本软件。");
        if(manifest.Files is null || manifest.Images is null || manifest.Tracks is null || manifest.Warnings is null || manifest.Files.Count>250000 || manifest.Tracks.Count>1000000)throw new InvalidDataException("个人资料清单无效。");
        if(!manifest.Files.Any(f=>f is not null && f.Path=="state/lyrics.db"))throw new InvalidDataException("备份缺少歌词数据库快照。");
        var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var file in manifest.Files)
        {
            if(file is null || string.IsNullOrWhiteSpace(file.Path) || !paths.Add(file.Path)||!IsHash(file.Sha256)||file.Bytes<0)throw new InvalidDataException("个人资料文件记录无效或重复。");
            bool valid=file.Kind switch {
                "state"=>StateFiles.Contains(file.Path),
                "lyric"=>file.Path.StartsWith("lyrics/",StringComparison.Ordinal) && Path.GetExtension(file.Path) is ".lrc" or ".txt",
                "translation"=>file.Path.StartsWith("translations/",StringComparison.Ordinal) && Path.GetExtension(file.Path)==".json",
                "review"=>file.Path.Split('/') is ["lyrics-review",{Length:>0},"review.json" or "capture.png"],
                "cover"=>file.Path.StartsWith("covers/",StringComparison.Ordinal) && Path.GetExtension(file.Path) is ".png" or ".jpg",
                "readme"=>file.Path=="README.md","metadata"=>file.Path==".gitignore",_=>false
            };
            if(!valid)throw new InvalidDataException("个人备份中包含不支持的文件类型。");
        }
        return manifest;
    }
    static PersonalLibraryManifest ValidateFolder(string root,CancellationToken token)
    {
        var manifestPath=AssetPath(root,PersonalLibraryManifest.FileName);
        if(new FileInfo(manifestPath).Length>64*1024*1024)throw new InvalidDataException("个人资料清单过大。");
        using var stream=File.OpenRead(manifestPath);var manifest=ReadManifest(stream);var files=manifest.Files.ToDictionary(f=>f.Path,StringComparer.OrdinalIgnoreCase);
        foreach(var file in manifest.Files)
        {
            token.ThrowIfCancellationRequested();string path=AssetPath(root,file.Path);
            if(!File.Exists(path)||new FileInfo(path).Length!=file.Bytes||!Hash(path).Equals(file.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("个人文件缺失或校验不一致："+file.Path);
        }
        IEnumerable<string> SafeFiles(string directory)
        {
            foreach(var path in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes=File.GetAttributes(path);
                if((attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("个人资料中不接受链接或重定向文件。");
                if((attributes&FileAttributes.Directory)!=0){foreach(var file in SafeFiles(path))yield return file;}else yield return path;
            }
        }
        var actual=SafeFiles(root).Select(p=>Path.GetRelativePath(root,p).Replace('\\','/')).ToArray();
        if(actual.Length!=files.Count+1||actual.Any(p=>p!=PersonalLibraryManifest.FileName&&!files.ContainsKey(p)))throw new InvalidDataException("个人资料目录含清单之外的文件。");
        PersonalLibrarySnapshot.CheckDatabase(AssetPath(root,"state/lyrics.db"),true);
        if(files.ContainsKey("state/artwork.db"))PersonalLibrarySnapshot.CheckDatabase(AssetPath(root,"state/artwork.db"),false);
        if(files.ContainsKey("state/translations.db"))TranslationRepository.CheckDatabase(AssetPath(root,"state/translations.db"));
        foreach(var translation in manifest.Files.Where(f=>f.Kind=="translation"))
        {
            string path=AssetPath(root,translation.Path);if(translation.Bytes>4*1024*1024)throw new InvalidDataException("中文译文文件过大。");
            var record=JsonSerializer.Deserialize<StoredTranslation>(File.ReadAllText(path),Json)??throw new InvalidDataException("中文译文文件为空。");
            if(record.Track is null || record.Translation is null)throw new InvalidDataException("中文译文文件记录无效。");TranslationRepository.Validate(record.Translation);
        }
        foreach(var group in manifest.Files.Where(f=>f.Kind=="review").GroupBy(f=>Path.GetDirectoryName(f.Path)!.Replace('\\','/')))
        {
            string relative=group.Key+"/review.json";
            if(!group.Any(f=>f.Path==relative))throw new InvalidDataException("校对图像没有对应的文字记录。");
            var review=LyricsReviewStore.Validate(AssetPath(root,relative));
            if(review.Id!=Path.GetFileName(group.Key) || group.Count()!=(review.ImageSha256 is null?1:2))throw new InvalidDataException("校对记录文件对应不一致。");
        }
        if(files.ContainsKey("state/settings.json")){using var settings=File.OpenRead(AssetPath(root,"state/settings.json"));if(JsonSerializer.Deserialize<AppSettings>(settings) is null)throw new InvalidDataException("个人设置为空。");}
        var keys=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var image in manifest.Images)
        {
            if(image is null||!IsHash(image.ImageKey)||string.IsNullOrWhiteSpace(image.Path)||!keys.Add(image.ImageKey)||!files.TryGetValue(image.Path,out var file)||file.Kind!="cover")throw new InvalidDataException("封面映射无效。");
            var size=ImageDimensions.Read(File.ReadAllBytes(AssetPath(root,image.Path)));if(size!=(image.Width,image.Height))throw new InvalidDataException("封面尺寸记录不一致。");
        }
        if(files.ContainsKey("state/artwork.db"))
        {
            using var db=PersonalLibrarySnapshot.Open(AssetPath(root,"state/artwork.db"));using var command=db.CreateCommand();command.CommandText="SELECT ImageKey,Hash,Width,Height FROM ArtworkImages";
            var images=manifest.Images.ToDictionary(i=>i.ImageKey,StringComparer.OrdinalIgnoreCase);int count=0;
            using(var reader=command.ExecuteReader())while(reader.Read())
            {
                if(!images.TryGetValue(reader.GetString(0),out var image)||!files[image.Path].Sha256.Equals(reader.GetString(1),StringComparison.OrdinalIgnoreCase)||image.Width!=reader.GetInt32(2)||image.Height!=reader.GetInt32(3))throw new InvalidDataException("封面清单与数据库映射不一致。");
                count++;
            }
            if(count!=images.Count)throw new InvalidDataException("封面清单与数据库数量不一致。");
        }
        else if(manifest.Images.Count>0)throw new InvalidDataException("备份缺少封面映射数据库。");
        var ids=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var track in manifest.Tracks)
        {
            if(track is null||track.Track is null||string.IsNullOrWhiteSpace(track.Id)||!ids.Add(track.Id)||!double.IsFinite(track.OffsetMs)||track.LyricState is not ("Synced" or "Manual" or "Instrumental" or "Missing" or "NeedsReview"))throw new InvalidDataException("曲目记录无效。");
            if(track.LyricPath is not null && (!files.TryGetValue(track.LyricPath,out var lyric)||lyric.Kind!="lyric"||track.LyricState is "Instrumental" or "Missing" or "NeedsReview"))throw new InvalidDataException("歌词文件映射无效。");
            if(track.CoverPath is not null&&!manifest.Images.Any(i=>i.Path==track.CoverPath))throw new InvalidDataException("曲目封面映射无效。");
        }
        return manifest;
    }
    static T ReadArchive<T>(string source,CancellationToken token,Func<string,PersonalLibraryManifest,T> read)
    {
        source=Path.GetFullPath(source);
        if(Directory.Exists(source))return read(source,ValidateFolder(source,token));
        string parent=Path.GetTempPath(),stage=Path.Combine(parent,".luma-personal-"+Guid.NewGuid().ToString("N")+".partial");Directory.CreateDirectory(stage);
        try
        {
            using var zip=ZipFile.OpenRead(source);
            var manifestEntry=zip.GetEntry(PersonalLibraryManifest.FileName)??throw new InvalidDataException("ZIP 中没有个人资料清单。");
            if(manifestEntry.Length>64*1024*1024)throw new InvalidDataException("个人资料清单过大。");
            PersonalLibraryManifest manifest;using(var manifestStream=manifestEntry.Open())manifest=ReadManifest(manifestStream);
            var declared=manifest.Files.Select(f=>f.Path).Append(PersonalLibraryManifest.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long total=0;
            foreach(var entry in zip.Entries)
            {
                token.ThrowIfCancellationRequested();string path=AssetPath(stage,entry.FullName);
                if(!declared.Contains(entry.FullName)||!seen.Add(entry.FullName)||((entry.ExternalAttributes>>16)&0xF000)==0xA000)throw new InvalidDataException("ZIP 中存在重复、链接或未声明文件。");
                total=checked(total+entry.Length);if(total>40L*1024*1024*1024)throw new InvalidDataException("个人资料 ZIP 超出容量限制。");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var input=entry.Open();using var output=new FileStream(path,FileMode.CreateNew);
                var buffer=new byte[65536];long copied=0;int readBytes;
                while((readBytes=input.Read(buffer))>0){token.ThrowIfCancellationRequested();copied=checked(copied+readBytes);if(copied>entry.Length)throw new InvalidDataException("ZIP 文件长度不一致。");output.Write(buffer,0,readBytes);}
                if(copied!=entry.Length)throw new InvalidDataException("ZIP 文件不完整。");
            }
            return read(stage,ValidateFolder(stage,token));
        }
        finally{DeleteStage(stage,parent);}
    }
    public static PersonalLibraryManifest Validate(string source,CancellationToken token=default)=>ReadArchive(source,token,(_,manifest)=>manifest);
    public static PersonalLibraryManifest ExportZip(string dataRoot,string destination,CancellationToken token=default,IProgress<string>? progress=null)
    {
        string parent=Path.GetTempPath(),stage=Path.Combine(parent,".luma-personal-"+Guid.NewGuid().ToString("N")+".partial");
        try{var manifest=Export(dataRoot,stage,token,progress);CreateZip(stage,destination,token,progress);return manifest;}
        finally{DeleteStage(stage,parent);}
    }
    public static PersonalLibraryManifest Restore(string source,string destination,CancellationToken token=default,IProgress<string>? progress=null)
    {
        Separate(source,destination);EnsureOutsideSoftware(destination);
        if(Directory.Exists(destination)||File.Exists(destination))throw new IOException("目标已存在，请选择一个新的资料目录。");
        return ReadArchive(source,token,(root,manifest)=>NewDirectory(destination,staging=>{
            progress?.Report("正在恢复到新目录，已有资料保持不变…");
            foreach(var file in manifest.Files.Where(f=>f.Kind=="state"))
            {
                token.ThrowIfCancellationRequested();string name=Path.GetFileName(file.Path);
                string target=name=="apple-reference-audit.json"?Path.Combine(staging,"cache",name):Path.Combine(staging,name);Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(AssetPath(root,file.Path),target);if(Hash(target)!=file.Sha256.ToLowerInvariant())throw new InvalidDataException("恢复时文件校验失败。");
            }
            foreach(var file in manifest.Files.Where(f=>f.Kind=="review"))
            {
                token.ThrowIfCancellationRequested();string target=AssetPath(staging,file.Path);Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(AssetPath(root,file.Path),target);
                if(Hash(target)!=file.Sha256.ToLowerInvariant())throw new InvalidDataException("恢复时校对记录校验失败。");
            }
            var covers=Path.Combine(staging,"cache","artwork");Directory.CreateDirectory(covers);
            foreach(var image in manifest.Images)
            {
                token.ThrowIfCancellationRequested();string target=Path.Combine(covers,image.ImageKey+".img");File.Copy(AssetPath(root,image.Path),target);
                if(Hash(target)!=manifest.Files.Single(f=>f.Path==image.Path).Sha256.ToLowerInvariant())throw new InvalidDataException("恢复时封面校验失败。");
            }
            DataDirectoryConfiguration.ProtectFromGit(staging);PersonalLibrarySnapshot.CheckDatabase(Path.Combine(staging,"lyrics.db"),true);progress?.Report("恢复完成；可选择下次启动使用此目录。");return manifest;
        },token));
    }
    public static void CreateZip(string source,string destination,CancellationToken token=default,IProgress<string>? progress=null)
    {
        Separate(source,destination);EnsureOutsideSoftware(destination);var manifest=ValidateFolder(source,token);
        destination=Path.GetFullPath(destination);if(File.Exists(destination)||Directory.Exists(destination))throw new IOException("目标文件已存在，请选择新的文件名。");
        string temp=destination+"."+Guid.NewGuid().ToString("N")+".partial";
        try
        {
            using(var zip=ZipFile.Open(temp,ZipArchiveMode.Create))
                foreach(var path in manifest.Files.Select(f=>f.Path).Append(PersonalLibraryManifest.FileName))
                {token.ThrowIfCancellationRequested();zip.CreateEntryFromFile(AssetPath(source,path),path,CompressionLevel.Fastest);}
            token.ThrowIfCancellationRequested();File.Move(temp,destination);progress?.Report("个人 ZIP 已单独保存。");
        }
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
