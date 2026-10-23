using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

internal sealed partial class MusicLibraryView
{
    readonly ThumbnailCache albumCovers=new(360);
    readonly Button albumsTab=new(){Content="专辑",Padding=new(14,8,14,8),Margin=new(0,0,6,0)},songsTab=new(){Content="歌曲",Padding=new(14,8,14,8),Margin=new(0,0,16,0)};
    readonly Border albumHeader=new(){Margin=new(0,0,0,18),Padding=new(18),CornerRadius=new(18),BorderThickness=new(1),Visibility=Visibility.Collapsed};
    readonly Image albumImage=new(){Stretch=Stretch.UniformToFill};
    readonly TextBlock albumTitle=new(){FontSize=23,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis},albumArtist=new(){FontSize=13,Margin=new(0,8,0,0)},albumSummary=new(){FontSize=11,Margin=new(0,8,0,0)};
    readonly Button playAlbumButton=new(){Content="▷ 播放专辑",Padding=new(14,8,14,8),Margin=new(0,0,8,0)};
    MusicLibraryAlbumView albumShelf=null!;
    IReadOnlyList<MusicAlbum> albumGroups=[];
    MusicAlbum? selectedAlbum;
    StackPanel headingCopy=null!;Border detailCover=null!;
    HashSet<string>? albumSongKeys;
    bool showAlbumShelf=true;
    string shelfQuery="",songQuery="",shelfFilterStamp="";double shelfOffset;int albumRevision;
    bool? albumSortMode;int albumSortIndex,songSortIndex;
    internal bool IsAlbumShelf=>showAlbumShelf;
    internal MusicAlbum? SelectedAlbum=>selectedAlbum;
    internal MusicLibraryAlbumView AlbumShelf=>albumShelf;
    internal IReadOnlyList<MusicAlbum> AlbumGroups=>albumGroups;
    internal int CachedAlbumCoverCount=>albumCovers.Count;
    void SetupAlbumNavigation(Grid heading,Button manage,Grid body)
    {
        headingCopy=heading.Children.OfType<StackPanel>().First();heading.Children.Remove(manage);var actions=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Top};actions.Children.Add(albumsTab);actions.Children.Add(songsTab);actions.Children.Add(manage);Grid.SetColumn(actions,1);heading.Children.Add(actions);
        AutomationProperties.SetName(albumsTab,"按专辑浏览");AutomationProperties.SetName(songsTab,"查看全部歌曲");albumsTab.Click+=(_,_)=>ShowAlbums();songsTab.Click+=(_,_)=>ShowAllSongs();
        body.RowDefinitions.Add(new(){Height=GridLength.Auto});body.RowDefinitions.Add(new());Grid.SetRow(songs,1);Grid.SetRow(empty,1);empty.IsHitTestVisible=false;
        albumShelf=new(OpenAlbum,BeginPlayAlbum,albumCovers,playAlbum is not null);Grid.SetRow(albumShelf,1);body.Children.Insert(0,albumShelf);body.Children.Add(albumHeader);
        ThemeManager.Bind(albumHeader,Border.BackgroundProperty,"PanelBrush");ThemeManager.Bind(albumHeader,Border.BorderBrushProperty,"BorderBrush");
        var detail=new DockPanel();albumHeader.Child=detail;var back=new Button{Content="‹ 所有专辑",Padding=new(12,8,12,8),VerticalAlignment=VerticalAlignment.Top,Margin=new(0)};AutomationProperties.SetName(back,"返回所有专辑");back.Click+=(_,_)=>ShowAlbums();
        var detailActions=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Top,Margin=new(14,0,0,0)};DockPanel.SetDock(detailActions,Dock.Right);detail.Children.Add(detailActions);
        AutomationProperties.SetName(playAlbumButton,"播放当前专辑");playAlbumButton.SetResourceReference(StyleProperty,"PrimaryButton");playAlbumButton.Click+=(_,_)=>{if(selectedAlbum is { } album)BeginPlayAlbum(album);};detailActions.Children.Add(playAlbumButton);detailActions.Children.Add(back);
        detailCover=new Border{Width=96,Height=96,CornerRadius=new(12),Margin=new(0,0,18,0),Child=albumImage,Clip=new RectangleGeometry(new Rect(0,0,96,96),12,12)};ThemeManager.Bind(detailCover,Border.BackgroundProperty,"ArtworkBrush");DockPanel.SetDock(detailCover,Dock.Left);detail.Children.Add(detailCover);
        var text=new StackPanel{VerticalAlignment=VerticalAlignment.Center};text.Children.Add(albumTitle);text.Children.Add(albumArtist);text.Children.Add(albumSummary);albumArtist.TextTrimming=albumSummary.TextTrimming=TextTrimming.CharacterEllipsis;ThemeManager.Bind(albumArtist,TextBlock.ForegroundProperty,"MutedBrush");ThemeManager.Bind(albumSummary,TextBlock.ForegroundProperty,"MutedBrush");detail.Children.Add(text);SizeChanged+=(_,_)=>SizeAlbumHeader();UpdateAlbumSurface();
    }
    void RefreshAlbumGroups(IReadOnlyList<MusicLibrarySong> items)
    {
        string? previous=selectedAlbum?.Key;albumGroups=MusicLibraryAlbums.Group(items);albumRevision++;
        if(previous is not null){selectedAlbum=albumGroups.FirstOrDefault(a=>a.Key==previous);albumSongKeys=selectedAlbum?.Songs.Select(s=>s.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);if(selectedAlbum is null)showAlbumShelf=true;else UpdateAlbumHeader(selectedAlbum);}
    }
    internal void ShowAllSongs()
    {
        HideQueue();
        if(!showAlbumShelf && selectedAlbum is null){Filter();return;}
        if(showAlbumShelf){shelfQuery=search.Text;shelfOffset=albumShelf.ScrollOffset;}
        bool wasDetail=selectedAlbum is not null;showAlbumShelf=false;selectedAlbum=null;albumSongKeys=null;if(wasDetail || search.Text!=songQuery)search.Text=songQuery;Filter();
    }
    internal void ShowAlbums()
    {
        HideQueue();
        if(showAlbumShelf && selectedAlbum is null){Filter();return;}
        if(!showAlbumShelf && selectedAlbum is null)songQuery=search.Text;
        showAlbumShelf=true;selectedAlbum=null;albumSongKeys=null;search.Text=shelfQuery;Filter();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(()=>{if(showAlbumShelf)albumShelf.RestoreOffset(shelfOffset);}));
    }
    internal void OpenAlbum(MusicAlbum album)
    {
        HideQueue();
        if(showAlbumShelf){shelfQuery=search.Text;shelfOffset=albumShelf.ScrollOffset;}
        selectedAlbum=album;albumSongKeys=album.Songs.Select(s=>s.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);showAlbumShelf=false;search.Text="";
        UpdateAlbumHeader(album);Filter();songs.SelectedItem=null;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(()=>{if(selectedAlbum==album && songs.Items.Count>0)songs.ScrollIntoView(songs.Items[0]);}));
    }
    void UpdateAlbumHeader(MusicAlbum album)
    {
        albumTitle.Text=album.Title;albumArtist.Text=album.Artist;albumSummary.Text=album.Summary+" · 选择歌曲或播放整张专辑";albumImage.Source=album.CoverPaths.Select(p=>albumCovers.Get(p)).FirstOrDefault(i=>i is not null);playAlbumButton.IsEnabled=album.CanPlay && playAlbum is not null;
    }
    void UpdateAlbumSurface()
    {
        if(albumSortMode!=showAlbumShelf)
        {
            if(albumSortMode.HasValue){if(albumSortMode.Value)albumSortIndex=Math.Max(0,sort.SelectedIndex);else songSortIndex=Math.Max(0,sort.SelectedIndex);}
            albumSortMode=showAlbumShelf;sort.ItemsSource=showAlbumShelf?new[]{"按专辑名","按艺人"}:new[]{"按歌曲名","按艺人","按专辑"};sort.SelectedIndex=showAlbumShelf?Math.Min(albumSortIndex,1):Math.Min(songSortIndex,2);
        }
        albumShelf.Visibility=showAlbumShelf?Visibility.Visible:Visibility.Collapsed;songs.Visibility=showAlbumShelf?Visibility.Collapsed:Visibility.Visible;albumHeader.Visibility=selectedAlbum is null?Visibility.Collapsed:Visibility.Visible;
        foreach(var child in headingCopy.Children.OfType<TextBlock>().Take(2))child.Visibility=selectedAlbum is null?Visibility.Visible:Visibility.Collapsed;
        playSelected.Visibility=showAlbumShelf?Visibility.Collapsed:Visibility.Visible;
        foreach(var (button,active) in new[]{(albumsTab,showAlbumShelf || selectedAlbum is not null),(songsTab,!showAlbumShelf && selectedAlbum is null)})
        {button.SetResourceReference(Control.BackgroundProperty,active?"AccentSoftBrush":"ControlBrush");button.SetResourceReference(Control.ForegroundProperty,active?"AccentBrush":"MutedBrush");}
    }
    void SizeAlbumHeader()
    {
        bool compact=ActualHeight<650;double size=compact?68:96;detailCover.Width=detailCover.Height=size;detailCover.Clip=new RectangleGeometry(new Rect(0,0,size,size),12,12);albumHeader.Padding=new(compact?12:18);albumTitle.FontSize=compact?19:23;
    }
    void FilterAlbums()
    {
        string stamp=search.Text+"\0"+sort.SelectedIndex+"\0"+albumRevision;
        if(stamp!=shelfFilterStamp)
        {
            var matching=albumGroups.Where(a=>a.Matches(search.Text));matching=sort.SelectedIndex==1?matching.OrderBy(a=>a.Artist,StringComparer.CurrentCultureIgnoreCase).ThenBy(a=>a.Title,StringComparer.CurrentCultureIgnoreCase):matching.OrderBy(a=>a.Title,StringComparer.CurrentCultureIgnoreCase).ThenBy(a=>a.Artist,StringComparer.CurrentCultureIgnoreCase);
            albumShelf.SetAlbums(matching.ToArray());shelfFilterStamp=stamp;
        }
        count.Text=$"{albumShelf.Albums.Count:N0} / {albumGroups.Count:N0} 张专辑 · {albumGroups.Sum(a=>a.Songs.Count):N0} 首歌曲";
        empty.Text=albumGroups.Count==0?"音乐库还在等第一张专辑。\n打开 Apple Music 完成同步，或在缓存管理中选择资料库。":"没有找到匹配的专辑\n试试专辑名、艺人或其中的歌曲。";empty.Visibility=albumShelf.Albums.Count==0?Visibility.Visible:Visibility.Collapsed;
    }
}
