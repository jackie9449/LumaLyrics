using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

public partial class MainWindow
{
    async Task VerifyAlbumPlaybackAsync(MusicLibraryView browser,string directory,Dictionary<string,object?> checks,Func<IReadOnlyList<MusicAlbum>> played,Func<int> cancelled,Action<bool> unavailable)
    {
        async Task Search(string text){browser.SearchBox.Text=text;await Task.Delay(200);UpdateLayout();}
        void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent,button));
        Button AlbumPlay(string title)=>Descendants<Button>(browser.AlbumShelf).Single(b=>b.DataContext is MusicAlbum a && a.Title==title && AutomationProperties.GetName(b).StartsWith("播放专辑 · ",StringComparison.Ordinal));
        Button DetailPlay()=>Descendants<Button>(browser).Single(b=>AutomationProperties.GetName(b)=="播放当前专辑");
        Button RowPlay()=>Descendants<Button>((ListBoxItem)browser.SongList.ItemContainerGenerator.ContainerFromItem(browser.SongList.SelectedItem)).Single();
        void SelectSong(string id){browser.SongList.SelectedItem=browser.SongList.Items.Cast<MusicLibraryView.MusicRow>().Single(r=>r.Song.PersistentId==id);browser.SongList.ScrollIntoView(browser.SongList.SelectedItem);UpdateLayout();}

        browser.ShowAlbums();await Search("创作集0 工作室0");var whole=browser.AlbumShelf.Albums.Single();
        var card=AlbumPlay(whole.Title);checks["albumPlay.cardAccessibleAndEnabled"]=card.IsEnabled && AutomationProperties.GetName(card).Contains(whole.Artist);
        Click(card);await browser.ActivePlayback!;
        checks["albumPlay.cardSubmitsEntireAlbum"]=played().Last().Key==whole.Key && played().Last().Songs.Count==whole.Songs.Count && whole.Songs.Count>1;
        checks["albumPlay.cardPreservesShelfAndQuery"]=browser.IsAlbumShelf && browser.SelectedAlbum is null && browser.SearchBox.Text=="创作集0 工作室0";
        checks["albumPlay.cardDoesNotNestInteractiveControls"]=!Descendants<Button>(Descendants<Button>(browser.AlbumShelf).First(b=>AutomationProperties.GetName(b).StartsWith("打开专辑 · ",StringComparison.Ordinal))).Any();
        browser.OpenAlbum(whole);await Task.Delay(60);UpdateLayout();Click(DetailPlay());await browser.ActivePlayback!;
        checks["albumPlay.detailSubmitsEntireAlbum"]=played().Last().Key==whole.Key && browser.SelectedAlbum?.Key==whole.Key && browser.StatusText=="已确认专辑 · "+whole.Title;
        checks["albumPlay.detailUsesThemeResources"]=DetailPlay().Foreground==FindResource("AccentTextBrush");
        VisualVerification.Save((FrameworkElement)Content,Path.Combine(directory,"album-play-detail.png"),true);
        browser.ShowAlbums();await Search("浮光");var slow=browser.AlbumShelf.Albums.Single(a=>a.Title=="原创测试");
        int before=cancelled();Click(AlbumPlay(slow.Title));var earlier=browser.ActivePlayback!;Click(AlbumPlay("夜色"));await Task.WhenAll(earlier,browser.ActivePlayback!);
        checks["albumPlay.newAlbumCancelsOldAlbum"]=cancelled()==before+1 && browser.StatusText=="已确认专辑 · 夜色";
        Click(AlbumPlay(slow.Title));earlier=browser.ActivePlayback!;before=cancelled();browser.ShowAllSongs();await Search("夜航");SelectSong("two");Click(RowPlay());await Task.WhenAll(earlier,browser.ActivePlayback!);
        checks["albumPlay.songCancelsPendingAlbum"]=cancelled()==before+1 && browser.StatusText=="已确认 · 夜航";
        await Search("微光");SelectSong("one");Click(RowPlay());earlier=browser.ActivePlayback!;browser.ShowAlbums();await Search("浮光");Click(AlbumPlay("夜色"));await Task.WhenAll(earlier,browser.ActivePlayback!);
        checks["albumPlay.albumCancelsPendingSong"]=browser.StatusText=="已确认专辑 · 夜色";
        unavailable(true);Click(AlbumPlay("夜色"));await browser.ActivePlayback!;unavailable(false);
        checks["albumPlay.unconfirmedResultShowsFailure"]=browser.StatusText.StartsWith("专辑播放未获确认",StringComparison.Ordinal) && browser.IsAlbumShelf;
        var empty=new MusicAlbum("empty-fixture","原创空专辑","浮光",[]);browser.OpenAlbum(empty);await Task.Delay(50);UpdateLayout();int calls=played().Count;
        checks["albumPlay.emptyAlbumDisabled"]=!DetailPlay().IsEnabled;Click(DetailPlay());await browser.ActivePlayback!;
        checks["albumPlay.emptyAlbumDoesNotInvokePlayer"]=played().Count==calls;
        browser.ShowAlbums();await Search("浮光");Click(AlbumPlay("夜色"));await browser.ActivePlayback!;
        foreach(var theme in AppThemes.All)
        {
            SelectUiTheme(theme.Id);browser.ShowAlbums();await Search("浮光");
            var button=AlbumPlay("夜色");button.Focus();
            checks["albumPlay."+theme.Id+".keyboardAndTheme"]=button.IsKeyboardFocused && button.Background==FindResource("AccentBrush") && button.Foreground==FindResource("AccentTextBrush");
            VisualVerification.Save((FrameworkElement)Content,Path.Combine(directory,"album-play-"+theme.Id+".png"),true);
        }
        foreach(var width in new[]{1220d,980d,820d})
        {
            Width=width;Height=width==820?640:820;var album=browser.AlbumShelf.Albums.First();browser.OpenAlbum(album);await Task.Delay(80);UpdateLayout();
            var button=DetailPlay();var bounds=button.TransformToAncestor(browser).TransformBounds(new Rect(0,0,button.ActualWidth,button.ActualHeight));
            checks["albumPlay.detailButtonFits."+width]=button.IsVisible && bounds.Left>=0 && bounds.Right<=browser.ActualWidth && browser.SongList.ActualHeight>100;
            browser.ShowAlbums();await Search("浮光");
        }
        Width=1220;Height=820;browser.ShowAllSongs();
    }
}
