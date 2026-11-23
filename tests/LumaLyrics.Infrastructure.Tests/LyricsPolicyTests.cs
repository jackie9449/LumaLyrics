using LumaLyrics.Core;
using Microsoft.Data.Sqlite;
namespace LumaLyrics.Infrastructure.Tests;
public sealed class LyricsPolicyTests:IDisposable
{
    readonly string root=Path.Combine(Path.GetTempPath(),"Luma-policy-"+Guid.NewGuid().ToString("N"));
    string Db=>Path.Combine(root,"lyrics.db");
    static readonly Track Song=new("Own tail fixture","Own author","Own album",479000);
    public LyricsPolicyTests()=>Directory.CreateDirectory(root);
    void Sql(string sql){using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Db,Pooling=false}.ToString());db.Open();using var c=db.CreateCommand();c.CommandText=sql;c.ExecuteNonQuery();}
    void Seed(){using var repo=new LyricsRepository(Db);repo.SyncLibrary(new("own",2,[new("one",Song),new("two",Song with{Title="Own manual"})],"own"));repo.Save(Song with{Title="Own manual"},"Own text","manual",true,plain:true);}
    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void UpgradeInvalidatesOldFailuresOnceAndPreservesManualData(int previousPolicy)
    {
        Seed();
        using(var writer=new LyricsRepository(Db))writer.RememberFailure(Song);
        if(previousPolicy==0)Sql("DELETE FROM LyricsPolicyState; DELETE FROM LookupFailurePolicy;");
        else Sql($"UPDATE LyricsPolicyState SET PolicyVersion={previousPolicy}; UPDATE LookupFailurePolicy SET PolicyVersion={previousPolicy};");
        Sql("UPDATE LibraryTracks SET State='Incomplete',Detail='old heuristic',RetryUtc='2099' WHERE PersistentId='one';");
        using(var read=new LyricsRepository(Db,true))Assert.False(read.HasFailure(Song));
        using(var repo=new LyricsRepository(Db)){Assert.Equal(1,repo.RequeuedForPolicy);Assert.False(repo.HasFailure(Song));Assert.True(repo.FindReadOnly(Song with{Title="Own manual"})!.Manual);repo.RememberFailure(Song);Assert.True(repo.HasFailure(Song));}
        using(var reopened=new LyricsRepository(Db)){Assert.Equal(0,reopened.RequeuedForPolicy);Assert.True(reopened.HasFailure(Song));}
    }
    [Fact]public void AnOldWriterCannotReuseANewPolicyStampWithADifferentExpiry()
    {
        Seed();using(var repo=new LyricsRepository(Db))repo.RememberFailure(Song);
        Sql("UPDATE LookupFailures SET ExpiresUtc='2099-01-01T00:00:00+00:00'");using var check=new LyricsRepository(Db,true);Assert.False(check.HasFailure(Song));
    }
    [Fact]public void StaleNativeMembershipRejectsLateAutomaticSave()
    {
        Seed();var expected=new LibraryQueueItem("own","one",Song);using var repo=new LyricsRepository(Db);repo.SyncLibrary(new("other",0,[],"own"));
        Assert.Throws<InvalidDataException>(()=>repo.Save(Song,"[00:01]Own line","Own",false,expectedLibrary:expected));Assert.Null(repo.FindReadOnly(Song));
    }
    [Fact]public void SameFingerprintAndNativeIdAreInsufficientAfterDurationChanges()
    {
        Seed();
        using var repo=new LyricsRepository(Db);
        var member=new LibraryQueueItem("own","one",Song);
        var changed=Song with{DurationMs=Song.DurationMs+1000};
        Assert.True(repo.IsCurrentLibraryMember(member));
        repo.SyncLibrary(new("own",1,[new("one",changed)],"own"));
        Assert.False(repo.IsCurrentLibraryMember(member));
        Assert.Throws<InvalidDataException>(()=>repo.Save(Song,"[00:01]Own line","Own",false,expectedLibrary:member));
        repo.RecordLookupFailure(Song,"stale failure",false,true,suppress:true,expectedLibrary:member);
        Assert.False(repo.HasFailure(changed));
    }
    [Fact]public void FailurePersistenceDoesNotDowngradeManualBindingOrOffset()
    {
        Seed();using var repo=new LyricsRepository(Db);var manual=repo.Save(Song,"Own manual body","manual",true,plain:true);repo.SetOffset(manual.Id,480);
        repo.RecordLookupFailure(Song,"时间戳超出歌曲时长",false,false,true);
        Assert.False(repo.HasFailure(Song));var kept=repo.FindReadOnly(Song)!;Assert.Equal(manual.Raw,kept.Raw);Assert.Equal(480,kept.OffsetMs);Assert.True(kept.Manual);
    }
    [Fact]public async Task WorkspaceCachesReasonsByRevisionAndClearsThemAfterSuccess()
    {
        Seed();using(var repo=new LyricsRepository(Db))repo.RecordLookupFailure(Song,"尾段较长且来源缺少全文对照，需手动确认",false,false,true);
        var catalog=new LibraryWorkspaceCatalog(root);var first=await catalog.ReadAsync("own",1,default);Assert.Contains("全文",first.Songs.Single(s=>s.Track==Song).Status.Reason);
        using(var repo=new LyricsRepository(Db))repo.Save(Song,"[00:01]We sail home\n[01:30]We follow the stars\n[04:30]We reach the distant shore","manual",true);
        var second=await catalog.ReadAsync("own",1,default);Assert.Null(second.Songs.Single(s=>s.Track==Song).Status.Reason);
    }
    [Fact]public void ReassessmentPreservesAnyExistingOriginalAndOffset()
    {
        Seed();using var repo=new LyricsRepository(Db);var existing=repo.Save(Song,"[00:01]Own earlier text","Own",false);repo.SetOffset(existing.Id,-250);
        var kept=repo.Save(Song,"[00:02]New proposed text","Own",false,expectedLibrary:new("own","one",Song),onlyIfMissing:true);
        Assert.Equal(existing.Id,kept.Id);Assert.Equal(existing.Raw,kept.Raw);Assert.Equal(-250,kept.OffsetMs);
    }
    [Fact]public void LibraryManualSaveRequiresExpectedBindingAndCurrentMember()
    {
        Seed();
        using var repo=new LyricsRepository(Db);
        var member=new LibraryQueueItem("own","one",Song);
        var first=repo.Save(Song,"[00:01]Own first manual line","Own",true,expectedLibrary:member);
        repo.SetOffset(first.Id,350);
        Assert.Throws<InvalidDataException>(()=>repo.Save(Song,"[00:02]Outdated choice","Own",true,expectedLibrary:member));
        Assert.Equal(350,repo.FindReadOnly(Song)!.OffsetMs);
        var replacement=repo.Save(Song,"[00:02]Own approved replacement","Own",true,expectedLibrary:member,expectedBindingId:first.Id);
        Assert.True(replacement.Manual);
        Assert.Equal(replacement.Id,repo.Save(Song,"[00:03]Automatic choice","Own",false).Id);
        repo.SyncLibrary(new("another library",0,[],"own"));
        Assert.Throws<InvalidDataException>(()=>repo.Save(Song,"[00:03]Stale choice","Own",true,expectedLibrary:member,expectedBindingId:replacement.Id));
        Assert.Equal(replacement.Id,repo.FindReadOnly(Song)!.Id);
    }
    [Fact]public async Task ProviderOutageIsVisibleWithoutNegativeSuppression()
    {
        Seed();using(var repo=new LyricsRepository(Db))
        {
            repo.RememberFailure(Song);repo.RecordLookupFailure(Song,"来源暂不可用，尚未完成查询",true,true,true,expectedLibrary:new("own","one",Song));Assert.False(repo.HasFailure(Song));
        }
        var rows=await new LibraryWorkspaceCatalog(root).ReadAsync("own",1,default);
        Assert.Equal("来源暂不可用，尚未完成查询",rows.Songs.Single(s=>s.Track==Song).Status.Reason);
    }
    public void Dispose(){SqliteConnection.ClearAllPools();if(Directory.Exists(root))Directory.Delete(root,true);}
}
