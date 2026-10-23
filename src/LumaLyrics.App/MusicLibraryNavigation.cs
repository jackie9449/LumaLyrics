using System.Windows;
using System.Windows.Controls;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

public partial class MainWindow
{
    MusicLibraryView? musicLibrary;
    async void MusicLibraryClicked(object sender,RoutedEventArgs e)=>await ShowMusicLibraryAsync();
    internal async Task ShowMusicLibraryAsync()
    {
        if(musicLibrary is null)
        {
            var bridge=new AppleMusicPlaybackBridge(coordinator);
            musicLibrary=new(token=>Task.Run(()=>new MusicLibraryCatalog(store.Root,settings.LibraryPath).Load(token),token),bridge.PlayAsync,
                ()=>LibraryClicked(this,new()),ShowLyricsPage,()=>{if(IsVisible && WindowState!=WindowState.Minimized)Activate();},vm,
                ()=>_=ControlAsync("previous"),()=>PlayClicked(this,new()),()=>_=ControlAsync("next"),bridge.PlayAlbumAsync,bridge.ReadQueueAsync,bridge.InsertNextAsync);
            MainPageHost.Children.Add(musicLibrary);
        }
        LyricsPage.Visibility=Visibility.Collapsed;musicLibrary.Visibility=Visibility.Visible;SelectNavigation(true);
        musicLibrary.ObserveArtworkProgress(coordinator.ArtworkStatus);
        await musicLibrary.EnsureLoadedAsync();musicLibrary.SetNowPlaying(coordinator.ActiveTrack,coordinator.Snapshot?.Status==LumaLyrics.Core.PlayState.Playing);
    }
    void ShowLyricsPage()
    {
        if(musicLibrary is not null)musicLibrary.Visibility=Visibility.Collapsed;LyricsPage.Visibility=Visibility.Visible;SelectNavigation(false);
    }
    void SelectNavigation(bool library)
    {
        foreach(var (button,active) in new[]{(LyricsNav,!library),(MusicNav,library)})
        {
            button.SetResourceReference(Control.ForegroundProperty,active?"AccentBrush":"MutedBrush");
            if(active)button.SetResourceReference(Control.BackgroundProperty,"AccentSoftBrush");else button.ClearValue(Control.BackgroundProperty);
        }
    }
}
