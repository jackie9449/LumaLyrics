using System.IO;
using System.Windows.Threading;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
using LumaLyrics.Windows;
namespace LumaLyrics.App;
public sealed partial class PlaybackCoordinator
{
    // Preserve report order before posting to the dispatcher; Progress<T> created on
    // a worker would queue separate pool callbacks that can arrive out of order.
    sealed class OfflineProgressSink<T>(Action<T> report):IProgress<T>{public void Report(T value)=>report(value);}
    readonly ArgosInstallation offlineInstallation=new();
    Func<IOfflineTranslationEngine>? offlineEngineFactory;
    CancellationTokenSource? offlineCancellation;Task? offlineTask;
    DateTime offlineLastUiRefresh;int offlineIgnoredSelection;
    long offlineVersion;
    public bool OfflineBusy=>offlineTask is {IsCompleted:false};
    public bool OfflineReady {get;private set;}
    public string OfflineSourceLanguage=>ArgosLanguages.IsSelection(Settings.OfflineTranslationLanguage)?Settings.OfflineTranslationLanguage:"auto";
    public long OfflineDownloadBytes=>offlineInstallation.DownloadBytesFor(OfflineSourceLanguage);
    public void SetOfflineSourceLanguage(string language)
    {
        if(OfflineBusy||!ArgosLanguages.IsSelection(language))return;
        offlineVersion++;Settings.OfflineTranslationLanguage=language;SaveSettings();OfflineReady=false;OfflineMessage="离线机翻尚未准备。";OfflineChanged?.Invoke();_=CheckOfflineReadyAsync();
    }
    public string OfflineMessage {get;private set;}="离线机翻尚未准备。";
    public OfflineTranslationProgress? OfflineProgress {get;private set;}
    public string OfflineSummary=>OfflineProgress is {} p?UiText.F($"离线机翻：完成 {p.Completed} · 跳过 {p.Skipped+offlineIgnoredSelection} · 失败 {p.Failed} · 待处理 {p.Pending}"):"";
    public string OfflineFailures {get;private set;}="";
    public event Action? OfflineChanged;
    public async Task CheckOfflineReadyAsync(){string source=OfflineSourceLanguage;long request=offlineVersion;bool ready=offlineEngineFactory is not null || await Task.Run(()=>offlineInstallation.ReadyFor(source));if(!disposed&&request==offlineVersion&&source==OfflineSourceLanguage){OfflineReady=ready;if(OfflineMessage=="离线机翻尚未准备。"&&ready)OfflineMessage="所选离线语言已就绪。";OfflineChanged?.Invoke();}}
    void ReportOffline(string message,long request,bool? ready=null){dispatcher.BeginInvoke(()=>{if(disposed||request!=offlineVersion)return;if(ready is {} value)OfflineReady=value;OfflineMessage=message;OfflineChanged?.Invoke();},DispatcherPriority.Background);}
    void NotifyOfflineCompletion(Task task,long request)
    {
        _=task.ContinueWith(_=>{if(!disposed)dispatcher.BeginInvoke(()=>{if(!disposed&&request==offlineVersion)OfflineChanged?.Invoke();},DispatcherPriority.Background);},CancellationToken.None,TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
    }
    public void PrepareOfflineEngine()
    {
        if(OfflineBusy)return;offlineCancellation?.Dispose();offlineCancellation=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);var token=offlineCancellation.Token;
        string source=OfflineSourceLanguage;
        long request=++offlineVersion;
        offlineTask=Task.Run(async()=>
        {
            try
            {
                ReportOffline("正在下载并校验离线组件…",request);
                await offlineInstallation.PrepareAsync(new OfflineProgressSink<(int Done,int Total)>(p=>ReportOffline(UiText.F($"离线组件 {p.Done}/{p.Total}"),request)),token,repair:true,language:source);
                // Self-authored startup probe; never pass personal lyrics during setup.
                await using(var probe=new ArgosEngine(offlineInstallation))foreach(string code in ArgosLanguages.Supported.Where(c=>source=="auto"||c==source))await probe.TranslateAsync([ArgosLanguages.ProbeText(code)],code,token);
                ReportOffline("所选离线语言已就绪。",request,offlineInstallation.ReadyFor(source));
            }
            catch(OperationCanceledException){ReportOffline("已暂停离线准备，已完成内容保留。",request);}
            catch(Exception e){store.Log("Offline setup: "+e.GetType().Name);ReportOffline("离线准备失败，请检查网络后重试。",request);}
        },token);NotifyOfflineCompletion(offlineTask,request);OfflineChanged?.Invoke();
    }
    public void PauseOfflineTranslation(){offlineCancellation?.Cancel();ReportOffline("正在暂停离线翻译…",offlineVersion);}
    public void TranslateCurrentOffline()
    {
        if(ActiveTrack is not { } track)throw new InvalidDataException(UiText.T("请先播放或选择歌曲。"));
        if(Binding is not {} binding || Document is not {IsSynced:true,Instrumental:false} doc)throw new InvalidDataException(UiText.T("请先取得动态原词。"));
        StartOfflineTranslation([new(track,binding.Id,TranslationAlignment.Hash(doc.Raw),null,SourceLanguage:OfflineSourceLanguage)]);
    }
    public void TranslateSelectedOffline(IReadOnlyList<LibraryManagedSong> selected)
    {
        if(selected.Count==0)throw new InvalidDataException(UiText.T("请先选择歌曲。"));
        var ready=selected.Where(s=>s.Status.Original==LibraryOriginalState.Ready && s.BindingId is not null && s.Status.SourceHash is not null).ToArray();
        if(ready.Length==0)throw new InvalidDataException(UiText.T("所选歌曲没有可用的动态原词。"));
        StartOfflineTranslation(ready.Select(s=>new OfflineTranslationItem(s.Track,s.BindingId!,s.Status.SourceHash!,s.Target,SourceLanguage:OfflineSourceLanguage)).ToArray(),selected.Count-ready.Length);
    }
    public void ContinueOfflineTranslation()=>StartOfflineTranslation([]);
    void StartOfflineTranslation(IReadOnlyList<OfflineTranslationItem> selected,int ignored=0)
    {
        if(OfflineBusy)throw new InvalidDataException(UiText.T("离线任务正在运行，请先暂停。"));
        if(!OfflineReady)throw new InvalidDataException(UiText.T("请先在资料库总览准备离线翻译组件。"));
        offlineIgnoredSelection=ignored;offlineLastUiRefresh=DateTime.MinValue;
        offlineCancellation?.Dispose();offlineCancellation=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);var token=offlineCancellation.Token;
        string? source=Settings.LibraryPath;
        long request=++offlineVersion;
        offlineTask=Task.Run(async()=>
        {
            try
            {
                if(offlineEngineFactory is null && !offlineInstallation.ReadyFor(OfflineSourceLanguage)){OfflineReady=false;throw new InvalidDataException("Offline engine not prepared");}
                var flow=new OfflineTranslationWorkflow(store.Root,TextLanguageDetector.Detect,()=>Settings.LibraryPath==source);flow.Enqueue(selected);
                await using var engine=offlineEngineFactory?.Invoke()??new ArgosEngine(offlineInstallation);
                await flow.RunAsync(engine,new OfflineProgressSink<OfflineTranslationProgress>(p=>dispatcher.BeginInvoke(()=>
                {
                    if(disposed||request!=offlineVersion)return;OfflineProgress=p;OfflineMessage=OfflineSummary;
                    OfflineFailures=string.Join("\n",p.Failures.Select(i=>i.Track.Title+" · "+UiText.T(i.Detail??"机翻失败或歌词组过长，请重试。")));
                    OfflineChanged?.Invoke();if(!p.Running||DateTime.UtcNow-offlineLastUiRefresh>TimeSpan.FromMilliseconds(500)){offlineLastUiRefresh=DateTime.UtcNow;InvalidateWorkspace();RefreshTranslation();Updated?.Invoke();}
                })),token);
                ReportOffline("本轮离线机翻完成。失败项目可重新选择生成。",request);
            }
            catch(OperationCanceledException){ReportOffline("离线翻译已暂停，可继续未完成歌曲。",request);}
            catch(Exception e){store.Log("Offline translation: "+e.GetType().Name);ReportOffline("离线翻译未完成，请检查组件或重新选择歌曲。",request);}
        },token);NotifyOfflineCompletion(offlineTask,request);OfflineChanged?.Invoke();
    }
    async Task StopOfflineAsync(){offlineCancellation?.Cancel();if(offlineTask is not null)try{await offlineTask;}catch(OperationCanceledException){}offlineCancellation?.Dispose();}
}
