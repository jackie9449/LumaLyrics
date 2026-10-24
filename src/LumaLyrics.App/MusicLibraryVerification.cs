using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

public partial class MainWindow
{
    async Task VerifyMusicLibraryAsync(string directory,Dictionary<string,object?> checks)
    {
        var originalTheme=settings.UiTheme;var originalLibrary=musicLibrary;string originalTitle=vm.Title,originalArtist=vm.Artist;
        var fixtureRoot=Path.Combine(store.Root,"browser-fixtures");Directory.CreateDirectory(fixtureRoot);
        var coverPaths=new List<string>();
        foreach(var (id,color) in new[]{("sea",Color.FromRgb(57,117,129)),("sun",Color.FromRgb(178,104,70)),("night",Color.FromRgb(82,69,123))})
        {
            var drawing=new DrawingVisual();using(var canvas=drawing.RenderOpen())
            {
                canvas.DrawRectangle(new LinearGradientBrush(color,Color.FromRgb(22,28,42),50),null,new Rect(0,0,600,600));
                canvas.DrawEllipse(new SolidColorBrush(Color.FromArgb(175,230,222,199)),null,new Point(370,230),170,170);
                canvas.DrawEllipse(new SolidColorBrush(color),null,new Point(285,165),160,160);
                canvas.DrawLine(new Pen(Brushes.White,2),new Point(60,480),new Point(540,480));
            }
            var image=new RenderTargetBitmap(600,600,96,96,PixelFormats.Pbgra32);image.Render(drawing);
            BitmapEncoder encoder=id=="sun"?new JpegBitmapEncoder():new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));
            string path=Path.Combine(fixtureRoot,id+".img");using(var stream=File.Create(path))encoder.Save(stream);coverPaths.Add(path);
        }
        var fixture=new List<MusicLibrarySong>
        {
            new("fixture","one",new("微光之间","浮光","原创测试",96000),coverPaths[0],"Synced"),
            new("fixture","two",new("夜航","浮光","夜色",185000),coverPaths[1]),
            new("fixture","three",new("静静的海面","浮光","海面",180000),coverPaths[2],"Instrumental")
        };
        fixture.AddRange(Enumerable.Range(1,3000).Select(i=>new MusicLibrarySong("fixture",i.ToString(),new("片段 "+i.ToString("0000"),"工作室 "+(i%8),"创作集 "+(i%12),120000+i*10),coverPaths[i%3])));
        int mode=0,cancelled=0,manageCount=0,transportCount=0,loadCount=0;bool playbackUnavailable=false;var played=new List<string>();
        int albumCancelled=0;bool albumUnavailable=false;var playedAlbums=new List<MusicAlbum>();
        async Task<MusicLibraryResult> Load(CancellationToken token)
        {
            loadCount++;
            await Task.Delay(10,token);
            return mode switch{1=>new([],"尚未找到音乐库"),2=>throw new IOException("fixture unavailable"),3=>new(fixture.Select((song,i)=>song with{Track=song.Track with{Album="专辑 "+i.ToString("0000")},CoverPath=i%17==1?null:song.CoverPath}).ToArray(),""),4=>new(fixture.Select(song=>song with{CoverPath=null}).ToArray(),""),_=>new(fixture,"")};
        }
        async Task<LibraryPlayResult> Play(MusicLibrarySong song,CancellationToken token)
        {
            try{if(playbackUnavailable)throw new TimeoutException("fixture playback timeout");await Task.Delay(song.PersistentId=="one"?700:20,token);played.Add(song.PersistentId);return new(true,"已确认 · "+song.Title);}
            catch(OperationCanceledException){cancelled++;throw;}
        }
        async Task<LibraryPlayResult> PlayAlbum(MusicAlbum album,CancellationToken token)
        {
            try
            {
                playedAlbums.Add(album);await Task.Delay(album.Title=="原创测试"?700:35,token);
                return albumUnavailable?new(false,"专辑播放未获确认 · 原创失败示例"):new(true,"已确认专辑 · "+album.Title);
            }
            catch(OperationCanceledException){albumCancelled++;throw;}
        }
        var browser=new MusicLibraryView(Load,Play,()=>manageCount++,ShowLyricsPage,()=>{},vm,()=>transportCount++,()=>transportCount++,()=>transportCount++,PlayAlbum);
        musicLibrary=browser;MainPageHost.Children.Add(browser);
        Button ButtonWith(string text)=>Descendants<Button>(browser).First(b=>b.Content?.ToString()==text);
        void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent,button));
        async Task Search(string text){browser.SearchBox.Text=text;for(int attempt=0;attempt<100 && browser.IsFiltering;attempt++)await Task.Delay(20);UpdateLayout();}
        void SelectSong(string id){browser.SongList.SelectedItem=browser.SongList.Items.Cast<MusicLibraryView.MusicRow>().Single(r=>r.Song.PersistentId==id);browser.SongList.ScrollIntoView(browser.SongList.SelectedItem);UpdateLayout();}
        try
        {
            Width=1220;Height=820;MusicNav.RaiseEvent(new RoutedEventArgs(Button.ClickEvent,MusicNav));await Task.Delay(200);UpdateLayout();
            checks["albums.defaultEntry"]=browser.IsAlbumShelf && browser.AlbumShelf.Albums.Count>0;browser.ShowAllSongs();await Task.Delay(80);UpdateLayout();
            checks["musicLibrary.navigationOpensIntegratedPage"]=browser.IsVisible && LyricsPage.Visibility==Visibility.Collapsed && browser.SongList.Items.Count==3003;
            checks["musicLibrary.virtualizedRows"]=Descendants<ListBoxItem>(browser.SongList).Count() is >0 and <80;
            checks["musicLibrary.visibleCoversDecoded"]=Descendants<Image>(browser).Any(i=>i.Source is BitmapImage {PixelWidth:96,IsFrozen:true});
            checks["musicLibrary.lazyThumbnails"]=browser.CachedThumbnailCount is >0 and <=3;
            await Search("微光 浮光");checks["musicLibrary.multiFieldSearch"]=browser.SongList.Items.Count==1 && ((MusicLibraryView.MusicRow)browser.SongList.Items[0]).Song.PersistentId=="one";
            SelectSong("one");var row=(ListBoxItem)browser.SongList.ItemContainerGenerator.ContainerFromItem(browser.SongList.SelectedItem);
            Click(Descendants<Button>(row).Single());await browser.ActivePlayback!;
            checks["musicLibrary.rowPlayUsesSelectedRecording"]=played.SequenceEqual(new[]{"one"}) && browser.StatusText=="已确认 · 微光之间";
            browser.SongList.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this),0,Key.Enter){RoutedEvent=Keyboard.PreviewKeyDownEvent});await browser.ActivePlayback!;
            checks["musicLibrary.enterPlaysSelectedSong"]=played.Count==2;
            browser.SongList.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=Control.MouseDoubleClickEvent,Source=row});await browser.ActivePlayback!;
            checks["musicLibrary.doubleClickPlaysSong"]=played.Count==3;
            await Search("浮光");SelectSong("one");Click(ButtonWith("播放所选"));var earlier=browser.ActivePlayback!;
            SelectSong("two");Click(ButtonWith("播放所选"));await Task.WhenAll(earlier,browser.ActivePlayback!);
            checks["musicLibrary.rapidSelectionCancelsPrevious"]=cancelled==1 && played.Last()=="two" && browser.StatusText=="已确认 · 夜航";
            playbackUnavailable=true;Click(ButtonWith("播放所选"));await browser.ActivePlayback!;checks["musicLibrary.playbackTimeoutHandled"]=browser.StatusText.Contains("播放切换失败");playbackUnavailable=false;
            Click(ButtonWith("播放所选"));await browser.ActivePlayback!;
            browser.SetNowPlaying(fixture[1].Track,true);
            checks["musicLibrary.nowPlayingTracksActualRecording"]=browser.SongList.Items.Cast<MusicLibraryView.MusicRow>().Single(r=>r.NowPlaying).Song.PersistentId=="two";
            browser.SortChoice.SelectedIndex=2;UpdateLayout();
            checks["musicLibrary.albumSort"]=browser.SongList.Items.Cast<MusicLibraryView.MusicRow>().Select(r=>r.Song.Album).SequenceEqual(browser.SongList.Items.Cast<MusicLibraryView.MusicRow>().Select(r=>r.Song.Album).OrderBy(s=>s,StringComparer.CurrentCulture));
            Click(ButtonWith("缓存管理"));checks["musicLibrary.cacheManagementPreserved"]=manageCount==1;
            vm.CanPlay=vm.CanPrevious=vm.CanNext=true;Click(ButtonWith("‹"));Click(ButtonWith("Ⅱ"));Click(ButtonWith("›"));checks["musicLibrary.transportCallbacks"]=transportCount==3;
            foreach(var app in AppThemes.All)
            {
                SelectUiTheme(app.Id);await Search("浮光");vm.Title=fixture[1].Title;vm.Artist="浮光 · 夜色 · 原创界面示例";await Task.Delay(80);
                VisualVerification.Save((FrameworkElement)Content,Path.Combine(directory,"music-library-"+app.Id+".png"),true);
                checks["musicLibrary."+app.Id+".paletteInherited"]=browser.FindResource("TextBrush")==FindResource("TextBrush");
            }
            await Search("");
            foreach(var width in new[]{1220d,980d,820d})
            {
                Width=width;Height=width==820?640:820;await Task.Delay(80);UpdateLayout();
                var bounds=browser.TransformToAncestor(MainPageHost).TransformBounds(new Rect(0,0,browser.ActualWidth,browser.ActualHeight));
                checks["musicLibrary.layout."+width]=bounds.Right<=MainPageHost.ActualWidth+.5 && bounds.Bottom<=MainPageHost.ActualHeight+.5 && browser.SongList.ActualHeight>100 && ButtonWith("回到歌词").IsVisible;
            }
            await Search("不存在的唯一测试");checks["musicLibrary.noResultsState"]=browser.SongList.Items.Count==0 && browser.HasEmptyMessage;
            mode=1;await browser.RefreshAsync();checks["musicLibrary.emptyLibraryState"]=browser.SongList.Items.Count==0 && browser.HasEmptyMessage;
            mode=2;await browser.RefreshAsync();checks["musicLibrary.readFailureState"]=browser.StatusText.Contains("暂时无法读取");
            mode=0;await Search("");await browser.RefreshAsync();checks["musicLibrary.refreshRecovers"]=browser.SongList.Items.Count==3003;
            Click(ButtonWith("回到歌词"));checks["musicLibrary.returnToLyrics"]=LyricsPage.IsVisible && !browser.IsVisible;
            await ShowMusicLibraryAsync();checks["musicLibrary.reopenPreservesLoadedLibrary"]=browser.SongList.Items.Count==3003;
            var cache=new MusicLibraryView.ThumbnailCache();
            for(int i=0;i<140;i++)cache.Get(Path.Combine(fixtureRoot,"missing-"+i+".png"));
            checks["musicLibrary.thumbnailCacheBounded"]=cache.Count==128;
            string bad=Path.Combine(fixtureRoot,"invalid.png");File.WriteAllText(bad,"invalid fixture");checks["musicLibrary.corruptCoverHandled"]=cache.Get(bad) is null;
            await VerifyAlbumBrowserAsync(browser,directory,checks,async large=>{mode=large?3:0;await browser.RefreshAsync();});
            await VerifyAlbumPlaybackAsync(browser,directory,checks,()=>playedAlbums,()=>albumCancelled,value=>albumUnavailable=value);
            await VerifyNativeSelectorIsolationAsync(checks);
            await VerifyAlbumArtworkAsync(browser,directory,checks,coverPaths,value=>mode=value,()=>loadCount);
            browser.ShowAllSongs();
            await Search("微光");SelectSong("one");int cancelledBeforeDispose=cancelled;Click(ButtonWith("播放所选"));var pending=browser.ActivePlayback!;browser.Dispose();await pending;
            checks["musicLibrary.disposeCancelsPlayback"]=cancelled==cancelledBeforeDispose+1;
        }
        finally
        {
            browser.Dispose();MainPageHost.Children.Remove(browser);musicLibrary=originalLibrary;ShowLyricsPage();vm.Title=originalTitle;vm.Artist=originalArtist;Width=1220;Height=820;SelectUiTheme(originalTheme);Update();
        }
    }
}
