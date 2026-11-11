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
    readonly Button albumsTab=new(){Content=UiText.T("专辑"),Padding=new(14,8,14,8),Margin=new(0,0,6,0)},songsTab=new(){Content=UiText.T("歌曲"),Padding=new(14,8,14,8),Margin=new(0,0,16,0)};
    readonly Border albumHeader=new(){Margin=new(0,0,0,18),Padding=new(18),CornerRadius=new(18),BorderThickness=new(1),Visibility=Visibility.Collapsed};
    readonly Image albumImage=new(){Stretch=Stretch.UniformToFill};
    readonly TextBlock albumTitle=new(){FontSize=23,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis},albumArtist=new(){FontSize=13,Margin=new(0,8,0,0)},albumSummary=new(){FontSize=11,Margin=new(0,8,0,0)};
    readonly Button playAlbumButton=new(){Content=UiText.T("▷ 播放专辑"),Padding=new(14,8,14,8),Margin=new(0,0,8,0)};
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
        headingCopy=heading.Children.OfType<StackPanel>().First();heading.Children.Remove(manage);var actions=new WrapPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Top};actions.Children.Add(albumsTab);actions.Children.Add(songsTab);actions.Children.Add(manage);Grid.SetColumn(actions,1);heading.Children.Add(actions);
        AutomationProperties.SetName(albumsTab,UiText.T("按专辑浏览"));AutomationProperties.SetName(songsTab,UiText.T("查看全部歌曲"));albumsTab.Click+=(_,_)=>ShowAlbums();songsTab.Click+=(_,_)=>ShowAllSongs();
        body.RowDefinitions.Add(new(){Height=GridLength.Auto});body.RowDefinitions.Add(new());Grid.SetRow(songs,1);Grid.SetRow(empty,1);empty.IsHitTestVisible=false;
        albumShelf=new(OpenAlbum,BeginPlayAlbum,albumCovers,playAlbum is not null);Grid.SetRow(albumShelf,1);body.Children.Insert(0,albumShelf);body.Children.Add(albumHeader);
        AppMotion.SetRevealOnChange(albumShelf,true);AppMotion.SetRevealOnChange(songs,true);AppMotion.SetRevealOnChange(albumHeader,true);
        ThemeManager.Bind(albumHeader,Border.BackgroundProperty,"PanelBrush");ThemeManager.Bind(albumHeader,Border.BorderBrushProperty,"BorderBrush");
        ThemeManager.Bind(albumHeader,Border.CornerRadiusProperty,"AlbumHeaderRadius");ThemeManager.Bind(albumTitle,TextBlock.FontFamilyProperty,"DisplayFont");
        var detail=new Grid();detail.ColumnDefinitions.Add(new(){Width=GridLength.Auto});detail.ColumnDefinitions.Add(new());detail.RowDefinitions.Add(new(){Height=GridLength.Auto});detail.RowDefinitions.Add(new(){Height=GridLength.Auto});albumHeader.Child=detail;var back=new Button{Content=UiText.T("‹ 所有专辑"),Padding=new(12,8,12,8),VerticalAlignment=VerticalAlignment.Top,Margin=new(0)};AutomationProperties.SetName(back,UiText.T("返回所有专辑"));back.Click+=(_,_)=>ShowAlbums();
        var detailActions=new WrapPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Top,Margin=new(0,12,0,0)};Grid.SetRow(detailActions,1);Grid.SetColumnSpan(detailActions,2);detail.Children.Add(detailActions);
        AutomationProperties.SetName(playAlbumButton,UiText.T("播放当前专辑"));playAlbumButton.SetResourceReference(StyleProperty,"PrimaryButton");playAlbumButton.Click+=(_,_)=>{if(selectedAlbum is { } album)BeginPlayAlbum(album);};detailActions.Children.Add(playAlbumButton);detailActions.Children.Add(back);if(manageSongs is not null){var manager=new Button{Content=UiText.T("管理本专辑歌词"),Padding=new(10,8,10,8)};manager.Click+=(_,_)=>{if(selectedAlbum is {} selected)manageSongs(selected.Songs);};detailActions.Children.Add(manager);}
        detailCover=new ArtworkFrame{Width=96,Height=96,Margin=new(0,0,18,0),Child=albumImage};ThemeManager.Bind(detailCover,Border.BackgroundProperty,"ArtworkBrush");ThemeManager.Bind(detailCover,Border.CornerRadiusProperty,"DetailArtworkRadius");Grid.SetColumn(detailCover,0);detail.Children.Add(detailCover);
        var text=new StackPanel{VerticalAlignment=VerticalAlignment.Center};text.Children.Add(albumTitle);text.Children.Add(albumArtist);text.Children.Add(albumSummary);albumArtist.TextTrimming=albumSummary.TextTrimming=TextTrimming.CharacterEllipsis;ThemeManager.Bind(albumArtist,TextBlock.ForegroundProperty,"MutedBrush");ThemeManager.Bind(albumSummary,TextBlock.ForegroundProperty,"MutedBrush");Grid.SetColumn(text,1);detail.Children.Add(text);SizeChanged+=(_,_)=>SizeAlbumHeader();UpdateAlbumSurface();
    }
    void RefreshAlbumGroups(IReadOnlyList<MusicLibrarySong> items)
    {
        string? previous=selectedAlbum?.Key;albumGroups=MusicLibraryAlbums.Group(items);albumRevision++;
        if(previous is not null){selectedAlbum=albumGroups.FirstOrDefault(a=>a.Key==previous);albumSongKeys=selectedAlbum?.Songs.Select(s=>s.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);if(selectedAlbum is null)showAlbumShelf=true;else UpdateAlbumHeader(selectedAlbum);}
    }
    internal void ShowAllSongs()
    {
        LeavePlaylists();HideQueue();
        if(!showAlbumShelf && selectedAlbum is null){Filter();return;}
        if(showAlbumShelf){shelfQuery=search.Text;shelfOffset=albumShelf.ScrollOffset;}
        bool wasDetail=selectedAlbum is not null;showAlbumShelf=false;selectedAlbum=null;albumSongKeys=null;if(wasDetail || search.Text!=songQuery)search.Text=songQuery;Filter();
    }
    internal void ShowAlbums()
    {
        LeavePlaylists();HideQueue();
        if(showAlbumShelf && selectedAlbum is null){Filter();return;}
        if(!showAlbumShelf && selectedAlbum is null)songQuery=search.Text;
        showAlbumShelf=true;selectedAlbum=null;albumSongKeys=null;search.Text=shelfQuery;Filter();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(()=>{if(showAlbumShelf)albumShelf.RestoreOffset(shelfOffset);}));
    }
    internal void OpenAlbum(MusicAlbum album)
    {
        LeavePlaylists();HideQueue();
        if(showAlbumShelf){shelfQuery=search.Text;shelfOffset=albumShelf.ScrollOffset;}
        selectedAlbum=album;albumSongKeys=album.Songs.Select(s=>s.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);showAlbumShelf=false;search.Text="";
        UpdateAlbumHeader(album);Filter();songs.SelectedItem=null;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(()=>{if(selectedAlbum==album && songs.Items.Count>0)songs.ScrollIntoView(songs.Items[0]);}));
    }
    void UpdateAlbumHeader(MusicAlbum album)
    {
        albumTitle.Text=LocalizedAlbumLabels.Title(album);albumArtist.Text=LocalizedAlbumLabels.Artist(album);albumSummary.Text=UiText.Status(album.Summary)+UiText.T(" · 选择歌曲或播放整张专辑");albumImage.Source=album.CoverPaths.Select(p=>albumCovers.Get(p)).FirstOrDefault(i=>i is not null);playAlbumButton.IsEnabled=album.CanPlay && playAlbum is not null;
    }
    void UpdateAlbumSurface()
    {
        if(albumSortMode!=showAlbumShelf)
        {
            if(albumSortMode.HasValue){if(albumSortMode.Value)albumSortIndex=Math.Max(0,sort.SelectedIndex);else songSortIndex=Math.Max(0,sort.SelectedIndex);}
            albumSortMode=showAlbumShelf;sort.ItemsSource=showAlbumShelf?new[]{UiText.T("按专辑名"),UiText.T("按艺人")}:new[]{UiText.T("按歌曲名"),UiText.T("按艺人"),UiText.T("按专辑")};sort.SelectedIndex=showAlbumShelf?Math.Min(albumSortIndex,1):Math.Min(songSortIndex,2);
        }
        albumShelf.Visibility=showAlbumShelf?Visibility.Visible:Visibility.Collapsed;songs.Visibility=showAlbumShelf?Visibility.Collapsed:Visibility.Visible;albumHeader.Visibility=selectedAlbum is null?Visibility.Collapsed:Visibility.Visible;
        foreach(var child in headingCopy.Children.OfType<TextBlock>().Take(2))child.Visibility=selectedAlbum is null?Visibility.Visible:Visibility.Collapsed;
        playSelected.Visibility=showAlbumShelf?Visibility.Collapsed:Visibility.Visible;
        foreach(var (button,active) in new[]{(albumsTab,showAlbumShelf || selectedAlbum is not null),(songsTab,!showAlbumShelf && selectedAlbum is null)})
        {AppMotion.SetIsSelected(button,active);button.SetResourceReference(Control.BackgroundProperty,active?"AccentSoftBrush":"ControlBrush");button.SetResourceReference(Control.ForegroundProperty,active?"AccentBrush":"MutedBrush");}
    }
    void SizeAlbumHeader()
    {
        bool compact=ActualHeight<650;double size=compact?68:96;detailCover.Width=detailCover.Height=size;albumHeader.Padding=new(compact?12:18);albumTitle.FontSize=compact?19:23;
    }
    void FilterAlbums()
    {
        string stamp=search.Text+"\0"+sort.SelectedIndex+"\0"+albumRevision;
        if(stamp!=shelfFilterStamp)
        {
            var matching=albumGroups.Where(a=>a.Matches(search.Text));matching=sort.SelectedIndex==1?matching.OrderBy(a=>a.Artist,StringComparer.CurrentCultureIgnoreCase).ThenBy(a=>a.Title,StringComparer.CurrentCultureIgnoreCase):matching.OrderBy(a=>a.Title,StringComparer.CurrentCultureIgnoreCase).ThenBy(a=>a.Artist,StringComparer.CurrentCultureIgnoreCase);
            albumShelf.SetAlbums(matching.ToArray());shelfFilterStamp=stamp;
        }
        count.Text=UiText.F($"{albumShelf.Albums.Count:N0} / {albumGroups.Count:N0} 张专辑 · {albumGroups.Sum(a=>a.Songs.Count):N0} 首歌曲");
        empty.Text=albumGroups.Count==0?UiText.T("音乐库还在等第一张专辑。\n打开 Apple Music 完成同步，或在缓存管理中选择资料库。"):UiText.T("没有找到匹配的专辑\n试试专辑名、艺人或其中的歌曲。");empty.Visibility=albumShelf.Albums.Count==0?Visibility.Visible:Visibility.Collapsed;
    }
}
