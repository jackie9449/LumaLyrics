using System.IO;
using System.Windows;
using System.Windows.Threading;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

internal sealed class PlaybackQueueSidebar:IDisposable
{
    readonly Window owner;
    readonly FrameworkElement scope;
    readonly Func<CancellationToken,Task<MusicQueueSnapshot>> read;
    readonly Func<CancellationToken,Task<MusicLibraryResult>> library;
    readonly Func<bool> busy;
    readonly DispatcherTimer timer=new();
    readonly RequestVersion versions=new();
    CancellationTokenSource? reading;
    bool enabled,disposed,libraryLoaded;
    int artworkRevision,loadedArtworkRevision;
    string current="";
    ArtworkProgress? artwork;
    internal PlaybackQueueView View {get;}
    internal Task? ActiveRefresh {get;private set;}
    internal bool IsPolling=>timer.IsEnabled;
    bool Active=>enabled && !disposed && scope.IsVisible && owner.IsVisible && owner.WindowState!=WindowState.Minimized;

    internal PlaybackQueueSidebar(Window owner,FrameworkElement scope,Func<CancellationToken,Task<MusicQueueSnapshot>> read,Func<CancellationToken,Task<MusicLibraryResult>> library,Action openLibrary,Func<bool>? busy=null)
    {
        this.owner=owner;this.scope=scope;this.read=read;this.library=library;this.busy=busy??(()=>false);
        View=new(openLibrary,()=>_=RefreshAsync(),true){Visibility=Visibility.Visible};View.Apply(MusicQueueSnapshot.Unavailable("连接 Apple Music 后显示播放列表"));
        scope.IsVisibleChanged+=VisibilityChanged;owner.StateChanged+=WindowStateChanged;timer.Tick+=Poll;
    }
    internal void Enable(){if(disposed)return;enabled=true;_=RefreshAsync();}
    void VisibilityChanged(object sender,DependencyPropertyChangedEventArgs e)=>VisibilityChanged();
    void WindowStateChanged(object? sender,EventArgs e)=>VisibilityChanged();
    void VisibilityChanged()
    {
        if(disposed)return;
        if(Active)_=RefreshAsync();else{timer.Stop();reading?.Cancel();versions.Next();ActiveRefresh=null;View.CancelLoading();}
    }
    void Poll(object? sender,EventArgs e){timer.Stop();_=RefreshAsync();}
    void Schedule(TimeSpan delay){if(!Active)return;timer.Stop();timer.Interval=delay;timer.Start();}
    internal void RequestRefresh()
    {
        if(!Active)return;
        if(ActiveRefresh?.IsCompleted==false){reading?.Cancel();versions.Next();ActiveRefresh=null;}
        Schedule(TimeSpan.FromMilliseconds(300));
    }
    internal void ObserveTrack(Track? track)
    {
        string key=track?.Fingerprint??"";if(key==current)return;current=key;RequestRefresh();
    }
    internal void ObserveArtwork(ArtworkProgress progress)
    {
        if(disposed || artwork==progress)return;bool changed=artwork is not null;artwork=progress;
        if(changed){artworkRevision++;if(ActiveRefresh?.IsCompleted!=false)Schedule(TimeSpan.FromSeconds(1));}
    }
    internal Task RefreshAsync(bool restart=false)
    {
        if(disposed || !Active)return Task.CompletedTask;
        if(!restart && ActiveRefresh?.IsCompleted==false)return ActiveRefresh;
        ActiveRefresh=RefreshCoreAsync();return ActiveRefresh;
    }
    async Task RefreshCoreAsync()
    {
        timer.Stop();
        if(busy()){View.WaitingForSelection();Schedule(TimeSpan.FromMilliseconds(300));return;}
        reading?.Cancel();reading?.Dispose();reading=new();var token=reading.Token;long version=versions.Next();View.Loading();int revision=artworkRevision;
        try
        {
            if(!libraryLoaded || loadedArtworkRevision!=revision)
            {
                try
                {
                    var songs=await library(token);token.ThrowIfCancellationRequested();
                    if(disposed || !Active || !versions.IsCurrent(version))return;
                    View.SetLibrary(songs.Songs);libraryLoaded=true;loadedArtworkRevision=revision;
                }
                catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException){/* A cover lookup failure must not hide a readable native queue. */}
            }
            if(busy())return;
            var snapshot=await read(token);token.ThrowIfCancellationRequested();
            if(!disposed && Active && versions.IsCurrent(version))View.Apply(snapshot);
        }
        catch(OperationCanceledException){}
        catch(Exception ex) when(ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException or IOException or TimeoutException)
        {if(!disposed && Active && versions.IsCurrent(version))View.Apply(MusicQueueSnapshot.Unavailable("播放列表暂不可用，可稍后刷新。"));}
        finally
        {
            if(!disposed && versions.IsCurrent(version))
            {
                if(busy()){View.WaitingForSelection();Schedule(TimeSpan.FromMilliseconds(300));}
                else{View.CancelLoading();Schedule(loadedArtworkRevision!=artworkRevision?TimeSpan.FromSeconds(1):TimeSpan.FromSeconds(15));}
            }
        }
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;timer.Stop();scope.IsVisibleChanged-=VisibilityChanged;owner.StateChanged-=WindowStateChanged;reading?.Cancel();reading?.Dispose();reading=null;View.Dispose();
    }
}
