using LumaLyrics.Core;
using Microsoft.Data.Sqlite;
namespace LumaLyrics.Infrastructure.Tests;
public sealed class LocalAgentPromptTests:IDisposable
{
    readonly string root=Path.Combine(Path.GetTempPath(),"Luma prompt 中文 "+Guid.NewGuid().ToString("N"));
    readonly Track a=new("Own first","Own artist","Own album",10000),b=new("Own second","Own artist","Own album",10000);
    public LocalAgentPromptTests()
    {
        Directory.CreateDirectory(root);using var lyrics=new LyricsRepository(Path.Combine(root,"lyrics.db"));lyrics.SyncLibrary(new("own",2,[new("1",a),new("2",b)],"own"));
        foreach(var t in new[]{a,b})lyrics.Save(t,"[00:01]We sail home\n[00:04]Across the bay\n[00:08]Under the stars","Own",true);
    }
    [Fact] public void PromptTargetsOnlySelectedBatchAndDoesNotCreateJobs()
    {
        var first=TranslationJobExchange.CreateMissingBatch(root,[a]);var second=TranslationJobExchange.CreateMissingBatch(root,[b]);int count=TranslationJobExchange.ReadJobs(root).Count;
        string text=LocalAgentTranslationPrompt.Create(root,first.Batch!.BatchId);
        Assert.Contains(first.Batch.EntryPath,text);Assert.DoesNotContain(second.Batch!.EntryPath,text);Assert.Contains("translation-result.json",text);Assert.Contains("GroupIndex",text);Assert.Contains("已提交，待接收",text);Assert.Equal(count,TranslationJobExchange.ReadJobs(root).Count);
    }
    [Fact] public void MissingOrChangedChunksCannotProduceHandoffPrompt()
    {
        var batch=TranslationJobExchange.CreateMissingBatch(root,[a]);File.AppendAllText(batch.Batch!.ChunkPaths[0],"changed");
        Assert.Throws<InvalidDataException>(()=>LocalAgentTranslationPrompt.Create(root,batch.Batch.BatchId));
    }
    [Fact] public void MissingMarkdownAndUnknownIdsAreRejected()
    {
        var batch=TranslationJobExchange.CreateMissingBatch(root,[a]);File.Delete(batch.Batch!.EntryPath);
        Assert.Throws<InvalidDataException>(()=>LocalAgentTranslationPrompt.Create(root,batch.Batch.BatchId));Assert.Throws<InvalidDataException>(()=>LocalAgentTranslationPrompt.Create(root,"unknown"));
    }
    [Fact] public void SingleRegisteredTaskHasAnExactHandoff()
    {
        var job=TranslationJobExchange.Create(root,a,TranslationJobMode.FillMissing);Assert.Contains(job.TaskPath,LocalAgentTranslationPrompt.Create(root,"job:"+job.Request.JobId));
    }
    [Fact] public void CancelledJobsAreExplicitlyExcludedFromSubmission()
    {
        var batch=TranslationJobExchange.CreateMissingBatch(root,[a]);var job=batch.Tickets.Single();TranslationJobExchange.Cancel(root,job.Request.JobId);
        string text=LocalAgentTranslationPrompt.Create(root,batch.Batch!.BatchId);Assert.Contains(job.Request.JobId+" (Cancelled)",text);Assert.Contains("不能靠修改旧结果重新接收",text);
    }
    public void Dispose(){SqliteConnection.ClearAllPools();Directory.Delete(root,true);}
}
