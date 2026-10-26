using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

internal sealed partial class MusicLibraryView
{
    readonly Func<MusicPlaylist,CancellationToken,Task<LibraryPlayResult>>? playPlaylist;
    readonly Button playlistsTab=new(){Content="歌单",Padding=new(14,8,14,8),Margin=new(0,0,6,0)};
    readonly ListBox playlistShelf=new(){HorizontalContentAlignment=HorizontalAlignment.Stretch,BorderThickness=new(0)};
    readonly ListBox playlistTracks=new(){HorizontalContentAlignment=HorizontalAlignment.Stretch,BorderThickness=new(0)};
    readonly Grid playlistPanel=new(){Visibility=Visibility.Collapsed};
    readonly StackPanel playlistHeader=new(){Margin=new(0,0,0,14)};
    readonly TextBlock playlistTitle=new(){FontSize=23,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis};
    readonly TextBlock playlistSummary=new(){FontSize=12,Margin=new(0,8,0,12),TextTrimming=TextTrimming.CharacterEllipsis};
    readonly Button playlistPlay=new(){Content="▷ 播放歌单",Padding=new(14,8,14,8),Margin=new(0,0,8,0)};
    IReadOnlyList<MusicPlaylist> playlists=[];
    MusicPlaylist? selectedPlaylist;
    bool showingPlaylists;
    string playlistQuery="",beforePlaylistQuery="";
    double playlistShelfOffset;
    string playlistFilterStamp="";int playlistRevision;
    internal ListBox PlaylistShelf=>playlistShelf;
    internal ListBox PlaylistTracks=>playlistTracks;
    internal MusicPlaylist? SelectedPlaylist=>selectedPlaylist;
    internal IReadOnlyList<MusicPlaylist> Playlists=>playlists;
    internal bool IsPlaylistVisible=>showingPlaylists;
    void SetupPlaylistNavigation(Grid heading,Grid body)
    {
        var actions=heading.Children.OfType<StackPanel>().Single(p=>p!=headingCopy);actions.Children.Insert(2,playlistsTab);
        // Keep navigation in its own row so titles never collide with tabs at narrow widths.
        heading.RowDefinitions.Add(new(){Height=GridLength.Auto});heading.RowDefinitions.Add(new(){Height=GridLength.Auto});
        Grid.SetColumnSpan(headingCopy,2);Grid.SetRow(actions,1);Grid.SetColumn(actions,0);Grid.SetColumnSpan(actions,2);actions.Margin=new(0,14,0,0);
        playlistsTab.Click+=(_,_)=>ShowPlaylists();AutomationProperties.SetName(playlistsTab,"浏览 Apple Music 歌单");
        playlistPanel.RowDefinitions.Add(new(){Height=GridLength.Auto});playlistPanel.RowDefinitions.Add(new());Grid.SetRow(playlistPanel,1);body.Children.Add(playlistPanel);
        playlistPanel.Children.Add(playlistHeader);playlistHeader.Children.Add(playlistTitle);playlistHeader.Children.Add(playlistSummary);ThemeManager.Bind(playlistSummary,TextBlock.ForegroundProperty,"MutedBrush");
        var commands=new WrapPanel{Orientation=Orientation.Horizontal};playlistHeader.Children.Add(commands);playlistPlay.SetResourceReference(StyleProperty,"PrimaryButton");commands.Children.Add(playlistPlay);
        playlistPlay.Click+=(_,_)=>{if(selectedPlaylist is { } p)BeginPlayPlaylist(p);};AutomationProperties.SetName(playlistPlay,"播放当前歌单");
        var back=new Button{Content="‹ 所有歌单",Padding=new(12,8,12,8)};commands.Children.Add(back);back.Click+=(_,_)=>ShowPlaylists();SetupPlaylistControls(commands);
        foreach(var list in new[]{playlistShelf,playlistTracks})
        {
            VirtualizingPanel.SetIsVirtualizing(list,true);VirtualizingPanel.SetVirtualizationMode(list,VirtualizationMode.Recycling);ScrollViewer.SetCanContentScroll(list,true);ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled);
            list.ItemsPanel=new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));Grid.SetRow(list,1);playlistPanel.Children.Add(list);
        }
        AutomationProperties.SetName(playlistShelf,"Apple Music 歌单");AutomationProperties.SetName(playlistTracks,"歌单原始顺序歌曲");
        playlistShelf.ItemTemplate=PlaylistTemplate();playlistTracks.ItemTemplate=PlaylistTrackTemplate();
        playlistShelf.MouseDoubleClick+=(_,e)=>{if(!IsSongAction(e.OriginalSource as DependencyObject) && playlistShelf.SelectedItem is MusicPlaylist p)OpenPlaylist(p);};
        playlistShelf.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Enter && !IsSongAction(e.OriginalSource as DependencyObject) && playlistShelf.SelectedItem is MusicPlaylist p){e.Handled=true;OpenPlaylist(p);}};
        AppMotion.SetRevealOnChange(playlistPanel,true);
    }
    static FrameworkElementFactory PlaylistLabel(string path,double size,string brush)
    {
        var label=new FrameworkElementFactory(typeof(TextBlock));label.SetBinding(TextBlock.TextProperty,new Binding(path));label.SetValue(TextBlock.FontSizeProperty,size);label.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);label.SetResourceReference(TextBlock.ForegroundProperty,brush);return label;
    }
    DataTemplate PlaylistTemplate()
    {
        var row=new FrameworkElementFactory(typeof(DockPanel));row.SetValue(HeightProperty,82d);row.SetValue(MarginProperty,new Thickness(8,0,8,0));
        var open=new FrameworkElementFactory(typeof(Button));open.SetValue(ContentControl.ContentProperty,"打开歌单");open.SetValue(VerticalAlignmentProperty,VerticalAlignment.Center);open.SetValue(DockPanel.DockProperty,Dock.Right);open.SetValue(MarginProperty,new Thickness(16,0,0,0));
        open.SetBinding(AutomationProperties.NameProperty,new Binding("Name"){StringFormat="打开歌单 · {0}"});open.AddHandler(Button.ClickEvent,new RoutedEventHandler((_,e)=>{e.Handled=true;if((e.Source as FrameworkElement)?.DataContext is MusicPlaylist p)OpenPlaylist(p);}));row.AppendChild(open);
        var copy=new FrameworkElementFactory(typeof(StackPanel));copy.SetValue(VerticalAlignmentProperty,VerticalAlignment.Center);copy.AppendChild(PlaylistLabel("Name",17,"TextBrush"));var summary=PlaylistLabel("Summary",12,"MutedBrush");summary.SetValue(MarginProperty,new Thickness(0,8,0,0));copy.AppendChild(summary);row.AppendChild(copy);
        return new(){VisualTree=row};
    }
    DataTemplate PlaylistTrackTemplate()
    {
        var row=new FrameworkElementFactory(typeof(DockPanel));row.SetValue(HeightProperty,64d);row.SetValue(MarginProperty,new Thickness(8,0,10,0));
        var playButton=new FrameworkElementFactory(typeof(Button));playButton.SetValue(ContentControl.ContentProperty,"▷");playButton.SetValue(WidthProperty,40d);playButton.SetValue(HeightProperty,36d);playButton.SetValue(MarginProperty,new Thickness(10,0,0,0));playButton.SetValue(DockPanel.DockProperty,Dock.Right);playButton.SetBinding(IsEnabledProperty,new Binding("CanContinue"));playButton.SetValue(ToolTipProperty,"从这首继续播放歌单");playButton.SetBinding(AutomationProperties.NameProperty,new Binding("Song.Title"){StringFormat="从这首继续 · {0}"});
        playButton.AddHandler(Button.ClickEvent,new RoutedEventHandler((_,e)=>{e.Handled=true;if((e.Source as FrameworkElement)?.DataContext is PlaylistTrack row)BeginContinuePlaylist(row);}));row.AppendChild(playButton);
        var duration=PlaylistLabel("Song.Duration",12,"MutedBrush");duration.SetValue(DockPanel.DockProperty,Dock.Right);duration.SetValue(VerticalAlignmentProperty,VerticalAlignment.Center);row.AppendChild(duration);
        var number=PlaylistLabel("Number",12,"MutedBrush");number.SetValue(WidthProperty,44d);number.SetValue(VerticalAlignmentProperty,VerticalAlignment.Center);row.AppendChild(number);
        var text=new FrameworkElementFactory(typeof(StackPanel));text.SetValue(VerticalAlignmentProperty,VerticalAlignment.Center);text.AppendChild(PlaylistLabel("Song.Title",14,"TextBrush"));var artist=PlaylistLabel("Song.Artist",11,"MutedBrush");artist.SetValue(MarginProperty,new Thickness(0,6,0,0));text.AppendChild(artist);row.AppendChild(text);return new(){VisualTree=row};
    }
    void RefreshPlaylists(MusicLibraryResult result)
    {
        if(result.PlaylistsUnavailable)return;
        playlists=result.Playlists;playlistRevision++;string? key=selectedPlaylist?.Key;selectedPlaylist=key is null?null:playlists.FirstOrDefault(p=>p.Key==key);
        if(showingPlaylists && selectedPlaylist is null && key is not null)search.Text=playlistQuery;
    }
    internal void ShowPlaylists()
    {
        HideQueue();if(!showingPlaylists){beforePlaylistQuery=search.Text;if(showAlbumShelf){shelfQuery=search.Text;shelfOffset=albumShelf.ScrollOffset;}showingPlaylists=true;status.Text="打开歌单查看原有顺序 · 在详情中播放整单";}
        bool returning=selectedPlaylist is not null;
        selectedPlaylist=null;search.Text=playlistQuery;Filter();
        if(returning)Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(()=>FindArtworkScroll(playlistShelf)?.ScrollToVerticalOffset(playlistShelfOffset)));
    }
    void LeavePlaylists()
    {
        if(!showingPlaylists)return;if(selectedPlaylist is null)playlistQuery=search.Text;
        showingPlaylists=false;search.Text=beforePlaylistQuery;
    }
    internal void OpenPlaylist(MusicPlaylist playlist)
    {
        if(!showingPlaylists)ShowPlaylists();if(selectedPlaylist is null){playlistQuery=search.Text;playlistShelfOffset=FindArtworkScroll(playlistShelf)?.VerticalOffset??0;}
        selectedPlaylist=playlist;playlistFilterStamp="";search.Text="";Filter();playlistTracks.SelectedItem=null;
        status.Text=!playlist.Complete?"歌单尚未同步完整，请在 Apple Music 完成同步后刷新。":!playlist.UniqueName?"存在同名歌单，请在 Apple Music 区分名称后刷新。":playlist.Songs.Count==0?"这张歌单还没有歌曲。":"双击歌曲或选择后按 Enter，从歌单中的该位置继续。";
    }
    void UpdatePlaylistSurface()
    {
        playlistPanel.Visibility=showingPlaylists?Visibility.Visible:Visibility.Collapsed;bool active=showingPlaylists && !showingQueue;UpdatePlaylistSelectedControl(active);
        AppMotion.SetIsSelected(playlistsTab,active);playlistsTab.SetResourceReference(Control.BackgroundProperty,active?"AccentSoftBrush":"ControlBrush");playlistsTab.SetResourceReference(Control.ForegroundProperty,active?"AccentBrush":"MutedBrush");
        sort.Visibility=showingPlaylists?Visibility.Collapsed:Visibility.Visible;
        if(!showingPlaylists)return;
        albumShelf.Visibility=songs.Visibility=albumHeader.Visibility=Visibility.Collapsed;sort.Visibility=Visibility.Collapsed;
        if(active)foreach(var copy in headingCopy.Children.OfType<TextBlock>().Take(2))copy.Visibility=selectedPlaylist is null?Visibility.Visible:Visibility.Collapsed;
        if(active)foreach(var button in new[]{albumsTab,songsTab}){AppMotion.SetIsSelected(button,false);button.SetResourceReference(Control.BackgroundProperty,"ControlBrush");button.SetResourceReference(Control.ForegroundProperty,"MutedBrush");}
    }
    void FilterPlaylists()
    {
        if(selectedPlaylist is null)
        {
            playlistQuery=search.Text;playlistHeader.Visibility=playlistTracks.Visibility=Visibility.Collapsed;playlistShelf.Visibility=Visibility.Visible;
            var filtered=playlists.Where(p=>p.Matches(search.Text)).ToArray();
            string stamp="shelf\0"+search.Text+"\0"+playlistRevision;if(playlistFilterStamp!=stamp){playlistShelf.ItemsSource=filtered;playlistFilterStamp=stamp;}count.Text=$"{filtered.Length:N0} / {playlists.Count:N0} 个歌单";
            empty.Text=playlists.Count==0?"还没有可读取的 Apple Music 歌单\n请在 Apple Music 完成资料库同步后刷新。":"没有找到匹配的歌单\n试试歌单名或其中的歌曲。";empty.Visibility=filtered.Length==0?Visibility.Visible:Visibility.Collapsed;
        }
        else
        {
            playlistHeader.Visibility=playlistTracks.Visibility=Visibility.Visible;playlistShelf.Visibility=Visibility.Collapsed;playlistTitle.Text=selectedPlaylist.Name;
            playlistSummary.Text=selectedPlaylist.Summary+" · 双击歌曲继续播放，顺序与循环在播放时应用";playlistPlay.IsEnabled=selectedPlaylist.CanPlay && (playPlaylist is not null || playPlaylistSelection is not null);
            var rows=selectedPlaylist.Songs.Select((song,index)=>new PlaylistTrack(index+1,song,selectedPlaylist,selectedPlaylist.CanPlay && playPlaylistSelection is not null)).Where(r=>MusicLibraryCatalog.Matches(r.Song,search.Text)).ToArray();string stamp=selectedPlaylist.Key+"\0"+search.Text+"\0"+playlistRevision;if(playlistFilterStamp!=stamp){playlistTracks.ItemsSource=rows;playlistFilterStamp=stamp;}
            count.Text=$"歌单 · {rows.Length:N0} / {selectedPlaylist.DeclaredCount:N0} 首歌曲";empty.Text="歌单中没有匹配的歌曲";empty.Visibility=rows.Length==0?Visibility.Visible:Visibility.Collapsed;
        }
    }
    void BeginPlayPlaylist(MusicPlaylist playlist)=>ActivePlayback=PlayPlaylistAsync(playlist);
    async Task PlayPlaylistAsync(MusicPlaylist playlist)
    {
        if(!playlist.CanPlay || playPlaylist is null && playPlaylistSelection is null){status.Text="歌单尚未同步完整或名称不唯一，请在 Apple Music 确认后刷新。";return;}
        var request=new MusicPlaylistSelection(playlist,Options:PlaylistOptions());
        await RequestPlaybackAsync("正在打开歌单 · "+playlist.Name,token=>playPlaylistSelection is not null?playPlaylistSelection(request,token):playPlaylist!(playlist,token));
    }
    internal sealed record PlaylistTrack(int Number,MusicLibrarySong Song,MusicPlaylist Playlist,bool CanContinue);
}
