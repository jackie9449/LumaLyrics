using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

public sealed class LibraryTasksPanel:UserControl,IDisposable
{
    readonly PlaybackCoordinator coordinator;
    readonly ListBox batches=new(),songs=new();
    readonly TextBlock status=LibraryWorkspaceUi.Text(UiText.T("正在读取任务…"),11),summary=LibraryWorkspaceUi.Text(UiText.T("请选择一个任务"),13,false),path=LibraryWorkspaceUi.Text(UiText.T("任务说明会在这里显示。"),11),empty=LibraryWorkspaceUi.Text(UiText.T("还没有任务。到歌曲页选择歌曲，再生成本地任务。"),13);
    readonly Button copyPrompt,open;readonly Grid body=new();readonly Border songCard;readonly StackPanel heading;readonly ScrollViewer feedback;
    readonly CancellationTokenSource stop=new();CancellationTokenSource? reading;long readVersion;bool readActive,rerun,copying,disposed;string? revision;
    readonly System.Windows.Threading.DispatcherTimer reloadDelay=new(){Interval=TimeSpan.FromMilliseconds(250)};
    internal ListBox BatchList=>batches;
    internal ListBox TaskSongs=>songs;
    internal Button CopyPromptButton=>copyPrompt;
    internal Action<string> CopyText {get;set;}=Clipboard.SetText;
    public LibraryTasksPanel(PlaybackCoordinator coordinator)
    {
        this.coordinator=coordinator;var root=new Grid();Content=root;root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){MinHeight=96});root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        heading=LibraryWorkspaceUi.Heading(UiText.T("本地翻译任务"),UiText.T("选择任务，复制提示词，再粘贴给能读写本机文件的 agent。"));root.Children.Add(heading);
        var actions=new WrapPanel{Margin=new(0,0,0,6)};Grid.SetRow(actions,1);root.Children.Add(actions);
        copyPrompt=LibraryWorkspaceUi.Button(actions,UiText.T("复制翻译提示词"),()=>_=CopyPrompt(),true);open=LibraryWorkspaceUi.Button(actions,UiText.T("打开任务说明"),Open);LibraryWorkspaceUi.Button(actions,UiText.T("刷新任务"),()=>_=Reload());
        var more=LibraryWorkspaceUi.Button(actions,UiText.T("更多…"),()=>{});var menu=new ContextMenu{PlacementTarget=more};menu.SetResourceReference(StyleProperty,"LibraryActionMenu");menu.ItemContainerStyle=(Style)FindResource("LibraryActionMenuItem");
        void Action(string label,Action action){var item=new MenuItem{Header=label};item.Click+=(_,_)=>action();menu.Items.Add(item);}
        Action(UiText.T("复制入口路径"),()=>{if(batches.SelectedItem is LibraryTaskBatch{EntryPath:{} value})CopyText(value);});Action(UiText.T("仅重新检查所选批次失败项"),()=>_=Retry());more.ContextMenu=menu;more.Click+=(_,_)=>menu.IsOpen=true;
        Grid.SetRow(body,2);root.Children.Add(body);body.ColumnDefinitions.Add(new());body.ColumnDefinitions.Add(new());body.RowDefinitions.Add(new());body.RowDefinitions.Add(new());
        batches.ItemTemplate=LibraryWorkspaceUi.BatchTemplate(true);songs.ItemTemplate=LibraryWorkspaceUi.BatchTemplate();
        foreach(var list in new[]{batches,songs}){VirtualizingPanel.SetIsVirtualizing(list,true);VirtualizingPanel.SetVirtualizationMode(list,VirtualizationMode.Recycling);ScrollViewer.SetCanContentScroll(list,true);ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled);}
        var listGrid=new Grid();listGrid.Children.Add(batches);empty.Margin=new(12);empty.VerticalAlignment=VerticalAlignment.Center;empty.TextAlignment=TextAlignment.Center;listGrid.Children.Add(empty);body.Children.Add(listGrid);
        var detail=new Grid();detail.RowDefinitions.Add(new(){Height=GridLength.Auto});detail.RowDefinitions.Add(new());detail.RowDefinitions.Add(new(){Height=GridLength.Auto});summary.Margin=new(0,0,0,10);detail.Children.Add(summary);Grid.SetRow(songs,1);detail.Children.Add(songs);path.Margin=new(0,10,0,0);path.TextTrimming=TextTrimming.CharacterEllipsis;path.MaxHeight=38;Grid.SetRow(path,2);detail.Children.Add(path);songCard=LibraryWorkspaceUi.Card(detail,14);body.Children.Add(songCard);
        feedback=new ScrollViewer{Content=status,MaxHeight=48,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Margin=new(0,8,0,0)};Grid.SetRow(feedback,3);root.Children.Add(feedback);
        batches.SelectionChanged+=(_,_)=>SelectTask();SizeChanged+=(_,_)=>Arrange();reloadDelay.Tick+=(_,_)=>{reloadDelay.Stop();_=Reload();};
        Loaded+=(_,_)=>{if(disposed)return;coordinator.Updated+=Updated;_=Reload();};Unloaded+=(_,_)=>{coordinator.Updated-=Updated;reloadDelay.Stop();if(!disposed)reading?.Cancel();};SelectTask();Arrange();
    }
    void Arrange()
    {
        bool wide=ActualWidth>=680,compact=ActualHeight<360;heading.Visibility=compact?Visibility.Collapsed:Visibility.Visible;summary.Visibility=compact?Visibility.Collapsed:Visibility.Visible;path.Visibility=compact?Visibility.Collapsed:Visibility.Visible;feedback.MaxHeight=compact?20:48;songCard.Padding=new Thickness(compact?8:14);body.ColumnDefinitions[0].Width=new(1,GridUnitType.Star);body.ColumnDefinitions[1].Width=wide?new(1.15,GridUnitType.Star):new(0);body.RowDefinitions[0].Height=new(wide?1:0.8,GridUnitType.Star);body.RowDefinitions[1].Height=wide?new(0):new(1,GridUnitType.Star);Grid.SetColumn(songCard,wide?1:0);Grid.SetRow(songCard,wide?0:1);songCard.Margin=wide?new(14,0,0,0):new(0,10,0,0);
    }
    void SelectTask()
    {
        if(batches.SelectedItem is not LibraryTaskBatch task){summary.Text=UiText.T("任务详情");path.Text=UiText.T("选择一个批次，查看每首歌曲的处理状态。");songs.ItemsSource=null;copyPrompt.IsEnabled=open.IsEnabled=false;return;}
        summary.Text=LibraryBatchTitleConverter.Title(task)+"\n"+UiText.Status(task.Summary);path.Text=task.UnavailableReason is {} reason?UiText.Status(reason):(task.EntryPath is {} entry?UiText.T("任务说明 · ")+Path.GetFileName(entry):UiText.T("任务说明不可用，请重新生成。"));path.ToolTip=task.EntryPath??(task.UnavailableReason is {} unavailable?UiText.Status(unavailable):null);
        songs.ItemsSource=task.Jobs.Select(j=>new TaskRow(j.Ticket.Request.Target.Track.Title,j.Ticket.Request.Target.Track.Artist,State(j.Receipt.State)+(j.Receipt.Message==j.Receipt.State.ToString()?"":" · "+UiText.Status(j.Receipt.Message)))).ToArray();copyPrompt.IsEnabled=!copying&&task.EntryPath is not null&&task.Jobs.Count>0;open.IsEnabled=task.EntryPath is not null;
    }
    static string State(TranslationJobState state)=>state switch{TranslationJobState.Applied=>UiText.T("已接收"),TranslationJobState.AppliedPartial=>UiText.T("已接收部分稿"),TranslationJobState.AwaitingResult=>UiText.T("等待制作"),TranslationJobState.PartialReady=>UiText.T("部分稿待采用"),TranslationJobState.Cancelled=>UiText.T("已取消"),TranslationJobState.Conflict=>UiText.T("译文已变化"),TranslationJobState.Stale=>UiText.T("任务已过期"),TranslationJobState.Rejected=>UiText.T("需要修正"),_=>UiText.T("正在准备")};
    sealed record TaskRow(string Title,string Summary,string Availability);
    void Open(){if(batches.SelectedItem is LibraryTaskBatch{EntryPath:{} value}&&File.Exists(value))Process.Start(new ProcessStartInfo(value){UseShellExecute=true});}
    async Task CopyPrompt()
    {
        if(copying||batches.SelectedItem is not LibraryTaskBatch task)return;copying=true;copyPrompt.IsEnabled=false;string id=task.Id;
        try{string text=await Task.Run(()=>LocalAgentTranslationPrompt.Create(coordinator.DataRoot,id,stop.Token),stop.Token);if(disposed||!IsLoaded||(batches.SelectedItem as LibraryTaskBatch)?.Id!=id)return;CopyText(text);status.Text=UiText.T("提示词已复制。粘贴给本地 agent，即可按任务说明开始或继续制作。");}
        catch(OperationCanceledException){}catch(Exception e){if(!disposed)status.Text=UiText.T("未能复制：")+UiText.Status(e.Message);}finally{copying=false;if(!disposed)copyPrompt.IsEnabled=batches.SelectedItem is LibraryTaskBatch{EntryPath:not null,Jobs.Count:>0};}
    }
    void Updated(){string stamp=coordinator.TranslationJobMessage+coordinator.WorkspaceMessage;if(stamp==revision)return;revision=stamp;reloadDelay.Stop();reloadDelay.Start();}
    internal async Task Reload()
    {
        if(disposed)return;if(readActive){rerun=true;return;}readActive=true;rerun=false;reading?.Cancel();reading?.Dispose();reading=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);var token=reading.Token;long request=++readVersion;string? source=coordinator.Settings.LibraryPath;
        try{var rows=await Task.Run(()=>LibraryTaskCatalog.Read(coordinator.DataRoot,token),token);if(disposed||token.IsCancellationRequested||request!=readVersion||source!=coordinator.Settings.LibraryPath)return;string? id=(batches.SelectedItem as LibraryTaskBatch)?.Id;batches.ItemsSource=rows;batches.SelectedItem=id is null?rows.FirstOrDefault(b=>b.EntryPath is not null)??rows.FirstOrDefault():rows.FirstOrDefault(b=>b.Id==id);empty.Visibility=rows.Count==0?Visibility.Visible:Visibility.Collapsed;SelectTask();status.Text=UiText.Status(coordinator.TranslationJobMessage);}
        catch(OperationCanceledException){}catch(Exception e){if(!disposed&&request==readVersion)status.Text=UiText.T("读取暂不可用：")+UiText.Status(e.Message);}
        finally{readActive=false;if(rerun&&!disposed&&IsLoaded){reloadDelay.Stop();reloadDelay.Start();}}
    }
    async Task Retry()
    {
        try{if(batches.SelectedItem is not LibraryTaskBatch task)return;var failed=task.Jobs.Where(j=>j.Ticket.Request.Mode==TranslationJobMode.FillMissing&&j.Receipt.State is TranslationJobState.Rejected or TranslationJobState.Conflict or TranslationJobState.Stale).ToArray();if(failed.Length==0){status.Text=UiText.T("没有可安全重试的补缺任务；重译或部分稿请到歌曲页明确处理。");return;}var snapshot=await coordinator.ReadWorkspaceAsync(stop.Token);var picked=snapshot.Songs.Where(s=>failed.Any(j=>j.Ticket.Request.Target.LibrarySource==s.Key.Source&&j.Ticket.Request.Target.PersistentId==s.Key.PersistentId)).ToArray();await coordinator.PrepareWorkspaceAsync(picked,false,true);if(!disposed)await Reload();}catch(Exception e){if(!disposed)status.Text=UiText.Status(e.Message);}
    }
    public void Dispose(){if(disposed)return;disposed=true;coordinator.Updated-=Updated;reloadDelay.Stop();stop.Cancel();reading?.Cancel();reading?.Dispose();reading=null;stop.Dispose();}
}
