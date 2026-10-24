using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

public partial class MainWindow
{
    async Task VerifyAlbumBrowserAsync(MusicLibraryView browser,string directory,Dictionary<string,object?> checks,Func<bool,Task> loadLarge)
    {
        async Task Search(string text){browser.SearchBox.Text=text;for(int attempt=0;attempt<100 && browser.IsFiltering;attempt++)await Task.Delay(20);UpdateLayout();}
        void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent,button));
        Button Card()=>Descendants<Button>(browser.AlbumShelf).First(b=>b.DataContext is MusicAlbum);
        Width=1220;Height=820;browser.ShowAlbums();await Search("创作集0 工作室0");
        checks["albums.albumAndArtistSearch"]=browser.AlbumShelf.Albums.Count==1;
        var album=browser.AlbumShelf.Albums.Single();Click(Card());await Task.Delay(80);UpdateLayout();
        checks["albums.cardOpensDetail"]=browser.SelectedAlbum?.Key==album.Key && !browser.IsAlbumShelf;
        checks["albums.detailContainsOnlyThisAlbum"]=browser.SongList.Items.Count==album.Songs.Count && browser.SongList.Items.Cast<MusicLibraryView.MusicRow>().All(r=>album.Songs.Contains(r.Song));
        checks["albums.detailCoverUsesDecodedImage"]=Descendants<Image>(browser).Any(i=>i.IsVisible && i.Source is BitmapImage {PixelWidth:360});
        browser.SongList.SelectedIndex=0;string first=((MusicLibraryView.MusicRow)browser.SongList.SelectedItem).Song.Title;
        browser.SongList.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this),0,Key.Enter){RoutedEvent=Keyboard.PreviewKeyDownEvent});await browser.ActivePlayback!;
        checks["albums.detailPlaysChosenTrack"]=browser.StatusText=="已确认 · "+first;
        await Search("片段0024");checks["albums.detailSearchStaysInAlbum"]=browser.SongList.Items.Count==1;
        Click(Descendants<Button>(browser).Single(b=>b.Content?.ToString()=="‹ 所有专辑"));await Task.Delay(80);UpdateLayout();
        checks["albums.backRestoresParentSearch"]=browser.IsAlbumShelf && browser.SearchBox.Text=="创作集0 工作室0" && browser.AlbumShelf.Albums.Count==1;
        await Search("不存在的专辑");checks["albums.noResultsState"]=browser.AlbumShelf.Albums.Count==0 && browser.HasEmptyMessage;
        foreach(var theme in AppThemes.All)
        {
            SelectUiTheme(theme.Id);await Search("浮光");
            checks["albums."+theme.Id+".cardsAndPalette"]=browser.AlbumShelf.Albums.Count==3 && browser.AlbumShelf.FindResource("TextBrush")==FindResource("TextBrush") && browser.AlbumShelf.Background is SolidColorBrush {Color.A:0};
            VisualVerification.Save((FrameworkElement)Content,Path.Combine(directory,"albums-"+theme.Id+".png"),true);
        }
        var originals=browser.AlbumShelf.Albums.First(a=>a.Songs.Any(s=>s.PersistentId=="two"));browser.OpenAlbum(originals);await Task.Delay(70);UpdateLayout();
        VisualVerification.Save((FrameworkElement)Content,Path.Combine(directory,"album-detail.png"),true);
        foreach(var width in new[]{1220d,980d,820d})
        {
            Width=width;Height=width==820?640:820;await Task.Delay(80);UpdateLayout();
            checks["albums.detailLayout."+width]=browser.SongList.ActualHeight>100 && browser.SelectedAlbum?.Key==originals.Key;
        }
        Width=1220;Height=820;browser.ShowAlbums();await Search("");await loadLarge(true);await Task.Delay(100);UpdateLayout();
        checks["albums.largeCatalogRetainsEveryAlbum"]=browser.AlbumShelf.Albums.Count==3003;
        checks["albums.virtualizedShelfRows"]=Descendants<ListBoxItem>(browser.AlbumShelf).Count() is >0 and <30 && browser.AlbumShelf.Items.Count>400;
        checks["albums.lazyHighQualityCovers"]=browser.CachedAlbumCoverCount is >0 and <=128 && Descendants<Image>(browser.AlbumShelf).Any(i=>i.Source is BitmapImage {PixelWidth:360,IsFrozen:true});
        checks["albums.missingCoverPlaceholder"]=Descendants<TextBlock>(browser.AlbumShelf).Any(t=>t.Text=="◉");
        int previousColumns=99;
        foreach(var width in new[]{1220d,980d,820d})
        {
            Width=width;Height=width==820?640:820;await Task.Delay(100);UpdateLayout();var card=Card();var cover=Descendants<Border>(card).First(b=>b.Child is Image or TextBlock);
            checks["albums.responsiveCards."+width]=browser.AlbumShelf.Columns<=previousColumns && cover.ActualWidth>100 && Math.Abs(cover.ActualWidth-cover.ActualHeight)<1;
            previousColumns=browser.AlbumShelf.Columns;
        }
        async Task<double> WaitForOffset(double expected)
        {
            double actual=0;
            for(int attempt=0;attempt<20;attempt++){await Task.Delay(50);UpdateLayout();actual=browser.AlbumShelf.ScrollOffset;if(Math.Abs(actual-expected)<2)break;}
            return actual;
        }
        Width=1220;Height=820;await Task.Delay(60);UpdateLayout();browser.AlbumShelf.RestoreOffset(900);double offset=await WaitForOffset(900);
        var selected=Descendants<Button>(browser.AlbumShelf).First(b=>b.DataContext is MusicAlbum);Click(selected);await Task.Delay(60);browser.ShowAlbums();double restoredOffset=await WaitForOffset(offset);
        checks["albums.shelfOffsetBeforeNavigation"]=offset;checks["albums.shelfOffsetAfterNavigation"]=restoredOffset;
        checks["albums.backRestoresShelfPosition"]=offset>0 && Math.Abs(restoredOffset-offset)<2;
        var focusCard=Card();focusCard.Focus();checks["albums.cardsAcceptKeyboardFocus"]=focusCard.IsKeyboardFocused;
        await loadLarge(false);browser.ShowAllSongs();await Search("微光 浮光");checks["albums.allSongsViewStillAvailable"]=browser.SongList.Items.Count==1 && browser.SelectedAlbum is null;
        browser.ShowAllSongs();checks["albums.activeSongsTabPreservesQuery"]=browser.SearchBox.Text=="微光 浮光" && browser.SongList.Items.Count==1;
        browser.ShowAlbums();await Search("浮光");browser.ShowAlbums();checks["albums.activeAlbumsTabPreservesQuery"]=browser.SearchBox.Text=="浮光" && browser.AlbumShelf.Albums.Count==3;
        browser.ShowAlbums();await Search("浮光");var retained=browser.AlbumShelf.Albums.First();browser.OpenAlbum(retained);await browser.RefreshAsync();
        checks["albums.refreshPreservesSelectedAlbum"]=browser.SelectedAlbum?.Key==retained.Key && browser.SongList.Items.Count==retained.Songs.Count;
        browser.ShowAllSongs();
    }
}
