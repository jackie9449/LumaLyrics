using System.IO;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;
public sealed partial class PlaybackCoordinator
{
    CancellationTokenSource? workspaceCancellation;Task? workspaceOperation;
    internal ILyricsProvider? WorkspaceOriginalProvider {get;set;}
    internal ITranslationProvider? WorkspaceChineseProvider {get;set;}
    public string WorkspaceMessage {get;private set;}="选择歌曲后，一次准备原词与中文译文。";
    public bool WorkspaceBusy=>workspaceOperation is {IsCompleted:false};
    Task<LibraryWorkspaceSnapshot>? workspaceRead;string? workspaceReadSource;DateTime workspaceReadUntil;long workspaceEpoch;
    public Task<LibraryWorkspaceSnapshot> ReadWorkspaceAsync(CancellationToken token)
    {
        if(workspaceRead is null||workspaceReadSource!=Settings.LibraryPath||DateTime.UtcNow>=workspaceReadUntil){if(workspaceReadSource!=Settings.LibraryPath)workspaceEpoch++;workspaceReadSource=Settings.LibraryPath;workspaceReadUntil=DateTime.UtcNow.AddSeconds(1);string? readSource=Settings.LibraryPath;if(Environment.GetCommandLineArgs().Contains("--verify-ui")&&readSource is null)readSource=Path.Combine(store.Root,"isolated-library.musicdb");workspaceRead=new LibraryWorkspaceCatalog(store.Root).ReadAsync(readSource,workspaceEpoch,lifetime.Token);}return workspaceRead.WaitAsync(token);
    }
    public void PauseWorkspace()=>workspaceCancellation?.Cancel();
    public Task PrepareWorkspaceAsync(IReadOnlyList<LibraryManagedSong> selected,bool retranslate,bool findExisting,bool prepareOriginal=true,bool exportAgent=true,bool includeArtwork=false)
    {
        if(WorkspaceBusy)throw new InvalidOperationException("已有准备工作，请等待或暂停。");
        var targets=selected.Select(s=>s.Target).ToArray();bool online=Settings.OnlineLookup;string? source=Settings.LibraryPath;
        workspaceCancellation?.Dispose();workspaceCancellation=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);var token=workspaceCancellation.Token;
        workspaceOperation=Task.Run(async()=>
        {
            var exported=new List<TranslationBatchSelection>();int handled=0;var messages=new List<string>();
            using var translationsProvider=new NetEaseTranslationProvider();var workflow=new TranslationWorkflow(store.Root,WorkspaceChineseProvider??translationsProvider);
            try
            {
                var membership=new MusicLibraryCatalog(store.Root,source).Load(token);
                token.ThrowIfCancellationRequested();if(Settings.LibraryPath!=source)throw new OperationCanceledException();
                if(membership.ConfirmedMembership is {} full){using var sync=new LyricsRepository(Path.Combine(store.Root,"lyrics.db"));sync.SyncLibrary(full);}
                foreach(var captured in targets)
                {
                    token.ThrowIfCancellationRequested();if(Settings.LibraryPath!=source)throw new OperationCanceledException("资料库来源已变化。");
                    try
                    {
                        using var lyrics=new LyricsRepository(Path.Combine(store.Root,"lyrics.db"));
                        var members=lyrics.GetLibraryQueue(true);if(!members.Any(m=>m.Source.Equals(captured.Key.Source,StringComparison.OrdinalIgnoreCase)&&m.PersistentId==captured.Key.PersistentId&&m.Track==captured.Track))throw new InvalidDataException("目标不在已确认资料库，请先刷新来源。");
                        var binding=lyrics.FindReadOnly(captured.Track);
                        if(!retranslate&&prepareOriginal&&(binding is null||!binding.Manual&&!LibraryPrefetcher.QualityOf(binding,captured.Track))&&online&&Settings.OnlineLookup)
                        {
                            using var lease=new FileStream(Path.Combine(store.Root,"library-prefetch.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
                            while(foregroundLookupActive){await Task.Delay(250,token);}
                            var found=await (WorkspaceOriginalProvider??provider).LookupAsync(captured.Track,token);token.ThrowIfCancellationRequested();if(!Settings.OnlineLookup||Settings.LibraryPath!=source)throw new OperationCanceledException();
                            var best=AutomaticLyricsMatcher.Select(captured.Track,found);
                            if(best is not null)binding=lyrics.Save(captured.Track,best.Candidate.Content,best.Candidate.SourceLabel+" · 自动检查",false,best.Candidate.Instrumental,providerJson:best.Candidate.RawJson);
                        }
                        if(binding is null||binding.Plain||binding.Instrumental||!binding.Manual&&!LibraryPrefetcher.QualityOf(binding,captured.Track)){messages.Add(captured.Track.Title+"：缺可靠动态原词或无需翻译");continue;}
                        var doc=new LrcParser().Parse(binding.Raw);using var tr=new TranslationRepository(store.Root,true);var active=tr.FindActive(captured.Track,doc);
                        if(retranslate&&(captured.SourceHash!=TranslationAlignment.Hash(doc.Raw)||captured.ExpectedTranslationRevision!=active?.Revision))throw new InvalidDataException("原词或当前译文已更新，请重新选择。");
                        if(!retranslate&&active is not null){if(active.Record.Translation.Lines.Count<doc.Groups.Count)messages.Add(captured.Track.Title+"：沿用已有译文；部分稿需明确重译");continue;}
                        if(!retranslate&&findExisting){using var sourceLease=new FileStream(Path.Combine(store.Root,"translation-preparation.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);await workflow.PrepareAsync(captured.Track,online&&Settings.OnlineLookup,false,token);token.ThrowIfCancellationRequested();active=tr.FindActive(captured.Track,doc);if(active is not null)continue;}
                        token.ThrowIfCancellationRequested();if(Settings.LibraryPath!=source)throw new OperationCanceledException();
                        if(!exportAgent){messages.Add(captured.Track.Title+"：来源暂无可用译文");continue;}
                        var target=captured with{BindingId=binding.Id,SourceHash=TranslationAlignment.Hash(doc.Raw),ExpectedTranslationRevision=active?.Revision};
                        exported.Add(new(captured.Track,retranslate&&active is not null?TranslationJobMode.ReplaceExpected:TranslationJobMode.FillMissing,Target:target));
                    }
                    catch(Exception e) when(e is IOException or InvalidDataException or LyricsProviderException){messages.Add(captured.Track.Title+"："+e.Message);}
                    finally{handled++;Report($"已处理 {handled}/{targets.Length} 首；待导出 {exported.Count} 首");}
                }
                token.ThrowIfCancellationRequested();if(Settings.LibraryPath!=source)throw new OperationCanceledException();
                if(includeArtwork&&online&&Settings.OnlineLookup&&membership.ConfirmedMembership is {} artworkMembership){using var artwork=new LibraryArtworkImporter(store.Root);artwork.Progress+=p=>{if(!disposed)_=dispatcher.BeginInvoke(()=>{if(disposed)return;ArtworkStatus=p;Updated?.Invoke();});};await artwork.RunAsync(artworkMembership,token,selectedKeys:targets.Select(t=>t.Key).ToHashSet(LibrarySongKey.Comparer));}
                token.ThrowIfCancellationRequested();if(Settings.LibraryPath!=source)throw new OperationCanceledException();var result=TranslationBatchExchange.Create(store.Root,exported,token:token);
                Report($"本轮处理 {handled} 首；生成 {result.Tickets.Count} 份任务；跳过 {messages.Count+result.Skipped.Count} 项。"+(result.Batch is null?"":"\n批量入口："+result.Batch.EntryPath)+string.Join("",messages.Concat(result.Skipped.Select(s=>s.Track.Title+"："+s.Message)).Select(m=>"\n"+m)));
            }
            catch(OperationCanceledException){Report("准备已暂停，已保存内容和任务保留。");}
            catch(Exception e){Report("准备中断："+e.Message);}
            finally{if(!disposed)_=dispatcher.BeginInvoke(()=>{if(disposed)return;RefreshTranslation();Updated?.Invoke();});}
        },token);
        return workspaceOperation;
        void Report(string message){if(!disposed)_=dispatcher.BeginInvoke(()=>{if(disposed)return;WorkspaceMessage=message;Updated?.Invoke();});}
    }
}
