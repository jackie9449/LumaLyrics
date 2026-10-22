using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
using Microsoft.Data.Sqlite;
namespace LumaLyrics.Infrastructure.Tests;

public sealed class PersonalLibraryTests : IDisposable
{
    readonly string root=Path.Combine(Path.GetTempPath(),"Luma-personal-tests-"+Guid.NewGuid().ToString("N"));
    static readonly Track Manual=new("Own calibrated song","Own artist","Own album",180000);
    static readonly Track Automatic=Manual with{Title="Own timed song"};
    static readonly Track Instrumental=Manual with{Title="Own instrumental"};
    static readonly Track Missing=Manual with{Title="Own missing song"};
    static readonly Track OldPlain=Manual with{Title="Old automatic plain text"};
    static readonly Track ManualPlain=Manual with{Title="Own manual text"};
    static readonly string Timed="[00:00.00]这是一段自写的测试歌词\n[00:45.00]清晨的微光穿过窗边\n[01:30.00]让这一刻随音乐走远\n[02:10.00]最后一句留在时间里面";
    string PathOf(string name)=>Path.Combine(root,name);
    public PersonalLibraryTests()=>Directory.CreateDirectory(root);
    LyricsRepository Seed()
    {
        string source=PathOf("source");Directory.CreateDirectory(source);
        var repository=new LyricsRepository(Path.Combine(source,"lyrics.db"));
        var tracks=new[]{Manual,Automatic,Instrumental,Missing,OldPlain,ManualPlain};
        var snapshot=new LibrarySnapshot("original-library",tracks.Length,tracks.Select((t,i)=>new LibraryTrack(i.ToString(),t)).ToArray(),"original");
        repository.SyncLibrary(snapshot);
        var binding=repository.Save(Manual,Timed,"Own manual correction",true);repository.SetOffset(binding.Id,450);
        repository.Save(Automatic,Timed,"Own checked fixture",false);
        repository.Save(Instrumental,"","Own instrumental fixture",false,instrumental:true);
        repository.Save(OldPlain,"Old untimed automatic content","Own old fixture",false,plain:true);
        repository.Save(ManualPlain,"这是手动保留的原创文字。","Own manual fixture",true,plain:true);
        using var artwork=new ArtworkCache(source);
        artwork.Sync(snapshot,new Dictionary<string,AppleArtworkSource>{
            ["0"]=new("0","shared",new("https://is1-ssl.mzstatic.com/own-shared.png"),null),
            ["1"]=new("1","shared",new("https://is1-ssl.mzstatic.com/own-shared.png"),null),
            ["2"]=new("2","small",new("https://is1-ssl.mzstatic.com/own-small.png"),null)
        });
        foreach(var item in artwork.GetQueue().Where(i=>i.Track==Manual||i.Track==Instrumental).DistinctBy(i=>i.ImageKey))artwork.Save(item,Png(item.Track==Manual?1200:600));
        new SettingsStore(source).Save(new(){UiTheme="frost",GlobalOffsetMs=123,OverlayFontSize=44,OnlineLookup=false});
        return repository;
    }
    static byte[] Png(int size)
    {
        using var output=new MemoryStream();output.Write(new byte[]{137,80,78,71,13,10,26,10});
        void Chunk(string type,byte[] data)
        {
            var name=System.Text.Encoding.ASCII.GetBytes(type);var length=new byte[4];BinaryPrimitives.WriteInt32BigEndian(length,data.Length);output.Write(length);output.Write(name);output.Write(data);
            uint crc=0xffffffff;foreach(byte b in name.Concat(data)){crc^=b;for(int k=0;k<8;k++)crc=(crc&1)!=0?(crc>>1)^0xedb88320:crc>>1;}BinaryPrimitives.WriteUInt32BigEndian(length,~crc);output.Write(length);
        }
        var header=new byte[13];BinaryPrimitives.WriteInt32BigEndian(header,size);BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4),size);header[8]=8;header[9]=6;Chunk("IHDR",header);
        using var compressed=new MemoryStream();using(var z=new ZLibStream(compressed,CompressionLevel.Fastest,true)){var line=new byte[size*4+1];for(int y=0;y<size;y++)z.Write(line);}Chunk("IDAT",compressed.ToArray());Chunk("IEND",[]);return output.ToArray();
    }
    [Fact] public void LiveWalExportPreservesManualOffsetsSettingsAndReadableFiles()
    {
        using var source=Seed();Assert.True(File.Exists(PathOf("source/lyrics.db-wal")));
        var manifest=PersonalLibraryArchive.Export(PathOf("source"),PathOf("collection"));
        Assert.Equal(6,manifest.Summary.Tracks);Assert.Equal(3,manifest.Summary.LyricFiles);Assert.Equal(1,manifest.Summary.AutomaticSynced);
        Assert.Equal(2,manifest.Summary.Manual);Assert.Equal(1,manifest.Summary.Instrumental);Assert.Equal(2,manifest.Summary.MissingOrNeedsReview);
        Assert.Equal(3,manifest.Summary.CoverTracks);Assert.Equal(2,manifest.Summary.HighResolutionCoverTracks);Assert.Equal(2,manifest.Summary.UniqueCovers);
        var manual=manifest.Tracks.Single(t=>t.Track==Manual);Assert.Equal(Timed,File.ReadAllText(PathOf("collection/"+manual.LyricPath)));Assert.Equal(450,manual.OffsetMs);
        Assert.Null(manifest.Tracks.Single(t=>t.Track==Instrumental).LyricPath);Assert.Null(manifest.Tracks.Single(t=>t.Track==OldPlain).LyricPath);
        Assert.EndsWith(".txt",manifest.Tracks.Single(t=>t.Track==ManualPlain).LyricPath);
        PersonalLibraryArchive.Restore(PathOf("collection"),PathOf("restored"));
        using var restored=new LyricsRepository(PathOf("restored/lyrics.db"));var binding=restored.Find(Manual)!;
        Assert.True(binding.Manual);Assert.Equal(450,binding.OffsetMs);Assert.Equal(Timed,binding.Raw);Assert.Equal(source.Find(Automatic),restored.Find(Automatic));
        var settings=new SettingsStore(PathOf("restored")).Load();Assert.Equal("frost",settings.UiTheme);Assert.Equal(44,settings.OverlayFontSize);Assert.Equal(123,settings.GlobalOffsetMs);
        using var artwork=new ArtworkCache(PathOf("restored"));Assert.Equal(1200,artwork.Find(Automatic)!.Width);Assert.Equal(600,artwork.Find(Instrumental)!.Width);
        Assert.Equal(2,Directory.GetFiles(PathOf("restored/cache/artwork"),"*.img").Length);
    }
    [Fact] public void ZipRoundTripIsOfflineAndContainsNoSoftwareOrRuntimeLocks()
    {
        using var source=Seed();File.WriteAllText(PathOf("source/library-prefetch.pause"),"");File.WriteAllText(PathOf("source/library-import-worker.json"),"private worker");
        var original=PersonalLibraryArchive.Export(PathOf("source"),PathOf("collection"));PersonalLibraryArchive.CreateZip(PathOf("collection"),PathOf("own.lumalibrary.zip"));
        var validated=PersonalLibraryArchive.Validate(PathOf("own.lumalibrary.zip"));Assert.Equal(original.Summary,validated.Summary);
        PersonalLibraryArchive.Restore(PathOf("own.lumalibrary.zip"),PathOf("restored"));
        using var restored=new LyricsRepository(PathOf("restored/lyrics.db"));Assert.Equal(source.GetLibraryProgress(),restored.GetLibraryProgress());
        using var zip=ZipFile.OpenRead(PathOf("own.lumalibrary.zip"));Assert.DoesNotContain(zip.Entries,e=>e.FullName.EndsWith(".dll")||e.FullName.Contains("prefetch.pause")||e.FullName.Contains("worker.json"));
        Assert.Equal("",File.ReadAllText(PathOf("source/library-prefetch.pause")));
    }
    [Fact] public void TranslationSnapshotAndReadableProvenanceSurvivePersonalRoundTrip()
    {
        using var source=Seed();var document=new LrcParser().Parse(Timed);
        var translation=new ChineseTranslation(TranslationAlignment.Hash(Timed),[new(0,"自写译文第一行"),new(1,"自写译文第二行"),new(2,"自写译文第三行"),new(3,"自写译文第四行")],"Own translation source","https://example.org/own","Own review status",DateTimeOffset.UtcNow);
        using(var translations=new TranslationRepository(PathOf("source")))translations.Save(Manual,translation,true);
        var settings=new SettingsStore(PathOf("source"));var preferences=settings.Load();preferences.ShowChineseTranslation=true;preferences.OverlayShowChineseTranslation=false;settings.Save(preferences);
        var manifest=PersonalLibraryArchive.Export(PathOf("source"),PathOf("translated"));Assert.Contains(manifest.Files,f=>f.Path=="state/translations.db");
        var readable=Assert.Single(manifest.Files,f=>f.Kind=="translation");Assert.Contains("sourceHash",File.ReadAllText(PathOf("translated/"+readable.Path)));
        PersonalLibraryArchive.CreateZip(PathOf("translated"),PathOf("translated.lumalibrary.zip"));PersonalLibraryArchive.Restore(PathOf("translated.lumalibrary.zip"),PathOf("restored-translated"));
        using var restored=new TranslationRepository(PathOf("restored-translated"),true);Assert.Equal(translation.SourceHash,restored.Find(Manual,document)!.SourceHash);Assert.True(Assert.Single(restored.ReadAll()).Manual);
        var restoredSettings=new SettingsStore(PathOf("restored-translated")).Load();Assert.True(restoredSettings.ShowChineseTranslation);Assert.False(restoredSettings.OverlayShowChineseTranslation);
    }
    [Fact] public void CorruptTranslationSnapshotCannotBeExportedAsAValidBackup()
    {
        using var source=Seed();File.WriteAllText(PathOf("source/translations.db"),"Corrupt original fixture");Assert.Throws<SqliteException>(()=>PersonalLibraryArchive.Export(PathOf("source"),PathOf("bad-translations")));Assert.False(Directory.Exists(PathOf("bad-translations")));
    }
    [Fact] public void CorruptCoverIsFlaggedAndOnlyTheExportSnapshotIsRepaired()
    {
        using var source=Seed();string bad=Directory.GetFiles(PathOf("source/cache/artwork"),"*.img").Order().First();File.WriteAllText(bad,"broken");
        var manifest=PersonalLibraryArchive.Export(PathOf("source"),PathOf("collection"));Assert.Single(manifest.Warnings);Assert.Single(manifest.Images);
        using(var db=new SqliteConnection($"Data Source={PathOf("source/artwork.db")}")){db.Open();using var c=db.CreateCommand();c.CommandText="SELECT COUNT(*) FROM ArtworkImages";Assert.Equal(2L,c.ExecuteScalar());}
        PersonalLibraryArchive.Restore(PathOf("collection"),PathOf("restored"));using var restored=new ArtworkCache(PathOf("restored"));Assert.Contains(restored.GetQueue(),q=>q.ImageKey==Path.GetFileNameWithoutExtension(bad));
    }
    [Fact] public void ExistingTargetsAreNeverOverwritten()
    {
        using var source=Seed();PersonalLibraryArchive.Export(PathOf("source"),PathOf("collection"));Directory.CreateDirectory(PathOf("existing"));File.WriteAllText(PathOf("existing/keep.txt"),"keep");
        Assert.Throws<IOException>(()=>PersonalLibraryArchive.Export(PathOf("source"),PathOf("existing")));
        Assert.Throws<IOException>(()=>PersonalLibraryArchive.Restore(PathOf("collection"),PathOf("existing")));
        File.WriteAllText(PathOf("own.zip"),"keep zip");Assert.Throws<IOException>(()=>PersonalLibraryArchive.CreateZip(PathOf("collection"),PathOf("own.zip")));
        Assert.Equal("keep",File.ReadAllText(PathOf("existing/keep.txt")));Assert.Equal("keep zip",File.ReadAllText(PathOf("own.zip")));
    }
    [Fact] public void TamperingIsDetectedBeforePublishingARestore()
    {
        using var source=Seed();var manifest=PersonalLibraryArchive.Export(PathOf("source"),PathOf("collection"));
        File.AppendAllText(PathOf("collection/"+manifest.Tracks.First(t=>t.LyricPath is not null).LyricPath),"tampered");
        Assert.Throws<InvalidDataException>(()=>PersonalLibraryArchive.Restore(PathOf("collection"),PathOf("restored")));Assert.False(Directory.Exists(PathOf("restored")));
        Assert.Equal(450,source.Find(Manual)!.OffsetMs);
    }
    [Fact] public void FutureManifestVersionsAreRejected()
    {
        using var source=Seed();PersonalLibraryArchive.Export(PathOf("source"),PathOf("collection"));string file=PathOf("collection/"+PersonalLibraryManifest.FileName);
        var json=JsonNode.Parse(File.ReadAllText(file))!;json["version"]=2;File.WriteAllText(file,json.ToJsonString());
        Assert.Throws<InvalidDataException>(()=>PersonalLibraryArchive.Validate(PathOf("collection")));
    }
    [Theory] [InlineData("../outside.txt")] [InlineData("C:/outside.txt")] [InlineData("state/../../outside.txt")] [InlineData("state\\outside.txt")]
    public void ArchiveTraversalCannotWriteOutsideStaging(string entry)
    {
        using var source=Seed();PersonalLibraryArchive.Export(PathOf("source"),PathOf("collection"));PersonalLibraryArchive.CreateZip(PathOf("collection"),PathOf("own.zip"));
        using(var zip=ZipFile.Open(PathOf("own.zip"),ZipArchiveMode.Update)){using var writer=new StreamWriter(zip.CreateEntry(entry).Open());writer.Write("unsafe");}
        Assert.Throws<InvalidDataException>(()=>PersonalLibraryArchive.Restore(PathOf("own.zip"),PathOf("restored")));Assert.False(Directory.Exists(PathOf("restored")));Assert.False(File.Exists(PathOf("outside.txt")));
    }
    sealed class DirectProgress(Action<string> report):IProgress<string>{public void Report(string value)=>report(value);}
    [Fact] public void CancelledExportRemovesOnlyItsOwnStagingDirectory()
    {
        using var source=Seed();using var cancel=new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(()=>PersonalLibraryArchive.Export(PathOf("source"),PathOf("collection"),cancel.Token,new DirectProgress(_=>cancel.Cancel())));
        Assert.False(Directory.Exists(PathOf("collection")));Assert.Empty(Directory.GetDirectories(root,".luma-personal-*"));Assert.Equal(Timed,source.Find(Manual)!.Raw);
    }
    [Fact] public void ProfileSelectionIsExternalExplicitAndDoesNotReplaceCurrentData()
    {
        using var source=Seed();string config=PathOf("location.json");
        Assert.Equal(DataDirectoryConfiguration.DefaultRoot,DataDirectoryConfiguration.Resolve(configurationFile:config));
        DataDirectoryConfiguration.SetDefault(PathOf("source"),config);Assert.Equal(PathOf("source"),DataDirectoryConfiguration.Resolve(configurationFile:config));
        Assert.Equal(PathOf("isolated"),DataDirectoryConfiguration.Resolve(PathOf("isolated"),config));
        File.WriteAllText(config,"{broken");Assert.Throws<InvalidDataException>(()=>DataDirectoryConfiguration.Resolve(configurationFile:config));
        Assert.Equal(PathOf("isolated"),DataDirectoryConfiguration.Resolve(PathOf("isolated"),config));
    }
    [Fact] public void MissingConfiguredDataDoesNotCreateAnEmptyLibrary()
    {
        string missing=PathOf("missing");string config=PathOf("location.json");File.WriteAllText(config,System.Text.Json.JsonSerializer.Serialize(new{Version=1,DataDirectory=missing}));
        Assert.Throws<DirectoryNotFoundException>(()=>DataDirectoryConfiguration.Resolve(configurationFile:config));Assert.False(Directory.Exists(missing));
        Assert.Throws<InvalidDataException>(()=>PersonalLibraryArchive.EnsureOutsideSoftware(Path.Combine(AppContext.BaseDirectory,"personal")));
    }
    [Fact] public void ClearingResponseCachePreservesAllImportedPersonalLyricsAndOffsets()
    {
        using var source=Seed();var automatic=source.Find(Automatic);var manual=source.Find(Manual);var progress=source.GetLibraryProgress();
        source.RememberFailure(Missing);Assert.True(source.HasFailure(Missing));source.ClearNetworkCache();
        Assert.False(source.HasFailure(Missing));Assert.Equal(automatic,source.Find(Automatic));Assert.Equal(manual,source.Find(Manual));Assert.Equal(progress,source.GetLibraryProgress());
    }
    [Fact] public void PersonalFoldersAreIgnoredByGitWithoutReplacingExistingIgnoreRules()
    {
        using var source=Seed();string existing=PathOf("source/.gitignore");File.WriteAllText(existing,"own existing rule\n");new SettingsStore(PathOf("source"));Assert.Equal("own existing rule\n",File.ReadAllText(existing));
        PersonalLibraryArchive.Export(PathOf("source"),PathOf("collection"));PersonalLibraryArchive.Restore(PathOf("collection"),PathOf("restored"));
        Assert.Equal("*",File.ReadAllText(PathOf("collection/.gitignore")).Trim());Assert.Equal("*",File.ReadAllText(PathOf("restored/.gitignore")).Trim());
    }
    [Fact] public void OldSoftwareFoldersCannotBecomePersonalDataLocations()
    {
        Directory.CreateDirectory(PathOf("old-software"));File.WriteAllText(PathOf("old-software/LumaLyrics.dll"),"own fixture");File.WriteAllText(PathOf("old-software/LumaLyrics.runtimeconfig.json"),"{}");
        Assert.Throws<InvalidDataException>(()=>PersonalLibraryArchive.EnsureOutsideSoftware(PathOf("old-software/personal")));Assert.False(Directory.Exists(PathOf("old-software/personal")));
    }
    [Fact] public void RestoredPathsSupportChineseSpacesAndSemicolons()
    {
        using var source=Seed();PersonalLibraryArchive.Export(PathOf("source"),PathOf("个人备份 ; folder"));PersonalLibraryArchive.Restore(PathOf("个人备份 ; folder"),PathOf("个人数据 ; restored"));
        using var restored=new LyricsRepository(PathOf("个人数据 ; restored/lyrics.db"));Assert.Equal(Timed,restored.Find(Manual)!.Raw);Assert.Equal(450,restored.Find(Manual)!.OffsetMs);
        string config=PathOf("location.json");DataDirectoryConfiguration.SetDefault(PathOf("个人数据 ; restored"),config);Assert.Equal(PathOf("个人数据 ; restored"),DataDirectoryConfiguration.Resolve(configurationFile:config));
    }
    [Fact] public void BrokenCoverMappingIsRejectedEvenWhenImageBytesAreIntact()
    {
        using var source=Seed();PersonalLibraryArchive.Export(PathOf("source"),PathOf("collection"));string file=PathOf("collection/"+PersonalLibraryManifest.FileName);
        var json=JsonNode.Parse(File.ReadAllText(file))!;json["images"]![0]!["imageKey"]=new string('0',64);File.WriteAllText(file,json.ToJsonString());
        Assert.Throws<InvalidDataException>(()=>PersonalLibraryArchive.Restore(PathOf("collection"),PathOf("restored")));Assert.False(Directory.Exists(PathOf("restored")));
    }
    [Fact] public void LegacyManualTextExportsAsTxtWithoutInventingTimestamps()
    {
        using var source=Seed();var legacy=Manual with{Title="Own legacy manual text"};source.Save(legacy,"旧版手动保留的自写文字。","Own legacy fixture",true);
        var manifest=PersonalLibraryArchive.Export(PathOf("source"),PathOf("collection"));var track=manifest.Tracks.Single(t=>t.Track==legacy);
        Assert.EndsWith(".txt",track.LyricPath);Assert.Equal("旧版手动保留的自写文字。",File.ReadAllText(PathOf("collection/"+track.LyricPath)));
    }
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        string full=Path.GetFullPath(root);if(!full.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException();
        if(Directory.Exists(full))Directory.Delete(full,true);
    }
}
