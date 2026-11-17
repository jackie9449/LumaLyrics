using System.Text.Json;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
using Microsoft.Data.Sqlite;
namespace LumaLyrics.Infrastructure.Tests;

public sealed class TranslationAlbumTests:IDisposable
{
    readonly string root=Path.Combine(Path.GetTempPath(),"Luma-album-translation-"+Guid.NewGuid().ToString("N"));
    const string raw="[00:01]I keep the lantern\n[00:04]You lead us home";
    static Track Song(string title,string artist="Own cast",string album="Own story")=>new(title,artist,album,10000);
    void Seed(params Track[] tracks)
    {
        Directory.CreateDirectory(root);using var lyrics=new LyricsRepository(Path.Combine(root,"lyrics.db"));
        lyrics.SyncLibrary(new("own",tracks.Length,tracks.Select((t,i)=>new LibraryTrack(i.ToString(),t)).ToArray(),"own"));
        foreach(var track in tracks)lyrics.Save(track,raw,"Own fixture",true);
    }
    public void Dispose(){SqliteConnection.ClearAllPools();if(Directory.Exists(root))Directory.Delete(root,true);}
    [Fact] public void SingleSelectionCarriesAlbumOriginalsAndAlignedTranslationsWithoutCreatingOtherJobs()
    {
        var a=Song("First");var b=Song("Second");Seed(a,b);
        using(var translations=new TranslationRepository(root))translations.Save(b,new(TranslationAlignment.Hash(raw),[new(0,"我守着灯火"),new(1,"你引我们归家")],"Own personal","manual","Own fixture",DateTimeOffset.UtcNow),true);
        var job=TranslationJobExchange.Create(root,a,TranslationJobMode.FillMissing);
        var album=Assert.IsType<TranslationAlbumContext>(job.Request.AlbumContext);
        Assert.Equal(2,album.Songs.Count);Assert.All(album.Songs,s=>Assert.Equal(2,s.Groups.Count));
        var reference=album.Songs.Single(s=>s.Track==b);Assert.Equal("我守着灯火",reference.ExistingTranslation![0].Text);Assert.True(reference.Manual);
        Assert.Equal(TranslationAlignment.Hash(raw),reference.SourceHash);Assert.Single(TranslationJobExchange.ReadJobs(root));
        string markdown=File.ReadAllText(job.TaskPath);Assert.Contains("专辑统筹",markdown);Assert.Contains("信达雅",markdown);Assert.Contains("Second",markdown);
        Assert.Equal(2,job.Request.Target.Groups.Count);Assert.Equal(a,job.Request.Target.Track);
    }
    [Fact] public void SameTitleByOtherArtistsAndDifferentEditionsNeverEnterReferences()
    {
        var a=Song("First");Seed(a,Song("Other cast",artist:"Other artist"),Song("Live",album:"Own story (Live)"));
        var job=TranslationJobExchange.Create(root,a,TranslationJobMode.FillMissing);Assert.Equal(a,Assert.Single(job.Request.AlbumContext!.Songs).Track);
    }
    [Fact] public void MissingLyricsAreExplicitAndWrongHashTranslationIsNotSupplied()
    {
        var a=Song("First");var b=Song("Second");var missing=Song("Missing");Seed(a,b);
        using(var lyrics=new LyricsRepository(Path.Combine(root,"lyrics.db")))lyrics.SyncLibrary(new("own",3,[new("0",a),new("1",b),new("2",missing)],"own"));
        using(var translations=new TranslationRepository(root))translations.Save(b,new(new string('a',64),[new(0,"不可用的旧稿")],"Own draft","local:agent","Own fixture",DateTimeOffset.UtcNow,true));
        var songs=TranslationJobExchange.Create(root,a,TranslationJobMode.FillMissing).Request.AlbumContext!.Songs;
        Assert.Null(songs.Single(s=>s.Track==b).ExistingTranslation);
        var unavailable=songs.Single(s=>s.Track==missing);Assert.NotNull(unavailable.UnavailableReason);Assert.Empty(unavailable.Groups);Assert.Null(unavailable.SourceHash);
    }
    [Fact] public void OneAlbumSpansChunksWithTheSameContextSnapshot()
    {
        var tracks=Enumerable.Range(0,7).Select(i=>Song("Own scene "+i)).ToArray();Seed(tracks);
        var batch=TranslationJobExchange.CreateMissingBatch(root,tracks);
        Assert.Equal(7,batch.Tickets.Count);Assert.Equal(2,batch.Batch!.ChunkPaths.Count);
        var album=batch.Tickets[0].Request.AlbumContext;Assert.All(batch.Tickets,j=>Assert.Same(album,j.Request.AlbumContext));
        Assert.Equal(7,album!.Songs.Count);Assert.Contains("AlbumDecisions",File.ReadAllText(batch.Batch.EntryPath));
    }
    [Fact] public void UpdatedAlbumReferenceMakesNewExportInsteadOfReusingOldSnapshot()
    {
        var a=Song("First");var b=Song("Second");Seed(a,b);var old=TranslationJobExchange.Create(root,a,TranslationJobMode.FillMissing);
        using(var translations=new TranslationRepository(root))translations.Save(b,new(TranslationAlignment.Hash(raw),[new(0,"新的统一译名")],"Own reference","manual","Own fixture",DateTimeOffset.UtcNow),true);
        var updated=TranslationJobExchange.Create(root,a,TranslationJobMode.FillMissing);Assert.NotEqual(old.Request.JobId,updated.Request.JobId);
        Assert.Null(old.Request.AlbumContext!.Songs.Single(s=>s.Track==b).ExistingTranslation);
        Assert.Equal("新的统一译名",updated.Request.AlbumContext!.Songs.Single(s=>s.Track==b).ExistingTranslation![0].Text);
        Assert.Equal(updated.Request.JobId,TranslationJobExchange.Create(root,a,TranslationJobMode.FillMissing).Request.JobId);
    }
    [Fact] public void LegacyRequestWithoutAlbumContextStillDeserializes()
    {
        var a=Song("First");Seed(a);var job=TranslationJobExchange.Create(root,a,TranslationJobMode.FillMissing);
        var json=JsonSerializer.SerializeToNode(job.Request)!;json.AsObject().Remove("AlbumContext");
        var legacy=json.Deserialize<TranslationJobRequest>();Assert.NotNull(legacy);Assert.Null(legacy.AlbumContext);Assert.Equal(job.Request.JobId,legacy.JobId);
    }
}
