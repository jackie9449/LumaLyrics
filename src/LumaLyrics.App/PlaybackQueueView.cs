using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

internal sealed partial class PlaybackQueueView:Grid
{
    readonly TextBlock detail=new(){FontSize=12,TextWrapping=TextWrapping.Wrap},message=new(){FontSize=12,TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,14)};
    readonly TextBlock empty=new(){FontSize=15,TextWrapping=TextWrapping.Wrap,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
    readonly ListBox upcoming=new(){BorderThickness=new(0),Padding=new(0),HorizontalContentAlignment=HorizontalAlignment.Stretch};
    readonly Border currentCard=new(){CornerRadius=new(16),Padding=new(18,12,18,12),Margin=new(0,0,0,20),BorderThickness=new(1)};
    readonly ArtworkImage currentCover=new(){Width=52,Height=52,Stretch=Stretch.UniformToFill,Margin=new(0,0,15,0)};
    readonly TextBlock currentTitle=new(){FontSize=16,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis},currentArtist=new(){FontSize=12,Margin=new(0,6,0,0),TextTrimming=TextTrimming.CharacterEllipsis};
    readonly MusicLibraryView.ThumbnailCache covers=new();
    readonly bool compact;
    readonly Func<MusicQueueSelection,CancellationToken,Task<LibraryPlayResult>>? play;
    readonly Action? activate;
    readonly RequestVersion playVersions=new();
    CancellationTokenSource? playing;
    bool disposed;
    string? playMessage;
    Dictionary<string,MusicLibrarySong[]> library=new(StringComparer.Ordinal);
    bool rowsNeedRefresh=true;
    internal MusicQueueSnapshot? Snapshot {get;private set;}
    internal ListBox UpcomingList=>upcoming;
    internal Task? ActivePlayback {get;private set;}
    bool selecting;
    internal bool IsSelecting=>selecting || IsDragging;
    internal event Action? PlaybackStarting;
    internal event Action<Task>? PlaybackStarted;
    internal event Action? PlaybackCompleted;
    internal Button RefreshButton {get;}=new(){Content="刷新列表",Padding=new(14,8,14,8),Margin=new(8,0,0,0)};
    internal Button BackButton {get;}=new(){Content="‹ 返回音乐库",Padding=new(14,8,14,8),Margin=new(0)};
    internal string Message=>message.Text;
    internal PlaybackQueueView(Action back,Action refresh,bool compact=false,Func<MusicQueueSelection,CancellationToken,Task<LibraryPlayResult>>? play=null,Action? activate=null,Func<MusicQueueMove,CancellationToken,Task<LibraryPlayResult>>? reorder=null)
    {
        this.compact=compact;this.play=play;this.activate=activate;
        Visibility=Visibility.Collapsed;
        RowDefinitions.Add(new(){Height=GridLength.Auto});RowDefinitions.Add(new(){Height=GridLength.Auto});RowDefinitions.Add(new(){Height=GridLength.Auto});RowDefinitions.Add(new(){Height=GridLength.Auto});RowDefinitions.Add(new());
        var heading=new DockPanel{Margin=new(0,0,0,compact?12:20)};Children.Add(heading);var actions=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Top};DockPanel.SetDock(actions,Dock.Right);heading.Children.Add(actions);actions.Children.Add(BackButton);actions.Children.Add(RefreshButton);
        BackButton.Click+=(_,_)=>back();RefreshButton.Click+=(_,_)=>{playMessage=null;refresh();};AutomationProperties.SetName(RefreshButton,"刷新当前播放列表");AutomationProperties.SetName(BackButton,"返回上次浏览的音乐库");
        var copy=new StackPanel();heading.Children.Add(copy);copy.Children.Add(new TextBlock{Text=compact?"播放列表":"当前播放列表",FontSize=compact?16:23,FontWeight=FontWeights.SemiBold,Margin=new(0,0,0,compact?6:8)});copy.Children.Add(detail);
        ThemeManager.Bind(detail,TextBlock.ForegroundProperty,"MutedBrush");ThemeManager.Bind(message,TextBlock.ForegroundProperty,"MutedBrush");ThemeManager.Bind(empty,TextBlock.ForegroundProperty,"MutedBrush");ThemeManager.Bind(currentArtist,TextBlock.ForegroundProperty,"MutedBrush");
        Grid.SetRow(message,1);Children.Add(message);Grid.SetRow(currentCard,2);Children.Add(currentCard);ThemeManager.Bind(currentCard,Border.BackgroundProperty,"AccentSoftBrush");ThemeManager.Bind(currentCard,Border.BorderBrushProperty,"BorderBrush");
        ThemeManager.Bind(currentCard,Border.CornerRadiusProperty,"QueueCardRadius");ThemeManager.Bind(currentCover,ArtworkImage.CornersProperty,"CurrentArtworkRadius");ThemeManager.Bind(currentTitle,TextBlock.FontFamilyProperty,"DisplayFont");
        var current=new DockPanel();currentCard.Child=current;DockPanel.SetDock(currentCover,Dock.Left);current.Children.Add(currentCover);currentCover.Clip=new RectangleGeometry(new Rect(0,0,52,52),9,9);var currentCopy=new StackPanel{VerticalAlignment=VerticalAlignment.Center};current.Children.Add(currentCopy);
        var now=new TextBlock{Text="♫  当前播放",FontSize=10,Margin=new(0,0,0,7)};ThemeManager.Bind(now,TextBlock.ForegroundProperty,"AccentBrush");currentCopy.Children.Add(now);currentCopy.Children.Add(currentTitle);currentCopy.Children.Add(currentArtist);
        var nextHeading=new TextBlock{Text="接下来播放",FontSize=13,FontWeight=FontWeights.SemiBold,Margin=new(2,0,0,12)};Grid.SetRow(nextHeading,3);Children.Add(nextHeading);
        Grid.SetRow(upcoming,4);Children.Add(upcoming);Grid.SetRow(empty,4);Children.Add(empty);AutomationProperties.SetName(upcoming,"接下来播放的歌曲，按实际播放顺序");
        VirtualizingPanel.SetIsVirtualizing(upcoming,true);VirtualizingPanel.SetVirtualizationMode(upcoming,VirtualizationMode.Recycling);VirtualizingPanel.SetScrollUnit(upcoming,ScrollUnit.Pixel);ScrollViewer.SetCanContentScroll(upcoming,true);ScrollViewer.SetHorizontalScrollBarVisibility(upcoming,ScrollBarVisibility.Disabled);
        upcoming.ItemsPanel=new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));upcoming.ItemTemplate=QueueRowTemplate();
        upcoming.MouseDoubleClick+=(_,e)=>
        {
            if(e.ChangedButton==MouseButton.Left && e.OriginalSource is DependencyObject source
                && ItemsControl.ContainerFromElement(upcoming,source) is ListBoxItem{DataContext:QueueRow row})
            {e.Handled=true;BeginPlayback(row);}
        };
        upcoming.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Enter && upcoming.SelectedItem is QueueRow row){e.Handled=true;BeginPlayback(row);}};
        upcoming.ToolTip=play is null?null:"双击曲目，或选中后按 Enter，跳到队列中的这首歌";
        SetupReordering(reorder);
        if(compact)
        {
            BackButton.Content="＋";BackButton.ToolTip="打开音乐库选歌";AutomationProperties.SetName(BackButton,"打开音乐库选歌");RefreshButton.Content="\uE72C";RefreshButton.FontFamily=new("Segoe MDL2 Assets");RefreshButton.ToolTip="刷新播放列表";
            foreach(var button in new[]{BackButton,RefreshButton}){button.Width=button.Height=28;button.MinWidth=0;button.Padding=new(0);button.Margin=new(button==RefreshButton?4:0,0,0,0);button.FontSize=12;}
            detail.FontSize=message.FontSize=10;message.Margin=new(0,0,0,10);empty.FontSize=12;empty.MaxWidth=180;
            currentCard.Padding=new(10);currentCard.CornerRadius=new(12);currentCard.Margin=new(0,0,0,14);currentCover.Width=currentCover.Height=34;currentCover.Margin=new(0,0,10,0);currentCover.Clip=new RectangleGeometry(new Rect(0,0,34,34),7,7);currentTitle.FontSize=13;currentArtist.FontSize=10;
            ThemeManager.Bind(currentCard,Border.CornerRadiusProperty,"CompactQueueRadius");ThemeManager.Bind(currentCover,ArtworkImage.CornersProperty,"QueueArtworkRadius");
            now.FontSize=9;now.Margin=new(0,0,0,5);nextHeading.FontSize=11;nextHeading.Margin=new(0,0,0,8);
            var itemStyle=new Style(typeof(ListBoxItem),System.Windows.Application.Current.TryFindResource(typeof(ListBoxItem)) as Style);itemStyle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(6,4,6,4)));itemStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty,new Thickness(0,0,0,4)));itemStyle.Setters.Add(new Setter(FrameworkElement.WidthProperty,new Binding("ActualWidth"){RelativeSource=new(RelativeSourceMode.FindAncestor,typeof(ScrollContentPresenter),1)}));upcoming.ItemContainerStyle=itemStyle;
            upcoming.ItemTemplate=QueueRowTemplate(true);
        }
    }
    static DataTemplate QueueRowTemplate(bool compact=false)
    {
        var row=new FrameworkElementFactory(typeof(DockPanel));row.SetValue(HeightProperty,compact?48d:64d);row.SetValue(MarginProperty,new Thickness(0,0,compact?0:6,0));row.SetBinding(ToolTipProperty,new Binding("FullDescription"));
        FrameworkElementFactory Label(string name,double size,string brush){var text=new FrameworkElementFactory(typeof(TextBlock));text.SetBinding(TextBlock.TextProperty,new Binding(name));text.SetValue(TextBlock.FontSizeProperty,size);text.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);text.SetValue(VerticalAlignmentProperty,VerticalAlignment.Center);text.SetResourceReference(TextBlock.ForegroundProperty,brush);return text;}
        var number=Label("Number",compact?10:11,"MutedBrush");number.SetValue(WidthProperty,compact?21d:32d);number.SetValue(DockPanel.DockProperty,Dock.Left);row.AppendChild(number);
        var duration=Label("Duration",compact?10:11,"MutedBrush");duration.SetValue(WidthProperty,compact?37d:52d);duration.SetValue(DockPanel.DockProperty,Dock.Right);duration.SetValue(TextBlock.TextAlignmentProperty,TextAlignment.Right);row.AppendChild(duration);
        double size=compact?32:44;var cover=new FrameworkElementFactory(typeof(ArtworkFrame));cover.SetValue(WidthProperty,size);cover.SetValue(HeightProperty,size);cover.SetValue(MarginProperty,new Thickness(0,0,compact?9:14,0));cover.SetValue(DockPanel.DockProperty,Dock.Left);cover.SetResourceReference(Border.BackgroundProperty,"ArtworkBrush");cover.SetResourceReference(Border.CornerRadiusProperty,"QueueBorderRadius");
        var image=new FrameworkElementFactory(typeof(ArtworkImage));image.SetBinding(Image.SourceProperty,new Binding("Cover"));image.SetValue(Image.StretchProperty,Stretch.UniformToFill);image.SetResourceReference(ArtworkImage.CornersProperty,"QueueArtworkRadius");cover.AppendChild(image);row.AppendChild(cover);
        var copy=new FrameworkElementFactory(typeof(StackPanel));copy.SetValue(VerticalAlignmentProperty,VerticalAlignment.Center);copy.AppendChild(Label("Title",compact?12:14,"TextBrush"));var artist=Label("Subtitle",compact?10:11,"MutedBrush");artist.SetValue(MarginProperty,new Thickness(0,compact?5:6,0,0));copy.AppendChild(artist);row.AppendChild(copy);return new(){VisualTree=row};
    }
    internal void SetLibrary(IReadOnlyList<MusicLibrarySong> songs){library=songs.GroupBy(s=>s.Track.Fingerprint,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.ToArray(),StringComparer.Ordinal);covers.Clear();rowsNeedRefresh=true;if(Snapshot is not null)Apply(Snapshot);}
    ImageSource? Cover(Track track)=>library.TryGetValue(track.Fingerprint,out var candidates)?covers.Get(MusicPlaybackQueue.MatchLibrarySong(track,candidates)?.CoverPath):null;
    internal void Loading(){RefreshButton.IsEnabled=false;message.Text=playMessage??"正在读取 Apple Music 播放列表…";}
    internal void WaitingForSelection(){RefreshButton.IsEnabled=false;message.Text="正在切换歌曲，播放列表稍后更新…";}
    internal void Apply(MusicQueueSnapshot snapshot)
    {
        bool sameRows=!rowsNeedRefresh && Snapshot is { } previous && previous.Upcoming.SequenceEqual(snapshot.Upcoming);
        Snapshot=snapshot;RefreshButton.IsEnabled=true;message.Text=playMessage??(compact && snapshot.Available && snapshot.IsComplete?"与 Apple Music 同步":snapshot.Message);
        detail.Text=snapshot.Available?$"接下来 {snapshot.Upcoming.Count:N0} 首"+(compact?"":$" · {snapshot.CapturedUtc.ToLocalTime():HH:mm:ss} 更新")+(snapshot.IsComplete?"":" · 部分列表"):compact?"当前与接下来播放":"连接 Apple Music，查看实际播放顺序";
        currentCard.Visibility=snapshot.Available && snapshot.Current is not null?Visibility.Visible:Visibility.Collapsed;
        if(snapshot.Current is { } track){currentTitle.Text=track.Title;currentArtist.Text=Subtitle(track);currentTitle.ToolTip=track.Title;currentArtist.ToolTip=Subtitle(track);currentCover.Source=Cover(track);}
        if(!sameRows){upcoming.ItemsSource=snapshot.Upcoming.Select((entry,index)=>new QueueRow(index+1,entry,Cover)).ToArray();rowsNeedRefresh=false;}
        empty.Visibility=snapshot.Upcoming.Count==0?Visibility.Visible:Visibility.Collapsed;empty.Text=!snapshot.Available?"暂时无法读取播放列表\n可打开 Apple Music 后刷新":snapshot.IsComplete?"接下来还没有歌曲\n在歌曲旁点‘插播’，把喜欢的歌放到下一首":"Apple Music 尚未提供后续歌曲\n请稍后刷新";
    }
    internal void CancelLoading(){RefreshButton.IsEnabled=true;}
    void BeginPlayback(QueueRow row)
    {
        if(disposed || play is null || Snapshot is not { } snapshot)return;
        int index=row.Number-1;
        if(index<0 || index>=upcoming.Items.Count || !ReferenceEquals(upcoming.Items[index],row))return;
        var selection=new MusicQueueSelection(snapshot,index);
        if(!MusicPlaybackQueue.CanSelect(selection)){message.Text=playMessage="这首歌的队列信息不完整，请刷新后重试。";return;}
        PlaybackStarting?.Invoke();ActivePlayback=PlayAsync(selection);PlaybackStarted?.Invoke(ActivePlayback);
    }
    async Task PlayAsync(MusicQueueSelection selection)
    {
        playing?.Cancel();playing?.Dispose();playing=new();var token=playing.Token;long version=playVersions.Next();selecting=true;
        message.Text=playMessage="正在跳到 · "+selection.Snapshot.Upcoming[selection.Index].Track.Title;
        try
        {
            var result=await play!(selection,token);token.ThrowIfCancellationRequested();
            if(disposed || !playVersions.IsCurrent(version))return;
            message.Text=playMessage=result.Message;if(result.Confirmed && result.ActivateWindow)activate?.Invoke();
        }
        catch(OperationCanceledException){}
        catch(Exception ex) when(ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException or System.IO.IOException or TimeoutException)
        {if(!disposed && playVersions.IsCurrent(version))message.Text=playMessage="队列跳转失败，请刷新播放列表后重试。";}
        finally{if(!disposed && playVersions.IsCurrent(version)){selecting=false;PlaybackCompleted?.Invoke();}}
    }
    internal void CancelPlayback(){playing?.Cancel();playVersions.Next();selecting=false;playMessage=null;}
    internal void Dispose(){if(disposed)return;disposed=true;CancelPlayback();ClearDropHint();playing?.Dispose();covers.Clear();upcoming.ItemsSource=null;currentCover.Source=null;}
    static string Subtitle(Track track)=>track.Artist+(track.Album.Length>0?" · "+track.Album:"");
    internal sealed record QueueRow(int Number,MusicQueueEntry Entry,Func<Track,ImageSource?> LoadCover)
    {
        public ImageSource? Cover=>LoadCover(Entry.Track);
        public string Title=>Entry.Track.Title.Length>0?Entry.Track.Title:"歌曲信息暂不可读";
        public string Subtitle=>Entry.MetadataComplete?(Entry.Inserted?"插播 · ":"")+PlaybackQueueView.Subtitle(Entry.Track):"Apple Music 暂未提供完整信息";
        public string Duration=>Entry.Track.DurationMs is >0?$"{(int)(Entry.Track.DurationMs.Value/60000)}:{(int)(Entry.Track.DurationMs.Value/1000)%60:00}":"—";
        public string FullDescription=>Title+"\n"+Subtitle+" · "+Duration;
    }
}
