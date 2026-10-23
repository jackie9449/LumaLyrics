using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

public partial class MainWindow
{
    async Task VerifyQueueNavigationAsync(Dictionary<string,object?> checks)
    {
        var track=new Track("微光之间","浮光工作室","原创测试",180000);var repeated=new Track("夜航","浮光工作室","原创测试",200000);
        var snapshot=new MusicQueueSnapshot(track,[new(repeated,Inserted:true),new(new("海面","浮光工作室","原创测试",220000)),new(repeated)],true,true,DateTimeOffset.UtcNow,"原创队列");
        var requests=new List<(MusicQueueSelection Selection,CancellationToken Token,TaskCompletionSource<LibraryPlayResult> Result)>();int activated=0,completed=0;
        Task<LibraryPlayResult> Play(MusicQueueSelection selection,CancellationToken token){var result=new TaskCompletionSource<LibraryPlayResult>(TaskCreationOptions.RunContinuationsAsynchronously);requests.Add((selection,token,result));return result.Task;}
        var view=new PlaybackQueueView(()=>{},()=>{},true,Play,()=>activated++){Visibility=Visibility.Visible};view.PlaybackCompleted+=()=>completed++;
        var window=new Window{Owner=this,Title="浮光歌词 · 原创队列交互回归",Content=view,Width=350,Height=600,ShowInTaskbar=false};window.Show();view.Apply(snapshot);window.UpdateLayout();
        void DoubleClick(DependencyObject source)=>view.UpcomingList.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=Control.MouseDoubleClickEvent,Source=source});
        ListBoxItem Row(int index){view.UpcomingList.ScrollIntoView(view.UpcomingList.Items[index]);window.UpdateLayout();return (ListBoxItem)view.UpcomingList.ItemContainerGenerator.ContainerFromIndex(index);}
        void Enter()=>view.UpcomingList.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window),0,Key.Enter){RoutedEvent=Keyboard.PreviewKeyDownEvent});
        try
        {
            view.UpcomingList.SelectedIndex=2;view.UpcomingList.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=Mouse.MouseUpEvent,Source=Row(2)});
            checks["queueJump.singleClickOnlySelects"]=requests.Count==0;
            DoubleClick(view.UpcomingList);DoubleClick(view.RefreshButton);checks["queueJump.blankAndButtonsDoNotPlaySelectedRow"]=requests.Count==0;
            DoubleClick(Row(2));checks["queueJump.doubleClickRetainsExactRepeatedOccurrence"]=requests.Count==1 && requests[0].Selection.Index==2 && ReferenceEquals(requests[0].Selection.Snapshot,snapshot);
            checks["queueJump.pendingDoesNotActivate"]=activated==0 && view.IsSelecting;
            requests[0].Result.SetResult(new(true,"原创跳转成功"));await view.ActivePlayback!;checks["queueJump.confirmedCompletionActivates"]=activated==1 && completed==1 && !view.IsSelecting;
            view.Apply(snapshot);checks["queueJump.resultRemainsReadableAfterRefresh"]=view.Message=="原创跳转成功";
            view.UpcomingList.SelectedIndex=0;Enter();checks["queueJump.enterUsesSelectedOccurrence"]=requests.Count==2 && requests[1].Selection.Index==0;
            requests[1].Result.SetResult(new(false,"原创过期队列，请刷新"));await view.ActivePlayback!;view.Apply(snapshot);
            checks["queueJump.failureDoesNotActivateAndSurvivesRefresh"]=activated==1 && view.Message=="原创过期队列，请刷新";
            DoubleClick(Row(0));var older=view.ActivePlayback!;DoubleClick(Row(2));var latest=view.ActivePlayback!;
            checks["queueJump.rapidSelectionCancelsEarlierRequest"]=requests.Count==4 && requests[2].Token.IsCancellationRequested && !requests[3].Token.IsCancellationRequested;
            requests[3].Result.SetResult(new(true,"最新原创跳转",false));await latest;requests[2].Result.SetResult(new(true,"过期原创跳转"));await older;
            checks["queueJump.staleSuccessCannotActivateOrOverwrite"]=activated==1 && view.Message=="最新原创跳转";
            view.Apply(snapshot with{Upcoming=[new(repeated,MetadataComplete:false)]});DoubleClick(Row(0));
            checks["queueJump.unknownRecordingDoesNotIssueCommand"]=requests.Count==4 && view.Message.Contains("信息不完整",StringComparison.Ordinal);
            view.Apply(snapshot);DoubleClick(Row(1));var pending=view.ActivePlayback!;view.Dispose();requests[4].Result.SetResult(new(true,"关闭后的原创回调"));await pending;
            checks["queueJump.disposeRejectsLateSuccess"]=requests[4].Token.IsCancellationRequested && activated==1 && view.UpcomingList.Items.Count==0;
        }
        finally{view.Dispose();window.Close();}

        int libraryPlays=0,queuePlays=0,reads=0;
        var songs=new[]{new MusicLibrarySong("fixture","one",track),new MusicLibrarySong("fixture","two",repeated)};
        var browser=new MusicLibraryView(_=>Task.FromResult(new MusicLibraryResult(songs,"")),(_,_)=>{libraryPlays++;return Task.FromResult(new LibraryPlayResult(true,"资料库播放"));},()=>{},()=>{},()=>{},vm,()=>{},()=>{},()=>{},readQueue:_=>{reads++;return Task.FromResult(snapshot);},playQueue:(selection,_)=>{queuePlays++;return Task.FromResult(new LibraryPlayResult(true,"原创队列跳转 · "+selection.Index,false));});
        var libraryWindow=new Window{Owner=this,Title="浮光歌词 · 原创音乐库队列交互",Content=browser,Width=820,Height=640,ShowInTaskbar=false};libraryWindow.Show();
        try
        {
            await browser.EnsureLoadedAsync();browser.ShowQueue();await browser.ActiveQueueRefresh!;libraryWindow.UpdateLayout();
            var list=browser.QueuePanel.UpcomingList;list.SelectedIndex=2;list.ScrollIntoView(list.SelectedItem);libraryWindow.UpdateLayout();
            var row=(ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(2);list.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=Control.MouseDoubleClickEvent,Source=row});await browser.ActivePlayback!;
            checks["queueJump.libraryQueueUsesOccurrencePath"]=queuePlays==1 && libraryPlays==0;
            checks["queueJump.libraryQueueRefreshesAfterCompletion"]=reads>=2 && !browser.QueuePanel.IsSelecting;
        }
        finally{browser.Dispose();libraryWindow.Close();}
    }
}
