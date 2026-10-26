using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

internal sealed partial class MusicLibraryView
{
    readonly Func<MusicPlaylistSelection,CancellationToken,Task<LibraryPlayResult>>? playPlaylistSelection;
    readonly ComboBox playlistOrder=new(){ItemsSource=new[]{"顺序：沿用 Apple Music","顺序播放","随机播放"},SelectedIndex=0,MinWidth=136,Margin=new(8,0,0,0),ToolTip="在开始歌单播放或从所选歌曲继续时应用播放顺序。"};
    readonly ComboBox playlistRepeat=new(){ItemsSource=new[]{"循环：沿用 Apple Music","不循环","列表循环","单曲循环"},SelectedIndex=0,MinWidth=136,Margin=new(8,0,0,0),ToolTip="在开始歌单播放或从所选歌曲继续时应用循环设置。"};
    internal ComboBox PlaylistOrderChoice=>playlistOrder;
    internal ComboBox PlaylistRepeatChoice=>playlistRepeat;
    MusicPlaybackOptions PlaylistOptions()=>new(playlistOrder.SelectedIndex switch{1=>false,2=>true,_=>(bool?)null},playlistRepeat.SelectedIndex switch{1=>MusicRepeatMode.None,2=>MusicRepeatMode.List,3=>MusicRepeatMode.One,_=>(MusicRepeatMode?)null});
    void SetupPlaylistControls(WrapPanel commands)
    {
        commands.Children.Add(playlistOrder);commands.Children.Add(playlistRepeat);playlistOrder.IsEnabled=playlistRepeat.IsEnabled=playPlaylistSelection is not null;
        AutomationProperties.SetName(playlistOrder,"歌单播放顺序");AutomationProperties.SetName(playlistRepeat,"歌单循环方式");
        playlistTracks.SelectionChanged+=(_,_)=>{if(showingPlaylists)playSelected.IsEnabled=playlistTracks.SelectedItem is PlaylistTrack{CanContinue:true};};
        PlaylistTrack? Row(DependencyObject? node)
        {
            while(node is not null && node!=playlistTracks){if(node is ListBoxItem{DataContext:PlaylistTrack row})return row;node=System.Windows.Media.VisualTreeHelper.GetParent(node);}return null;
        }
        playlistTracks.MouseDoubleClick+=(_,e)=>{if(!IsSongAction(e.OriginalSource as DependencyObject) && Row(e.OriginalSource as DependencyObject) is { } row)BeginContinuePlaylist(row);};
        playlistTracks.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Enter && !IsSongAction(e.OriginalSource as DependencyObject) && playlistTracks.SelectedItem is PlaylistTrack row){e.Handled=true;BeginContinuePlaylist(row);}};
    }
    void BeginContinuePlaylist(PlaylistTrack row)
    {
        if(disposed)return;
        if(!ReferenceEquals(row.Playlist,selectedPlaylist) || !row.CanContinue || playPlaylistSelection is null){status.Text="歌单位置已变化或尚未同步完整，请刷新后重新选择。";return;}
        var request=new MusicPlaylistSelection(row.Playlist,row.Number-1,PlaylistOptions());
        ActivePlayback=RequestPlaybackAsync("正在从所选歌曲继续 · "+row.Song.Title,token=>playPlaylistSelection(request,token));
    }
    void UpdatePlaylistSelectedControl(bool active)
    {
        if(!showingPlaylists){playSelected.Content="播放所选";playSelected.IsEnabled=songs.SelectedItem is MusicRow{Song.CanPlay:true};return;}
        playSelected.Content="从所选歌曲播放";playSelected.Visibility=active && selectedPlaylist is not null && playPlaylistSelection is not null?Visibility.Visible:Visibility.Collapsed;
        playSelected.IsEnabled=playlistTracks.SelectedItem is PlaylistTrack{CanContinue:true};
    }
}
