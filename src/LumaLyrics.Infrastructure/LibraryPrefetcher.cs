using LumaLyrics.Core;
namespace LumaLyrics.Infrastructure;
public sealed record LibraryQueueItem(string Source,string PersistentId,Track Track);
public sealed record LibraryProgress(int Total,int Cached,int Unavailable,int Errors,bool Running,string Message)
{
    public int Synced {get;init;}
    public int Instrumental {get;init;}
    public int Manual {get;init;}
    public int Pending=>Math.Max(0,Total-Cached-Unavailable-Errors-Manual);
    public override string ToString()=>$"资料库 {Total} 首 · 动态歌词 {Synced} · 纯音乐 {Instrumental} · 手动保留 {Manual}\n待处理 {Pending} · 待补齐 {Unavailable} · 网络待重试 {Errors}\n{Message}";
}
public sealed class LibraryPrefetcher(string databasePath,ILyricsProvider provider,Func<bool>? foregroundBusy=null,TimeSpan? pacing=null)
{
    const int BatchConcurrency=3;
    public static string PausePath(string databasePath)=>Path.Combine(Path.GetDirectoryName(Path.GetFullPath(databasePath))!,"library-prefetch.pause");
    public static string ProgressPath(string databasePath)=>Path.Combine(Path.GetDirectoryName(Path.GetFullPath(databasePath))!,"library-prefetch-progress.json");
    public static bool IsRunning(string databasePath)=>LeaseHeld(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(databasePath))!,"library-prefetch.lock"));
    internal static bool LeaseHeld(string path)
    {
        if(!File.Exists(path))return false;
        try{using var lease=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.None);return false;}
        catch(IOException){return true;}
    }
    void Report(LibraryProgress progress)
    {
        PreparationProgressFile.Write(ProgressPath(databasePath),System.Text.Json.JsonSerializer.Serialize(progress));Progress?.Invoke(progress);
    }
    public event Action<LibraryProgress>? Progress;
    public event Action<Track>? Cached;
    public async Task RunAsync(string libraryPath,CancellationToken token,int? maximum=null)
    {
        using var repository=new LyricsRepository(databasePath);
        FileStream lease;
        try{lease=new FileStream(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(databasePath))!,"library-prefetch.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
        catch(IOException){Progress?.Invoke(repository.GetLibraryProgress("其他实例正在后台预缓存，本实例避免重复请求",true));return;}
        using var heldLease=lease;
        using var pause=new LibraryPauseMonitor(databasePath,token);
        var runToken=pause.Token;
        string pausePath=PausePath(databasePath);
        try
        {
            var snapshot=await Task.Run(()=>new AppleMusicLibraryReader().Read(libraryPath),runToken);
            repository.SyncLibrary(snapshot,runToken);
            foreach(var item in repository.GetLibraryQueue(cachedOnly:true))
            {
                runToken.ThrowIfCancellationRequested();var binding=repository.FindReadOnly(item.Track);
                if(binding?.Manual==true)repository.SetLibraryState(item,"Manual","已保留手动锁定的歌词");
                else if(binding is null || !QualityOf(binding,item.Track))repository.SetLibraryState(item,"Pending","本地自动缓存需重新验证");
            }
            Report(repository.GetLibraryProgress("已读取完整资料库并复查自动缓存，后台开始预缓存",true));
            var eligible=new List<LibraryQueueItem>();
            foreach(var item in repository.GetLibraryQueue())
            {
                runToken.ThrowIfCancellationRequested();
                if(!KeepLocalBinding(item,repository.FindReadOnly(item.Track)))eligible.Add(item);
            }
            int processed=0,consecutiveErrors=0;
            long lastProgress=Environment.TickCount64;
            // The fingerprint omits duration. Keep different lengths eligible for their own lookup.
            var recordings=eligible.GroupBy(item=>(TrackMetadata.FromAppleSession(item.Track).Fingerprint,item.Track.DurationMs)).ToArray();
            var exactMisses=new Dictionary<(string Fingerprint,double? DurationMs),LyricsLookupResult>();
            var resolved=new HashSet<(string Fingerprint,double? DurationMs)>();
            var staged=provider as ResilientLyricsProvider;
            var interval=pacing??(staged is null?TimeSpan.FromMilliseconds(200):TimeSpan.Zero);
            if(staged is not null && recordings.Length>1 && (!maximum.HasValue || maximum.Value>1))
            {
                foreach(var recording in recordings)
                {
                    CheckRun();
                    if((maximum.HasValue && processed>=maximum.Value) || staged.IsPrimaryRateLimited)break;
                    await WaitForPlaybackAsync(runToken);
                    CheckRun();
                    if(staged.IsPrimaryRateLimited)break;
                    var track=recording.First().Track;
                    var exact=await staged.LookupExactAsync(track,runToken);
                    CheckRun();
                    if(exact is not null && AutomaticLyricsMatcher.Select(track,exact) is {Candidate.Instrumental:false} best)
                    {
                        SaveResult(recording,exact,best);
                        resolved.Add(recording.Key);
                        processed++;
                        ReportProgress(fast:true);
                    }
                    else if(exact is not null)exactMisses.Add(recording.Key,exact);
                    if(staged.IsPrimaryRateLimited)break;
                    await Task.Delay(interval,runToken);
                }
            }

            var remaining=recordings.Where(recording=>!resolved.Contains(recording.Key)).ToArray();
            int slots=maximum is {} limit?Math.Min(remaining.Length,Math.Max(0,limit-processed)):remaining.Length;
            int workerCount=staged is null?Math.Min(1,slots):Math.Min(BatchConcurrency,slots);
            using var batchCts=CancellationTokenSource.CreateLinkedTokenSource(runToken);
            var batchToken=batchCts.Token;
            using var dbGate=new SemaphoreSlim(1,1);
            using var startGate=new SemaphoreSlim(1,1);
            int next=0;
            bool pausedByFile=false,tooManyErrors=false;
            await Task.WhenAll(Enumerable.Range(0,workerCount).Select(_=>CompleteAsync()));
            token.ThrowIfCancellationRequested();
            Report(repository.GetLibraryProgress(pausedByFile || runToken.IsCancellationRequested || File.Exists(pausePath)
                ?"已暂停，下次从本地记录继续"
                :tooManyErrors?"歌词服务连续失败，暂停本轮并稍后自动重试":"本轮完成；未找到及网络失败项目稍后自动重试",false));

            async Task CompleteAsync()
            {
                try
                {
                    while(true)
                    {
                        int slot;
                        // Admit work in queue order, with any explicit pacing shared across workers.
                        await startGate.WaitAsync(batchToken);
                        try
                        {
                            if(next==slots || StopForPause())return;
                            if(workerCount>1 && next>0)await Task.Delay(interval,batchToken);
                            await WaitForPlaybackAsync(batchToken);
                            if(StopForPause())return;
                            slot=next++;
                        }
                        finally{startGate.Release();}

                        var recording=remaining[slot];
                        var track=recording.First().Track;
                        IReadOnlyList<LyricsCandidate> candidates=[];
                        Exception? error=null;
                        try
                        {
                            batchToken.ThrowIfCancellationRequested();
                            candidates=staged is not null
                                ?await staged.CompleteLookupAsync(track,exactMisses.GetValueOrDefault(recording.Key),batchToken,preferTranslation:false)
                                :await provider.LookupAsync(track,batchToken);
                        }
                        catch(Exception e) when(e is LyricsProviderException or HttpRequestException){error=e;}

                        await dbGate.WaitAsync(batchToken);
                        try
                        {
                            if(StopForPause())return;
                            batchToken.ThrowIfCancellationRequested();
                            if(error is null)
                            {
                                SaveResult(recording,candidates,AutomaticLyricsMatcher.Select(track,candidates));
                                consecutiveErrors=0;
                            }
                            else
                            {
                                foreach(var item in recording)
                                    if(repository.IsCurrentLibraryMember(item) && !KeepLocalBinding(item,repository.FindReadOnly(item.Track)))
                                        repository.SetLibraryState(item,"Error",error.Message);
                                consecutiveErrors++;
                            }
                            processed++;
                            if(consecutiveErrors>=4)
                            {
                                tooManyErrors=true;
                                batchCts.Cancel();
                                return;
                            }
                            ReportProgress();
                        }
                        finally{dbGate.Release();}
                        if(workerCount==1)await Task.Delay(interval,batchToken);
                    }
                }
                catch(OperationCanceledException) when(batchToken.IsCancellationRequested){}
                catch{batchCts.Cancel();throw;}
            }

            bool StopForPause()
            {
                if(!File.Exists(pausePath))return false;
                Volatile.Write(ref pausedByFile,true);
                batchCts.Cancel();
                return true;
            }
            void CheckRun()
            {
                runToken.ThrowIfCancellationRequested();
                if(File.Exists(pausePath))throw new OperationCanceledException();
            }
            async Task WaitForPlaybackAsync(CancellationToken checkToken)
            {
                while(foregroundBusy?.Invoke()==true)await Task.Delay(250,checkToken);
                checkToken.ThrowIfCancellationRequested();
            }
            void SaveResult(IEnumerable<LibraryQueueItem> recording,IReadOnlyList<LyricsCandidate> candidates,AutomaticLyricsChoice? selected)
            {
                var representative=recording.First();
                if(!repository.IsCurrentLibraryMember(representative))return;
                var track=representative.Track;
                var saved=selected is {} best
                    ?repository.Save(track,best.Candidate.Content,best.Candidate.SourceLabel+" · 自动检查",false,best.Candidate.Instrumental,
                        providerJson:best.Candidate.RawJson,expectedLibrary:representative,preserveReliable:true)
                    :repository.SaveStaticFallback(track,candidates,representative);
                var binding=saved??repository.FindReadOnly(track);
                foreach(var item in recording)
                {
                    if(!repository.IsCurrentLibraryMember(item) || KeepLocalBinding(item,binding,selected?.Assessment.Reason))continue;
                    string reason=selected is null?LyricsLookupDiagnosis.Describe(item.Track,candidates)
                        :binding?.StaticFallback==true?"已保留静态歌词，可重新查找动态原词。":"本地自动缓存需重新验证";
                    repository.RecordLookupFailure(item.Track,reason,selected is null && LyricsLookupDiagnosis.Unavailable(candidates),
                        candidates.Count==0,expectedLibrary:item);
                }
                if(saved is {Manual:false} && (selected is not null || saved.StaticFallback))Cached?.Invoke(track);
            }
            bool KeepLocalBinding(LibraryQueueItem item,LyricsBinding? binding,string? detail=null)
            {
                if(binding?.Manual==true)
                    repository.SetLibraryState(item,"Manual","已保留手动锁定的歌词");
                else if(binding is not null && QualityOf(binding,item.Track))
                    repository.SetLibraryState(item,"Cached",detail??"已有通过检查的本地缓存",binding.Instrumental?"Instrumental":"Synced");
                else return false;
                return true;
            }
            void ReportProgress(bool fast=false)
            {
                if(Environment.TickCount64-lastProgress<500)return;
                Report(repository.GetLibraryProgress(fast?$"快速精确查词 · 本轮已处理 {processed} 首；播放查词优先"
                    :$"本轮已处理 {processed} 首；播放查词优先",true));
                lastProgress=Environment.TickCount64;
            }
        }
        catch(OperationCanceledException){Report(repository.GetLibraryProgress("已暂停，下次从本地记录继续",false));}
    }
    public static bool QualityOf(LyricsBinding binding,Track track)
    {
        if(binding.Plain && !binding.Instrumental)return false;
        if(!string.IsNullOrWhiteSpace(binding.ProviderJson))
        {
            try
            {
                using var json=System.Text.Json.JsonDocument.Parse(binding.ProviderJson);
                if(json.RootElement.TryGetProperty("provider",out var native) && native.GetString()=="Apple Music")
                {
                    var saved=AppleOriginalProvenance.Validate(binding.ProviderJson,binding.Raw);
                    var expected=TrackMetadata.FromAppleSession(track);
                    return !binding.Instrumental && !binding.Plain && expected.Fingerprint==saved.Track.Fingerprint
                        && expected.DurationMs is { } duration && Math.Abs(duration-saved.Track.DurationMs!.Value)<=1000
                        && AutomaticLyricsMatcher.Assess(new(0,saved.Track,binding.Raw,null,false)).Accepted;
                }
                var original=LrclibProvider.Parse(json.RootElement);
                if(json.RootElement.TryGetProperty("Provider",out var source) && source.GetString()=="NetEase")
                {
                    var saved=System.Text.Json.JsonSerializer.Deserialize<NetEaseOriginal>(binding.ProviderJson);
                    if(saved is null || saved.Track is null || saved.Original!=binding.Raw || saved.Instrumental!=binding.Instrumental || binding.Plain)return false;
                    if(saved.Instrumental && !NetEaseRecording.IsExplicitInstrumental(saved.InstrumentalEvidence??""))return false;
                    var savedCandidate=NetEaseTranslationProvider.CatalogCandidate(saved.Id,saved.Track,saved.CatalogAlbumId,saved.ReleaseEvidence)
                        with{SyncedLyrics=saved.Instrumental?null:saved.Original,Instrumental=saved.Instrumental};
                    if(saved.ReleaseEvidence is null)savedCandidate=LegacyNetEaseRelease.Attach(savedCandidate,saved.CatalogAlbumId);
                    return NetEaseTranslationProvider.RecordingMatches(track,savedCandidate) && AutomaticLyricsMatcher.Assess(savedCandidate).Accepted;
                }
                if(original.Instrumental!=binding.Instrumental)return false;
                if(!AutomaticLyricsMatcher.Assess(original).Accepted)return false;
                var candidate=original with{SyncedLyrics=binding.Plain?null:binding.Raw};
                return AutomaticLyricsMatcher.Select(track,[candidate]) is not null;
            }
            catch(Exception e) when(e is System.Text.Json.JsonException or InvalidOperationException or InvalidDataException or System.Xml.XmlException){return false;}
        }
        return AutomaticLyricsMatcher.Assess(new(0,track,binding.Plain?null:binding.Raw,binding.Plain?binding.Raw:null,binding.Instrumental)).Accepted;
    }
}
