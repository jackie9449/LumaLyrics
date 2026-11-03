using System.Text.Json;
using LumaLyrics.Core;
using Microsoft.Data.Sqlite;
namespace LumaLyrics.Infrastructure.Tests;

public sealed class TranslationJobTests:IDisposable
{
    readonly string root=Path.Combine(Path.GetTempPath(),"Luma-agent-"+Guid.NewGuid().ToString("N"));
    static readonly Track Track=new("Own song","Own artist","Own album",10000);
    const string Raw="[00:01]We sail home\n[00:03]\n[00:04]Across the quiet bay\n[00:08]The stars guide us";
    static readonly JsonSerializerOptions Json=new(){WriteIndented=true};
    public TranslationJobTests(){Directory.CreateDirectory(root);using var lyrics=new LyricsRepository(Path.Combine(root,"lyrics.db"));lyrics.SyncLibrary(new("own",1,[new("1",Track)],"own"));lyrics.Save(Track,Raw,"Own source",true);}
    TranslationJobTicket Job(TranslationJobMode mode=TranslationJobMode.FillMissing)=>TranslationJobExchange.Create(root,Track,mode);
    string Complete(TranslationJobTicket job,bool partial=false)
    {
        var result=new TranslationJobResult("LumaLyrics.TranslationJobResult",1,job.Request.JobId,job.RequestSha256,job.Request.Target.SourceHash,"Own fixture",job.Request.Target.Groups.Select(g=>new TranslationJobResultLine(g.GroupIndex,g.Original,g.NeedsTranslation?(partial&&g.GroupIndex==2?"":"我们驶向家乡"):"",partial&&g.GroupIndex==2?"无法确认":null)).ToArray());
        File.WriteAllText(job.ResultPath,JsonSerializer.Serialize(result,Json));return job.ResultPath;
    }
    void Manual(string text="个人旧译文") {using var repo=new TranslationRepository(root);repo.Save(Track,new(TranslationAlignment.Hash(Raw),[new(0,text)],"Own manual","manual","Own review",DateTimeOffset.UtcNow),true);}
    [Fact] public async Task MissingImportIsIdempotentAndRetainsBlankGroups()
    {
        var job=Job();string file=Complete(job);var first=await TranslationJobExchange.ImportAsync(root,file);var second=await TranslationJobExchange.ImportAsync(root,file);
        Assert.Equal(TranslationJobState.Applied,first.State);Assert.Equal(first.AppliedRevision,second.AppliedRevision);
        using var repo=new TranslationRepository(root,true);var t=repo.Find(Track,new LrcParser().Parse(Raw))!;Assert.True(t.Draft);Assert.Equal("",t.TextAt(1));
    }
    [Fact] public async Task LateResultCannotOverwriteManualOrChangedOriginal()
    {
        var job=Job();string file=Complete(job);Manual();Assert.Equal(TranslationJobState.Conflict,(await TranslationJobExchange.ImportAsync(root,file)).State);
        var replace=Job(TranslationJobMode.ReplaceExpected);Complete(replace);using(var lyrics=new LyricsRepository(Path.Combine(root,"lyrics.db")))lyrics.Save(Track,Raw+"\n[00:09]New line","Own",true);
        Assert.Equal(TranslationJobState.Stale,(await TranslationJobExchange.ImportAsync(root,replace.ResultPath)).State);
    }
    [Fact] public async Task ExplicitReplacementRetainsManualHistoryAndCanRestore()
    {
        Manual();var job=Job(TranslationJobMode.ReplaceExpected);Complete(job);var receipt=await TranslationJobExchange.ImportAsync(root,job.ResultPath);Assert.Equal(TranslationJobState.Applied,receipt.State);
        string revision;using(var repo=new TranslationRepository(root,true)){var old=Assert.Single(repo.ReadHistory(Track,job.Request.Target.SourceHash));Assert.True(old.Record.Manual);revision=old.Revision;}
        Assert.True(await TranslationJobExchange.RestoreHistoryAsync(root,Track,job.Request.Target.SourceHash,revision,receipt.AppliedRevision!));
        using var current=new TranslationRepository(root,true);Assert.Equal("个人旧译文",current.Find(Track,new LrcParser().Parse(Raw))!.TextAt(0));
    }
    [Fact] public async Task PartialReplacementRequiresExplicitApprovalAndRejectsTampering()
    {
        Manual();var job=Job(TranslationJobMode.ReplaceExpected);Complete(job,true);Assert.Equal(TranslationJobState.PartialReady,(await TranslationJobExchange.ImportAsync(root,job.ResultPath)).State);
        Complete(job,false);await Assert.ThrowsAsync<InvalidDataException>(()=>TranslationJobExchange.ApprovePartialAsync(root,job.Request.JobId,job.Request.ExpectedRevision!));
        Complete(job,true);Assert.Equal(TranslationJobState.AppliedPartial,(await TranslationJobExchange.ApprovePartialAsync(root,job.Request.JobId,job.Request.ExpectedRevision!)).State);
    }
    [Fact] public async Task CancellationAndTwoConcurrentImportsCannotDoubleApply()
    {
        var job=Job();Complete(job);var receipts=await Task.WhenAll(TranslationJobExchange.ImportAsync(root,job.ResultPath),TranslationJobExchange.ImportAsync(root,job.ResultPath));Assert.Equal(receipts[0].AppliedRevision,receipts[1].AppliedRevision);
        var replace=Job(TranslationJobMode.ReplaceExpected);Complete(replace);TranslationJobExchange.Cancel(root,replace.Request.JobId);Assert.Equal(TranslationJobState.Cancelled,(await TranslationJobExchange.ImportAsync(root,replace.ResultPath)).State);
    }
    [Fact] public void BatchHasOneEntryAndTwentyChunksForHundredSongs()
    {
        var tracks=Enumerable.Range(0,100).Select(i=>Track with{Title=$"Own song {i}"}).ToArray();using(var lyrics=new LyricsRepository(Path.Combine(root,"lyrics.db"))){lyrics.SyncLibrary(new("own",100,tracks.Select((t,i)=>new LibraryTrack(i.ToString(),t)).ToArray(),"own"));foreach(var t in tracks)lyrics.Save(t,Raw,"Own",true);}
        var batch=TranslationJobExchange.CreateMissingBatch(root,tracks);Assert.Equal(100,batch.Tickets.Count);Assert.Equal(20,batch.Batch!.ChunkPaths.Count);Assert.Contains(batch.Batch.ManifestPath,File.ReadAllText(batch.Batch.EntryPath));Assert.Contains("继续",File.ReadAllText(batch.Batch.EntryPath));Assert.All(batch.Tickets,t=>Assert.Contains(t.ResultPath,File.ReadAllText(t.TaskPath)));
    }
    [Fact] public void StrictValidationRejectsChangedOriginalAndDuplicateProperties()
    {
        var job=Job();Complete(job);File.WriteAllText(job.ResultPath,File.ReadAllText(job.ResultPath).Replace("We sail home","Changed text"));Assert.Throws<InvalidDataException>(()=>TranslationJobExchange.Validate(root,job.ResultPath));
        Complete(job);File.WriteAllText(job.ResultPath,File.ReadAllText(job.ResultPath).Replace("\"SchemaVersion\": 1","\"SchemaVersion\": 1, \"SchemaVersion\": 1"));Assert.Throws<InvalidDataException>(()=>TranslationJobExchange.Validate(root,job.ResultPath));
    }
    [Fact] public async Task InboxFindsResultsCreatedWhileAppWasClosed()
    {
        var job=Job();Complete(job);var done=new TaskCompletionSource<TranslationJobReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);await using var inbox=new TranslationJobInbox(root);inbox.Received+=r=>done.TrySetResult(r);using var stop=new CancellationTokenSource();var running=inbox.RunAsync(stop.Token);Assert.Equal(TranslationJobState.Applied,(await done.Task.WaitAsync(TimeSpan.FromSeconds(10))).State);stop.Cancel();try{await running;}catch(OperationCanceledException){}
    }
    [Fact] public async Task BackupRestoresTranslationAndHistoryButSuspendsOldPaths()
    {
        Manual();var job=Job(TranslationJobMode.ReplaceExpected);Complete(job);await TranslationJobExchange.ImportAsync(root,job.ResultPath);
        var pending=Job(TranslationJobMode.ReplaceExpected);
        string backup=root+"-backup",restored=root+"-restore";
        try{PersonalLibraryArchive.Export(root,backup);Assert.True(File.Exists(Path.Combine(backup,"translation-state","TranslationJobs.json")));PersonalLibraryArchive.Restore(backup,restored);
            var jobs=TranslationJobExchange.ReadJobs(restored);Assert.Equal(TranslationJobState.Stale,jobs.Single(j=>j.Ticket.Request.JobId==pending.Request.JobId).Receipt.State);
            using var repo=new TranslationRepository(restored,true);Assert.True(repo.Find(Track,new LrcParser().Parse(Raw))!.Draft);Assert.True(Assert.Single(repo.ReadHistory(Track,TranslationAlignment.Hash(Raw))).Record.Manual);
        }finally{SqliteConnection.ClearAllPools();if(Directory.Exists(backup))Directory.Delete(backup,true);if(Directory.Exists(restored))Directory.Delete(restored,true);}
    }
    [Fact] public void MigrationPreservesVersionOneContentAndRevisionAcrossReopen()
    {
        var legacy=new ChineseTranslation(TranslationAlignment.Hash(Raw),[new(0,"原有个人译文")],"Own legacy","manual","Original review",DateTimeOffset.UtcNow,true);
        using(var db=new SqliteConnection($"Data Source={Path.Combine(root,"translations.db")}")){db.Open();using var c=db.CreateCommand();c.CommandText="CREATE TABLE Translations(Fingerprint TEXT,SourceHash TEXT,Duration REAL,Title TEXT,Artist TEXT,Album TEXT,Content TEXT NOT NULL,Manual INTEGER DEFAULT 0,PRIMARY KEY(Fingerprint,SourceHash)); PRAGMA user_version=1";c.ExecuteNonQuery();c.CommandText="INSERT INTO Translations VALUES($f,$h,10000,'Own song','Own artist','Own album',$c,1)";c.Parameters.AddWithValue("$f",Track.Fingerprint);c.Parameters.AddWithValue("$h",legacy.SourceHash);c.Parameters.AddWithValue("$c",JsonSerializer.Serialize(legacy));c.ExecuteNonQuery();}
        string revision;using(var repo=new TranslationRepository(root)){var active=repo.FindActive(Track,new LrcParser().Parse(Raw))!;revision=active.Revision;Assert.True(active.Record.Manual);Assert.True(active.Record.Translation.Draft);Assert.Equal(legacy.Review,active.Record.Translation.Review);Assert.Equal(legacy.Source,active.Record.Translation.Source);Assert.Equal("原有个人译文",active.Record.Translation.TextAt(0));Assert.Empty(repo.ReadHistory(Track,legacy.SourceHash));}
        using(var repo=new TranslationRepository(root)){Assert.Equal(revision,repo.FindActive(Track,new LrcParser().Parse(Raw))!.Revision);}
    }

    [Fact] public async Task OfflineWorkflowDoesNotCallProviderAndReadOnlyValidationDoesNotWrite()
    {
        var result=await new TranslationWorkflow(root,new NoNetwork()).PrepareAsync(Track,false,true,CancellationToken.None);Assert.Equal(TranslationLookupState.Offline,result.Lookup.State);var job=result.Ticket!;Complete(job);
        byte[] ReadOriginal(){using var stream=new FileStream(Path.Combine(root,"lyrics.db"),FileMode.Open,FileAccess.Read,FileShare.ReadWrite);using var memory=new MemoryStream();stream.CopyTo(memory);return memory.ToArray();}
        var before=ReadOriginal();TranslationJobExchange.Validate(root,job.ResultPath);Assert.Equal(before,ReadOriginal());
    }
    sealed class NoNetwork:ITranslationProvider {public Task<IReadOnlyList<TranslationCandidate>> LookupAsync(Track track,CancellationToken token)=>throw new InvalidOperationException("Network must not be used");}
    public void Dispose(){SqliteConnection.ClearAllPools();Directory.Delete(root,true);}
}
