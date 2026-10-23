using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

internal sealed class PlaybackQueueView:Grid
{
    readonly TextBlock detail=new(){FontSize=12,TextWrapping=TextWrapping.Wrap},message=new(){FontSize=12,TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,14)};
    readonly TextBlock empty=new(){FontSize=15,TextWrapping=TextWrapping.Wrap,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
    readonly ListBox upcoming=new(){BorderThickness=new(0),Padding=new(0),HorizontalContentAlignment=HorizontalAlignment.Stretch};
    readonly Border currentCard=new(){CornerRadius=new(16),Padding=new(18,12,18,12),Margin=new(0,0,0,20),BorderThickness=new(1)};
    readonly Image currentCover=new(){Width=52,Height=52,Stretch=Stretch.UniformToFill,Margin=new(0,0,15,0)};
    readonly TextBlock currentTitle=new(){FontSize=16,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis},currentArtist=new(){FontSize=12,Margin=new(0,6,0,0),TextTrimming=TextTrimming.CharacterEllipsis};
    readonly MusicLibraryView.ThumbnailCache covers=new();
    Dictionary<string,MusicLibrarySong[]> library=new(StringComparer.Ordinal);
    bool rowsNeedRefresh=true;
    internal MusicQueueSnapshot? Snapshot {get;private set;}
    internal ListBox UpcomingList=>upcoming;
    internal Button RefreshButton {get;}=new(){Content="刷新列表",Padding=new(14,8,14,8),Margin=new(8,0,0,0)};
    internal Button BackButton {get;}=new(){Content="‹ 返回音乐库",Padding=new(14,8,14,8),Margin=new(0)};
    internal string Message=>message.Text;
    internal PlaybackQueueView(Action back,Action refresh)
    {
        Visibility=Visibility.Collapsed;
        RowDefinitions.Add(new(){Height=GridLength.Auto});RowDefinitions.Add(new(){Height=GridLength.Auto});RowDefinitions.Add(new(){Height=GridLength.Auto});RowDefinitions.Add(new(){Height=GridLength.Auto});RowDefinitions.Add(new());
        var heading=new DockPanel{Margin=new(0,0,0,20)};Children.Add(heading);var actions=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Top};DockPanel.SetDock(actions,Dock.Right);heading.Children.Add(actions);actions.Children.Add(BackButton);actions.Children.Add(RefreshButton);
        BackButton.Click+=(_,_)=>back();RefreshButton.Click+=(_,_)=>refresh();AutomationProperties.SetName(RefreshButton,"刷新当前播放列表");AutomationProperties.SetName(BackButton,"返回上次浏览的音乐库");
        var copy=new StackPanel();heading.Children.Add(copy);copy.Children.Add(new TextBlock{Text="当前播放列表",FontSize=23,FontWeight=FontWeights.SemiBold,Margin=new(0,0,0,8)});copy.Children.Add(detail);
        ThemeManager.Bind(detail,TextBlock.ForegroundProperty,"MutedBrush");ThemeManager.Bind(message,TextBlock.ForegroundProperty,"MutedBrush");ThemeManager.Bind(empty,TextBlock.ForegroundProperty,"MutedBrush");ThemeManager.Bind(currentArtist,TextBlock.ForegroundProperty,"MutedBrush");
        Grid.SetRow(message,1);Children.Add(message);Grid.SetRow(currentCard,2);Children.Add(currentCard);ThemeManager.Bind(currentCard,Border.BackgroundProperty,"AccentSoftBrush");ThemeManager.Bind(currentCard,Border.BorderBrushProperty,"BorderBrush");
        var current=new DockPanel();currentCard.Child=current;DockPanel.SetDock(currentCover,Dock.Left);current.Children.Add(currentCover);currentCover.Clip=new RectangleGeometry(new Rect(0,0,52,52),9,9);var currentCopy=new StackPanel{VerticalAlignment=VerticalAlignment.Center};current.Children.Add(currentCopy);
        var now=new TextBlock{Text="♫  当前播放",FontSize=10,Margin=new(0,0,0,7)};ThemeManager.Bind(now,TextBlock.ForegroundProperty,"AccentBrush");currentCopy.Children.Add(now);currentCopy.Children.Add(currentTitle);currentCopy.Children.Add(currentArtist);
        var nextHeading=new TextBlock{Text="接下来播放",FontSize=13,FontWeight=FontWeights.SemiBold,Margin=new(2,0,0,12)};Grid.SetRow(nextHeading,3);Children.Add(nextHeading);
        Grid.SetRow(upcoming,4);Children.Add(upcoming);Grid.SetRow(empty,4);Children.Add(empty);AutomationProperties.SetName(upcoming,"接下来播放的歌曲，按实际播放顺序");
        VirtualizingPanel.SetIsVirtualizing(upcoming,true);VirtualizingPanel.SetVirtualizationMode(upcoming,VirtualizationMode.Recycling);VirtualizingPanel.SetScrollUnit(upcoming,ScrollUnit.Pixel);ScrollViewer.SetCanContentScroll(upcoming,true);ScrollViewer.SetHorizontalScrollBarVisibility(upcoming,ScrollBarVisibility.Disabled);
        upcoming.ItemsPanel=new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));upcoming.ItemTemplate=QueueRowTemplate();
    }
    static DataTemplate QueueRowTemplate()
    {
        var row=new FrameworkElementFactory(typeof(DockPanel));row.SetValue(HeightProperty,64d);row.SetValue(MarginProperty,new Thickness(0,0,6,0));
        FrameworkElementFactory Label(string name,double size,string brush){var text=new FrameworkElementFactory(typeof(TextBlock));text.SetBinding(TextBlock.TextProperty,new Binding(name));text.SetValue(TextBlock.FontSizeProperty,size);text.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);text.SetValue(VerticalAlignmentProperty,VerticalAlignment.Center);text.SetResourceReference(TextBlock.ForegroundProperty,brush);return text;}
        var number=Label("Number",11,"MutedBrush");number.SetValue(WidthProperty,32d);number.SetValue(DockPanel.DockProperty,Dock.Left);row.AppendChild(number);
        var duration=Label("Duration",11,"MutedBrush");duration.SetValue(WidthProperty,52d);duration.SetValue(DockPanel.DockProperty,Dock.Right);duration.SetValue(TextBlock.TextAlignmentProperty,TextAlignment.Right);row.AppendChild(duration);
        var cover=new FrameworkElementFactory(typeof(Border));cover.SetValue(WidthProperty,44d);cover.SetValue(HeightProperty,44d);cover.SetValue(MarginProperty,new Thickness(0,0,14,0));cover.SetValue(DockPanel.DockProperty,Dock.Left);cover.SetResourceReference(Border.BackgroundProperty,"ArtworkBrush");cover.SetValue(Border.CornerRadiusProperty,new CornerRadius(8));
        var image=new FrameworkElementFactory(typeof(Image));image.SetBinding(Image.SourceProperty,new Binding("Cover"));image.SetValue(Image.StretchProperty,Stretch.UniformToFill);image.SetValue(ClipProperty,new RectangleGeometry(new Rect(0,0,44,44),8,8));cover.AppendChild(image);row.AppendChild(cover);
        var copy=new FrameworkElementFactory(typeof(StackPanel));copy.SetValue(VerticalAlignmentProperty,VerticalAlignment.Center);copy.AppendChild(Label("Title",14,"TextBrush"));var artist=Label("Subtitle",11,"MutedBrush");artist.SetValue(MarginProperty,new Thickness(0,6,0,0));copy.AppendChild(artist);row.AppendChild(copy);return new(){VisualTree=row};
    }
    internal void SetLibrary(IReadOnlyList<MusicLibrarySong> songs){library=songs.GroupBy(s=>s.Track.Fingerprint,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.ToArray(),StringComparer.Ordinal);covers.Clear();rowsNeedRefresh=true;if(Snapshot is not null)Apply(Snapshot);}
    ImageSource? Cover(Track track)=>library.TryGetValue(track.Fingerprint,out var candidates)?covers.Get(MusicPlaybackQueue.MatchLibrarySong(track,candidates)?.CoverPath):null;
    internal void Loading(){RefreshButton.IsEnabled=false;message.Text="正在读取 Apple Music 播放列表…";}
    internal void Apply(MusicQueueSnapshot snapshot)
    {
        bool sameRows=!rowsNeedRefresh && Snapshot is { } previous && previous.Upcoming.SequenceEqual(snapshot.Upcoming);
        Snapshot=snapshot;RefreshButton.IsEnabled=true;message.Text=snapshot.Message;
        detail.Text=snapshot.Available?$"接下来 {snapshot.Upcoming.Count:N0} 首 · {snapshot.CapturedUtc.ToLocalTime():HH:mm:ss} 更新"+(snapshot.IsComplete?"":" · 部分列表"):"连接 Apple Music，查看实际播放顺序";
        currentCard.Visibility=snapshot.Available && snapshot.Current is not null?Visibility.Visible:Visibility.Collapsed;
        if(snapshot.Current is { } track){currentTitle.Text=track.Title;currentArtist.Text=Subtitle(track);currentCover.Source=Cover(track);}
        if(!sameRows){upcoming.ItemsSource=snapshot.Upcoming.Select((entry,index)=>new QueueRow(index+1,entry,Cover)).ToArray();rowsNeedRefresh=false;}
        empty.Visibility=snapshot.Upcoming.Count==0?Visibility.Visible:Visibility.Collapsed;empty.Text=!snapshot.Available?"暂时无法读取播放列表\n可打开 Apple Music 后刷新":snapshot.IsComplete?"接下来还没有歌曲\n在歌曲旁点‘插播’，把喜欢的歌放到下一首":"Apple Music 尚未提供后续歌曲\n请稍后刷新";
    }
    internal void CancelLoading(){RefreshButton.IsEnabled=true;}
    internal void Dispose(){covers.Clear();upcoming.ItemsSource=null;currentCover.Source=null;}
    static string Subtitle(Track track)=>track.Artist+(track.Album.Length>0?" · "+track.Album:"");
    internal sealed record QueueRow(int Number,MusicQueueEntry Entry,Func<Track,ImageSource?> LoadCover)
    {
        public ImageSource? Cover=>LoadCover(Entry.Track);
        public string Title=>Entry.Track.Title.Length>0?Entry.Track.Title:"歌曲信息暂不可读";
        public string Subtitle=>Entry.MetadataComplete?(Entry.Inserted?"插播 · ":"")+PlaybackQueueView.Subtitle(Entry.Track):"Apple Music 暂未提供完整信息";
        public string Duration=>Entry.Track.DurationMs is >0?$"{(int)(Entry.Track.DurationMs.Value/60000)}:{(int)(Entry.Track.DurationMs.Value/1000)%60:00}":"—";
    }
}
