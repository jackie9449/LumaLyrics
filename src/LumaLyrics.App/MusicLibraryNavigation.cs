using System.Windows;
using System.Windows.Controls;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

public partial class MainWindow
{
    LyricsReviewWindow? lyricsReview;
    internal void ShowLyricsReview(Window owner)
    {
        if(lyricsReview is null){lyricsReview=new(coordinator,playbackBridge.CaptureLyricsReviewAsync){Owner=owner};lyricsReview.Closed+=(_,_)=>lyricsReview=null;lyricsReview.Show();}else lyricsReview.Activate();
    }
    MusicLibraryView? musicLibrary;
    async void MusicLibraryClicked(object sender,RoutedEventArgs e)=>await ShowMusicLibraryAsync();
    internal async Task ShowMusicLibraryAsync()
    {
        if(musicLibrary is null)
        {
            var bridge=playbackBridge;
            musicLibrary=new(token=>Task.Run(()=>new MusicLibraryCatalog(store.Root,settings.LibraryPath).Load(token),token),bridge.PlayAsync,
                ()=>LibraryClicked(this,new()),ShowLyricsPage,()=>{if(IsVisible && WindowState!=WindowState.Minimized)Activate();},vm,
                ()=>_=ControlAsync("previous"),()=>PlayClicked(this,new()),()=>_=ControlAsync("next"),bridge.PlayAlbumAsync,bridge.ReadQueueAsync,bridge.InsertNextAsync,bridge.PlayQueueAsync,bridge.ReorderQueueAsync);
            MainPageHost.Children.Add(musicLibrary);
        }
        LyricsPage.Visibility=Visibility.Collapsed;musicLibrary.Visibility=Visibility.Visible;SelectNavigation(true);
        AppMotion.Reveal(musicLibrary);
        musicLibrary.ObserveArtworkProgress(coordinator.ArtworkStatus);
        await musicLibrary.EnsureLoadedAsync();musicLibrary.SetNowPlaying(coordinator.ActiveTrack,coordinator.Snapshot?.Status==LumaLyrics.Core.PlayState.Playing);
    }
    void ShowLyricsPage()
    {
        if(musicLibrary is not null)musicLibrary.Visibility=Visibility.Collapsed;LyricsPage.Visibility=Visibility.Visible;SelectNavigation(false);
        AppMotion.Reveal(LyricsPage);
    }
    void SelectNavigation(bool library)
    {
        foreach(var (button,active) in new[]{(LyricsNav,!library),(MusicNav,library)})
        {
            AppMotion.SetIsSelected(button,active);
            button.SetResourceReference(Control.ForegroundProperty,active?"AccentBrush":"MutedBrush");
            if(active)button.SetResourceReference(Control.BackgroundProperty,"AccentSoftBrush");else button.ClearValue(Control.BackgroundProperty);
        }
    }
}
