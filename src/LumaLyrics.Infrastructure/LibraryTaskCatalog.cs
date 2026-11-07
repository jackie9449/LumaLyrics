using System.Text.Json;
namespace LumaLyrics.Infrastructure;
public sealed record LibraryTaskBatch(string Id,string Label,string? EntryPath,IReadOnlyList<TranslationJobSnapshot> Jobs);
public static class LibraryTaskCatalog
{
    public static IReadOnlyList<LibraryTaskBatch> Read(string root,CancellationToken token=default)
    {
        var jobs=TranslationJobExchange.ReadJobs(root,token).ToDictionary(j=>j.Ticket.Request.JobId);var rows=new List<LibraryTaskBatch>();var batched=new HashSet<string>();
        using var db=TranslationJobRepository.Open(root,true);using var c=TranslationJobRepository.Command(db,"SELECT Manifest FROM TranslationBatches ORDER BY rowid DESC");using var r=c.ExecuteReader();
        while(r.Read())
        {
            token.ThrowIfCancellationRequested();var manifest=JsonSerializer.Deserialize<TranslationBatchManifest>(r.GetString(0),TranslationJobFiles.Json)!;var members=new List<TranslationJobSnapshot>();string? entry=null;string? error=null;
            try
            {
                if(!Path.GetFullPath(root).Equals(manifest.ProfileRoot,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("历史个人目录，需要重新生成入口");
                var prefix=PersonalLibraryArchive.AssetPath(root,"translation-batches/"+manifest.BatchId);
                foreach(var chunk in manifest.Chunks)
                {
                    token.ThrowIfCancellationRequested();var relative=Path.GetRelativePath(root,chunk.Path).Replace('\\','/');var path=PersonalLibraryArchive.AssetPath(root,relative);if(!path.StartsWith(prefix+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("分块越界");
                    var data=TranslationJobFiles.Read<TranslationBatchChunk>(path,out var hash);if(hash!=chunk.Sha256||data.BatchId!=manifest.BatchId||data.ChunkIndex!=chunk.ChunkIndex)throw new InvalidDataException("分块身份或哈希不符");
                    foreach(var item in data.Items){if(!jobs.TryGetValue(item.JobId,out var job)||job.Ticket.RequestSha256!=item.RequestSha256||job.Ticket.TaskPath!=item.TaskPath||job.Ticket.ResultPath!=item.ResultPath)throw new InvalidDataException("成员不可核验");members.Add(job);batched.Add(item.JobId);}
                }
                var proposed=PersonalLibraryArchive.AssetPath(root,"translation-batches/"+manifest.BatchId+"/batch-task.md");if(File.Exists(proposed))entry=proposed;
            }
            catch(Exception e) when(e is IOException or InvalidDataException or JsonException){error=e.Message;members.Clear();}
            string label=$"批次 {manifest.CreatedUtc.LocalDateTime:g} · {manifest.Total} 首 · 已接收 {members.Count(j=>j.Receipt.State is TranslationJobState.Applied or TranslationJobState.AppliedPartial)}";
            if(error is not null)label+=" · 成员清单不可用："+error;
            else label+="\n"+string.Join("；",members.Where(j=>j.Receipt.State is TranslationJobState.Conflict or TranslationJobState.Stale or TranslationJobState.Rejected or TranslationJobState.PartialReady).Take(3).Select(j=>j.Ticket.Request.Target.Track.Title+"："+j.Receipt.Message));
            rows.Add(new(manifest.BatchId,label,entry,members));
        }
        foreach(var job in jobs.Values.Where(j=>!batched.Contains(j.Ticket.Request.JobId)))
            rows.Add(new("job:"+job.Ticket.Request.JobId,"单首／历史任务 · "+job.Ticket.Request.Target.Track.Title+" · "+job.Receipt.State+" · "+job.Receipt.Message,null,[job]));
        return rows;
    }
}
