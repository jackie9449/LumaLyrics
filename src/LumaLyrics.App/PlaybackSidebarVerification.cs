using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

public partial class MainWindow
{
    async Task VerifyPlaybackSidebarAsync(string directory,Dictionary<string,object?> checks)
    {
        var oldTheme=settings.UiTheme;var oldLibrary=musicLibrary;var oldChild=PlaybackQueueHost.Child;var oldTrack=coordinator.ActiveTrack;var oldBinding=coordinator.Binding;var oldArtwork=Artwork.Source;var oldPalette=artworkPalette;var oldPlaceholder=CoverPlaceholder.Visibility;
        string root=Path.Combine(store.Root,"sidebar-fixtures");Directory.CreateDirectory(root);var covers=new List<string>();
        foreach(var (name,color) in new[]{("tide",Color.FromRgb(50,117,135)),("light",Color.FromRgb(184,107,69)),("night",Color.FromRgb(88,69,130))})
        {
            var drawing=new DrawingVisual();using(var dc=drawing.RenderOpen()){dc.DrawRectangle(new LinearGradientBrush(color,Color.FromRgb(22,28,42),60),null,new Rect(0,0,600,600));dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(230,222,198)),null,new Point(380,230),170,170);dc.DrawEllipse(new SolidColorBrush(color),null,new Point(295,150),170,170);dc.DrawLine(new Pen(Brushes.White,2),new Point(60,475),new Point(540,475));}
            var bitmap=new RenderTargetBitmap(600,600,96,96,PixelFormats.Pbgra32);bitmap.Render(drawing);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));string path=Path.Combine(root,name+".img");using(var stream=File.Create(path))png.Save(stream);covers.Add(path);
        }
        var tracks=new[]{new Track("微光之间","浮光工作室","原创测试",180000),new Track("夜航","浮光工作室","夜色",200000),new Track("静静的海面","浮光工作室","海面",220000)};
        var songs=tracks.Select((t,i)=>new MusicLibrarySong("fixture",i.ToString(),t,covers[i])).Concat(Enumerable.Range(0,150).Select(i=>new MusicLibrarySong("fixture","part-"+i,new("原创片段 "+i.ToString("000"),"浮光工作室","创作集",120000),covers[i%3]))).ToArray();
        var entries=new List<MusicQueueEntry>{new(tracks[1],Inserted:true),new(tracks[2]),new(tracks[1])};entries.AddRange(songs.Skip(3).Select(s=>new MusicQueueEntry(s.Track)));
        int mode=0,reads=0,loads=0,cancelled=0,opened=0,activated=0;bool busy=false,libraryFails=false,busyAfterLoad=false;
        TaskCompletionSource<MusicQueueSnapshot>? delayed=null;TaskCompletionSource<LibraryPlayResult>? selection=null;
        MusicQueueSnapshot Queue()=>new(tracks[0],entries.ToArray(),true,true,DateTimeOffset.UtcNow,"与 Apple Music 待播清单同步");
        async Task<MusicQueueSnapshot> Read(CancellationToken token)
        {
            int captured=mode;reads++;
            try
            {
                if(captured==4)return await delayed!.Task;
                await Task.Delay(captured==7?2000:10,token);
                return captured switch{1=>MusicQueueSnapshot.Unavailable("原创连接失败示例"),2=>Queue() with{Upcoming=entries.Take(2).ToArray(),IsComplete=false,Message="部分列表"},3=>Queue() with{Upcoming=[]},5=>Queue() with{Upcoming=[new(new("最新队列","浮光工作室","原创测试",100000))]},6=>throw new TimeoutException("original timeout fixture"),_=>Queue()};
            }
            catch(OperationCanceledException){cancelled++;throw;}
        }
        async Task<MusicLibraryResult> Load(CancellationToken token){loads++;await Task.Delay(10,token);if(libraryFails)throw new IOException("original cover lookup failure");if(busyAfterLoad)busy=true;return new(songs,"");}
        var browser=new MusicLibraryView(_=>Task.FromResult(new MusicLibraryResult(songs,"")),(_,_)=>selection?.Task??Task.FromResult(new LibraryPlayResult(true,"原创播放成功")),()=>{},ShowLyricsPage,()=>activated++,vm,()=>{},()=>{},()=>{});
        var sidebar=new PlaybackQueueSidebar(this,LyricsPage,Read,Load,()=>{opened++;_=ShowMusicLibraryAsync();},()=>busy);PlaybackQueueHost.Child=sidebar.View;musicLibrary=browser;MainPageHost.Children.Add(browser);browser.Visibility=Visibility.Collapsed;
        void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent,button));
        async Task AwaitScheduledRefresh()
        {
            var wait=Stopwatch.StartNew();
            while(sidebar.ActiveRefresh is null && wait.Elapsed<TimeSpan.FromSeconds(5)){await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(25);}
            await (sidebar.ActiveRefresh??throw new InvalidOperationException("The original sidebar fixture did not schedule its current-track refresh."));
        }
        Button SongPlay(string id){var row=browser.SongList.Items.Cast<MusicLibraryView.MusicRow>().Single(r=>r.Song.PersistentId==id);browser.SongList.SelectedItem=row;browser.SongList.ScrollIntoView(row);UpdateLayout();return Descendants<Button>((ListBoxItem)browser.SongList.ItemContainerGenerator.ContainerFromItem(row)).Single();}
        try
        {
            Width=1220;Height=820;ShowLyricsPage();coordinator.Import(tracks[0],"[00:00.00]让此刻，慢慢亮起来。\n[00:06.00]微光掠过安静的海面\n[00:12.00]每一句，都有回响。\n[00:18.00]让旋律停留在你身旁。\n[00:24.00]把远方，轻轻唱成一束光。", "Original sidebar fixture",false);SetArtwork(File.ReadAllBytes(covers[0]));sidebar.Enable();await sidebar.ActiveRefresh!;UpdateLayout();
            checks["sidebar.currentAndActualQueueVisible"]=PlaybackQueueHost.IsVisible && sidebar.View.Snapshot?.Current==tracks[0] && sidebar.View.Snapshot.Upcoming.Count==153;
            checks["sidebar.orderAndRepeatedOccurrences"]=sidebar.View.UpcomingList.Items.Cast<PlaybackQueueView.QueueRow>().Take(3).Select(r=>r.Title).SequenceEqual(new[]{"夜航","静静的海面","夜航"});
            checks["sidebar.virtualizedAndCoversDecoded"]=Descendants<ListBoxItem>(sidebar.View.UpcomingList).Count() is >0 and <30 && Descendants<Image>(sidebar.View).Any(i=>i.Source is BitmapImage{PixelWidth:96});
            var items=sidebar.View.UpcomingList.ItemsSource;await sidebar.RefreshAsync();checks["sidebar.unchangedRefreshPreservesRows"]=ReferenceEquals(items,sidebar.View.UpcomingList.ItemsSource) && loads==1;
            int before=reads;busy=true;await sidebar.RefreshAsync();checks["sidebar.nativeHandoffDefersQueueRead"]=reads==before && sidebar.View.Message.Contains("正在切换歌曲",StringComparison.Ordinal);busy=false;await sidebar.RefreshAsync();
            mode=4;delayed=new(TaskCreationOptions.RunContinuationsAsynchronously);var older=sidebar.RefreshAsync();mode=5;sidebar.ObserveTrack(tracks[1]);await AwaitScheduledRefresh();delayed.SetResult(Queue() with{Message="旧请求"});await older;
            checks["sidebar.currentChangeDiscardsDelayedOldRead"]=sidebar.View.Snapshot?.Upcoming[0].Track.Title=="最新队列";
            mode=4;delayed=new(TaskCreationOptions.RunContinuationsAsynchronously);older=sidebar.RefreshAsync();await ShowMusicLibraryAsync();mode=5;ShowLyricsPage();await sidebar.ActiveRefresh!;delayed.SetResult(Queue());await older;
            checks["sidebar.reentryDoesNotWaitForCancelledOldRead"]=sidebar.View.Snapshot?.Upcoming[0].Track.Title=="最新队列" && sidebar.IsPolling;
            mode=1;await sidebar.RefreshAsync();checks["sidebar.unavailableIsExplicit"]=sidebar.View.Snapshot?.Available==false && sidebar.View.Message=="原创连接失败示例";
            mode=2;await sidebar.RefreshAsync();checks["sidebar.partialListIsExplicit"]=sidebar.View.Snapshot is {IsComplete:false,Upcoming.Count:2} && Descendants<TextBlock>(sidebar.View).Any(t=>t.Text.Contains("部分列表",StringComparison.Ordinal));
            mode=3;await sidebar.RefreshAsync();checks["sidebar.emptyQueueRetainsCurrentRecording"]=sidebar.View.Snapshot is {Available:true,IsComplete:true,Current:not null,Upcoming.Count:0};
            mode=6;await sidebar.RefreshAsync();checks["sidebar.readFailureCanBeRetried"]=sidebar.View.Snapshot?.Available==false && sidebar.View.RefreshButton.IsEnabled;mode=0;await sidebar.RefreshAsync();
            var progress=new ArtworkProgress(153,0,0,0,0,true,"原创封面进度");sidebar.ObserveArtwork(progress);libraryFails=true;sidebar.ObserveArtwork(progress with{Cached=1});await sidebar.RefreshAsync();
            checks["sidebar.coverLookupFailureDoesNotHideQueue"]=sidebar.View.Snapshot?.Available==true;libraryFails=false;await sidebar.RefreshAsync();checks["sidebar.artworkChangeReloadsCoverIndex"]=loads>=3 && sidebar.View.Snapshot?.Available==true;
            before=reads;busyAfterLoad=true;sidebar.ObserveArtwork(progress with{Cached=2});await sidebar.RefreshAsync();checks["sidebar.handoffStartingDuringCoverLoadDefersRead"]=reads==before && sidebar.View.Message.Contains("正在切换歌曲",StringComparison.Ordinal);busyAfterLoad=busy=false;await sidebar.RefreshAsync();
            before=reads;Click(sidebar.View.BackButton);await Task.Delay(30);checks["sidebar.opensLibraryAndStopsPolling"]=opened==1 && LyricsPage.Visibility==Visibility.Collapsed && !sidebar.IsPolling;
            browser.ShowAllSongs();selection=new(TaskCreationOptions.RunContinuationsAsynchronously);Click(SongPlay("0"));var pending=browser.ActivePlayback!;await Task.Delay(80);
            checks["handoff.pendingSelectionDoesNotActivateApp"]=!pending.IsCompleted && activated==0;selection.SetResult(new(false,"原创确认失败示例"));await pending;checks["handoff.failedSelectionDoesNotActivateApp"]=activated==0 && browser.StatusText=="原创确认失败示例";
            selection=new(TaskCreationOptions.RunContinuationsAsynchronously);Click(SongPlay("0"));selection.SetResult(new(true,"已确认但已失去窗口归属",false));await browser.ActivePlayback!;checks["handoff.staleWindowOwnerDoesNotActivateApp"]=activated==0;
            selection=new(TaskCreationOptions.RunContinuationsAsynchronously);Click(SongPlay("0"));selection.SetResult(new(true,"原创确认与最小化成功",true));await browser.ActivePlayback!;checks["handoff.confirmedMinimizedSelectionCanActivateApp"]=activated==1;
            ShowLyricsPage();mode=0;await sidebar.RefreshAsync();
            foreach(var theme in AppThemes.All)
            {
                SetArtwork(File.ReadAllBytes(covers[0]));SelectUiTheme(theme.Id);await Task.Delay(80);UpdateLayout();sidebar.View.RefreshButton.Focus();checks["sidebar."+theme.Id+".themeAndKeyboard"]=sidebar.View.RefreshButton.IsKeyboardFocused && PlaybackQueueHost.Background==FindResource("PanelBrush");VisualVerification.Save((FrameworkElement)Content,Path.Combine(directory,"playback-sidebar-"+theme.Id+".png"),true);
            }
            checks["sidebar.wideArtworkRemainsSquare"]=Math.Abs(ArtworkBorder.ActualWidth-ArtworkBorder.ActualHeight)<1;
            foreach(double width in new[]{1220d,980d,820d})
            {
                Width=width;Height=width==820?640:820;await Task.Delay(80);UpdateLayout();
                Rect Bounds(FrameworkElement element)=>element.TransformToAncestor(Body).TransformBounds(new Rect(0,0,element.ActualWidth,element.ActualHeight));
                var queue=Bounds(PlaybackQueueHost);var lyrics=Bounds(LyricsPanel);var info=Bounds(InfoPanel);var controls=Bounds(PlaybackControls);
                checks["sidebar.layout."+width+".staysRightWithoutOverlap"]=queue.Right<=Body.ActualWidth+.5 && queue.Left>=lyrics.Right-.5 && queue.Left>=info.Right-.5 && queue.Top>=-.5 && queue.Bottom<=Body.ActualHeight+.5;
                checks["sidebar.layout."+width+".lyricsAndControlsRemainReadable"]=LyricsPanel.ActualWidth>=300 && sidebar.View.UpcomingList.ActualHeight>=100 && controls.Left>=-.5 && controls.Right<=queue.Left+.5;
                var viewport=Descendants<ScrollContentPresenter>(sidebar.View.UpcomingList).First();checks["sidebar.layout."+width+".queueRowsFitViewport"]=Descendants<ListBoxItem>(sidebar.View.UpcomingList).Where(i=>i.IsVisible).All(i=>i.ActualWidth<=viewport.ActualWidth+.5);
                if(width==820)VisualVerification.Save((FrameworkElement)Content,Path.Combine(directory,"playback-sidebar-820.png"),true);
            }
            Width=1220;Height=820;WindowState=WindowState.Minimized;await Task.Delay(100);before=reads;await Task.Delay(100);checks["sidebar.minimizedMainWindowStopsPolling"]=!sidebar.IsPolling && reads==before;WindowState=WindowState.Normal;await Task.Delay(60);await sidebar.ActiveRefresh!;checks["sidebar.restoredMainWindowRefreshes"]=sidebar.IsPolling && sidebar.View.Snapshot?.Available==true;
            mode=7;int cancelledBefore=cancelled;pending=sidebar.RefreshAsync();await Task.Delay(30);sidebar.Dispose();await pending;checks["sidebar.disposeCancelsReadAndTimer"]=cancelled==cancelledBefore+1 && !sidebar.IsPolling;
        }
        catch{File.WriteAllText(Path.Combine(directory,"sidebar-check.partial.json"),System.Text.Json.JsonSerializer.Serialize(checks,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));throw;}
        finally
        {
            sidebar.Dispose();browser.Dispose();MainPageHost.Children.Remove(browser);musicLibrary=oldLibrary;PlaybackQueueHost.Child=oldChild;ShowLyricsPage();Width=1220;Height=820;WindowState=WindowState.Normal;
            if(oldTrack is not null && oldBinding is not null){coordinator.Import(oldTrack,oldBinding.Raw,oldBinding.Source,oldBinding.Plain);coordinator.SetOffset(oldBinding.OffsetMs);}Artwork.Source=oldArtwork;artworkPalette=oldPalette;CoverPlaceholder.Visibility=oldPlaceholder;SelectUiTheme(oldTheme);Update();
        }
    }
}
