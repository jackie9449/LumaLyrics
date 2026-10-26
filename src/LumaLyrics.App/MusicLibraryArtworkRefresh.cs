using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

internal sealed partial class MusicLibraryView
{
    readonly DispatcherTimer artworkRefresh=new(){Interval=TimeSpan.FromSeconds(1)};
    ArtworkProgress? observedArtwork;
    int artworkRevision,artworkLoadedRevision;
    Track? nowPlayingTrack;bool nowPlaying;

    void SetupArtworkRefresh()
    {
        artworkRefresh.Tick+=async(_,_)=>
        {
            artworkRefresh.Stop();
            if(!disposed && loaded && IsVisible && refresh.IsEnabled && artworkRevision!=artworkLoadedRevision)await RefreshAsync(true);
        };
        IsVisibleChanged+=(_,_)=>{if(IsVisible)ScheduleArtworkRefresh();else artworkRefresh.Stop();};
    }
    internal void ObserveArtworkProgress(ArtworkProgress progress)
    {
        if(disposed || observedArtwork==progress)return;
        bool changed=observedArtwork is not null;observedArtwork=progress;
        if(changed){artworkRevision++;ScheduleArtworkRefresh();}
    }
    void ScheduleArtworkRefresh()
    {
        // Throttle rather than resetting on every imported image: long-running
        // imports must update visible covers before the whole queue completes.
        if(!disposed && loaded && IsVisible && refresh.IsEnabled && artworkRevision!=artworkLoadedRevision && !artworkRefresh.IsEnabled)artworkRefresh.Start();
    }
    sealed record ArtworkBrowsingContext(bool Shelf,string? Album,string Query,double ShelfOffset,double SongOffset,string? SelectedSong,bool Playlists,string? Playlist,double PlaylistOffset);
    ArtworkBrowsingContext CaptureArtworkContext()=>new(showAlbumShelf,selectedAlbum?.Key,search.Text,albumShelf.ScrollOffset,FindArtworkScroll(songs)?.VerticalOffset??0,(songs.SelectedItem as MusicRow)?.Song.Key,showingPlaylists,selectedPlaylist?.Key,FindArtworkScroll(selectedPlaylist is null?playlistShelf:playlistTracks)?.VerticalOffset??0);
    async Task RestoreArtworkContextAsync(ArtworkBrowsingContext context)
    {
        if(context.SelectedSong is { } key)songs.SelectedItem=view?.Cast<MusicRow>().FirstOrDefault(r=>r.Song.Key==key);
        await Dispatcher.InvokeAsync(()=>
        {
            if(disposed || showingPlaylists!=context.Playlists || selectedPlaylist?.Key!=context.Playlist || showAlbumShelf!=context.Shelf || selectedAlbum?.Key!=context.Album || search.Text!=context.Query)return;
            if(showingPlaylists)FindArtworkScroll(selectedPlaylist is null?playlistShelf:playlistTracks)?.ScrollToVerticalOffset(context.PlaylistOffset);
            else if(showAlbumShelf)albumShelf.RestoreOffset(context.ShelfOffset);
            else FindArtworkScroll(songs)?.ScrollToVerticalOffset(context.SongOffset);
        },DispatcherPriority.Loaded);
    }
    static ScrollViewer? FindArtworkScroll(DependencyObject parent)
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {
            var child=VisualTreeHelper.GetChild(parent,i);
            if(child is ScrollViewer scroll)return scroll;
            if(FindArtworkScroll(child) is { } nested)return nested;
        }
        return null;
    }
}
