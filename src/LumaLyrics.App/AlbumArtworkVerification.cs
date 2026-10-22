using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

public partial class MainWindow
{
    async Task VerifyAlbumArtworkAsync(MusicLibraryView browser,string directory,Dictionary<string,object?> checks,IReadOnlyList<string> covers,Action<int> mode,Func<int> loads)
    {
        async Task Search(string query){browser.SearchBox.Text=query;await Task.Delay(180);UpdateLayout();}
        void Progress(int cached,string message)=>browser.ObserveArtworkProgress(new(3003,cached,cached,0,0,cached<3003,message));
        async Task Settle(){await Task.Delay(1350);UpdateLayout();}
        int Images()=>Descendants<Image>(browser.AlbumShelf).Count(i=>i.Source is BitmapImage{PixelWidth:360,IsFrozen:true});

        mode(4);browser.ShowAlbums();await Search("浮光");await browser.RefreshAsync();
        checks["albumArtwork.initialMissingState"]=Images()==0;
        mode(0);await ShowMusicLibraryAsync();await Task.Delay(100);UpdateLayout();
        checks["albumArtwork.reopenSeesImportedCovers"]=Images()==3;
        var cache=new MusicLibraryView.ThumbnailCache(360);
        checks["albumArtwork.pngCacheFormatDecodes"]=cache.Get(covers[0]) is BitmapImage{PixelWidth:360,IsFrozen:true};
        checks["albumArtwork.jpegCacheFormatDecodes"]=cache.Get(covers[1]) is BitmapImage{PixelWidth:360,IsFrozen:true};

        mode(4);await browser.RefreshAsync();mode(0);int before=loads();
        for(int i=1;i<=12;i++)Progress(i,"导入原创封面 "+i);
        await Settle();
        checks["albumArtwork.visibleImportsUpdateAutomatically"]=Images()==3;
        checks["albumArtwork.importBurstsAreCoalesced"]=loads()==before+1;
        checks["albumArtwork.albumSearchSurvivesRefresh"]=browser.SearchBox.Text=="浮光" && browser.AlbumShelf.Albums.Count==3;
        VisualVerification.Save((FrameworkElement)Content,Path.Combine(directory,"album-artwork-imported.png"),true);

        await Search("");browser.AlbumShelf.RestoreOffset(500);await Task.Delay(90);double offset=browser.AlbumShelf.ScrollOffset;
        Progress(20,"继续导入");await Settle();
        checks["albumArtwork.albumScrollSurvivesRefresh"]=offset>0 && Math.Abs(browser.AlbumShelf.ScrollOffset-offset)<2;

        await Search("夜航");var album=browser.AlbumShelf.Albums.Single();browser.OpenAlbum(album);await Task.Delay(80);
        await Search("夜航");browser.SongList.SelectedIndex=0;string selected=((MusicLibraryView.MusicRow)browser.SongList.SelectedItem).Song.Key;
        browser.SetNowPlaying(album.Songs.Single().Track,true);string status=browser.StatusText;
        Progress(30,"详情中的封面更新");await Settle();
        checks["albumArtwork.detailContextSurvivesRefresh"]=browser.SelectedAlbum?.Key==album.Key && browser.SearchBox.Text=="夜航" && ((MusicLibraryView.MusicRow?)browser.SongList.SelectedItem)?.Song.Key==selected;
        checks["albumArtwork.playbackStateSurvivesRefresh"]=browser.StatusText==status && browser.SongList.Items.Cast<MusicLibraryView.MusicRow>().Single().NowPlaying;
        checks["albumArtwork.detailAndSongCoversRefresh"]=Descendants<Image>(browser).Any(i=>i.IsVisible && i.Source is BitmapImage{PixelWidth:360}) && Descendants<Image>(browser.SongList).Any(i=>i.Source is BitmapImage{PixelWidth:96});
        VisualVerification.Save((FrameworkElement)Content,Path.Combine(directory,"album-artwork-detail.png"),true);

        mode(2);Progress(40,"临时读锁");before=loads();await Settle();await Task.Delay(1150);
        checks["albumArtwork.transientFailureRetainsPage"]=loads()==before+1 && browser.SelectedAlbum?.Key==album.Key && browser.StatusText==status && !browser.HasEmptyMessage;
        mode(0);Progress(3003,"封面导入完成");await Settle();
        checks["albumArtwork.completionRetriesRead"]=loads()==before+2;

        browser.Visibility=Visibility.Collapsed;before=loads();Progress(3003,"外部导入完成");await Settle();
        checks["albumArtwork.hiddenPageDefersWork"]=loads()==before;
        await ShowMusicLibraryAsync();checks["albumArtwork.reentryRetainsAlbumContext"]=browser.SelectedAlbum?.Key==album.Key && browser.SearchBox.Text=="夜航";

        string samePath=Path.Combine(Path.GetDirectoryName(covers[0])!,"same-path.img");var images=new MusicLibraryView.ThumbnailCache(360);
        File.Copy(covers[0],samePath,true);var original=(BitmapImage)images.Get(samePath)!;byte[] first=new byte[4];original.CopyPixels(new Int32Rect(0,0,1,1),first,4,0);
        File.Copy(covers[1],samePath,true);images.Clear();var replacement=(BitmapImage)images.Get(samePath)!;byte[] second=new byte[4];replacement.CopyPixels(new Int32Rect(0,0,1,1),second,4,0);
        checks["albumArtwork.samePathReplacementUsesNewPixels"]=!first.SequenceEqual(second) && replacement.IsFrozen;
        browser.ShowAllSongs();
    }
}
