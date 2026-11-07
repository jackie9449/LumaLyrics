using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;
public sealed class LibraryTasksPanel:UserControl,IDisposable
{
    readonly PlaybackCoordinator coordinator;readonly ListBox batches=new(){DisplayMemberPath="Label"};readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};readonly TextBox detail=new(){IsReadOnly=true,Height=130,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};readonly CancellationTokenSource stop=new();bool disposed;string? revision;CancellationTokenSource? reading;long readVersion;bool readActive,rerun;readonly System.Windows.Threading.DispatcherTimer reloadDelay=new(){Interval=TimeSpan.FromMilliseconds(250)};
    public LibraryTasksPanel(PlaybackCoordinator coordinator)
    {
        this.coordinator=coordinator;reloadDelay.Tick+=(_,_)=>{reloadDelay.Stop();_=Reload();};var root=new Grid();Content=root;root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new());root.RowDefinitions.Add(new(){Height=GridLength.Auto});var actions=new WrapPanel();root.Children.Add(actions);
        DialogUi.Button(actions,"刷新任务",()=>_=Reload());DialogUi.Button(actions,"打开所选批量 MD",()=>Open());DialogUi.Button(actions,"复制入口路径",()=>{if(batches.SelectedItem is LibraryTaskBatch{EntryPath:{} path})Clipboard.SetText(path);});
        DialogUi.Button(actions,"仅重新检查所选批次失败项",()=>_=Retry());
        Grid.SetRow(batches,1);root.Children.Add(batches);VirtualizingPanel.SetIsVirtualizing(batches,true);VirtualizingPanel.SetVirtualizationMode(batches,VirtualizationMode.Recycling);var details=new StackPanel();Grid.SetRow(details,2);root.Children.Add(details);details.Children.Add(detail);details.Children.Add(new ScrollViewer{Content=status,MaxHeight=64,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});batches.SelectionChanged+=(_,_)=>{detail.Text=batches.SelectedItem is LibraryTaskBatch batch?string.Join("\n",batch.Jobs.Select(j=>j.Ticket.Request.Target.Track.Title+" · "+j.Receipt.State+" · "+j.Receipt.Message)):"";};ThemeManager.Bind(status,TextBlock.ForegroundProperty,"MutedBrush");
        Loaded+=(_,_)=>{if(disposed)return;coordinator.Updated+=Updated;_=Reload();};Unloaded+=(_,_)=>{coordinator.Updated-=Updated;reloadDelay.Stop();if(!disposed)reading?.Cancel();};
    }
    void Open(){if(batches.SelectedItem is LibraryTaskBatch{EntryPath:{} path})Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}
    void Updated(){string stamp=coordinator.TranslationJobMessage+coordinator.WorkspaceMessage;if(stamp==revision)return;revision=stamp;status.Text=coordinator.WorkspaceMessage;reloadDelay.Stop();reloadDelay.Start();}
    async Task Reload()
    {
        if(disposed)return;if(readActive){rerun=true;return;}readActive=true;rerun=false;reading?.Cancel();reading?.Dispose();reading=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);var token=reading.Token;long request=++readVersion;string? source=coordinator.Settings.LibraryPath;
        try{var rows=await Task.Run(()=>LibraryTaskCatalog.Read(coordinator.DataRoot,token),token);if(disposed||token.IsCancellationRequested||request!=readVersion||source!=coordinator.Settings.LibraryPath)return;string? id=(batches.SelectedItem as LibraryTaskBatch)?.Id;batches.ItemsSource=rows;batches.SelectedItem=rows.FirstOrDefault(b=>b.Id==id);status.Text=coordinator.WorkspaceMessage+"\n"+coordinator.TranslationJobMessage;}
        catch(OperationCanceledException){}catch(Exception e){if(!disposed&&request==readVersion)status.Text=e.Message;}
        finally{readActive=false;if(rerun&&!disposed&&IsLoaded){reloadDelay.Stop();reloadDelay.Start();}}
    }
    async Task Retry(){try{if(batches.SelectedItem is not LibraryTaskBatch batch)return;var failed=batch.Jobs.Where(j=>j.Ticket.Request.Mode==TranslationJobMode.FillMissing&&j.Receipt.State is TranslationJobState.Rejected or TranslationJobState.Conflict or TranslationJobState.Stale).ToArray();if(failed.Length==0){status.Text="没有可安全重试的补缺任务；重译或部分稿请在歌曲详情明确处理。";return;}var snapshot=await coordinator.ReadWorkspaceAsync(stop.Token);var picked=snapshot.Songs.Where(s=>failed.Any(j=>j.Ticket.Request.Target.LibrarySource==s.Key.Source&&j.Ticket.Request.Target.PersistentId==s.Key.PersistentId)).ToArray();await coordinator.PrepareWorkspaceAsync(picked,false,true);await Reload();}catch(Exception e){status.Text=e.Message;}}
    public void Dispose(){disposed=true;coordinator.Updated-=Updated;reloadDelay.Stop();stop.Cancel();reading?.Cancel();reading?.Dispose();reading=null;stop.Dispose();}
}
