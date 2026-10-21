using System.Collections;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

internal sealed partial class MusicLibraryView:UserControl,IDisposable
{
    readonly Func<CancellationToken,Task<MusicLibraryResult>> load;
    readonly Func<MusicLibrarySong,CancellationToken,Task<LibraryPlayResult>> play;
    readonly Action activate;
    readonly TextBox search=new(){MinWidth=180,Padding=new(14,11,14,11),FontSize=13};
    readonly ListBox songs=new(){HorizontalContentAlignment=HorizontalAlignment.Stretch,BorderThickness=new(0),Padding=new(0,0,5,0)};
    readonly TextBlock count=new(){FontSize=12},status=new(){FontSize=11,TextWrapping=TextWrapping.Wrap},empty=new(){FontSize=17,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap};
    readonly Button playSelected=new(){Content="播放所选",Padding=new(18,10,18,10),IsEnabled=false};
    readonly Button refresh=new(){Content="刷新",Padding=new(14,10,14,10)};
    readonly ComboBox sort=new(){ItemsSource=new[]{"按歌曲名","按艺人","按专辑"},SelectedIndex=0,MinWidth=130,Margin=new(12,0,8,0)};
    readonly DispatcherTimer debounce=new(){Interval=TimeSpan.FromMilliseconds(140)};
    readonly RequestVersion requests=new();
    readonly ThumbnailCache thumbnails=new();
    readonly Button toggleButton;
    CancellationTokenSource? loading,playing;
    ListCollectionView? view;
    string[] terms=[];
    bool loaded,disposed;
    string lastTrack="";
    internal ListBox SongList=>songs;
    internal TextBox SearchBox=>search;
    internal string StatusText=>status.Text;
    internal int CachedThumbnailCount=>thumbnails.Count;
    internal ComboBox SortChoice=>sort;
    internal bool HasEmptyMessage=>empty.Visibility==Visibility.Visible;
    internal Task? ActivePlayback {get;private set;}
    internal MusicLibraryView(Func<CancellationToken,Task<MusicLibraryResult>> load,Func<MusicLibrarySong,CancellationToken,Task<LibraryPlayResult>> play,Action manage,Action returnLyrics,Action activate,PlayerViewModel player,Action previous,Action toggle,Action next)
    {
        this.load=load;this.play=play;this.activate=activate;Margin=new(28,14,30,18);
        var root=new Grid();Content=root;root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new());root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var heading=new Grid{Margin=new(0,0,0,22)};heading.ColumnDefinitions.Add(new());heading.ColumnDefinitions.Add(new(){Width=GridLength.Auto});root.Children.Add(heading);
        var copy=new StackPanel();heading.Children.Add(copy);copy.Children.Add(new TextBlock{Text="你的音乐，随时抵达。",FontSize=25,FontWeight=FontWeights.SemiBold});copy.Children.Add(new TextBlock{Text="M Y   M U S I C",FontSize=9,Margin=new(1,7,0,12)});copy.Children.Add(count);
        ThemeManager.Bind(count,TextBlock.ForegroundProperty,"MutedBrush");
        var manageButton=new Button{Content="缓存管理",Padding=new(14,9,14,9),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Margin=new(0)};manageButton.Click+=(_,_)=>manage();Grid.SetColumn(manageButton,1);heading.Children.Add(manageButton);
        var filters=new Grid{Margin=new(0,0,0,18)};filters.ColumnDefinitions.Add(new());filters.ColumnDefinitions.Add(new(){Width=GridLength.Auto});filters.ColumnDefinitions.Add(new(){Width=GridLength.Auto});Grid.SetRow(filters,1);root.Children.Add(filters);
        var searchHost=new Grid();searchHost.Children.Add(search);var hint=new TextBlock{Text="搜索歌曲、艺人或专辑",Margin=new(15,0,0,0),VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false};ThemeManager.Bind(hint,TextBlock.ForegroundProperty,"MutedBrush");searchHost.Children.Add(hint);filters.Children.Add(searchHost);AutomationProperties.SetName(search,"搜索音乐库");
        Grid.SetColumn(sort,1);filters.Children.Add(sort);AutomationProperties.SetName(sort,"音乐库排序");Grid.SetColumn(refresh,2);filters.Children.Add(refresh);refresh.Margin=new(0);refresh.Click+=async(_,_)=>await RefreshAsync();
        var body=new Grid();Grid.SetRow(body,2);root.Children.Add(body);
        VirtualizingPanel.SetIsVirtualizing(songs,true);VirtualizingPanel.SetVirtualizationMode(songs,VirtualizationMode.Recycling);VirtualizingPanel.SetScrollUnit(songs,ScrollUnit.Pixel);ScrollViewer.SetCanContentScroll(songs,true);ScrollViewer.SetHorizontalScrollBarVisibility(songs,ScrollBarVisibility.Disabled);
        songs.ItemsPanel=new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));songs.ItemTemplate=RowTemplate();
        AutomationProperties.SetName(songs,"音乐库歌曲");songs.SelectionChanged+=(_,_)=>playSelected.IsEnabled=songs.SelectedItem is MusicRow{Song.CanPlay:true};
        songs.MouseDoubleClick+=(_,e)=>{if(FindRow(e.OriginalSource as DependencyObject) is { } row)BeginPlay(row);};songs.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Enter && songs.SelectedItem is MusicRow row){e.Handled=true;BeginPlay(row);}};
        body.Children.Add(songs);empty.VerticalAlignment=VerticalAlignment.Center;empty.Margin=new(24);ThemeManager.Bind(empty,TextBlock.ForegroundProperty,"MutedBrush");body.Children.Add(empty);
        SetupAlbumNavigation(heading,manageButton,body);
        var footer=new Grid{Margin=new(0,16,0,0)};Grid.SetRow(footer,3);root.Children.Add(footer);footer.RowDefinitions.Add(new(){Height=GridLength.Auto});footer.RowDefinitions.Add(new(){Height=GridLength.Auto});footer.ColumnDefinitions.Add(new());footer.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        ThemeManager.Bind(status,TextBlock.ForegroundProperty,"MutedBrush");status.Margin=new(0,0,14,14);footer.Children.Add(status);playSelected.SetResourceReference(StyleProperty,"PrimaryButton");Grid.SetColumn(playSelected,1);footer.Children.Add(playSelected);playSelected.Click+=(_,_)=>{if(songs.SelectedItem is MusicRow row)BeginPlay(row);};
        var now=new Border{CornerRadius=new(16),Padding=new(16,10,16,10),Margin=new(0,4,0,0),BorderThickness=new(1)};ThemeManager.Bind(now,Border.BackgroundProperty,"PanelBrush");ThemeManager.Bind(now,Border.BorderBrushProperty,"BorderBrush");Grid.SetRow(now,1);Grid.SetColumnSpan(now,2);footer.Children.Add(now);
        var playback=new DockPanel();now.Child=playback;var controls=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(controls,Dock.Right);playback.Children.Add(controls);
        Button Command(string glyph,string name,string enabled,Action action){var button=new Button{Content=glyph,ToolTip=name};button.SetResourceReference(StyleProperty,"IconButton");button.SetBinding(IsEnabledProperty,new Binding(enabled){Source=player});AutomationProperties.SetName(button,name);button.Click+=(_,_)=>action();controls.Children.Add(button);return button;}
        Command("‹","上一首","CanPrevious",previous);toggleButton=Command("▷","播放或暂停","CanPlay",toggle);Command("›","下一首","CanNext",next);
        var back=new Button{Content="回到歌词",Padding=new(12,7,12,7),Margin=new(12,0,0,0)};back.Click+=(_,_)=>returnLyrics();controls.Children.Add(back);
        var text=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,12,0)};var title=new TextBlock{FontSize=12,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis};title.SetBinding(TextBlock.TextProperty,new Binding("Title"){Source=player});text.Children.Add(title);var artist=new TextBlock{FontSize=10,Margin=new(0,4,0,0),TextTrimming=TextTrimming.CharacterEllipsis};artist.SetBinding(TextBlock.TextProperty,new Binding("Artist"){Source=player});ThemeManager.Bind(artist,TextBlock.ForegroundProperty,"MutedBrush");text.Children.Add(artist);playback.Children.Add(text);
        search.TextChanged+=(_,_)=>{hint.Visibility=search.Text.Length==0?Visibility.Visible:Visibility.Collapsed;debounce.Stop();debounce.Start();};debounce.Tick+=(_,_)=>{debounce.Stop();Filter();};sort.SelectionChanged+=(_,_)=>Filter();
    }
    DataTemplate RowTemplate()
    {
        var row=new FrameworkElementFactory(typeof(DockPanel));row.SetValue(HeightProperty,68d);row.SetValue(MarginProperty,new Thickness(1,0,1,0));
        var playButton=new FrameworkElementFactory(typeof(Button));playButton.SetValue(ContentControl.ContentProperty,"▷");playButton.SetValue(WidthProperty,40d);playButton.SetValue(HeightProperty,40d);playButton.SetValue(Control.PaddingProperty,new Thickness(0));playButton.SetValue(MarginProperty,new Thickness(8,0,0,0));playButton.SetValue(DockPanel.DockProperty,Dock.Right);playButton.SetBinding(IsEnabledProperty,new Binding("Song.CanPlay"));playButton.SetValue(ToolTipProperty,"播放这首歌");playButton.AddHandler(Button.ClickEvent,new RoutedEventHandler((_,e)=>{e.Handled=true;if((e.Source as FrameworkElement)?.DataContext is MusicRow item)BeginPlay(item);}));row.AppendChild(playButton);
        FrameworkElementFactory Label(string binding,double size,string brush,int width=0){var t=new FrameworkElementFactory(typeof(TextBlock));t.SetBinding(TextBlock.TextProperty,new Binding(binding));t.SetValue(TextBlock.FontSizeProperty,size);t.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);t.SetValue(VerticalAlignmentProperty,VerticalAlignment.Center);t.SetResourceReference(TextBlock.ForegroundProperty,brush);if(width>0)t.SetValue(WidthProperty,(double)width);return t;}
        var duration=Label("Song.Duration",11,"MutedBrush",54);duration.SetValue(DockPanel.DockProperty,Dock.Right);duration.SetValue(TextBlock.TextAlignmentProperty,TextAlignment.Right);row.AppendChild(duration);
        var album=Label("Song.Album",11,"MutedBrush",170);album.SetValue(DockPanel.DockProperty,Dock.Right);album.SetValue(MarginProperty,new Thickness(16,0,14,0));row.AppendChild(album);
        var cover=new FrameworkElementFactory(typeof(Border));cover.SetValue(WidthProperty,50d);cover.SetValue(HeightProperty,50d);cover.SetValue(MarginProperty,new Thickness(0,0,16,0));cover.SetValue(Border.CornerRadiusProperty,new CornerRadius(10));cover.SetResourceReference(Border.BackgroundProperty,"ArtworkBrush");cover.SetValue(DockPanel.DockProperty,Dock.Left);
        var picture=new FrameworkElementFactory(typeof(Image));picture.SetBinding(Image.SourceProperty,new Binding("Cover"));picture.SetValue(Image.StretchProperty,Stretch.UniformToFill);picture.AddHandler(FrameworkElement.SizeChangedEvent,new SizeChangedEventHandler((sender,_)=>{var image=(Image)sender;image.Clip=new RectangleGeometry(new Rect(0,0,image.ActualWidth,image.ActualHeight),10,10);}));cover.AppendChild(picture);row.AppendChild(cover);
        var metadata=new FrameworkElementFactory(typeof(StackPanel));metadata.SetValue(VerticalAlignmentProperty,VerticalAlignment.Center);metadata.AppendChild(Label("Song.Title",14,"TextBrush"));var artist=Label("ArtistLine",11,"MutedBrush");artist.SetValue(MarginProperty,new Thickness(0,7,0,0));metadata.AppendChild(artist);row.AppendChild(metadata);
        return new DataTemplate{VisualTree=row};
    }
    MusicRow? FindRow(DependencyObject? node)
    {
        while(node is not null && node!=songs){if(node is ListBoxItem{DataContext:MusicRow row})return row;node=VisualTreeHelper.GetParent(node);}return null;
    }
    internal Task EnsureLoadedAsync()=>loaded?Task.CompletedTask:RefreshAsync();
    internal async Task RefreshAsync()
    {
        loading?.Cancel();loading?.Dispose();loading=new();var token=loading.Token;refresh.IsEnabled=false;status.Text="正在读取本机音乐库…";
        try
        {
            var result=await load(token);token.ThrowIfCancellationRequested();if(disposed)return;
            thumbnails.Clear();albumCovers.Clear();lastTrack="";var rows=result.Songs.Select(s=>new MusicRow(s,thumbnails)).ToList();view=new ListCollectionView(rows){Culture=System.Globalization.CultureInfo.CurrentCulture};songs.ItemsSource=view;RefreshAlbumGroups(result.Songs);loaded=true;Filter();
            status.Text=result.Message.Length>0?result.Message:"双击歌曲或按 Enter 播放 · 歌词与封面随实际播放同步";
        }
        catch(OperationCanceledException){}
        catch(Exception ex) when(ex is IOException or InvalidDataException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException){if(!disposed){status.Text="音乐库暂时无法读取，请稍后刷新或检查缓存管理。";empty.Text="暂时无法读取音乐库";empty.Visibility=Visibility.Visible;}}
        finally{if(!disposed && !token.IsCancellationRequested)refresh.IsEnabled=true;}
    }
    void Filter()
    {
        if(view is null)return;terms=search.Text.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Select(MatchRules.Normalize).Where(s=>s.Length>0).ToArray();
        UpdateAlbumSurface();if(showAlbumShelf){FilterAlbums();return;}
        using(view.DeferRefresh())
        {
            view.Filter=item=>item is MusicRow row && (albumSongKeys is null || albumSongKeys.Contains(row.Song.Key)) && terms.All(t=>row.SearchText.Contains(t,StringComparison.Ordinal));view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new(sort.SelectedIndex switch{1=>nameof(MusicRow.Artist),2=>nameof(MusicRow.Album),_=>nameof(MusicRow.Title)},ListSortDirection.Ascending));
            if(sort.SelectedIndex!=0)view.SortDescriptions.Add(new(nameof(MusicRow.Title),ListSortDirection.Ascending));
        }
        int total=selectedAlbum?.Songs.Count??view.SourceCollection.Cast<object>().Count();count.Text=$"{view.Count:N0} / {total:N0} 首歌曲";
        empty.Text=total==0?"音乐库还在等第一首歌。\n打开 Apple Music 完成同步，或在缓存管理中选择资料库。":"没有找到匹配的歌曲\n试试歌曲名、艺人或专辑。";empty.Visibility=view.Count==0?Visibility.Visible:Visibility.Collapsed;
    }
    void BeginPlay(MusicRow row){ActivePlayback=PlayAsync(row);}
    async Task PlayAsync(MusicRow row)
    {
        if(!row.Song.CanPlay){status.Text="这首歌的名称或艺人信息不完整，暂时无法准确点播。";return;}
        playing?.Cancel();playing?.Dispose();playing=new();var token=playing.Token;var request=requests.Next();status.Text="正在切换 · "+row.Song.Title;
        try{var result=await play(row.Song,token);if(!disposed && requests.IsCurrent(request)){status.Text=result.Message;activate();}}
        catch(OperationCanceledException){}
        catch(Exception ex) when(ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException or IOException or TimeoutException){if(!disposed && requests.IsCurrent(request))status.Text="播放切换失败，请确认 Apple Music 可用后重试。";}
    }
    internal void SetNowPlaying(Track? track,bool isPlaying=false)
    {
        toggleButton.Content=isPlaying?"Ⅱ":"▷";
        string key=track?.Fingerprint??"";if(key==lastTrack || view is null)return;lastTrack=key;
        foreach(MusicRow row in view.SourceCollection)row.NowPlaying=track is not null && MusicLibraryPlayback.IsRequestedRecording(row.Song.Track,track);
    }
    public void Dispose(){if(disposed)return;disposed=true;debounce.Stop();loading?.Cancel();playing?.Cancel();loading?.Dispose();playing?.Dispose();thumbnails.Clear();albumCovers.Clear();}
    internal sealed class MusicRow(MusicLibrarySong song,ThumbnailCache cache):INotifyPropertyChanged
    {
        public MusicLibrarySong Song=>song;
        public string Title=>song.Title;
        public string Artist=>song.Artist;
        public string Album=>song.Album;
        public string SearchText {get;}=MatchRules.Normalize(song.Title+" "+song.Artist+" "+song.Album);
        public ImageSource? Cover=>cache.Get(song.CoverPath);
        bool current;
        public bool NowPlaying{get=>current;set{if(current==value)return;current=value;PropertyChanged?.Invoke(this,new(nameof(ArtistLine)));}}
        public string ArtistLine=>(current?"♫ 正在播放 · ":"")+song.Artist+(song.LyricsLabel.Length>0?"  ·  "+song.LyricsLabel:"");
        public event PropertyChangedEventHandler? PropertyChanged;
    }
    internal sealed class ThumbnailCache
    {
        readonly int decodeWidth;
        internal ThumbnailCache(int decodeWidth=96)=>this.decodeWidth=decodeWidth;
        readonly Dictionary<string,BitmapImage?> images=new(StringComparer.OrdinalIgnoreCase);readonly Queue<string> order=[];
        internal int Count=>images.Count;
        internal ImageSource? Get(string? path)
        {
            if(path is null)return null;if(images.TryGetValue(path,out var cached))return cached;
            BitmapImage? image=null;try{if(File.Exists(path)){image=new();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=decodeWidth;image.UriSource=new Uri(path);image.EndInit();image.Freeze();}}catch(Exception ex) when(ex is IOException or FormatException or UnauthorizedAccessException or NotSupportedException or ArgumentException){image=null;}
            while(order.Count>=128)images.Remove(order.Dequeue());images[path]=image;order.Enqueue(path);return image;
        }
        internal void Clear(){images.Clear();order.Clear();}
    }
}
