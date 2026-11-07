using LumaLyrics.Core;
using Microsoft.Data.Sqlite;
namespace LumaLyrics.Infrastructure.Tests;
public sealed class LibraryWorkspaceTests:IDisposable
{
    readonly string root=Path.Combine(Path.GetTempPath(),"Luma-workspace-"+Guid.NewGuid().ToString("N"));
    static readonly Track Song=new("Own song","Own artist","Own album",10000);
    const string Raw="[00:01]We sail home\n[00:04]Across the bay\n[00:08]Under the stars";
    public LibraryWorkspaceTests(){Directory.CreateDirectory(root);using var lyrics=new LyricsRepository(Path.Combine(root,"lyrics.db"));lyrics.SyncLibrary(new("own",2,[new("a",Song),new("b",Song with{Title="Own B"})],"own"));lyrics.Save(Song,Raw,"Own source",true);lyrics.Save(Song with{Title="Own B"},Raw,"Own source",true);using var t=new TranslationRepository(root);}
    [Theory] [InlineData(0,LibraryChineseState.Missing)] [InlineData(1,LibraryChineseState.Partial)] [InlineData(3,LibraryChineseState.Complete)]
    public void CountsOnlyRequiredGroups(int count,LibraryChineseState expected)
    {
        var doc=new LrcParser().Parse(Raw);var t=count==0?null:new ChineseTranslation(TranslationAlignment.Hash(Raw),Enumerable.Range(0,count).Select(i=>new TranslationLine(i,"我们归家")).ToArray(),"own","manual","own",DateTimeOffset.UtcNow);
        var s=LibraryWorkspaceProjection.Assess(new(doc,true,true,t,true,false,"own",LibraryCoverState.Ready));Assert.Equal(expected,s.Chinese);Assert.Equal(count,s.TranslatedGroups);Assert.Equal(3,s.RequiredGroups);
    }
    [Fact] public void ChangedHashAndManualPlainCannotLookComplete()
    {
        var t=new ChineseTranslation(TranslationAlignment.Hash(Raw+"changed"),[new(0,"家")],"own","manual","own",DateTimeOffset.UtcNow);
        Assert.Equal(LibraryChineseState.Outdated,LibraryWorkspaceProjection.Assess(new(new LrcParser().Parse(Raw),true,true,t,true,true,"r",LibraryCoverState.Missing)).Chinese);
        Assert.Equal(LibraryChineseState.BlockedOriginal,LibraryWorkspaceProjection.Assess(new(new LrcParser().Parse(Raw,true),true,true,t,true,true,"r",LibraryCoverState.Missing)).Chinese);
    }
    [Fact] public async Task CatalogPreservesNativeDuplicatesAndExcludesOrphans()
    {
        using(var l=new LyricsRepository(Path.Combine(root,"lyrics.db"))){l.SyncLibrary(new("own",3,[new("a",Song),new("b",Song),new("c",Song with{Title="Own C"})],"own"));l.Save(Song with{Title="Orphan"},Raw,"own",true);}
        var snapshot=await new LibraryWorkspaceCatalog(root).ReadAsync("own",2,CancellationToken.None);Assert.Equal(3,snapshot.Songs.Count);Assert.Equal(2,snapshot.Songs.Count(s=>s.Track==Song));Assert.DoesNotContain(snapshot.Songs,s=>s.Track.Title=="Orphan");
    }
    [Fact] public async Task ExactTargetDoesNotUseCurrentlyPlayingSongAndRejectsStaleRevision()
    {
        var snapshot=await new LibraryWorkspaceCatalog(root).ReadAsync("own",1,CancellationToken.None);var b=snapshot.Songs.Single(s=>s.Key.PersistentId=="b");
        var job=TranslationJobExchange.Create(root,b.Track,TranslationJobMode.FillMissing,expected:b.Target);Assert.Equal("b",job.Request.Target.PersistentId);
        LibrarySongActions.Import(root,b.Target,"[00:01]我们归家\n[00:04]穿过海湾\n[00:08]星光之下");
        Assert.Throws<InvalidDataException>(()=>LibrarySongActions.Check(root,b.Target));using var repo=new TranslationRepository(root,true);Assert.Null(repo.Find(Song,new LrcParser().Parse(Raw)));Assert.NotNull(repo.Find(b.Track,new LrcParser().Parse(Raw)));
    }
    [Fact] public async Task TaskCatalogShowsAllBatchesAndRejectsMissingChunks()
    {
        var first=TranslationJobExchange.CreateMissingBatch(root,[Song]);var second=TranslationJobExchange.CreateMissingBatch(root,[Song with{Title="Own B"}]);Assert.Equal(2,LibraryTaskCatalog.Read(root).Count);
        File.Delete(first.Batch!.ChunkPaths[0]);var broken=LibraryTaskCatalog.Read(root).Single(b=>b.Id==first.Batch.BatchId);Assert.Null(broken.EntryPath);Assert.Empty(broken.Jobs);Assert.Contains("不可用",broken.Label);await Task.CompletedTask;
    }
    [Fact] public void NativeIdentityKeepsPersistentIdCase()
    {
        var keys=new HashSet<LibrarySongKey>(LibrarySongKey.Comparer){new("SOURCE","Ab")};
        Assert.Contains(new("source","Ab"),keys);Assert.DoesNotContain(new("SOURCE","ab"),keys);
    }
    [Fact] public void CoverageReportAndWorkspaceUseTheSameRequiredGroups()
    {
        var snapshot=new LibrarySnapshot("own",1,[new("a",Song)],"own");
        var report=LibraryCoverageAudit.Create(root,snapshot);
        var status=LibraryWorkspaceProjection.Assess(new(new LrcParser().Parse(Raw),true,true,null,false,false,null,LibraryCoverState.Missing));
        Assert.Equal(status.RequiredGroups,report.Tracks.Single().RequiredGroups);Assert.Equal(status.TranslatedGroups,report.Tracks.Single().TranslatedGroups);
    }
    [Fact] public async Task AssessmentReuseTracksOriginalAndTranslationChanges()
    {
        LibraryManagedSong A(LibraryWorkspaceSnapshot snapshot)=>snapshot.Songs.Single(s=>s.Key.PersistentId=="a");
        LibraryManagedSong B(LibraryWorkspaceSnapshot snapshot)=>snapshot.Songs.Single(s=>s.Key.PersistentId=="b");
        var catalog=new LibraryWorkspaceCatalog(root);var first=await catalog.ReadAsync("own",1,default);var second=await catalog.ReadAsync("own",1,default);
        Assert.Same(A(first),A(second));
        using(var translations=new TranslationRepository(root))translations.Save(Song,new(TranslationAlignment.Hash(Raw),[new(0,"我们归家")],"own","manual","own",DateTimeOffset.UtcNow),true);
        var third=await catalog.ReadAsync("own",1,default);Assert.NotSame(A(second),A(third));Assert.Equal(LibraryChineseState.Partial,A(third).Status.Chinese);Assert.Same(B(second),B(third));
        using(var lyrics=new LyricsRepository(Path.Combine(root,"lyrics.db")))lyrics.Save(Song,Raw+"\n[00:09]New words","own",true);
        var fourth=await catalog.ReadAsync("own",1,default);Assert.Equal(LibraryChineseState.Outdated,A(fourth).Status.Chinese);Assert.NotEqual(A(third).Status.SourceHash,A(fourth).Status.SourceHash);
    }
    public void Dispose(){SqliteConnection.ClearAllPools();Directory.Delete(root,true);}
}
