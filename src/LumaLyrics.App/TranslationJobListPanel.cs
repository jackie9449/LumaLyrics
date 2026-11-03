using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
using Microsoft.Win32;
namespace LumaLyrics.App;

public sealed class TranslationJobListPanel:UserControl,IDisposable
{
    readonly PlaybackCoordinator coordinator;
    readonly ListBox songs=new(){SelectionMode=SelectionMode.Extended,DisplayMemberPath="Label",HorizontalContentAlignment=HorizontalAlignment.Stretch};
    readonly TextBox search=new(){MinWidth=100};
    readonly ComboBox filter=new(){ItemsSource=new[]{"全部","缺动态原词","缺中文译文","部分译文","原词已更新"},SelectedIndex=0,MinWidth=120};
    readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap},detail=new(){TextWrapping=TextWrapping.Wrap};
    readonly CheckBox prefer,artwork;readonly HashSet<LibrarySongKey> selection=new(LibrarySongKey.Comparer);
    readonly CancellationTokenSource stop=new();CancellationTokenSource? reading;long version;bool loading,disposed;
    LibraryManagedSong[] rows=[];string? entry,lastRevision;bool lastBusy;
    internal ListBox SongList=>songs;
    internal IReadOnlyList<LibraryManagedSong> SelectedSongs=>rows.Where(r=>selection.Contains(Key(r))).ToArray();
    internal CheckBox PreferExisting=>prefer;
    internal string StatusText=>status.Text;
    static LibrarySongKey Key(LibraryManagedSong row)=>row.Key;
    public TranslationJobListPanel(PlaybackCoordinator coordinator)
    {
        this.coordinator=coordinator;var root=new Grid();Content=root;
        root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){MinHeight=48});root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var actions=new WrapPanel{Margin=new(0,8,0,8)};root.Children.Add(actions);
        var menu=new ContextMenu();menu.SetResourceReference(StyleProperty,"LibraryActionMenu");menu.ItemContainerStyle=(Style)FindResource("LibraryActionMenuItem");ThemeManager.Bind(menu,Control.BackgroundProperty,"ControlBrush");ThemeManager.Bind(menu,Control.ForegroundProperty,"TextBrush");
        var options=new StackPanel();prefer=DialogUi.Check(options,"优先查找现有来源译文",true);artwork=DialogUi.Check(options,"同时补封面",false);menu.Items.Add(new MenuItem{Header=options,StaysOpenOnClick=true});
        void Action(string label,Action action){var item=new MenuItem{Header=label};item.Click+=(_,_)=>action();menu.Items.Add(item);}
        DialogUi.Button(actions,"准备所选原词与译文",()=>_=Prepare(false,true));
        DialogUi.Button(actions,"直接生成本地任务",()=>_=Prepare(false,false,false));
        var more=DialogUi.Button(actions,"更多操作…",()=>{menu.IsOpen=true;});more.ContextMenu=menu;menu.PlacementTarget=more;
        Action("仅查现有中文",()=>_=Prepare(false,true,false,false));
        Action("重新制作所选译文…",()=>_=Prepare(true,false,false));
        Action("暂停准备",coordinator.PauseWorkspace);
        Action("打开批量入口 MD",()=>Open(entry));
        Action("复制任务路径",()=>{if(entry is not null)Clipboard.SetText(entry);});
        Action("刷新 / 接收结果",()=>{coordinator.RefreshTranslationJobs();_=Reload();});
        Action("导入所选歌曲中文 LRC…",()=>_=Import());
        Action("恢复所选歌曲上一版",()=>_=Restore());
        Action("采用所选歌曲部分稿…",()=>_=Approve());
        Action("取消所选歌曲任务",CancelSelected);
        var filters=new DockPanel{Margin=new(0,0,0,8)};Grid.SetRow(filters,1);root.Children.Add(filters);
        DockPanel.SetDock(filter,Dock.Right);filters.Children.Add(filter);filters.Children.Add(search);AutomationProperties.SetName(search,"搜索资料库与歌词");
        search.TextChanged+=(_,_)=>ApplyFilter();filter.SelectionChanged+=(_,_)=>ApplyFilter();
        VirtualizingPanel.SetIsVirtualizing(songs,true);VirtualizingPanel.SetVirtualizationMode(songs,VirtualizationMode.Recycling);ScrollViewer.SetCanContentScroll(songs,true);ScrollViewer.SetHorizontalScrollBarVisibility(songs,ScrollBarVisibility.Disabled);AutomationProperties.SetName(songs,"统一资料库歌曲选择");Grid.SetRow(songs,2);root.Children.Add(songs);
        songs.SelectionChanged+=(_,_)=>{if(loading)return;foreach(var r in songs.Items.Cast<LibraryManagedSong>()){if(songs.SelectedItems.Contains(r))selection.Add(Key(r));else selection.Remove(Key(r));}UpdateDetail();};
        var details=new StackPanel{Margin=new(0,8,0,0)};Grid.SetRow(details,3);root.Children.Add(details);details.Children.Add(detail);
        var tools=new WrapPanel();details.Children.Add(tools);
        DialogUi.Button(tools,"全选当前筛选",()=>{foreach(var r in songs.Items.Cast<LibraryManagedSong>())selection.Add(Key(r));ApplyFilter();});
        DialogUi.Button(tools,"清空选择",()=>{selection.Clear();ApplyFilter();});
        var statusScroll=new ScrollViewer{Content=status,MaxHeight=64,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetRow(statusScroll,4);status.Margin=new(0,8,0,0);root.Children.Add(statusScroll);ThemeManager.Bind(status,TextBlock.ForegroundProperty,"MutedBrush");ThemeManager.Bind(detail,TextBlock.ForegroundProperty,"MutedBrush");
        SizeChanged+=(_,_)=>{statusScroll.MaxHeight=ActualHeight<340?32:64;detail.Visibility=ActualHeight<300?Visibility.Collapsed:Visibility.Visible;};
        Loaded+=(_,_)=>{if(disposed)return;coordinator.Updated+=Updated;_=Reload();};Unloaded+=(_,_)=>{coordinator.Updated-=Updated;if(!disposed)reading?.Cancel();};
    }
    static void Open(string? path){if(path is not null&&File.Exists(path))Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}
    void Updated(){status.Text=coordinator.WorkspaceMessage;string stamp=coordinator.TranslationJobMessage+coordinator.WorkspaceMessage+coordinator.LibraryStatus+coordinator.ArtworkStatus+coordinator.Settings.LibraryPath;if(lastBusy!=coordinator.WorkspaceBusy||lastRevision!=stamp){lastBusy=coordinator.WorkspaceBusy;lastRevision=stamp;if(!lastBusy)_=Reload();}}
    internal async Task Reload()
    {
        if(disposed)return;reading?.Cancel();reading?.Dispose();reading=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);var token=reading.Token;long request=++version;string? source=coordinator.Settings.LibraryPath;
        try{var snapshot=await coordinator.ReadWorkspaceAsync(token);if(token.IsCancellationRequested||request!=version||source!=coordinator.Settings.LibraryPath)return;rows=snapshot.Songs.ToArray();selection.IntersectWith(rows.Select(Key));entry=await Task.Run(()=>TranslationBatchExchange.LatestEntry(coordinator.DataRoot),token);ApplyFilter();status.Text=coordinator.WorkspaceMessage+"\n"+snapshot.Message;}
        catch(OperationCanceledException){}catch(Exception e){if(!disposed&&request==version)status.Text="读取暂不可用，保留上次清单："+e.Message;}
    }
    void ApplyFilter()
    {
        if(songs is null)return;var terms=search.Text.Split(' ',StringSplitOptions.RemoveEmptyEntries);
        var shown=rows.Where(r=>terms.All(t=>(r.Track.Title+" "+r.Track.Artist+" "+r.Track.Album).Contains(t,StringComparison.CurrentCultureIgnoreCase))&&(filter.SelectedIndex switch{1=>r.Status.Original is not (LibraryOriginalState.Ready or LibraryOriginalState.Instrumental),2=>r.Status.Chinese==LibraryChineseState.Missing,3=>r.Status.Chinese==LibraryChineseState.Partial,4=>r.Status.Chinese==LibraryChineseState.Outdated,_=>true})).ToArray();
        loading=true;try{songs.ItemsSource=shown;foreach(var r in shown)if(selection.Contains(Key(r)))songs.SelectedItems.Add(r);}finally{loading=false;}UpdateDetail();
    }
    void UpdateDetail(){var picked=rows.Where(r=>selection.Contains(Key(r))).ToArray();detail.Text=$"已选 {picked.Length} 首 · 当前可见 {songs.Items.Count} 首 · 共 {rows.Length} 首"+(picked.Length==1?$"\n{picked[0].Track.Title} · {picked[0].Status.OriginalLabel} · {picked[0].Status.ChineseLabel}":"");}
    internal void SelectKeys(IEnumerable<LibrarySongKey> keys){selection.Clear();foreach(var key in keys)selection.Add(key);ApplyFilter();}
    async Task Prepare(bool replace,bool lookup,bool originals=true,bool export=true)
    {
        var picked=rows.Where(r=>selection.Contains(Key(r))).ToArray();if(picked.Length==0){status.Text="请先选择歌曲。";return;}
        if(replace&&MessageBox.Show(Window.GetWindow(this),$"重新制作所选 {picked.Length} 首，替换通过校验的当前译文，旧版保留。","重新制作",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;
        try{await coordinator.PrepareWorkspaceAsync(picked,replace,lookup&&prefer.IsChecked==true,originals,export,originals&&artwork.IsChecked==true);if(!disposed)await Reload();}catch(Exception e){status.Text=e.Message;}
    }
    LibraryManagedSong? Single(){var picked=rows.Where(r=>selection.Contains(Key(r))).ToArray();if(picked.Length!=1){status.Text="此操作请选择一首歌曲。";return null;}return picked[0];}
    async Task WithTarget(Func<LibraryManagedSong,Task> action){try{var row=Single();if(row is not null){await action(row);coordinator.RefreshTranslationJobs();await Reload();}}catch(Exception e){status.Text=e.Message;}}
    Task Import()=>WithTarget(async row=>
    {
        var dialog=new OpenFileDialog{Filter="中文歌词|*.lrc"};if(dialog.ShowDialog(Window.GetWindow(this))!=true)return;
        if(new FileInfo(dialog.FileName).Length>2*1024*1024)throw new InvalidDataException("中文歌词不能超过 2 MB。");string raw=await File.ReadAllTextAsync(dialog.FileName,new System.Text.UTF8Encoding(false,true),stop.Token);
        await Task.Run(()=>LibrarySongActions.Import(coordinator.DataRoot,row.Target,raw),stop.Token);
    });
    Task Restore()=>WithTarget(async row=>{using var tr=new TranslationRepository(coordinator.DataRoot,true);var old=tr.ReadHistory(row.Track,row.Status.SourceHash??"").FirstOrDefault();if(old is null)throw new InvalidDataException("没有旧版本。");LibrarySongActions.Check(coordinator.DataRoot,row.Target);if(!await TranslationJobExchange.RestoreHistoryAsync(coordinator.DataRoot,row.Track,row.Status.SourceHash!,old.Revision,row.Status.Revision!,stop.Token,row.Target))throw new InvalidDataException("资料已变化，未恢复。");});
    Task Approve()=>WithTarget(async row=>{var job=TranslationJobExchange.ReadJobs(coordinator.DataRoot).FirstOrDefault(j=>j.Ticket.Request.Target.LibrarySource==row.Key.Source&&j.Ticket.Request.Target.PersistentId==row.Key.PersistentId&&j.Receipt.State==TranslationJobState.PartialReady)??throw new InvalidDataException("没有待采用的部分稿。");if(MessageBox.Show(Window.GetWindow(this),"采用部分稿？旧译文保留在历史中。","部分稿",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;LibrarySongActions.Check(coordinator.DataRoot,row.Target);await TranslationJobExchange.ApprovePartialAsync(coordinator.DataRoot,job.Ticket.Request.JobId,row.Status.Revision!,stop.Token,row.Target);});
    void CancelSelected(){try{var keys=rows.Where(r=>selection.Contains(Key(r))).Select(r=>r.Key).ToHashSet(LibrarySongKey.Comparer);foreach(var job in TranslationJobExchange.ReadJobs(coordinator.DataRoot).Where(j=>keys.Contains(new(j.Ticket.Request.Target.LibrarySource??"",j.Ticket.Request.Target.PersistentId??""))))TranslationJobExchange.Cancel(coordinator.DataRoot,job.Ticket.Request.JobId);coordinator.RefreshTranslationJobs();}catch(Exception e){status.Text=e.Message;}}
    public void Dispose(){if(disposed)return;disposed=true;coordinator.Updated-=Updated;stop.Cancel();reading?.Cancel();reading?.Dispose();reading=null;stop.Dispose();}
}
