using System.IO;
using System.Net.Http;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;
public sealed partial class PlaybackCoordinator
{
    const int BatchConcurrency=3;
    CancellationTokenSource? workspaceCancellation;Task? workspaceOperation;
    internal ILyricsProvider? WorkspaceOriginalProvider {get;set;}
    internal ITranslationProvider? WorkspaceChineseProvider {get;set;}
    public string WorkspaceMessage {get;private set;}="选择歌曲后，一次准备原词与中文译文。";
    internal long WorkspaceResultsRevision {get;private set;}
    internal IReadOnlySet<LibrarySongKey> WorkspacePendingLookup {get;private set;}=new HashSet<LibrarySongKey>(LibrarySongKey.Comparer);
    public bool WorkspaceBusy=>workspaceOperation is {IsCompleted:false};
    Task<LibraryWorkspaceSnapshot>? workspaceRead;LibraryWorkspaceCatalog? workspaceCatalog;string? workspaceReadSource;DateTime workspaceReadUntil;long workspaceEpoch,workspaceReadResultsRevision;
    void InvalidateWorkspace()=>workspaceReadUntil=DateTime.MinValue;
    public Task<LibraryWorkspaceSnapshot> ReadWorkspaceAsync(CancellationToken token,bool refresh=false)
    {
        bool sameSource=workspaceReadSource==Settings.LibraryPath;
        if(workspaceRead is null||!sameSource||workspaceReadResultsRevision!=WorkspaceResultsRevision||workspaceRead.IsCompleted&&(refresh||DateTime.UtcNow>=workspaceReadUntil))
        {
            if(!sameSource)workspaceEpoch++;workspaceReadSource=Settings.LibraryPath;workspaceReadUntil=DateTime.UtcNow.AddSeconds(5);
            workspaceReadResultsRevision=WorkspaceResultsRevision;
            string? source=Settings.LibraryPath;if(Environment.GetCommandLineArgs().Contains("--verify-ui")&&source is null)source=Path.Combine(store.Root,"isolated-library.musicdb");
            workspaceCatalog??=new(store.Root);workspaceRead=workspaceCatalog.ReadAsync(source,workspaceEpoch,lifetime.Token,confirmedOnly:WorkspaceBusy);
        }
        return workspaceRead.WaitAsync(token);
    }
    public async Task SynchronizeWorkspaceAsync(CancellationToken token)
    {
        string? expectedSource=Settings.LibraryPath;string source=expectedSource??AppleMusicLibraryReader.Discover()??throw new IOException("未发现 Apple Music 资料库。");
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(token,lifetime.Token);
        int count=await Task.Run(()=>
        {
            var snapshot=new AppleMusicLibraryReader().Read(source);linked.Token.ThrowIfCancellationRequested();
            if(expectedSource!=Settings.LibraryPath)throw new OperationCanceledException("资料库来源已变化。");
            using var lyrics=new LyricsRepository(Path.Combine(store.Root,"lyrics.db"));lyrics.SyncLibrary(snapshot,linked.Token,()=>expectedSource==Settings.LibraryPath);return snapshot.Tracks.Count;
        },linked.Token);
        if(disposed||expectedSource!=Settings.LibraryPath)return;InvalidateWorkspace();WorkspaceMessage=$"已同步 {count:N0} 首资料库歌曲。";Updated?.Invoke();
    }
    public void PauseWorkspace()=>workspaceCancellation?.Cancel();
    public Task PrepareWorkspaceAsync(IReadOnlyList<LibraryManagedSong> selected,bool retranslate,bool findExisting,bool prepareOriginal=true,bool exportAgent=true,bool includeArtwork=false)
    {
        if(WorkspaceBusy)throw new InvalidOperationException("已有准备工作，请等待或暂停。");
        var targets=selected.Select(song=>song.Target).ToArray();
        var recordings=targets.GroupBy(target=>target.Track).GroupBy(recording=>
        {
            var track=TrackMetadata.FromAppleSession(recording.Key);
            return (MatchRules.Normalize(track.Artist),MatchRules.Normalize(track.Album));
        }).SelectMany(album=>album).ToArray();
        bool online=Settings.OnlineLookup;
        string? source=Settings.LibraryPath;
        workspaceCancellation?.Dispose();
        var operation=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        workspaceCancellation=operation;
        var token=operation.Token;
        workspaceOperation=Task.Run(async()=>
        {
            var stateLock=new object();
            var exported=new List<(int Order,TranslationBatchSelection Selection)>();
            var messages=new List<(int Order,string Message)>();
            var exactMisses=new Dictionary<Track,LyricsLookupResult>();
            var pending=new HashSet<LibrarySongKey>(LibrarySongKey.Comparer);
            var originalProvider=WorkspaceOriginalProvider??provider;
            var staged=originalProvider as ResilientLyricsProvider;
            var chineseProvider=WorkspaceChineseProvider;
            var workflow=new TranslationWorkflow(store.Root,chineseProvider??(ITranslationProvider)(staged??provider));
            int handled=0,checkedFast=0,savedFast=0;
            bool resultsChanged=false;
            long lastProgress=0,lastResults=0;
            try
            {
                CheckCurrent(token);
                var membership=new MusicLibraryCatalog(store.Root,source).Load(token);
                if(!await PrepareLyricsAsync(membership.ConfirmedMembership))return;
                CheckCurrent(token);
                if(includeArtwork && online && Settings.OnlineLookup && membership.ConfirmedMembership is {} artworkMembership)
                {
                    using var artwork=new LibraryArtworkImporter(store.Root);
                    artwork.Progress+=p=>{if(!disposed)_=dispatcher.BeginInvoke(()=>{if(disposed)return;ArtworkStatus=p;Updated?.Invoke();});};
                    await artwork.RunAsync(artworkMembership,token,selectedKeys:targets.Select(t=>t.Key).ToHashSet(LibrarySongKey.Comparer));
                }
                CheckCurrent(token);
                var batch=TranslationBatchExchange.Create(store.Root,exported.OrderBy(item=>item.Order).Select(item=>item.Selection).ToArray(),token:token);
                Report($"本轮处理 {handled} 首；生成 {batch.Tickets.Count} 份任务；跳过 {messages.Count+batch.Skipped.Count} 项。"
                    +(batch.Batch is null?"":"\n批量入口："+batch.Batch.EntryPath)
                    +string.Concat(messages.OrderBy(item=>item.Order).Select(item=>item.Message)
                        .Concat(batch.Skipped.Select(s=>s.Track.Title+"："+s.Message)).Select(m=>"\n"+m)),force:true,done:true);
            }
            catch(OperationCanceledException){Report("准备已暂停，已保存内容和任务保留。",force:true,done:true);}
            catch(Exception e){Report("准备中断："+e.Message,force:true,done:true);}

            async Task<bool> PrepareLyricsAsync(LibrarySnapshot? membership)
            {
                CheckCurrent(token);
                FileStream? originalLease=null;
                if(!retranslate && prepareOriginal && online && Settings.OnlineLookup)
                {
                    await SuspendLyricsPrefetchForWorkspaceAsync(token);
                    CheckCurrent(token);
                    try{originalLease=OriginalLease();}
                    catch(IOException)
                    {
                        Report("另一个浮光歌词进程正在后台预缓存，请稍候或先暂停后台预缓存。",force:true,done:true);
                        return false;
                    }
                }
                using var heldOriginalLease=originalLease;
                using var lyrics=new LyricsRepository(Path.Combine(store.Root,"lyrics.db"));
                CheckCurrent(token);
                if(membership is {} full)
                    lyrics.SyncLibrary(full,token,()=>Settings.LibraryPath==source);
                if(staged is not null && originalLease is not null && Settings.OnlineLookup)
                {
                    foreach(var recording in recordings)
                    {
                        CheckCurrent(token);
                        var track=recording.Key;
                        var binding=lyrics.FindReadOnly(track);
                        if(NeedsOriginal(binding,track))
                        {
                            var member=recording.Select(Member).FirstOrDefault(lyrics.IsCurrentLibraryMember);
                            if(member is not null)
                            {
                                try
                                {
                                    await WaitForPlaybackAsync(token);
                                    var exact=await staged.LookupExactAsync(track,token);
                                    CheckCurrent(token);
                                    if(!Settings.OnlineLookup)throw new OperationCanceledException();
                                    if(exact is not null && AutomaticLyricsMatcher.Select(track,exact) is {Candidate.Instrumental:false} best)
                                    {
                                        SaveOriginal(lyrics,member,best);
                                        savedFast+=recording.Count();
                                        lock(stateLock)resultsChanged=true;
                                    }
                                    else
                                    {
                                        if(exact is not null)exactMisses.Add(track,exact);
                                        lock(stateLock)
                                        {
                                            foreach(var target in recording)pending.Add(target.Key);
                                            resultsChanged=true;
                                        }
                                    }
                                }
                                catch(Exception e) when(e is IOException or InvalidDataException)
                                {
                                    // Retry the normal chain in pass two if no exact result was retained.
                                }
                            }
                        }
                        checkedFast+=recording.Count();
                        Report($"原词快查 {checkedFast}/{targets.Length} 首 · 已保存 {savedFast} 首 · 待补查 {pending.Count} 首");
                    }
                    Report($"原词快查 {checkedFast}/{targets.Length} 首 · 已保存 {savedFast} 首 · 待补查 {pending.Count} 首",force:true);
                }

                CheckCurrent(token);
                bool lookupTranslations=!retranslate && findExisting;
                FileStream? translationLease=null;
                if(lookupTranslations)
                {
                    try{translationLease=new(Path.Combine(store.Root,"translation-preparation.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
                    catch(IOException)
                    {
                        Report("后台译文准备正在运行，请稍候或先暂停。",force:true,done:true);
                        return false;
                    }
                }
                using var heldTranslationLease=translationLease;
                using var translations=new TranslationRepository(store.Root,readOnly:!lookupTranslations);
                using var dbGate=new SemaphoreSlim(1,1);
                int concurrency=staged is not null && chineseProvider is null?BatchConcurrency:1;
                using var batchCts=CancellationTokenSource.CreateLinkedTokenSource(token);
                var batchToken=batchCts.Token;
                var running=new List<Task>(concurrency);
                try
                {
                    // Start groups in album order after the exact sweep. The providers retain their own host limits.
                    for(int index=0;index<recordings.Length;index++)
                    {
                        if(running.Count==concurrency)
                        {
                            var finished=await Task.WhenAny(running);
                            running.Remove(finished);
                            await finished;
                        }
                        CheckCurrent(batchToken);
                        var recording=recordings[index];
                        exactMisses.Remove(recording.Key,out var exact);
                        running.Add(PrepareRecordingAsync(recording,index,exact));
                    }
                    await Task.WhenAll(running);
                }
                catch
                {
                    batchCts.Cancel();
                    try{await Task.WhenAll(running);}
                    catch(Exception){/* Keep the error that stopped the batch. */}
                    throw;
                }
                return true;

                async Task PrepareRecordingAsync(IEnumerable<LibraryPreparationTarget> recording,int order,LyricsLookupResult? exact)
                {
                    IReadOnlyList<LyricsCandidate>? found=null;
                    foreach(var captured in recording)
                    {
                        CheckCurrent(batchToken);
                        var member=Member(captured);
                        try
                        {
                            var binding=await WithDatabaseAsync(()=>
                            {
                                if(!lyrics.IsCurrentLibraryMember(member))throw new InvalidDataException("目标不在已确认资料库，请先刷新来源。");
                                return lyrics.FindReadOnly(captured.Track);
                            });
                            string? originalReason=null;
                            if(originalLease is not null && NeedsOriginal(binding,captured.Track) && Settings.OnlineLookup)
                            {
                                if(found is null)
                                {
                                    await WaitForPlaybackAsync(batchToken);
                                    found=staged is not null
                                        ?await staged.CompleteLookupAsync(captured.Track,exact,batchToken)
                                        :await originalProvider.LookupAsync(captured.Track,batchToken);
                                }
                                var best=AutomaticLyricsMatcher.Select(captured.Track,found);
                                binding=await WithDatabaseAsync(()=>
                                {
                                    if(!Settings.OnlineLookup)throw new OperationCanceledException();
                                    if(best is not null)return SaveOriginal(lyrics,member,best);
                                    originalReason=LyricsLookupDiagnosis.Describe(captured.Track,found);
                                    var fallback=lyrics.SaveStaticFallback(captured.Track,found,member)??lyrics.FindReadOnly(captured.Track);
                                    lyrics.RecordLookupFailure(captured.Track,originalReason,LyricsLookupDiagnosis.Unavailable(found),found.Count==0,expectedLibrary:member);
                                    return fallback;
                                });
                                lock(stateLock)resultsChanged=true;
                            }

                            if(binding?.Instrumental==true)
                            {
                                AddMessage(captured.Track.Title+"：已明确纯音乐，无需译文");
                                continue;
                            }
                            if(binding is null || binding.Plain || NeedsOriginal(binding,captured.Track))
                            {
                                if(originalReason is null)
                                {
                                    originalReason=binding is {Manual:true,Plain:true}?"当前为手动静态原词，需动态时间轴。"
                                        :binding?.StaticFallback==true?"已保留静态歌词，可重新查找动态原词。"
                                        :!online?"联网查词已关闭，尚未查询原词。":"尚无可靠动态原词，请先准备原词。";
                                    if(binding?.StaticFallback!=true)
                                        await RecordFailureAsync(member,originalReason,false);
                                }
                                AddMessage(captured.Track.Title+"："+originalReason);
                                continue;
                            }

                            var doc=new LrcParser().Parse(binding.Raw);
                            var active=await WithDatabaseAsync(()=>translations.FindActive(captured.Track,doc));
                            var sourceHash=TranslationAlignment.Hash(doc.Raw);
                            if(retranslate && (captured.SourceHash!=sourceHash || captured.ExpectedTranslationRevision!=active?.Revision))
                                throw new InvalidDataException("原词或当前译文已更新，请重新选择。");
                            if(!retranslate && IsUsableTranslation(active))
                            {
                                if(active!.Record.Translation.Lines.Count<doc.Groups.Count)
                                    AddMessage(captured.Track.Title+"：沿用已有译文；部分稿需明确重译");
                                continue;
                            }
                            if(lookupTranslations)
                            {
                                await WaitForPlaybackAsync(batchToken);
                                var previous=chineseProvider is null?(found as LyricsLookupResult)?.TranslationCandidates:null;
                                var result=await workflow.PrepareAsync(lyrics,translations,captured.Track,online&&Settings.OnlineLookup,false,batchToken,previous,member,dbGate);
                                active=await WithDatabaseAsync(()=>translations.FindActive(captured.Track,doc));
                                if(result.Lookup.State==TranslationLookupState.Found)
                                    lock(stateLock)resultsChanged=true;
                                if(IsUsableTranslation(active))continue;
                            }
                            CheckCurrent(batchToken);
                            if(!exportAgent)
                            {
                                AddMessage(captured.Track.Title+"：来源暂无可用译文");
                                continue;
                            }
                            var target=captured with{BindingId=binding.Id,SourceHash=sourceHash,ExpectedTranslationRevision=active?.Revision};
                            lock(stateLock)
                                exported.Add((order,new(captured.Track,retranslate&&active is not null?TranslationJobMode.ReplaceExpected:TranslationJobMode.FillMissing,Target:target)));
                        }
                        catch(Exception e) when(e is IOException or InvalidDataException or LyricsProviderException or HttpRequestException)
                        {
                            await RecordFailureAsync(member,e.Message,e is LyricsProviderException or HttpRequestException);
                            AddMessage(captured.Track.Title+"："+e.Message);
                        }
                        finally
                        {
                            lock(stateLock)
                            {
                                if(pending.Remove(captured.Key))resultsChanged=true;
                                handled++;
                                if(!batchToken.IsCancellationRequested)
                                    Report($"补查与译文 {handled}/{targets.Length} 首 · 待导出 {exported.Count} 首");
                            }
                        }
                    }

                    void AddMessage(string message)
                    {
                        lock(stateLock)messages.Add((order,message));
                    }
                }

                async Task<T> WithDatabaseAsync<T>(Func<T> action)
                {
                    await dbGate.WaitAsync(batchToken);
                    try{CheckCurrent(batchToken);return action();}
                    finally{dbGate.Release();}
                }

                async Task RecordFailureAsync(LibraryQueueItem member,string reason,bool unavailable)
                {
                    await dbGate.WaitAsync(batchToken);
                    try
                    {
                        CheckCurrent(batchToken);
                        lyrics.RecordLookupFailure(member.Track,reason,unavailable,false,expectedLibrary:member);
                    }
                    finally{dbGate.Release();}
                    lock(stateLock)resultsChanged=true;
                }
            }

            void CheckCurrent(CancellationToken checkToken)
            {
                checkToken.ThrowIfCancellationRequested();
                if(Settings.LibraryPath!=source)throw new OperationCanceledException();
            }
            async Task WaitForPlaybackAsync(CancellationToken checkToken)
            {
                while(foregroundLookupActive)await Task.Delay(250,checkToken);
                CheckCurrent(checkToken);
            }
            void Report(string message,bool force=false,bool done=false)
            {
                lock(stateLock)
                {
                    var now=Environment.TickCount64;
                    if(!force && now-lastProgress<500)return;
                    lastProgress=now;
                    bool refresh=done || resultsChanged && (force || now-lastResults>=5000);
                    HashSet<LibrarySongKey>? pendingSnapshot=null;
                    if(refresh)
                    {
                        resultsChanged=false;
                        lastResults=now;
                        pendingSnapshot=done?new(LibrarySongKey.Comparer):new(pending,LibrarySongKey.Comparer);
                    }
                    if(disposed)return;
                    _=dispatcher.BeginInvoke(()=>
                    {
                        if(disposed || workspaceCancellation!=operation)return;
                        WorkspaceMessage=message;
                        if(refresh)
                        {
                            WorkspacePendingLookup=pendingSnapshot!;
                            WorkspaceResultsRevision++;
                            InvalidateWorkspace();
                        }
                        if(done){RefreshCachedOriginal();RefreshTranslation();}
                        Updated?.Invoke();
                    });
                }
            }
        });
        _=ResumeLyricsPrefetchAfterWorkspaceAsync(workspaceOperation);
        return workspaceOperation;
    }

    static LibraryQueueItem Member(LibraryPreparationTarget target)=>new(target.Key.Source,target.Key.PersistentId,target.Track);
    static bool NeedsOriginal(LyricsBinding? binding,Track track)=>binding is null || !binding.Manual && !LibraryPrefetcher.QualityOf(binding,track);
    static bool IsUsableTranslation(ActiveTranslationRevision? active)=>active is not null && (active.Record.Manual || !OfflineTranslationWorkflow.IsMachine(active.Record.Translation));
    FileStream OriginalLease()=>new(Path.Combine(store.Root,"library-prefetch.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
    static LyricsBinding SaveOriginal(LyricsRepository lyrics,LibraryQueueItem member,AutomaticLyricsChoice choice)
    {
        var candidate=choice.Candidate;
        return lyrics.Save(member.Track,candidate.Content,candidate.SourceLabel+" · 自动检查",false,candidate.Instrumental,
            providerJson:candidate.RawJson,expectedLibrary:member,preserveReliable:true);
    }
}
