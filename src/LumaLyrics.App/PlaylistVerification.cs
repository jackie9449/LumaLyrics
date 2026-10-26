using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

public partial class MainWindow
{
    async Task VerifyPlaylistsAsync(string directory,Dictionary<string,object?> checks)
    {
        string originalTheme=settings.UiTheme;var previous=musicLibrary;
        MusicLibrarySong Song(string id,string title)=>new("fixture",id,new(title,"浮光工作室","原创测试歌集",180000));
        var a=Song("a","晚风与海");var b=Song("b","星光旅途");
        var sample=new MusicPlaylist("fixture","original","海边的夜晚",4,new[]{b,a,b,a},true);
        var others=Enumerable.Range(0,350).Select(i=>new MusicPlaylist("fixture","p"+i,"原创歌单 "+i.ToString("000"),2,new[]{a,b},true)).ToArray();
        int mode=0,activations=0,cancellations=0;bool fail=false;var played=new List<string>();
        var browser=new MusicLibraryView(_=>Task.FromResult(mode switch
        {
            1=>new MusicLibraryResult([a,b],"暂不可读"){PlaylistsUnavailable=true},
            2=>new MusicLibraryResult([a,b],""){Playlists=[]},
            _=>new MusicLibraryResult([a,b],""){Playlists=new[]{sample}.Concat(others).ToArray()}
        }),(_,_)=>Task.FromResult(new LibraryPlayResult(true,"单曲已确认")),()=>{},ShowLyricsPage,()=>activations++,vm,()=>{},()=>{},()=>{},
        readQueue:_=>Task.FromResult(MusicQueueSnapshot.Unavailable("原创队列示例")),playPlaylist:async (p,token)=>
        {
            try{await Task.Delay(p.Id==sample.Id?400:10,token);played.Add(p.Id);return new(!fail,fail?"歌单未获确认":"已确认歌单 · "+p.Name);}
            catch(OperationCanceledException){cancellations++;throw;}
        });
        musicLibrary=browser;MainPageHost.Children.Add(browser);
        void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent,button));
        Button Play()=>Descendants<Button>(browser).Single(b=>b.Content?.ToString()=="▷ 播放歌单");
        async Task Search(string text){browser.SearchBox.Text=text;while(browser.IsFiltering)await Task.Delay(20);UpdateLayout();}
        try
        {
            await ShowMusicLibraryAsync();checks["playlists.defaultRemainsAlbums"]=browser.IsAlbumShelf && !browser.IsPlaylistVisible;
            browser.ShowPlaylists();await Task.Delay(80);UpdateLayout();
            checks["playlists.shelfContainsOnlyPlaylists"]=browser.PlaylistShelf.Items.Count==351;
            checks["playlists.shelfVirtualized"]=Descendants<ListBoxItem>(browser.PlaylistShelf).Count() is >0 and <40;
            var shelfScroll=Descendants<ScrollViewer>(browser.PlaylistShelf).First();shelfScroll.ScrollToVerticalOffset(400);await Task.Delay(40);double shelfOffset=shelfScroll.VerticalOffset;
            browser.OpenPlaylist(sample);browser.ShowPlaylists();await Task.Delay(60);UpdateLayout();
            checks["playlists.backRestoresShelfOffset"]=shelfOffset>0 && Math.Abs(shelfScroll.VerticalOffset-shelfOffset)<2;

            await Search("星光");checks["playlists.searchIncludesSongs"]=browser.PlaylistShelf.Items.Count==351;
            await Search("海边");checks["playlists.nameSearch"]=browser.PlaylistShelf.Items.Count==1;
            browser.PlaylistShelf.SelectedIndex=0;browser.PlaylistShelf.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this),0,Key.Enter){RoutedEvent=Keyboard.PreviewKeyDownEvent});UpdateLayout();
            checks["playlists.enterOpensDetail"]=browser.SelectedPlaylist?.Id==sample.Id;
            checks["playlists.originalOrderAndDuplicates"]=browser.PlaylistTracks.Items.Cast<MusicLibraryView.PlaylistTrack>().Select(r=>r.Song.PersistentId).SequenceEqual(new[]{"b","a","b","a"});
            await Search("星光");checks["playlists.detailFilterKeepsOccurrenceNumbers"]=browser.PlaylistTracks.Items.Cast<MusicLibraryView.PlaylistTrack>().Select(r=>r.Number).SequenceEqual(new[]{1,3});
            Click(Play());await browser.ActivePlayback!;checks["playlists.playUsesEntireSelectedPlaylist"]=played.SequenceEqual(new[]{sample.Id}) && activations==1;
            fail=true;Click(Play());await browser.ActivePlayback!;checks["playlists.unconfirmedDoesNotActivate"]=activations==1 && browser.StatusText=="歌单未获确认";fail=false;
            Click(Play());var slow=browser.ActivePlayback!;browser.OpenPlaylist(others[0]);Click(Play());await Task.WhenAll(slow,browser.ActivePlayback!);
            checks["playlists.latestSelectionCancelsEarlier"]=cancellations==1 && played.Last()==others[0].Id && activations==2;
            browser.OpenPlaylist(sample);await Search("星光");browser.ShowQueue();await Task.Delay(40);browser.HideQueue();
            checks["playlists.queueReturnKeepsContext"]=browser.SelectedPlaylist?.Id==sample.Id && browser.SearchBox.Text=="星光" && browser.PlaylistTracks.Items.Count==2;
            mode=1;await browser.RefreshAsync();checks["playlists.transientReadRetainsLastVerifiedList"]=browser.SelectedPlaylist?.Id==sample.Id && browser.Playlists.Count==351;
            mode=0;await browser.RefreshAsync();browser.ShowPlaylists();checks["playlists.backRestoresShelfSearch"]=browser.SearchBox.Text=="海边";
            browser.ShowAllSongs();await Search("");checks["playlists.globalSongsNotInflated"]=browser.SongList.Items.Count==2 && browser.SortChoice.IsVisible && !browser.PlaylistShelf.IsVisible;
            await Search("晚风");mode=2;await browser.RefreshAsync();checks["playlists.removalDoesNotReplaceSongQuery"]=browser.SearchBox.Text=="晚风" && browser.SongList.Items.Count==1;
            mode=0;await browser.RefreshAsync();
            browser.ShowAlbums();await Search("原创");browser.ShowPlaylists();browser.ShowAlbums();checks["playlists.albumSearchRestored"]=browser.SearchBox.Text=="原创" && browser.IsAlbumShelf;
            foreach(var theme in AppThemes.All)
            {
                SelectUiTheme(theme.Id);
                foreach(double width in new[]{1220d,980d,820d})
                {
                    Width=width;Height=width==820?640:820;browser.ShowPlaylists();await Search("");await Task.Delay(65);UpdateLayout();
                    var shelfBounds=browser.PlaylistShelf.TransformToAncestor(browser).TransformBounds(new Rect(browser.PlaylistShelf.RenderSize));
                    checks[$"playlists.{theme.Id}.{width}.shelfBounds"]=shelfBounds.Right<=browser.ActualWidth+.5 && shelfBounds.Bottom<=browser.ActualHeight+.5 && browser.PlaylistShelf.ActualHeight>75;
                    var tabs=Descendants<Button>(browser).Where(b=>b.Content?.ToString() is "专辑" or "歌曲" or "歌单" or "播放列表" or "缓存管理").ToArray();
                    checks[$"playlists.{theme.Id}.{width}.navigationFits"]=tabs.All(t=>t.TransformToAncestor(browser).TransformBounds(new Rect(t.RenderSize)).Right<=browser.ActualWidth+.5);
                    if(width==1220)VisualVerification.Save((FrameworkElement)Content,Path.Combine(directory,"playlist-shelf-"+theme.Id+".png"),true);
                    browser.OpenPlaylist(sample);await Task.Delay(45);UpdateLayout();
                    checks[$"playlists.{theme.Id}.{width}.detailVisible"]=browser.PlaylistTracks.IsVisible && browser.PlaylistTracks.ActualHeight>75 && Play().IsVisible && Play().IsEnabled;
                    if(width==820)VisualVerification.Save((FrameworkElement)Content,Path.Combine(directory,"playlist-detail-"+theme.Id+".png"),true);
                }
            }
            browser.OpenPlaylist(sample with{Complete=false});checks["playlists.incompleteCannotPlay"]=!Play().IsEnabled;
            browser.ShowPlaylists();await Search("无此原创歌单");checks["playlists.emptySearch"]=browser.PlaylistShelf.Items.Count==0 && browser.HasEmptyMessage;
            mode=2;await Search("");await browser.RefreshAsync();checks["playlists.emptyLibrary"]=browser.Playlists.Count==0 && browser.HasEmptyMessage;
            mode=0;await browser.RefreshAsync();browser.OpenPlaylist(sample);Click(Play());var pending=browser.ActivePlayback!;browser.Dispose();await pending;checks["playlists.disposeCancelsPlayback"]=cancellations==2;
        }
        finally{browser.Dispose();MainPageHost.Children.Remove(browser);musicLibrary=previous;ShowLyricsPage();Width=1220;Height=820;SelectUiTheme(originalTheme);}
    }
}
