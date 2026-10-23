using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

public partial class MainWindow
{
    async Task VerifyPlaybackQueueAsync(string directory,Dictionary<string,object?> checks)
    {
        var originalTheme=settings.UiTheme;var originalLibrary=musicLibrary;string originalTitle=vm.Title,originalArtist=vm.Artist;
        string fixtureRoot=Path.Combine(store.Root,"queue-fixtures");Directory.CreateDirectory(fixtureRoot);var coverPaths=new List<string>();
        foreach(var (id,color) in new[]{("tide",Color.FromRgb(57,117,129)),("light",Color.FromRgb(181,112,77)),("night",Color.FromRgb(88,70,129))})
        {
            var drawing=new DrawingVisual();using(var canvas=drawing.RenderOpen()){canvas.DrawRectangle(new LinearGradientBrush(color,Color.FromRgb(22,28,42),60),null,new Rect(0,0,600,600));canvas.DrawEllipse(new SolidColorBrush(Color.FromRgb(223,217,192)),null,new Point(380,210),150,150);canvas.DrawEllipse(new SolidColorBrush(color),null,new Point(300,140),150,150);canvas.DrawLine(new Pen(Brushes.White,2),new Point(60,470),new Point(540,470));}
            var image=new RenderTargetBitmap(600,600,96,96,PixelFormats.Pbgra32);image.Render(drawing);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));string path=Path.Combine(fixtureRoot,id+".img");using(var stream=File.Create(path))encoder.Save(stream);coverPaths.Add(path);
        }
        var fixture=new[]{new MusicLibrarySong("fixture","one",new("微光之间","浮光工作室","原创测试",180000),coverPaths[0]),new MusicLibrarySong("fixture","two",new("夜航","浮光工作室","原创测试",200000),coverPaths[1]),new MusicLibrarySong("fixture","three",new("静静的海面","浮光工作室","原创测试",220000),coverPaths[2])};
        var all=fixture.Concat(Enumerable.Range(0,250).Select(i=>new MusicLibrarySong("fixture","part-"+i,new("原创片段 "+i.ToString("000"),"浮光工作室","创作集",120000),coverPaths[i%3]))).ToArray();
        var upcoming=new List<MusicQueueEntry>{new(fixture[1].Track),new(fixture[2].Track),new(fixture[1].Track)};upcoming.AddRange(all.Skip(3).Take(180).Select(s=>new MusicQueueEntry(s.Track)));
        int mode=0,readCancelled=0,insertCancelled=0,activeInsert=0,maxInsert=0;var inserted=new List<string>();var played=new List<string>();bool insertionFails=false;
        MusicQueueSnapshot Snapshot(int readMode)=>readMode switch
        {
            1=>MusicQueueSnapshot.Unavailable("原创连接失败示例"),
            2=>new(fixture[0].Track,[],true,true,DateTimeOffset.UtcNow,"原生队列为空"),
            3=>new(fixture[0].Track,upcoming.Take(2).ToArray(),true,false,DateTimeOffset.UtcNow,"只读到部分列表"),
            4=>new(fixture[0].Track,[new(new("较早的队列","浮光","原创",100000))],true,true,DateTimeOffset.UtcNow,"旧请求"),
            5=>new(fixture[0].Track,[new(new("最新的队列","浮光","原创",100000))],true,true,DateTimeOffset.UtcNow,"新请求"),
            _=>new(fixture[0].Track,upcoming.ToArray(),true,true,DateTimeOffset.UtcNow,"与 Apple Music 待播清单同步")
        };
        async Task<MusicQueueSnapshot> Read(CancellationToken token)
        {
            int captured=mode;
            try{if(captured==4)await Task.Delay(300);else await Task.Delay(captured==7?2000:25,token);if(captured==6)throw new TimeoutException("original queue timeout");return Snapshot(captured);}
            catch(OperationCanceledException){readCancelled++;throw;}
        }
        async Task<LibraryPlayResult> Insert(MusicLibrarySong song,CancellationToken token)
        {
            activeInsert++;maxInsert=Math.Max(maxInsert,activeInsert);
            try{await Task.Delay(180,token);if(insertionFails)return new(false,"原创插播失败示例");inserted.Add(song.PersistentId);upcoming.Insert(0,new(song.Track,Inserted:true));return new(true,"已插播 · "+song.Title);}
            catch(OperationCanceledException){insertCancelled++;throw;}
            finally{activeInsert--;}
        }
        var browser=new MusicLibraryView(_=>Task.FromResult(new MusicLibraryResult(all,"")),(song,_)=>{played.Add(song.PersistentId);return Task.FromResult(new LibraryPlayResult(true,"正在播放 · "+song.Title));},()=>{},ShowLyricsPage,()=>{},vm,()=>{},()=>{},()=>{},(_,_)=>Task.FromResult(new LibraryPlayResult(true,"专辑播放")),Read,Insert);
        musicLibrary=browser;if(originalLibrary is not null)originalLibrary.Visibility=Visibility.Collapsed;MainPageHost.Children.Add(browser);
        void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent,button));
        async Task OpenQueue(){browser.ShowQueue();await browser.ActiveQueueRefresh!;await Task.Delay(30);UpdateLayout();}
        Button RowButton(string id,string content)
        {
            var item=browser.SongList.Items.Cast<MusicLibraryView.MusicRow>().Single(r=>r.Song.PersistentId==id);browser.SongList.SelectedItem=item;browser.SongList.ScrollIntoView(item);UpdateLayout();return Descendants<Button>((ListBoxItem)browser.SongList.ItemContainerGenerator.ContainerFromItem(item)).Single(b=>b.Content?.ToString()==content);
        }
        try
        {
            Width=1220;Height=820;vm.Title="微光之间";vm.Artist="浮光工作室";await ShowMusicLibraryAsync();browser.ShowAllSongs();browser.SearchBox.Text="浮光工作室";await Task.Delay(180);UpdateLayout();
            var selection=browser.SongList.Items.Cast<MusicLibraryView.MusicRow>().Single(r=>r.Song.PersistentId=="part-100");browser.SongList.SelectedItem=selection;browser.SongList.ScrollIntoView(selection);UpdateLayout();var scroll=Descendants<ScrollViewer>(browser.SongList).First();double offset=scroll.VerticalOffset;
            await OpenQueue();checks["queue.navigationOpensActualQueue"]=browser.IsQueueVisible && browser.QueuePanel.Snapshot?.Upcoming.Count==183;
            checks["queue.currentSeparatedFromNext"]=browser.QueuePanel.Snapshot?.Current==fixture[0].Track && browser.QueuePanel.Snapshot.Upcoming[0].Track==fixture[1].Track;
            checks["queue.nativeOrderAndDuplicatesPreserved"]=browser.QueuePanel.UpcomingList.Items.Cast<PlaybackQueueView.QueueRow>().Take(3).Select(r=>r.Title).SequenceEqual(new[]{"夜航","静静的海面","夜航"});
            checks["queue.listVirtualized"]=Descendants<ListBoxItem>(browser.QueuePanel.UpcomingList).Count() is >0 and <40;
            checks["queue.coverDecodedFromPersonalStyleImg"]=Descendants<Image>(browser.QueuePanel).Any(i=>i.Source is BitmapImage{PixelWidth:96});
            var items=browser.QueuePanel.UpcomingList.ItemsSource;Click(browser.QueuePanel.RefreshButton);await browser.ActiveQueueRefresh!;checks["queue.unchangedRefreshKeepsRows"]=ReferenceEquals(items,browser.QueuePanel.UpcomingList.ItemsSource);
            Click(browser.QueuePanel.BackButton);await Task.Delay(30);UpdateLayout();checks["queue.returnKeepsBrowseSearchSelectionAndScroll"]=!browser.IsQueueVisible && browser.SearchBox.Text=="浮光工作室" && browser.SongList.SelectedItem==selection && Math.Abs(scroll.VerticalOffset-offset)<1;
            browser.OpenAlbum(browser.AlbumGroups.Single(a=>a.Title=="原创测试"));await OpenQueue();browser.HideQueue();checks["queue.returnKeepsAlbumContext"]=browser.SelectedAlbum?.Title=="原创测试" && !browser.IsAlbumShelf;
            browser.ShowAllSongs();browser.SearchBox.Text="";await Task.Delay(170);UpdateLayout();var insert=RowButton("two","插播");
            checks["queue.insertHasAccessibleName"]=AutomationProperties.GetName(insert)=="插播 · 夜航" && insert.IsEnabled;
            Click(insert);await browser.ActivePlayback!;checks["queue.insertDoesNotPlayImmediately"]=inserted.SequenceEqual(new[]{"two"}) && played.Count==0;
            await OpenQueue();checks["queue.insertReflectedInUpcoming"]=browser.QueuePanel.Snapshot?.Upcoming[0] is {Inserted:true} first && first.Track==fixture[1].Track && browser.QueuePanel.Snapshot.Current==fixture[0].Track;
            browser.HideQueue();Click(RowButton("one","插播"));var older=browser.ActivePlayback!;Click(RowButton("three","插播"));await Task.WhenAll(older,browser.ActivePlayback!);
            checks["queue.rapidInsertionsSerializedWithoutDropping"]=inserted.SequenceEqual(new[]{"two","one","three"}) && maxInsert==1 && browser.StatusText=="已插播 · 静静的海面";
            insert=RowButton("two","插播");insert.Focus();insert.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this),0,Key.Enter){RoutedEvent=Keyboard.PreviewKeyDownEvent});await browser.ActivePlayback!;
            checks["queue.keyboardInsertNeverBecomesImmediatePlay"]=inserted.Last()=="two" && inserted.Count==4 && played.Count==0;
            var doubleClick=new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=Control.MouseDoubleClickEvent,Source=insert};browser.SongList.RaiseEvent(doubleClick);
            checks["queue.doubleClickOnInsertDoesNotPlayRow"]=played.Count==0;
            insertionFails=true;Click(RowButton("one","插播"));await browser.ActivePlayback!;insertionFails=false;checks["queue.failedInsertionIsReported"]=browser.StatusText=="原创插播失败示例" && inserted.Count==4;
            Click(RowButton("two","▷"));await browser.ActivePlayback!;checks["queue.immediatePlayRemainsSeparate"]=played.SequenceEqual(new[]{"two"}) && inserted.Count==4;
            await OpenQueue();mode=4;Click(browser.QueuePanel.RefreshButton);older=browser.ActiveQueueRefresh!;mode=5;Click(browser.QueuePanel.RefreshButton);await Task.WhenAll(older,browser.ActiveQueueRefresh!);
            checks["queue.staleReadCannotOverwriteNewQueue"]=browser.QueuePanel.Snapshot?.Upcoming[0].Track.Title=="最新的队列";
            mode=1;Click(browser.QueuePanel.RefreshButton);await browser.ActiveQueueRefresh!;checks["queue.unavailableIsNotEmptySuccess"]=browser.QueuePanel.Snapshot is {Available:false,IsComplete:false} && browser.QueuePanel.Message=="原创连接失败示例";
            mode=2;Click(browser.QueuePanel.RefreshButton);await browser.ActiveQueueRefresh!;checks["queue.emptyQueueKeepsCurrentRecording"]=browser.QueuePanel.Snapshot is {Available:true,IsComplete:true,Upcoming.Count:0,Current:not null};
            mode=3;Click(browser.QueuePanel.RefreshButton);await browser.ActiveQueueRefresh!;checks["queue.partialQueueClearlyReported"]=browser.QueuePanel.Snapshot is {IsComplete:false,Upcoming.Count:2} && browser.QueuePanel.Message=="只读到部分列表";
            mode=6;Click(browser.QueuePanel.RefreshButton);await browser.ActiveQueueRefresh!;checks["queue.readExceptionIsRecoverable"]=browser.QueuePanel.Snapshot?.Available==false && browser.QueuePanel.RefreshButton.IsEnabled;
            mode=0;Click(browser.QueuePanel.RefreshButton);await browser.ActiveQueueRefresh!;
            foreach(var theme in AppThemes.All)
            {
                SelectUiTheme(theme.Id);await Task.Delay(80);UpdateLayout();browser.QueuePanel.RefreshButton.Focus();
                checks["queue."+theme.Id+".themeAndKeyboard"]=browser.QueuePanel.RefreshButton.IsKeyboardFocused && Descendants<TextBlock>(browser.QueuePanel).First(t=>t.Text=="当前播放列表").Foreground==FindResource("TextBrush");
                VisualVerification.Save((FrameworkElement)Content,Path.Combine(directory,"playback-queue-"+theme.Id+".png"),true);
            }
            foreach(double width in new[]{1220d,980d,820d})
            {
                Width=width;Height=width==820?640:820;await Task.Delay(70);UpdateLayout();
                var button=browser.QueuePanel.RefreshButton;var bounds=button.TransformToAncestor(browser).TransformBounds(new Rect(0,0,button.ActualWidth,button.ActualHeight));
                checks["queue.layoutFits."+width]=bounds.Left>=0 && bounds.Right<=browser.ActualWidth && browser.QueuePanel.UpcomingList.ActualHeight>90;
            }
            Width=1220;Height=820;browser.HideQueue();browser.ShowAllSongs();browser.SearchBox.Text="";await Task.Delay(170);UpdateLayout();VisualVerification.Save((FrameworkElement)Content,Path.Combine(directory,"playback-queue-song-actions.png"),true);
            await VerifyNativeQueueStructureAsync(checks);
            await OpenQueue();mode=7;Click(browser.QueuePanel.RefreshButton);var pendingRead=browser.ActiveQueueRefresh!;browser.HideQueue();await pendingRead;checks["queue.leavingCancelsRead"]=readCancelled>0 && !browser.IsQueueVisible;
            mode=0;Click(RowButton("one","插播"));older=browser.ActivePlayback!;Click(RowButton("three","插播"));var pendingInsert=browser.ActivePlayback!;browser.Dispose();await Task.WhenAll(older,pendingInsert);
            checks["queue.disposeCancelsActiveAndPendingInsertions"]=insertCancelled==1 && inserted.Count==4;
        }
        catch{File.WriteAllText(Path.Combine(directory,"queue-check.partial.json"),System.Text.Json.JsonSerializer.Serialize(checks,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));throw;}
        finally{browser.Dispose();MainPageHost.Children.Remove(browser);musicLibrary=originalLibrary;ShowLyricsPage();vm.Title=originalTitle;vm.Artist=originalArtist;Width=1220;Height=820;SelectUiTheme(originalTheme);Update();}
    }
}
