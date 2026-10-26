using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

public partial class MainWindow
{
    async Task VerifyPlaylistContinuationUiAsync(string directory,Dictionary<string,object?> checks)
    {
        string theme=settings.UiTheme;var previous=musicLibrary;
        var a=new MusicLibrarySong("fixture","a",new("海边晚风","浮光工作室","原创测试",180000));
        var b=new MusicLibrarySong("fixture","b",new("星光旅途","浮光工作室","原创测试",200000));
        var playlist=new MusicPlaylist("fixture","continue","继续听见微光",6,new[]{a,b,a,b,a,b},true);
        var other=playlist with{Id="other",Name="另一张原创歌单"};
        var requests=new List<MusicPlaylistSelection>();int activations=0,cancelled=0;bool fail=false,ignoreCancellation=false;
        async Task<LibraryPlayResult> Select(MusicPlaylistSelection request,CancellationToken token)
        {
            requests.Add(request);
            try{await Task.Delay(request.Index==0?350:15,ignoreCancellation?CancellationToken.None:token);return new(!fail,fail?"所选位置未确认":"已确认位置 · "+request.Index);}
            catch(OperationCanceledException){cancelled++;throw;}
        }
        var browser=new MusicLibraryView(_=>Task.FromResult(new MusicLibraryResult([a,b],""){Playlists=[playlist,other]}),(_,_)=>throw new InvalidOperationException("Playlist selection must not use library song Play"),()=>{},ShowLyricsPage,()=>activations++,vm,()=>{},()=>{},()=>{},playPlaylistSelection:Select);
        musicLibrary=browser;MainPageHost.Children.Add(browser);
        Button FindButton(string text)=>Descendants<Button>(browser).Single(b=>b.Content?.ToString()==text);
        void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent,button));
        async Task Search(string value){browser.SearchBox.Text=value;while(browser.IsFiltering)await Task.Delay(15);UpdateLayout();}
        async Task<ListBoxItem> Row(int number)
        {
            browser.PlaylistTracks.SelectedItem=browser.PlaylistTracks.Items.Cast<MusicLibraryView.PlaylistTrack>().Single(r=>r.Number==number);browser.PlaylistTracks.ScrollIntoView(browser.PlaylistTracks.SelectedItem);await Task.Delay(25);UpdateLayout();
            return (ListBoxItem)browser.PlaylistTracks.ItemContainerGenerator.ContainerFromItem(browser.PlaylistTracks.SelectedItem);
        }
        void Enter(object source)=>browser.PlaylistTracks.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this),0,Key.Enter){RoutedEvent=Keyboard.PreviewKeyDownEvent,Source=source});
        try
        {
            await ShowMusicLibraryAsync();browser.OpenPlaylist(playlist);await Task.Delay(40);UpdateLayout();
            checks["continuation.defaultKeepsNativeSettings"]=browser.PlaylistOrderChoice.SelectedIndex==0 && browser.PlaylistRepeatChoice.SelectedIndex==0;
            int before=requests.Count;browser.PlaylistOrderChoice.SelectedIndex=1;browser.PlaylistRepeatChoice.SelectedIndex=1;
            checks["continuation.modeChoicesDoNotIssuePlayback"]=requests.Count==before;
            await Search("海边");await Row(5);Click(FindButton("从所选歌曲播放"));await browser.ActivePlayback!;
            checks["continuation.filteredSelectionUsesOriginalOccurrence"]=requests.Last().Index==4 && requests.Last().Playlist.Songs.Count==6 && ReferenceEquals(requests.Last().Playlist,playlist);
            checks["continuation.explicitSequentialAndNoRepeatCaptured"]=requests.Last().Options==new MusicPlaybackOptions(false,MusicRepeatMode.None);
            var row=await Row(3);int count=requests.Count;Click(Descendants<Button>(row).Single());await browser.ActivePlayback!;
            checks["continuation.rowPlayTargetsRepeatedOccurrence"]=requests.Count==count+1 && requests.Last().Index==2;
            await Row(1);Enter(browser.PlaylistTracks);var pending=browser.ActivePlayback!;
            browser.PlaylistOrderChoice.SelectedIndex=2;browser.PlaylistRepeatChoice.SelectedIndex=2;await pending;
            checks["continuation.inFlightRequestKeepsCapturedOptions"]=requests.Last().Index==0 && requests.Last().Options==new MusicPlaybackOptions(false,MusicRepeatMode.None);
            row=await Row(5);browser.PlaylistTracks.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=Control.MouseDoubleClickEvent,Source=row});await browser.ActivePlayback!;
            checks["continuation.doubleClickCapturesShuffleAndListRepeat"]=requests.Last().Index==4 && requests.Last().Options==new MusicPlaybackOptions(true,MusicRepeatMode.List);
            browser.PlaylistRepeatChoice.SelectedIndex=3;Click(FindButton("▷ 播放歌单"));await browser.ActivePlayback!;
            checks["continuation.wholePlayUsesNativeWholeRequest"]=requests.Last().Index is null && requests.Last().Options==new MusicPlaybackOptions(true,MusicRepeatMode.One);
            fail=true;int activated=activations;await Row(3);Click(FindButton("从所选歌曲播放"));await browser.ActivePlayback!;
            checks["continuation.unconfirmedDoesNotActivate"]=activations==activated && browser.StatusText=="所选位置未确认";fail=false;
            await Row(1);Click(FindButton("从所选歌曲播放"));var first=browser.ActivePlayback!;await Row(3);Click(FindButton("从所选歌曲播放"));await Task.WhenAll(first,browser.ActivePlayback!);
            checks["continuation.latestOccurrenceCancelsEarlier"]=cancelled==1 && requests.Last().Index==2 && browser.StatusText=="已确认位置 · 2";
            ignoreCancellation=true;await Row(1);Click(FindButton("从所选歌曲播放"));first=browser.ActivePlayback!;await Row(5);Click(FindButton("从所选歌曲播放"));await Task.WhenAll(first,browser.ActivePlayback!);ignoreCancellation=false;
            checks["continuation.staleUncooperativeResultDiscarded"]=browser.StatusText=="已确认位置 · 4";
            var stale=(MusicLibraryView.PlaylistTrack)browser.PlaylistTracks.SelectedItem;row=await Row(5);var oldButton=Descendants<Button>(row).Single();browser.OpenPlaylist(other);oldButton.DataContext=stale;count=requests.Count;Click(oldButton);
            checks["continuation.stalePlaylistRowRejected"]=requests.Count==count && browser.StatusText.Contains("位置已变化");
            browser.OpenPlaylist(playlist with{Complete=false});await Task.Delay(20);UpdateLayout();
            checks["continuation.incompleteRowsDisabled"]=!FindButton("▷ 播放歌单").IsEnabled && browser.PlaylistTracks.Items.Cast<MusicLibraryView.PlaylistTrack>().All(r=>!r.CanContinue);
            browser.OpenPlaylist(playlist);await Search("");
            foreach(var app in AppThemes.All)
            {
                SelectUiTheme(app.Id);
                foreach(double width in new[]{1220d,980d,820d})
                {
                    Width=width;Height=width==820?640:820;await Task.Delay(60);UpdateLayout();
                    bool Fits(FrameworkElement element){var bounds=element.TransformToAncestor(browser).TransformBounds(new Rect(element.RenderSize));return element.IsVisible && bounds.Right<=browser.ActualWidth+.5 && bounds.Bottom<=browser.ActualHeight+.5;}
                    checks[$"continuation.{app.Id}.{width}.controlsFit"]=Fits(browser.PlaylistOrderChoice) && Fits(browser.PlaylistRepeatChoice) && Fits(FindButton("从所选歌曲播放")) && browser.PlaylistTracks.ActualHeight>60;
                    checks[$"continuation.{app.Id}.{width}.usesTheme"]=ReferenceEquals(browser.PlaylistOrderChoice.FindResource("TextBrush"),FindResource("TextBrush"));
                    if(width==820)VisualVerification.Save((FrameworkElement)Content,Path.Combine(directory,"playlist-continuation-"+app.Id+".png"),true);
                }
            }
            browser.PlaylistOrderChoice.SelectedIndex=0;browser.PlaylistRepeatChoice.SelectedIndex=0;Click(FindButton("▷ 播放歌单"));await browser.ActivePlayback!;
            checks["continuation.keepOptionsRemainUnspecified"]=requests.Last().Options==new MusicPlaybackOptions();
            await Row(1);Click(FindButton("从所选歌曲播放"));pending=browser.ActivePlayback!;browser.Dispose();await pending;checks["continuation.disposeCancelsSelection"]=cancelled==2;
        }
        finally{browser.Dispose();MainPageHost.Children.Remove(browser);musicLibrary=previous;ShowLyricsPage();Width=1220;Height=820;SelectUiTheme(theme);}
    }
}
