using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

public sealed class TranslationJobListPanel:StackPanel,IDisposable
{
    readonly PlaybackCoordinator coordinator;readonly ListBox songs=new(){SelectionMode=SelectionMode.Extended,Height=170,DisplayMemberPath="Label"};
    readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Margin=new(0,8,0,8)};
    readonly CheckBox prefer;readonly Button all,selected,replace,current,open;readonly CancellationTokenSource stop=new();
    string? entry;bool busy;DateTime nextStatusRefresh;sealed record Row(Track Track,bool HasTranslation,string Label);
    public TranslationJobListPanel(PlaybackCoordinator coordinator)
    {
        this.coordinator=coordinator;
        Children.Add(new TextBlock{Text="本地 agent · 批量中文译文",FontSize=15,Margin=new(0,18,0,8)});
        prefer=DialogUi.Check(this,"优先查找现有来源译文",true);
        AutomationProperties.SetName(songs,"批量翻译歌曲选择");VirtualizingPanel.SetIsVirtualizing(songs,true);VirtualizingPanel.SetVirtualizationMode(songs,VirtualizationMode.Recycling);Children.Add(songs);
        var actions=new WrapPanel();Children.Add(actions);
        all=DialogUi.Button(actions,"全部缺失：生成批量任务",()=>_=Generate(false,true));
        selected=DialogUi.Button(actions,"为所选歌曲生成任务",()=>_=Generate(false,false));
        replace=DialogUi.Button(actions,"重新制作所选译文…",()=>_=Generate(true,false));
        current=DialogUi.Button(actions,"当前歌曲制作 / 重译…",()=>_=GenerateCurrent());
        open=DialogUi.Button(actions,"打开批量入口 MD",()=>{if(entry is not null)Process.Start(new ProcessStartInfo(entry){UseShellExecute=true});});open.IsEnabled=false;
        DialogUi.Button(actions,"复制任务路径",()=>{if(entry is not null)System.Windows.Clipboard.SetText(entry);});
        DialogUi.Button(actions,"打开任务文件夹",()=>{if(entry is not null)Process.Start(new ProcessStartInfo(Path.GetDirectoryName(entry)!){UseShellExecute=true});});
        DialogUi.Button(actions,"刷新 / 接收结果",()=>{coordinator.RefreshTranslationJobs();LoadSongs();});
        DialogUi.Button(actions,"取消正在准备",()=>operation?.Cancel());
        DialogUi.Button(actions,"恢复当前歌曲上一版译文",()=>_=Restore());
        DialogUi.Button(actions,"采用当前歌曲的部分稿…",()=>_=Approve());
        DialogUi.Button(actions,"取消当前歌曲未完成任务",CancelCurrent);
        Children.Add(status);ThemeManager.Bind(status,TextBlock.ForegroundProperty,"MutedBrush");
        Loaded+=(_,_)=>{coordinator.Updated+=Refresh;LoadSongs();Refresh();};Unloaded+=(_,_)=>coordinator.Updated-=Refresh;
    }
    CancellationTokenSource? operation;
    void LoadSongs()
    {
        if(busy)return;
        try{entry??=TranslationBatchExchange.LatestEntry(coordinator.DataRoot);open.IsEnabled=entry is not null;using var lyrics=new LyricsRepository(Path.Combine(coordinator.DataRoot,"lyrics.db"),true);using var translations=new TranslationRepository(coordinator.DataRoot,true);
            songs.ItemsSource=coordinator.TranslationLibraryTracks().Select(item=>{var b=lyrics.FindReadOnly(item.Track);var d=b is null?null:new LrcParser().Parse(b.Raw,b.Plain) with{Instrumental=b.Instrumental};bool exists=d is not null&&translations.Find(item.Track,d) is not null;return new Row(item.Track,exists,$"{item.Track.Artist} — {item.Track.Title} · {(exists?"已有译文":"缺失 / 原词待确认")}");}).ToArray();}
        catch(Exception e) when(e is IOException or InvalidDataException or Microsoft.Data.Sqlite.SqliteException){status.Text=e.Message;}
    }
    void Refresh()
    {
        if(busy||DateTime.UtcNow<nextStatusRefresh)return;nextStatusRefresh=DateTime.UtcNow.AddSeconds(2);
        try{status.Text=coordinator.TranslationJobMessage+"\n"+TranslationJobExchange.Summary(coordinator.DataRoot);}
        catch(Exception e) when(e is IOException or Microsoft.Data.Sqlite.SqliteException){status.Text=e.Message;}
    }
    async Task Generate(bool retranslate,bool missing)
    {
        var rows=(missing?songs.Items.Cast<Row>().Where(r=>!r.HasTranslation):songs.SelectedItems.Cast<Row>()).ToArray();
        if(rows.Length==0){status.Text="请选择歌曲，或先确认资料库已有动态原词。";return;}
        if(retranslate&&MessageBox.Show(Window.GetWindow(this),$"为所选 {rows.Length} 首重新制作：完成后替换各首当前译文，保留旧版本；部分稿不自动替换。","批量重译",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;
        await Run(rows.Select(r=>new TranslationBatchSelection(r.Track,retranslate&&r.HasTranslation?TranslationJobMode.ReplaceExpected:TranslationJobMode.FillMissing)).ToArray(),retranslate?false:prefer.IsChecked==true);
    }
    async Task GenerateCurrent()
    {
        if(coordinator.ActiveTrack is not { } track)return;
        bool replace=coordinator.Translation is not null;
        if(replace&&MessageBox.Show(Window.GetWindow(this),"完成后替换当前歌曲中文译文，原版本保留。","重新制作",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;
        // Current songs need not be library members; single-song export is independently supported.
        if(busy)return;bool lookupCurrent=prefer.IsChecked==true;SetBusy(true);try{var ticket=await Task.Run(async()=>{if(!replace&&lookupCurrent&&coordinator.Settings.OnlineLookup){using var provider=new NetEaseTranslationProvider();return (await new TranslationWorkflow(coordinator.DataRoot,provider).PrepareAsync(track,true,true,stop.Token)).Ticket;}return TranslationJobExchange.Create(coordinator.DataRoot,track,replace?TranslationJobMode.ReplaceExpected:TranslationJobMode.FillMissing);},stop.Token);if(ticket is null){coordinator.RefreshTranslationJobs();status.Text="已有来源译文可用。";return;}entry=ticket.TaskPath;open.IsEnabled=true;status.Text="任务已生成："+entry;}catch(Exception e){status.Text=e.Message;}finally{SetBusy(false);}
    }
    async Task Run(TranslationBatchSelection[] selections,bool lookup)
    {
        if(busy)return;SetBusy(true);operation?.Dispose();operation=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        try{status.Text="正在检查来源并生成批量任务…";var result=await coordinator.CreateTranslationBatchAsync(selections,lookup,operation.Token);entry=result.Batch?.EntryPath;open.IsEnabled=entry is not null;status.Text=$"已生成 {result.Tickets.Count} 首；跳过 {result.Skipped.Count} 首。\n"+(entry is null?"已有译文或没有可靠动态原词。":$"只需把此 MD 交给 agent：{entry}")+string.Join("",result.Skipped.Take(5).Select(s=>$"\n{s.Track.Title}：{s.Message}"));}
        catch(OperationCanceledException){status.Text="准备已取消，已生成任务保留。";}catch(Exception e){status.Text=e.Message;}finally{SetBusy(false);}
    }
    void SetBusy(bool value){busy=value;all.IsEnabled=selected.IsEnabled=replace.IsEnabled=current.IsEnabled=!value;}
    async Task Restore()
    {
        try{if(coordinator.ActiveTrack is not { } t||coordinator.Document is not { } d)return;using var repo=new TranslationRepository(coordinator.DataRoot,true);var active=repo.FindActive(t,d);var old=repo.ReadHistory(t,TranslationAlignment.Hash(d.Raw)).FirstOrDefault();if(active is null||old is null){status.Text="没有可恢复的旧版本。";return;}await TranslationJobExchange.RestoreHistoryAsync(coordinator.DataRoot,t,TranslationAlignment.Hash(d.Raw),old.Revision,active.Revision,stop.Token);coordinator.RefreshTranslationJobs();}
        catch(Exception e){status.Text=e.Message;}
    }
    async Task Approve()
    {
        try{if(coordinator.ActiveTrack is not { } t||coordinator.Document is not { } d)return;var job=TranslationJobExchange.ReadJobs(coordinator.DataRoot).FirstOrDefault(j=>j.Ticket.Request.Target.Track.Fingerprint==t.Fingerprint&&j.Receipt.State==TranslationJobState.PartialReady);if(job is null)return;using var repo=new TranslationRepository(coordinator.DataRoot,true);var active=repo.FindActive(t,d);if(active is null)return;if(MessageBox.Show(Window.GetWindow(this),"采用有未译行的部分稿？当前完整译文将保留在历史中。","采用部分译稿",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;await TranslationJobExchange.ApprovePartialAsync(coordinator.DataRoot,job.Ticket.Request.JobId,active.Revision,stop.Token);coordinator.RefreshTranslationJobs();}catch(Exception e){status.Text=e.Message;}
    }
    void CancelCurrent(){if(coordinator.ActiveTrack is not { } t)return;foreach(var j in TranslationJobExchange.ReadJobs(coordinator.DataRoot).Where(j=>j.Ticket.Request.Target.Track.Fingerprint==t.Fingerprint))TranslationJobExchange.Cancel(coordinator.DataRoot,j.Ticket.Request.JobId);Refresh();}
    public void Dispose(){stop.Cancel();operation?.Cancel();operation?.Dispose();coordinator.Updated-=Refresh;stop.Dispose();}
}
