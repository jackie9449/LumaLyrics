using System.Text.Json;
using LumaLyrics.Core;
using static LumaLyrics.Infrastructure.TranslationJobRepository;
namespace LumaLyrics.Infrastructure;

public static class TranslationJobExchange
{
    public static TranslationJobTicket Create(string root,Track track,TranslationJobMode mode,string? userInstructions=null,bool includeExistingTranslation=false)
    {
        root=Path.GetFullPath(root);if(!File.Exists(Path.Combine(root,"lyrics.db")))throw new DirectoryNotFoundException("个人资料不可用。");
        using var lyrics=new LyricsRepository(Path.Combine(root,"lyrics.db"),true);using var translations=new TranslationRepository(root);
        track=TrackMetadata.FromAppleSession(track);var binding=lyrics.FindReadOnly(track)??throw new InvalidDataException("请先取得可靠动态原词。");
        var doc=new LrcParser().Parse(binding.Raw,binding.Plain) with{Instrumental=binding.Instrumental};
        if(!doc.IsSynced||doc.Instrumental||!binding.Manual&&!LibraryPrefetcher.QualityOf(binding,track))throw new InvalidDataException("没有可确认的动态原词。");
        var groups=doc.Groups.Select((g,i)=>new TranslationJobGroup(i,g.Text,TranslationAlignment.NeedsChineseTranslation(doc,g.Text))).ToArray();
        if(!groups.Any(g=>g.NeedsTranslation)||TranslationAlignment.IsChinese(string.Join("\n",groups.Select(g=>g.Original))))throw new InvalidDataException("原词不需要中文翻译。");
        var active=translations.FindActive(track,doc);
        if(mode==TranslationJobMode.FillMissing&&active is not null)throw new InvalidDataException("已有译文，请明确选择重新制作。");
        if(mode==TranslationJobMode.ReplaceExpected&&active is null)throw new InvalidDataException("没有可替换的当前译文。");
        using var db=Open(root);
        foreach(var old in ReadJobs(root).Where(j=>j.Receipt.State==TranslationJobState.AwaitingResult))
            if(old.Ticket.Request.Target.BindingId==binding.Id&&old.Ticket.Request.Mode==mode&&old.Ticket.Request.ExpectedRevision==active?.Revision&&old.Ticket.Request.UserInstructions==userInstructions)return old.Ticket;
        var member=lyrics.GetLibraryQueue(true).FirstOrDefault(x=>x.Track.Fingerprint==track.Fingerprint);
        string id=Guid.NewGuid().ToString("N");
        var request=new TranslationJobRequest(1,id,mode,root,new(track,binding.Id,TranslationAlignment.Hash(doc.Raw),doc.Raw,member?.Source,member?.PersistentId,groups),active?.Revision,DateTimeOffset.UtcNow,binding.Source,userInstructions,includeExistingTranslation?active?.Record.Translation.Lines:null);
        string json=JsonSerializer.Serialize(request,TranslationJobFiles.Json),hash=TranslationAlignment.Hash(json);
        string task=TranslationJobFiles.PathFor(root,id,"task.md"),result=TranslationJobFiles.PathFor(root,id,"translation-result.json");
        Execute(db,"INSERT INTO TranslationJobs(Id,Request,RequestHash,State,Receipt) VALUES($id,$r,$h,'Exporting',NULL)",("$id",id),("$r",json),("$h",hash));
        Directory.CreateDirectory(Path.GetDirectoryName(task)!);
        TranslationJobFiles.Write(TranslationJobFiles.PathFor(root,id,"request.json"),json);
        TranslationJobFiles.Write(task,TranslationJobMarkdown.Render(request,hash,result+".tmp",result,null));
        Execute(db,"UPDATE TranslationJobs SET State='AwaitingResult' WHERE Id=$id",("$id",id));return new(request,hash,task,result);
    }
    public static TranslationJobBatchResult CreateMissingBatch(string root,IReadOnlyList<Track> selectedTracks)=>TranslationBatchExchange.Create(root,selectedTracks.Select(t=>new TranslationBatchSelection(t,TranslationJobMode.FillMissing)).ToArray());
    public static string Summary(string root)
    {
        using var db=Open(root,true);using var c=Command(db,"SELECT Id,State FROM TranslationJobs");using var r=c.ExecuteReader();int total=0,applied=0,pending=0,submitted=0,attention=0;
        while(r.Read()){total++;string state=r.GetString(1);if(state is "Applied" or "AppliedPartial")applied++;else if(state=="AwaitingResult"){if(File.Exists(TranslationJobFiles.PathFor(root,r.GetString(0),"translation-result.json")))submitted++;else pending++;}else if(state is "Conflict" or "Stale" or "Rejected" or "PartialReady")attention++;}
        return $"任务 {total} · 已接收 {applied} · 待制作 {pending} · 已提交待接收 {submitted} · 需处理 {attention}";
    }
    public static IReadOnlyList<TranslationJobSnapshot> ReadJobs(string root)
    {
        using var db=Open(root,true);using var c=Command(db,"SELECT Request,RequestHash,State,Receipt FROM TranslationJobs ORDER BY rowid DESC");using var r=c.ExecuteReader();var rows=new List<TranslationJobSnapshot>();
        while(r.Read())
        {
            var request=JsonSerializer.Deserialize<TranslationJobRequest>(r.GetString(0),TranslationJobFiles.Json)!;
            var state=Enum.Parse<TranslationJobState>(r.GetString(2));
            var receipt=r.IsDBNull(3)?new(request.JobId,state,state.ToString(),state.ToString(),0,request.Target.Groups.Count(g=>g.NeedsTranslation),null,request.CreatedUtc):JsonSerializer.Deserialize<TranslationJobReceipt>(r.GetString(3),TranslationJobFiles.Json)!;
            if(state is (TranslationJobState.AwaitingResult or TranslationJobState.PartialReady or TranslationJobState.Exporting) && !Path.GetFullPath(request.ProfileRoot).Equals(Path.GetFullPath(root),StringComparison.OrdinalIgnoreCase))receipt=receipt with{State=TranslationJobState.Stale,Code="ProfileRelocated",Message="个人目录已迁移，请重新生成任务。"};
            rows.Add(new(new(request,r.GetString(1),TranslationJobFiles.PathFor(root,request.JobId,"task.md"),TranslationJobFiles.PathFor(root,request.JobId,"translation-result.json")),receipt));
        }return rows;
    }
    public static TranslationJobValidation Validate(string root,string resultPath)
    {
        string id=Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(resultPath)))!;
        string expected=TranslationJobFiles.PathFor(root,id,Path.GetFileName(resultPath));
        if(!expected.Equals(Path.GetFullPath(resultPath),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("结果路径超出登记任务。");
        var result=TranslationJobFiles.Read<TranslationJobResult>(resultPath,out var resultHash);using var db=Open(root,true);var request=Request(db,id);
        if(!Path.GetFullPath(root).Equals(request.ProfileRoot,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("任务目录已迁移，请重新生成。");
        if(result.RequestSha256!=Scalar(db,"SELECT RequestHash FROM TranslationJobs WHERE Id=$id",("$id",id)))throw new InvalidDataException("任务请求哈希不一致。");
        var count=TranslationJobProtocol.ValidateShape(request,result);
        return new(request,result,resultHash,count.TranslatedCount,count.RequiredCount);
    }
    public static Task<TranslationJobReceipt> ImportAsync(string root,string path,CancellationToken token=default)=>Task.Run(()=>Apply(root,path,null,token),token);
    static TranslationJobReceipt Apply(string root,string path,string? partialRevision,CancellationToken token)
    {
        var validation=Validate(root,path);var request=validation.Request;
        if(Path.GetFileName(path)!="translation-result.json")throw new InvalidDataException("只接收已最终化的 translation-result.json。");
        using var db=Open(root);Execute(db,"ATTACH DATABASE $p AS originals",("$p",Path.Combine(root,"lyrics.db")));
        using var tx=db.BeginTransaction(deferred:false);token.ThrowIfCancellationRequested();
        string state=Scalar(db,"SELECT State FROM TranslationJobs WHERE Id=$id",("$id",request.JobId))!;
        var previous=Scalar(db,"SELECT Receipt FROM TranslationJobs WHERE Id=$id",("$id",request.JobId));
        if(state is not ("AwaitingResult" or "PartialReady"))return previous is null?new(request.JobId,Enum.Parse<TranslationJobState>(state),state,state,0,validation.RequiredCount,null,DateTimeOffset.UtcNow):JsonSerializer.Deserialize<TranslationJobReceipt>(previous,TranslationJobFiles.Json)!;
        string? raw=Scalar(db,"SELECT d.Raw FROM originals.TrackLyricsBindings b JOIN originals.LyricsDocuments d ON d.Id=b.DocumentId JOIN originals.Tracks t ON t.Id=b.TrackId WHERE b.Id=$id AND t.Fingerprint=$f AND d.Instrumental=0 AND d.Plain=0 AND ($duration IS NULL OR t.Duration IS NULL OR abs(t.Duration-$duration)<=5000)",("$id",request.Target.BindingId),("$f",request.Target.Track.Fingerprint),("$duration",request.Target.Track.DurationMs));
        if(partialRevision is not null && (state!="PartialReady" || Scalar(db,"SELECT ResultHash FROM TranslationJobs WHERE Id=$id",("$id",request.JobId))!=validation.ResultSha256))throw new InvalidDataException("部分结果已变化，请重新生成任务。");
        if(request.Target.LibrarySource is { } library && Scalar(db,"SELECT PersistentId FROM originals.LibraryMembership WHERE Source=$s AND PersistentId=$p",("$s",library),("$p",request.Target.PersistentId)) is null)raw=null;
        string? revision=Scalar(db,"SELECT Revision FROM Translations WHERE Fingerprint=$f AND SourceHash=$h",("$f",request.Target.Track.Fingerprint),("$h",request.Target.SourceHash));
        TranslationJobState next;string message;string? applied=null;
        if(raw is null||TranslationAlignment.Hash(raw)!=request.Target.SourceHash){next=TranslationJobState.Stale;message="原词或录音已变化，原译文保留。";}
        else if(request.Mode==TranslationJobMode.FillMissing?revision is not null:revision!=(partialRevision??request.ExpectedRevision)){next=TranslationJobState.Conflict;message="当前译文已变化，未覆盖。";}
        else if(request.Mode==TranslationJobMode.ReplaceExpected&&validation.TranslatedCount<validation.RequiredCount&&partialRevision is null){next=TranslationJobState.PartialReady;message="有未译行，原译文已保留；可明确采用部分稿。";}
        else
        {
            applied=Guid.NewGuid().ToString("N");var t=request.Target.Track;
            var translation=new ChineseTranslation(request.Target.SourceHash,validation.Result.Lines.Where(l=>request.Target.Groups[l.GroupIndex].NeedsTranslation&&!string.IsNullOrWhiteSpace(l.Chinese)).Select(l=>new TranslationLine(l.GroupIndex,l.Chinese!)).ToArray(),"本地 agent · "+validation.Result.DraftEngine,"local:agent",$"AI 译稿 · {validation.TranslatedCount}/{validation.RequiredCount} 行 · 原词哈希及逐组对应已检查；语义未人工核实",DateTimeOffset.UtcNow,true);
            TranslationRepository.Validate(translation);token.ThrowIfCancellationRequested();
            Execute(db,"INSERT INTO Translations(Fingerprint,SourceHash,Duration,Title,Artist,Album,Content,Manual,Revision) VALUES($f,$h,$d,$t,$a,$al,$c,0,$r) ON CONFLICT(Fingerprint,SourceHash) DO UPDATE SET Content=excluded.Content,Manual=0,Revision=excluded.Revision",("$f",t.Fingerprint),("$h",translation.SourceHash),("$d",t.DurationMs),("$t",t.Title),("$a",t.Artist),("$al",t.Album),("$c",JsonSerializer.Serialize(translation)),("$r",applied));
            Execute(db,"UPDATE PreparationQueue SET State='Draft',OriginalHash=$h,Detail='本地 agent 译文',RetryUtc=NULL WHERE Active=1 AND Fingerprint=$f AND ($d IS NULL OR Duration IS NULL OR abs(Duration-$d)<=5000)",("$h",translation.SourceHash),("$f",t.Fingerprint),("$d",t.DurationMs));
            next=validation.TranslatedCount<validation.RequiredCount?TranslationJobState.AppliedPartial:TranslationJobState.Applied;message="已接收中文译稿。";
        }
        var receipt=new TranslationJobReceipt(request.JobId,next,next.ToString(),message,validation.TranslatedCount,validation.RequiredCount,applied,DateTimeOffset.UtcNow);
        Execute(db,"UPDATE TranslationJobs SET State=$s,Receipt=$r,ResultHash=$hash WHERE Id=$id",("$hash",validation.ResultSha256),("$s",next.ToString()),("$r",JsonSerializer.Serialize(receipt,TranslationJobFiles.Json)),("$id",request.JobId));tx.Commit();
        try{TranslationJobFiles.Write(TranslationJobFiles.PathFor(root,request.JobId,"receipt.json"),JsonSerializer.Serialize(receipt,TranslationJobFiles.Json),true);}catch(IOException){}return receipt;
    }
    public static Task<TranslationJobReceipt> ApprovePartialAsync(string root,string jobId,string expectedRevision,CancellationToken token=default)=>Task.Run(()=>Apply(root,TranslationJobFiles.PathFor(root,jobId,"translation-result.json"),expectedRevision,token),token);
    public static void Cancel(string root,string jobId){using var db=Open(root);Execute(db,"UPDATE TranslationJobs SET State='Cancelled',Receipt=NULL WHERE Id=$id AND State IN ('AwaitingResult','PartialReady','Exporting')",("$id",jobId));}
    public static Task<bool> RestoreHistoryAsync(string root,Track track,string sourceHash,string historyRevision,string expectedRevision,CancellationToken token=default)=>Task.Run(()=>
    {
        using var db=Open(root);Execute(db,"ATTACH DATABASE $p AS originals",("$p",Path.Combine(root,"lyrics.db")));using var tx=db.BeginTransaction(deferred:false);token.ThrowIfCancellationRequested();
        string? content=Scalar(db,"SELECT Content FROM TranslationHistory WHERE Revision=$r AND Fingerprint=$f AND SourceHash=$h",("$r",historyRevision),("$f",track.Fingerprint),("$h",sourceHash));
        if(content is null)return false;
        var restored=JsonSerializer.Deserialize<ChineseTranslation>(content)??throw new InvalidDataException("历史译文为空。");TranslationRepository.Validate(restored);if(restored.SourceHash!=sourceHash)throw new InvalidDataException("历史原词哈希不一致。");
        using var c=Command(db,"UPDATE Translations SET Content=$c,Manual=(SELECT Manual FROM TranslationHistory WHERE Revision=$old),Revision=$r WHERE Fingerprint=$f AND SourceHash=$h AND Revision=$expected AND EXISTS(SELECT 1 FROM originals.TrackLyricsBindings b JOIN originals.LyricsDocuments d ON d.Id=b.DocumentId JOIN originals.Tracks t ON t.Id=b.TrackId WHERE t.Fingerprint=$f AND lower(d.Hash)=$h)",("$c",content),("$old",historyRevision),("$r",Guid.NewGuid().ToString("N")),("$f",track.Fingerprint),("$h",sourceHash),("$expected",expectedRevision));bool changed=c.ExecuteNonQuery()==1;tx.Commit();return changed;
    },token);
}
