using LumaLyrics.Core;
using Microsoft.Data.Sqlite;
namespace LumaLyrics.Infrastructure.Tests;

public sealed class LyricsReviewStoreTests:IDisposable
{
    readonly string root=Path.Combine(Path.GetTempPath(),"Luma-own-review-"+Guid.NewGuid().ToString("N"));
    static readonly Track Track=new("Original fixture","Original artist","Original album",10000);
    static readonly LyricsDocument Original=new LrcParser().Parse("[00:01]We sail home\n[00:04]Across the bay\n[00:08]Under the stars");
    static LyricsOcrObservation Observation=>new(Track,TranslationAlignment.Hash(Original.Raw),DateTimeOffset.UtcNow,1000,"native-accessibility",new(0,0,100,100),["We sail home","Across the bay"]);
    public LyricsReviewStoreTests()=>Directory.CreateDirectory(root);
    [Fact] public void RefusesStaleOriginalAndMissingCaptureImage()
    {
        var store=new LyricsReviewStore(root);
        Assert.Throws<InvalidDataException>(()=>store.Save(Track,Original,Observation with{SourceHash=new string('f',64)},"native-text"));
        Assert.Throws<InvalidDataException>(()=>store.Save(Track,Original,Observation,"windows-ocr"));
        Assert.False(Directory.Exists(store.DirectoryPath));
    }
    [Fact] public void PersonalExportAndRestoreRetainReviewWithoutAlteringBindings()
    {
        string profile=Path.Combine(root,"profile");Directory.CreateDirectory(profile);
        using(var lyrics=new LyricsRepository(Path.Combine(profile,"lyrics.db")))lyrics.Save(Track,Original.Raw,"own",true);
        string record=new LyricsReviewStore(profile).Save(Track,Original,Observation,"native-text");
        string exported=Path.Combine(root,"export"),restored=Path.Combine(root,"restored");var manifest=PersonalLibraryArchive.Export(profile,exported);
        Assert.Single(manifest.Files,f=>f.Kind=="review");PersonalLibraryArchive.Restore(exported,restored);
        Assert.Equal(File.ReadAllText(record),File.ReadAllText(Path.Combine(restored,Path.GetRelativePath(profile,record))));
        using var read=new LyricsRepository(Path.Combine(restored,"lyrics.db"));var binding=read.Find(Track)!;Assert.True(binding.Manual);Assert.Equal(Original.Raw,binding.Raw);
    }
    public void Dispose(){SqliteConnection.ClearAllPools();Directory.Delete(root,true);}
}
