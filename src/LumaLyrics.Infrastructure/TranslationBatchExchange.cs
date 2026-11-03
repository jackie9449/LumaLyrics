using System.Text.Json;
using LumaLyrics.Core;
namespace LumaLyrics.Infrastructure;

public static class TranslationBatchExchange
{
    public static string? LatestEntry(string root)
    {
        using var db=TranslationJobRepository.Open(root,true);
        var text=TranslationJobRepository.Scalar(db,"SELECT Manifest FROM TranslationBatches ORDER BY rowid DESC LIMIT 1");if(text is null)return null;
        var manifest=JsonSerializer.Deserialize<TranslationBatchManifest>(text,TranslationJobFiles.Json)!;
        if(!Path.GetFullPath(root).Equals(manifest.ProfileRoot,StringComparison.OrdinalIgnoreCase))return null;
        var path=PersonalLibraryArchive.AssetPath(root,"translation-batches/"+manifest.BatchId+"/batch-task.md");return File.Exists(path)?path:null;
    }
    public static TranslationJobBatchResult Create(string root,IReadOnlyList<TranslationBatchSelection> selections,int chunkSize=5,CancellationToken token=default)
    {
        if(chunkSize is <1 or >10)throw new ArgumentOutOfRangeException(nameof(chunkSize));
        root=Path.GetFullPath(root);using var lyrics=new LyricsRepository(Path.Combine(root,"lyrics.db"),true);
        var members=lyrics.GetLibraryQueue(true);var tickets=new List<TranslationJobTicket>();var skipped=new List<TranslationJobSkip>();
        foreach(var selection in selections.DistinctBy(s=>s.Target is {} t?t.Key.Source.ToUpperInvariant()+"\0"+t.Key.PersistentId:s.Track.Fingerprint+"|"+s.Track.DurationMs))
        {
            token.ThrowIfCancellationRequested();
            if(!members.Any(m=>m.Track==selection.Track)){skipped.Add(new(selection.Track,"NotMember","不是已确认的资料库成员。"));continue;}
            try{tickets.Add(TranslationJobExchange.Create(root,selection.Track,selection.Mode,selection.UserInstructions,selection.IncludeExistingTranslation,selection.Target));}
            catch(InvalidDataException e){skipped.Add(new(selection.Track,"NotEligible",e.Message));}
        }
        if(tickets.Count==0)return new(null,tickets,skipped);
        string id=Guid.NewGuid().ToString("N"),folder=PersonalLibraryArchive.AssetPath(root,"translation-batches/"+id);Directory.CreateDirectory(Path.Combine(folder,"chunks"));
        var refs=new List<TranslationBatchChunkReference>();int index=0;
        foreach(var group in tickets.Chunk(chunkSize))
        {
            token.ThrowIfCancellationRequested();
            string path=Path.Combine(folder,"chunks",$"{index+1:D6}.json");
            var chunk=new TranslationBatchChunk("LumaLyrics.TranslationBatchChunk",1,id,index,group.Select(t=>new TranslationBatchItem(t.Request.JobId,t.RequestSha256,t.TaskPath,t.ResultPath,Path.Combine(Path.GetDirectoryName(t.ResultPath)!,"receipt.json"))).ToArray());
            string json=JsonSerializer.Serialize(chunk,TranslationJobFiles.Json);TranslationJobFiles.Write(path,json);refs.Add(new(index++,path,TranslationAlignment.Hash(json),group.Length));
        }
        var manifest=new TranslationBatchManifest("LumaLyrics.TranslationBatch",1,id,root,DateTimeOffset.UtcNow,tickets.Count,chunkSize,refs);
        var batch=new TranslationBatchTicket(id,Path.Combine(folder,"batch-task.md"),Path.Combine(folder,"batch.json"),Path.Combine(folder,"agent-progress.json"),refs.Select(r=>r.Path).ToArray());
        string text=JsonSerializer.Serialize(manifest,TranslationJobFiles.Json);TranslationJobFiles.Write(batch.ManifestPath,text);
        using(var db=TranslationJobRepository.Open(root))TranslationJobRepository.Execute(db,"INSERT INTO TranslationBatches VALUES($id,$m)",("$id",id),("$m",text));
        TranslationJobFiles.Write(batch.EntryPath,TranslationBatchMarkdown.Render(batch,manifest));return new(batch,tickets,skipped);
    }
}
public static class TranslationBatchMarkdown
{
    public static string Render(TranslationBatchTicket ticket,TranslationBatchManifest manifest)=>TranslationJobMarkdown.Template("LocalAgentTranslationBatch.zh-CN.md")+$"\n\nBatchId：`{ticket.BatchId}`\n\n共 {manifest.Total} 首；每块 {manifest.ChunkSize} 首。\n\n不可变索引：`{ticket.ManifestPath}`\n\n你的进度文件：`{ticket.ProgressPath}`\n";
}
