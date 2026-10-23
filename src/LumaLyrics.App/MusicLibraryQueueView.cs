using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

internal sealed partial class MusicLibraryView
{
    readonly Func<CancellationToken,Task<MusicQueueSnapshot>>? readQueue;
    readonly Func<MusicLibrarySong,CancellationToken,Task<LibraryPlayResult>>? insertNext;
    readonly Func<MusicQueueSelection,CancellationToken,Task<LibraryPlayResult>>? playQueue;
    readonly Button queueTab=new(){Content="播放列表",Padding=new(14,8,14,8),Margin=new(0,0,6,0)};
    readonly DispatcherTimer queuePoll=new(){Interval=TimeSpan.FromSeconds(15)};
    readonly CancellationTokenSource queueLifetime=new();
    readonly RequestVersion queueVersions=new();
    CancellationTokenSource? queueReading;
    Task insertTail=Task.CompletedTask;
    FrameworkElement browseBody=null!,browseFilters=null!;
    PlaybackQueueView queuePanel=null!;
    bool showingQueue;
    internal bool IsQueueVisible=>showingQueue;
    internal PlaybackQueueView QueuePanel=>queuePanel;
    internal Task? ActiveQueueRefresh {get;private set;}
    void SetupQueueNavigation(Grid root,Grid heading,Grid filters,Grid body)
    {
        browseBody=body;browseFilters=filters;
        queuePanel=new(HideQueue,()=>ActiveQueueRefresh=RefreshQueueAsync(),play:playQueue,activate:activate);Grid.SetRow(queuePanel,2);root.Children.Add(queuePanel);
        queuePanel.PlaybackStarting+=()=>{playing?.Cancel();requests.Next();queueReading?.Cancel();queueVersions.Next();ActiveQueueRefresh=null;queuePanel.CancelLoading();};queuePanel.PlaybackStarted+=task=>ActivePlayback=task;queuePanel.PlaybackCompleted+=RefreshVisibleQueue;
        if(readQueue is not null)
        {
            heading.Children.OfType<StackPanel>().Single(p=>p!=headingCopy).Children.Insert(2,queueTab);
            AutomationProperties.SetName(queueTab,"查看当前播放列表");queueTab.Click+=(_,_)=>{if(showingQueue)HideQueue();else ShowQueue();};
        }
        queuePoll.Tick+=(_,_)=>{if(ActiveQueueRefresh?.IsCompleted!=false && insertTail.IsCompleted)ActiveQueueRefresh=RefreshQueueAsync();};
        IsVisibleChanged+=(_,_)=>
        {
            if(disposed)return;
            if(IsVisible && showingQueue){queuePoll.Start();ActiveQueueRefresh=RefreshQueueAsync();}
            else{queuePoll.Stop();queueReading?.Cancel();queueVersions.Next();queuePanel.CancelLoading();}
        };
    }
    internal void ShowQueue()
    {
        if(disposed || readQueue is null)return;showingQueue=true;UpdateQueueSurface();queuePoll.Start();ActiveQueueRefresh=RefreshQueueAsync();
    }
    internal void HideQueue()
    {
        if(!showingQueue)return;showingQueue=false;queuePoll.Stop();queueReading?.Cancel();queueVersions.Next();queuePanel.CancelLoading();UpdateQueueSurface();Filter();
    }
    void UpdateQueueSurface()
    {
        browseBody.Visibility=browseFilters.Visibility=showingQueue?Visibility.Collapsed:Visibility.Visible;queuePanel.Visibility=showingQueue?Visibility.Visible:Visibility.Collapsed;
        queueTab.SetResourceReference(Control.BackgroundProperty,showingQueue?"AccentSoftBrush":"ControlBrush");queueTab.SetResourceReference(Control.ForegroundProperty,showingQueue?"AccentBrush":"MutedBrush");
        if(showingQueue){foreach(var copy in headingCopy.Children.OfType<TextBlock>().Take(2))copy.Visibility=Visibility.Collapsed;count.Text="正在播放与接下来播放";playSelected.Visibility=Visibility.Collapsed;foreach(var tab in new[]{albumsTab,songsTab}){tab.SetResourceReference(Control.BackgroundProperty,"ControlBrush");tab.SetResourceReference(Control.ForegroundProperty,"MutedBrush");}}
    }
    async Task RefreshQueueAsync()
    {
        if(disposed || !showingQueue || !IsVisible || readQueue is null || queuePanel.IsSelecting)return;
        queueReading?.Cancel();queueReading?.Dispose();queueReading=CancellationTokenSource.CreateLinkedTokenSource(queueLifetime.Token);var token=queueReading.Token;var version=queueVersions.Next();queuePanel.Loading();
        try
        {
            var snapshot=await readQueue(token);token.ThrowIfCancellationRequested();
            if(!disposed && showingQueue && queueVersions.IsCurrent(version))queuePanel.Apply(snapshot);
        }
        catch(OperationCanceledException){}
        catch(Exception ex) when(ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException or IOException or TimeoutException)
        {if(!disposed && showingQueue && queueVersions.IsCurrent(version))queuePanel.Apply(MusicQueueSnapshot.Unavailable("播放列表暂不可用，请稍后刷新。"));}
        finally{if(!disposed && queueVersions.IsCurrent(version))queuePanel.CancelLoading();}
    }
    void RefreshVisibleQueue(){if(showingQueue && IsVisible && ActiveQueueRefresh?.IsCompleted!=false && insertTail.IsCompleted)ActiveQueueRefresh=RefreshQueueAsync();}
    void BeginInsert(MusicRow row)
    {
        if(disposed || insertNext is null)return;
        if(!row.Song.CanPlay){status.Text="这首歌的录音信息不完整，暂时无法插播。";return;}
        var request=requests.Next();status.Text="正在加入插播 · "+row.Song.Title;
        // Every accepted click is retained. Cancelling earlier insertions would silently
        // lose tracks when several different songs are queued in quick succession.
        insertTail=InsertAfterAsync(insertTail,row.Song,request);ActivePlayback=insertTail;
    }
    async Task InsertAfterAsync(Task previous,MusicLibrarySong song,long request)
    {
        var token=queueLifetime.Token;
        try
        {
            await previous;token.ThrowIfCancellationRequested();var result=await insertNext!(song,token);
            if(!disposed && requests.IsCurrent(request)){status.Text=result.Message;if(result.Confirmed && result.ActivateWindow)activate();}
        }
        catch(OperationCanceledException){}
        catch(Exception ex) when(ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException or IOException or TimeoutException)
        {if(!disposed && requests.IsCurrent(request))status.Text="插播失败，请确认 Apple Music 可用后重试。";}
        finally{if(!disposed && showingQueue)ActiveQueueRefresh=RefreshQueueAsync();}
    }
    void DisposeQueue(){queuePoll.Stop();queueLifetime.Cancel();queueReading?.Cancel();queueReading?.Dispose();queueReading=null;queueLifetime.Dispose();queuePanel.Dispose();}
    static bool IsSongAction(DependencyObject? node)
    {
        while(node is not null){if(node is Button)return true;if(node is ListBoxItem)return false;node=VisualTreeHelper.GetParent(node);}return false;
    }
}
