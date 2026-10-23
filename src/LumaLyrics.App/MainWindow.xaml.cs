using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
using Forms=System.Windows.Forms;
namespace LumaLyrics.App;

public partial class MainWindow : Window
{
    readonly PlayerViewModel vm = new(); readonly SettingsStore store;
    readonly PlaybackCoordinator coordinator; readonly AppSettings settings;
    readonly OverlayWindow overlay; readonly DispatcherTimer timer;
    Forms.NotifyIcon? tray; Hotkeys? hotkeys; bool quitting, scrollProgrammatically;AppearanceWindow? appearanceWindow;
    SettingsWindow? settingsWindow;
    LyricsDocument? displayed; int currentIndex=-2; DateTime manualUntil, pausedSince; string? hotkeyError;
    readonly List<TextBlock> lyricBlocks=[];
    readonly List<TextBlock> translationBlocks=[];ChineseTranslation? displayedTranslation;bool displayedTranslationEnabled;
    ArtworkPalette? artworkPalette;
    public bool CanRecoverOverlay => tray?.Visible == true;
    public MainWindow(string? dataDirectory=null)
    {
        store=new(dataDirectory);settings=store.Load();ThemeManager.Apply(settings.UiTheme,applicationScope:Resources);
        InitializeComponent();DataContext=vm;coordinator=new(Dispatcher,store,settings);
        ArtworkBorder.SizeChanged+=(_,_)=>ClipArtwork();
        overlay=new(this,vm,settings);
        SelectUiTheme(settings.UiTheme,false);
        coordinator.Updated+=Update; coordinator.ArtworkChanged+=SetArtwork;
        timer=new DispatcherTimer(TimeSpan.FromMilliseconds(33),DispatcherPriority.Background,(_,_)=>Tick(),Dispatcher);
        Loaded+=OnLoaded; Closing+=OnClosing; SizeChanged+=OnSize;
        SystemEvents.PowerModeChanged+=PowerModeChanged; SystemEvents.DisplaySettingsChanged+=DisplayChanged;
        LocationChanged+=(_,_)=> { if(IsLoaded && WindowState==WindowState.Normal) { var p=NativeWindow.Location(this); settings.MainLeft=p.Left; settings.MainTop=p.Top; } };
    }
    internal void SelectUiTheme(string id,bool persist=true)
    {
        var theme=AppThemes.Find(id);settings.UiTheme=theme.Id;ThemeManager.Apply(theme.Id,artworkPalette,applicationScope:Resources);
        ClipArtwork();
        StyleLyricLines(-1,currentIndex);
        if(persist)coordinator.SaveSettings();
    }
    void ClipArtwork()
    {
        if(ArtworkBorder.ActualWidth>0 && ArtworkBorder.ActualHeight>0)
            ArtworkBorder.Clip=new RectangleGeometry(new Rect(0,0,ArtworkBorder.ActualWidth,ArtworkBorder.ActualHeight),ThemeManager.Current.Radius,ThemeManager.Current.Radius);
    }
    void TitleBarPressed(object sender,MouseButtonEventArgs e)
    {
        if(e.OriginalSource is DependencyObject source)
            for(var node=source;node is not null;node=VisualTreeHelper.GetParent(node))
                if(node is System.Windows.Controls.Primitives.ButtonBase)return;
        if(e.ClickCount==2)WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;
        else if(e.LeftButton==MouseButtonState.Pressed)DragMove();
    }
    void MinimizeClicked(object sender,RoutedEventArgs e)=>WindowState=WindowState.Minimized;
    void MaximizeClicked(object sender,RoutedEventArgs e)=>WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;
    void CloseClicked(object sender,RoutedEventArgs e)=>Close();
    async void OnLoaded(object sender,RoutedEventArgs e)
    {
        NativeWindow.Place(this,settings.MainLeft,settings.MainTop);
        var startupArgs=Environment.GetCommandLineArgs();int uiArg=Array.IndexOf(startupArgs,"--verify-ui");
        int nativeArg=Array.IndexOf(startupArgs,"--verify-native-library-playback");
        if(nativeArg>=0 && nativeArg+1<startupArgs.Length)
        {
            timer.Stop();settings.OnlineLookup=false;settings.AutoPrefetchLibrary=false;
            try{await VerifyNativeLibraryPlaybackAsync(startupArgs[nativeArg+1],startupArgs);}
            catch(Exception ex){Directory.CreateDirectory(startupArgs[nativeArg+1]);File.WriteAllText(Path.Combine(startupArgs[nativeArg+1],"failure.txt"),ex.ToString());}
            await QuitAsync();return;
        }
        if(uiArg>=0 && uiArg+1<startupArgs.Length)
        {
            SetupTray();timer.Stop();
            try{await VerifyUiThemesAsync(startupArgs[uiArg+1]);}
            catch(Exception ex){store.Log("UI verification: "+ex);Directory.CreateDirectory(startupArgs[uiArg+1]);File.WriteAllText(Path.Combine(startupArgs[uiArg+1],"failure.txt"),ex.ToString());}
            if(startupArgs.Contains("--exit-after-capture"))await QuitAsync();return;
        }
        SetupTray(); hotkeys=new(this,id=>{if(id==1)ToggleOverlay();else {if(!overlay.IsVisible)ToggleOverlay();overlay.SetLocked(false);}});
        RegisterHotkeys(); if(settings.OverlayVisible)overlay.Show();
        try { await coordinator.StartAsync(); } catch(Exception ex) { vm.Status="连接失败，可在诊断页重新连接";store.Log("Start: "+ex.Message); }
        store.Log("Main window loaded; tray="+CanRecoverOverlay+"; hotkeys="+(string.IsNullOrEmpty(hotkeyError)?"OK":hotkeyError));
        var args=Environment.GetCommandLineArgs();int captureArg=Array.IndexOf(args,"--capture-dir");
        if(captureArg>=0 && captureArg+1<args.Length)
        {
            await Task.Delay(2000);for(int i=0;i<30 && coordinator.LyricsStatus.Contains("正在查找");i++)await Task.Delay(500);
            int verifyArg=Array.IndexOf(args,"--verify-import");
            if(verifyArg>=0 && verifyArg+1<args.Length)await VerifyImportAsync(args[verifyArg+1],args[captureArg+1]);
            await CaptureAsync(args[captureArg+1]);if(args.Contains("--exit-after-capture"))await QuitAsync();
        }
        if(args.Contains("--appearance") && !quitting)ShowAppearance();
        if(args.Contains("--music-library") && !quitting)await ShowMusicLibraryAsync();
    }
    void SetupTray()
    {
        try
        {
            var menu=new Forms.ContextMenuStrip();
            void Item(string text,Action action)=>menu.Items.Add(text,null,(_,_)=>Dispatcher.Invoke(action));
            Item("打开浮光歌词",Restore);Item("显示 / 隐藏歌词",ToggleOverlay);Item("解锁歌词",()=>{if(!overlay.IsVisible)ToggleOverlay();overlay.SetLocked(false);});Item("恢复默认位置",()=>{if(!overlay.IsVisible)ToggleOverlay();overlay.ResetPosition();});
            Item("桌面歌词外观…",()=>ShowAppearance());
            menu.Items.Add(new Forms.ToolStripSeparator());Item("退出",()=>_=QuitAsync());
            tray=new Forms.NotifyIcon{Icon=System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application,Text="浮光歌词 LumaLyrics",ContextMenuStrip=menu,Visible=true};
            tray.DoubleClick+=(_,_)=>Dispatcher.Invoke(Restore);
        }
        catch(Exception ex) {store.Log("Tray: "+ex.Message);vm.Status="托盘不可用，关闭窗口将退出";}
    }
    void RegisterHotkeys() { hotkeyError=hotkeys?.Register(settings.ToggleHotkey,settings.UnlockHotkey);if(!string.IsNullOrEmpty(hotkeyError) && !Environment.GetCommandLineArgs().Contains("--capture-dir"))MessageBox.Show(this,hotkeyError+"。仍可通过托盘解锁。","快捷键提示"); }
    void Update()
    {
        var s=coordinator.Snapshot;
        vm.Title=coordinator.ActiveTrack?.Title is {Length:>0} t?t:"让歌词，浮在音乐之上。";vm.Artist=s is null?coordinator.ManualTrack is { } mt?$"{mt.Artist} · {mt.Album} · 手动浏览":"打开 Apple Music，播放你喜欢的歌":$"{s.Track.Artist} · {s.Track.Album}";
        vm.Status=s is null?"等待 Apple Music":$"已连接 · {(s.Status==PlayState.Playing?"正在播放":s.Status==PlayState.Paused?"已暂停":"已停止")}";
        vm.CanPlay=s is not null && (s.Status==PlayState.Playing?s.CanPause:s.CanPlay); vm.CanNext=s?.CanNext==true;vm.CanPrevious=s?.CanPrevious==true;vm.CanSeek=s?.CanSeek==true;
        PlayButton.Content=s?.Status==PlayState.Playing?"Ⅱ 暂停":"▷ 播放";
        vm.LyricStatus=coordinator.LyricsStatus;vm.Duration=s?.Track.DurationMs ?? 1;
        overlay.RefreshTranslationToggle();
        vm.Library=coordinator.LibraryStatus+"\n"+coordinator.ArtworkStatus;
        musicLibrary?.SetNowPlaying(coordinator.ActiveTrack,s?.Status==PlayState.Playing);
        musicLibrary?.ObserveArtworkProgress(coordinator.ArtworkStatus);
        vm.LibraryLabel=coordinator.LibraryStatus.Total>0?$"资料库 {coordinator.LibraryStatus.Cached}/{coordinator.LibraryStatus.Total}":"资料库";
        vm.Offset=$"歌词偏移 {(coordinator.Binding?.OffsetMs ?? 0)/1000:+0.0;-0.0;0.0} 秒";
        if(!ReferenceEquals(displayed,coordinator.Document) || !ReferenceEquals(displayedTranslation,coordinator.Translation) || displayedTranslationEnabled!=settings.ShowChineseTranslation)BuildLyrics();
        Tick();
    }
    void BuildLyrics()
    {
        bool sameDocument=ReferenceEquals(displayed,coordinator.Document);double previousScroll=LyricsScroll.VerticalOffset;var browsingUntil=manualUntil;
        displayed=coordinator.Document;displayedTranslation=coordinator.Translation;displayedTranslationEnabled=settings.ShowChineseTranslation;currentIndex=-2;lyricBlocks.Clear();translationBlocks.Clear();LyricLines.Children.Clear();
        bool empty=displayed is null || displayed.Instrumental || string.IsNullOrWhiteSpace(displayed.Raw);
        bool instrumental=displayed?.Instrumental==true;
        overlay.SetInstrumental(instrumental);
        EmptyState.Visibility=empty&&!instrumental?Visibility.Visible:Visibility.Collapsed;
        LyricsScroll.Visibility=instrumental?Visibility.Collapsed:Visibility.Visible;ReturnButton.Visibility=Visibility.Collapsed;
        EmptyHint.Text=coordinator.Snapshot is null?"在 Apple Music 中播放歌曲，或导入本地歌词进行静态浏览。":coordinator.LyricsStatus;
        if(empty)return;
        foreach(var text in displayed!.IsSynced?displayed.Groups.Select(g=>g.Text):displayed.Raw.Split('\n'))
        {
            var line=new TextBlock{Text=string.IsNullOrWhiteSpace(text)?" ":text,TextWrapping=TextWrapping.Wrap,FontSize=24,LineHeight=36,Margin=new(0,0,0,24),Foreground=(Brush)FindResource("MutedBrush")};
            string translation=displayedTranslationEnabled && displayed.IsSynced?displayedTranslation?.TextAt(lyricBlocks.Count)??"":"";
            var translated=new TextBlock{Text=translation,TextWrapping=TextWrapping.Wrap,FontSize=16,LineHeight=26,Margin=new(0,5,0,24),Visibility=translation.Length>0?Visibility.Visible:Visibility.Collapsed};
            translated.SetResourceReference(TextBlock.ForegroundProperty,"MutedBrush");
            if(translation.Length>0){line.Margin=new(0);var pair=new StackPanel();pair.Children.Add(line);pair.Children.Add(translated);LyricLines.Children.Add(pair);}else LyricLines.Children.Add(line);
            lyricBlocks.Add(line);translationBlocks.Add(translated);
        }
        if(sameDocument){manualUntil=browsingUntil;scrollProgrammatically=true;LyricsScroll.ScrollToVerticalOffset(previousScroll);scrollProgrammatically=false;}
        else{LyricsScroll.ScrollToTop();manualUntil=DateTime.MinValue;}
    }
    internal void SetOverlayChineseTranslation(bool enabled)=>coordinator.SetTranslationVisibility(settings.ShowChineseTranslation,enabled);
    void Tick()
    {
        var s=coordinator.Snapshot; var p=coordinator.Position;
        static string T(double? ms)=>ms.HasValue?TimeSpan.FromMilliseconds(Math.Max(0,ms.Value)).ToString(ms>=3600000?@"h\:mm\:ss":@"mm\:ss"):"--:--";
        vm.Time=$"{T(p)} / {T(s?.Track.DurationMs)}";
        if(!SeekSlider.IsMouseCaptureWithin)vm.Position=p ?? 0;
        vm.Quality=s is null?"尚无播放时间轴":p.HasValue?"GSMTC · 整秒位置插值 · B 级兼容":"同步受限 · 当前无可信播放位置";
        if(s?.Status==PlayState.Paused) { if(pausedSince==DateTime.MinValue)pausedSince=DateTime.UtcNow; }else pausedSince=DateTime.MinValue;
        overlay.Opacity=!overlay.AppearanceEditing && settings.FadeOnPause && pausedSince!=DateTime.MinValue && DateTime.UtcNow-pausedSince>TimeSpan.FromSeconds(10)?.25:1;
        int index=displayed?.IsSynced==true && p.HasValue?displayed.FindIndex(p.Value,coordinator.Binding?.OffsetMs ?? 0,settings.GlobalOffsetMs):-1;
        if(index!=currentIndex)
        {
            int old=currentIndex;currentIndex=index;
            StyleLyricLines(old,index);
            if(index>=0 && DateTime.UtcNow>=manualUntil)CenterLine(Math.Abs(index-old)>2);
        }
        if(DateTime.UtcNow>=manualUntil && ReturnButton.Visibility==Visibility.Visible) {ReturnButton.Visibility=Visibility.Collapsed;CenterLine(true);}
        if(displayed?.Instrumental==true) {vm.Current="";vm.Next="";}
        else if(displayed?.IsSynced==true && p.HasValue) {vm.Current=index>=0?displayed.Groups[index].Text:"前奏";vm.Next=index+1<displayed.Groups.Count?displayed.Groups[index+1].Text:"";}
        else {vm.Current=s?.Track.Title ?? "浮光歌词";vm.Next=displayed is null?"等待歌词":displayed.IsSynced?"同步受限 · 请在主窗口浏览":"静态歌词 · 请在主窗口浏览";}
        bool translate=settings.OverlayShowChineseTranslation && displayed?.Instrumental!=true && displayed?.IsSynced==true && p.HasValue;
        vm.CurrentTranslation=translate && index>=0?coordinator.Translation?.TextAt(index)??"":"";
        vm.NextTranslation=translate && index+1<displayed!.Groups.Count?coordinator.Translation?.TextAt(index+1)??"":"";
        if(EmptyState.Visibility==Visibility.Visible && displayed?.Instrumental!=true)EmptyHint.Text=coordinator.LyricsStatus;
        var desired=s?.Status==PlayState.Playing && (IsVisible||overlay.IsVisible)?33:500;
        if(timer.Interval.TotalMilliseconds!=desired)timer.Interval=TimeSpan.FromMilliseconds(desired);
    }
    public static readonly DependencyProperty AnimatedOffsetProperty=DependencyProperty.Register("AnimatedOffset",typeof(double),typeof(MainWindow),new PropertyMetadata(0d,(d,e)=>{var w=(MainWindow)d;w.scrollProgrammatically=true;w.LyricsScroll.ScrollToVerticalOffset((double)e.NewValue);w.scrollProgrammatically=false;}));
    void StyleLyricLines(int oldIndex,int newIndex)
    {
        bool motion=oldIndex>=0 && newIndex>=0 && settings.OverlayAnimations && !settings.ReduceMotion && SystemParameters.ClientAreaAnimation;
        var duration=TimeSpan.FromMilliseconds(Math.Min(260,OverlayAppearance.From(settings).TransitionMs));
        for(int i=0;i<lyricBlocks.Count;i++)
        {
            var line=lyricBlocks[i];bool active=i==newIndex;
            if(i<translationBlocks.Count){translationBlocks[i].FontSize=active?19:16;translationBlocks[i].Opacity=active?.95:.72;}
            double oldSize=line.FontSize*(line.RenderTransform is ScaleTransform prior?prior.ScaleX:1),oldOpacity=line.Opacity;
            var color=((SolidColorBrush)FindResource(active?"TextBrush":"MutedBrush")).Color;
            var priorColor=line.Foreground is SolidColorBrush previous?previous.Color:color;
            line.FontSize=active?34:24;line.FontWeight=active?FontWeights.SemiBold:FontWeights.Normal;
            line.BeginAnimation(OpacityProperty,null);line.Opacity=active?1:ThemeManager.Current.IsDark?.65:.82;line.RenderTransform=Transform.Identity;
            var brush=new SolidColorBrush(color);line.Foreground=brush;
            if(!motion || i!=oldIndex && i!=newIndex)continue;
            var ease=new CubicEase{EasingMode=EasingMode.EaseOut};var scale=new ScaleTransform(1,1);line.RenderTransformOrigin=new(0,.5);line.RenderTransform=scale;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(oldSize/line.FontSize,1,duration){EasingFunction=ease,FillBehavior=FillBehavior.Stop});
            scale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(oldSize/line.FontSize,1,duration){EasingFunction=ease,FillBehavior=FillBehavior.Stop});
            line.BeginAnimation(OpacityProperty,new DoubleAnimation(oldOpacity,line.Opacity,duration){EasingFunction=ease,FillBehavior=FillBehavior.Stop});
            brush.BeginAnimation(SolidColorBrush.ColorProperty,new ColorAnimation(priorColor,color,duration){FillBehavior=FillBehavior.Stop});
        }
    }
    void CenterLine(bool jump)
    {
        if(currentIndex<0 || currentIndex>=lyricBlocks.Count)return;
        LyricLines.UpdateLayout(); var target=lyricBlocks[currentIndex];var y=target.TranslatePoint(new Point(0,0),LyricLines).Y+LyricLines.Margin.Top-LyricsScroll.ViewportHeight*.38;
        BeginAnimation(AnimatedOffsetProperty,null);
        if(jump||settings.ReduceMotion) {scrollProgrammatically=true;LyricsScroll.ScrollToVerticalOffset(Math.Max(0,y));scrollProgrammatically=false;}
        else {SetValue(AnimatedOffsetProperty,LyricsScroll.VerticalOffset);BeginAnimation(AnimatedOffsetProperty,new DoubleAnimation(LyricsScroll.VerticalOffset,Math.Max(0,y),TimeSpan.FromMilliseconds(250)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}});}
    }
    void ManualScroll(object s,MouseWheelEventArgs e) {manualUntil=DateTime.UtcNow.AddSeconds(8);ReturnButton.Visibility=Visibility.Visible;BeginAnimation(AnimatedOffsetProperty,null);}
    void LyricsScrollChanged(object s,ScrollChangedEventArgs e) {if(!scrollProgrammatically && LyricsScroll.IsMouseCaptureWithin){manualUntil=DateTime.UtcNow.AddSeconds(8);ReturnButton.Visibility=Visibility.Visible;}}
    void ReturnClicked(object s,RoutedEventArgs e) {ShowLyricsPage();manualUntil=DateTime.MinValue;ReturnButton.Visibility=Visibility.Collapsed;CenterLine(true);}
    void SetArtwork(byte[]? bytes)
    {
        Artwork.Source=null;CoverPlaceholder.Visibility=Visibility.Visible;artworkPalette=null;
        if(bytes is not null)
        {
            try
            {
                using var stream=new MemoryStream(bytes);
                var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();image.Freeze();
                Artwork.Source=image;CoverPlaceholder.Visibility=Visibility.Collapsed;
                try{artworkPalette=ArtworkPaletteSampler.Extract(image);}
                catch(Exception ex){store.Log("Artwork palette: "+ex.GetType().Name);}
            }
            catch(Exception ex){store.Log("Artwork decode: "+ex.GetType().Name);}
        }
        ThemeManager.Apply(settings.UiTheme,artworkPalette,animate:!settings.ReduceMotion,applicationScope:Resources);
        StyleLyricLines(-1,currentIndex);
    }
    void OnSize(object s,SizeChangedEventArgs e)
    {
        if(InfoPanel is null)return;bool narrow=ActualWidth<930;
        Grid.SetColumn(InfoPanel,0);Grid.SetRow(InfoPanel,0);Grid.SetColumn(LyricsPanel,narrow?0:1);Grid.SetRow(LyricsPanel,narrow?1:0);Grid.SetColumnSpan(InfoPanel,narrow?2:1);Grid.SetColumnSpan(LyricsPanel,narrow?2:1);
        InfoColumn.Width=narrow?new GridLength(1,GridUnitType.Star):new GridLength(ActualWidth<1100?260:310);InfoRow.Height=narrow?new GridLength(154):new GridLength(1,GridUnitType.Star);LyricsRow.Height=narrow?new GridLength(1,GridUnitType.Star):new GridLength(0);
        ArtworkBorder.Visibility=narrow?Visibility.Collapsed:Visibility.Visible;InfoPanel.Margin=narrow?new Thickness(0,0,0,12):new Thickness(0,0,28,0);
        TrackTitle.FontSize=narrow?18:25;TrackTitle.MaxHeight=narrow?26:70;TrackTitle.Margin=narrow?new Thickness(0,0,0,4):new Thickness(0,20,0,7);TrackArtist.MaxHeight=narrow?18:36;TrackTime.Margin=narrow?new Thickness(0,5,0,0):new Thickness(0,18,0,2);
        PlaybackControls.Margin=narrow?new Thickness(0,2,0,0):new Thickness(0,8,0,12);
        ConnectionStatus.Visibility=QualityStatus.Visibility=narrow?Visibility.Collapsed:Visibility.Visible;
        EmptyState.MaxWidth=narrow?460:390;
        EmptyState.LayoutTransform=new ScaleTransform(narrow?.68:1,narrow?.68:1);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded,()=>{if(currentIndex>=0 && DateTime.UtcNow>=manualUntil)CenterLine(true);});
    }
    public void Restore() {Show();WindowState=WindowState.Normal;Activate();NativeWindow.ClampVisible(this);}
    public void ToggleOverlay() {if(overlay.IsVisible)overlay.Hide();else {overlay.Show();NativeWindow.ClampVisible(overlay);}settings.OverlayVisible=overlay.IsVisible;coordinator.SaveSettings();}
    void ToggleOverlayClicked(object s,RoutedEventArgs e)=>ToggleOverlay();
    async void PlayClicked(object s,RoutedEventArgs e)=>await ControlAsync(coordinator.Snapshot?.Status==PlayState.Playing?"pause":"play");
    async void NextClicked(object s,RoutedEventArgs e)=>await ControlAsync("next");
    async void PreviousClicked(object s,RoutedEventArgs e)=>await ControlAsync("previous");
    async Task ControlAsync(string action,double? position=null) {if(!await coordinator.Source.ControlAsync(action,position))MessageBox.Show(this,"播放器未接受此操作。","浮光歌词");}
    async void SeekReleased(object s,MouseButtonEventArgs e) {if(coordinator.Snapshot?.CanSeek==true)await ControlAsync("seek",SeekSlider.Value);}
    void EarlierClicked(object s,RoutedEventArgs e)=>coordinator.SetOffset((coordinator.Binding?.OffsetMs ?? 0)-100);
    void LaterClicked(object s,RoutedEventArgs e)=>coordinator.SetOffset((coordinator.Binding?.OffsetMs ?? 0)+100);
    void ResetOffsetClicked(object s,RoutedEventArgs e)=>coordinator.SetOffset(0);
    async void RetryClicked(object s,RoutedEventArgs e)=>await coordinator.LookupAsync(true);
    void ImportClicked(object s,RoutedEventArgs e)=>new ImportWindow(coordinator,null){Owner=this}.ShowDialog();
    void OnDragOver(object s,DragEventArgs e) {e.Effects=e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;}
    void OnDrop(object s,DragEventArgs e) {if(e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length>0)new ImportWindow(coordinator,files[0]){Owner=this}.ShowDialog();}
    void CandidatesClicked(object s,RoutedEventArgs e)=>new CandidateWindow(coordinator){Owner=this}.ShowDialog();
    void SettingsClicked(object s,RoutedEventArgs e)
    {
        if(settingsWindow is not null){settingsWindow.Activate();return;}
        var window=settingsWindow=new SettingsWindow(coordinator,id=>SelectUiTheme(id)){Owner=this};
        window.Closed+=(_,_)=>{settingsWindow=null;if(quitting)return;SelectUiTheme(settings.UiTheme,false);overlay.ApplyStyle();RegisterHotkeys();coordinator.SaveSettings();Tick();};
        window.Show();
    }
    void AppearanceClicked(object s,RoutedEventArgs e)=>ShowAppearance();
    public void ShowAppearance(Window? dialogOwner=null)
    {
        if(appearanceWindow is not null){appearanceWindow.Activate();return;}
        overlay.BeginAppearanceEditing();
        try
        {
            var window=appearanceWindow=new(settings,overlay.PreviewAppearance,coordinator.SaveSettings,()=>overlay.MaterialStatus){Owner=dialogOwner??this};
            window.Closed+=(_,_)=>{appearanceWindow=null;overlay.EndAppearanceEditing();if(!quitting)coordinator.SaveSettings();};window.Show();
        }
        catch{appearanceWindow=null;overlay.EndAppearanceEditing();throw;}
    }
    void DiagnosticsClicked(object s,RoutedEventArgs e)=>new DiagnosticsWindow(coordinator,store,hotkeyError){Owner=this}.ShowDialog();
    void LibraryClicked(object s,RoutedEventArgs e)=>new LibraryWindow(coordinator){Owner=this}.ShowDialog();
    void PowerModeChanged(object s,PowerModeChangedEventArgs e) {if(e.Mode==PowerModes.Resume)Dispatcher.BeginInvoke(()=>coordinator.Resume());}
    void DisplayChanged(object? s,EventArgs e)=>Dispatcher.BeginInvoke(()=>{NativeWindow.ClampVisible(this);NativeWindow.ClampVisible(overlay);});
    void OnClosing(object? s,CancelEventArgs e) {if(quitting)return;e.Cancel=true;if(settings.CloseToTray && CanRecoverOverlay){Hide();coordinator.SaveSettings();}else _=QuitAsync();}
    public async Task QuitAsync()
    {
        if(quitting)return;quitting=true;timer.Stop();hotkeys?.Dispose();tray?.Dispose();SystemEvents.PowerModeChanged-=PowerModeChanged;SystemEvents.DisplaySettingsChanged-=DisplayChanged;
        musicLibrary?.Dispose();appearanceWindow?.Close();settingsWindow?.Close();overlay.Close();await coordinator.DisposeAsync();Close();System.Windows.Application.Current.Shutdown();
    }
    async Task CaptureAsync(string directory)
    {
        // Capture actual WPF clients. The explicit motion demo uses self-authored text, never player metadata.
        Directory.CreateDirectory(directory);
        var motionChecks=await VisualVerification.RunAsync(this,directory);
        var savedAppearance=OverlayAppearance.From(settings);
        (savedAppearance with{TwoLines=true}).Apply(settings);overlay.ApplyStyle();overlay.ResetPosition();double beforeY=overlay.CurrentScreenY;
        (savedAppearance with{TwoLines=false}).Apply(settings);overlay.ApplyStyle();double afterY=overlay.CurrentScreenY;
        motionChecks["singleLineKeepsCurrentLyricScreenY"]=Math.Abs(beforeY-afterY)<=1;
        motionChecks["currentLineYBefore"]=beforeY;motionChecks["currentLineYAfter"]=afterY;
        motionChecks["singleLineWindowCentered"]=Math.Abs(overlay.CurrentCenterOffset)<1;
        overlay.ResetPosition();motionChecks["singleLineDefaultMatchesTwoLineAnchor"]=Math.Abs(beforeY-overlay.CurrentScreenY)<=2;
        VisualVerification.Save(overlay,Path.Combine(directory,"single-line.png"));
        savedAppearance.Apply(settings);overlay.ApplyStyle();
        motionChecks["mainSliderUsesSharedTemplate"]=SeekSlider.Style==FindResource("LumaSliderStyle") || SeekSlider.Style?.BasedOn==FindResource("LumaSliderStyle");
        File.WriteAllText(Path.Combine(directory,"motion-check.json"),System.Text.Json.JsonSerializer.Serialize(motionChecks,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        static void Capture(Window window,string path)
        {
            window.UpdateLayout();var dpi=VisualTreeHelper.GetDpi(window);
            var bmp=new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth*dpi.DpiScaleX),(int)Math.Ceiling(window.ActualHeight*dpi.DpiScaleY),96*dpi.DpiScaleX,96*dpi.DpiScaleY,PixelFormats.Pbgra32);bmp.Render(window);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bmp));using var f=File.Create(path);png.Save(f);
        }
        Capture(this,Path.Combine(directory,"main-window.png"));
        bool visible=overlay.IsVisible;var original=OverlayAppearance.From(settings);
        if(!visible)overlay.Show();overlay.SetLocked(true,false);
        var appearance=new AppearanceWindow(settings,overlay.PreviewAppearance,coordinator.SaveSettings,()=>overlay.MaterialStatus){Owner=this};appearance.Show();
        var appearanceChecks=new Dictionary<string,object?>();
        appearanceChecks["controls"]=appearance.VerifyControls();
        foreach(var theme in OverlayThemes.All)
        {
            appearance.PreviewTheme(theme);await Task.Delay(150);
            string name=theme.Tag.ToLowerInvariant();Capture(overlay,Path.Combine(directory,$"overlay-{name}.png"));
            appearanceChecks[name]=new{blurRequested=overlay.BlurAccepted,overlay.MaterialStatus,width=overlay.ActualWidth,height=overlay.ActualHeight,settings.OverlayFontSize};
            if(theme.Tag=="GLASS")Capture(appearance,Path.Combine(directory,"appearance.png"));
            if(theme.Tag=="LIGHT")Capture(appearance,Path.Combine(directory,"appearance-material.png"));
            if(theme.Tag=="FROST")Capture(appearance,Path.Combine(directory,"appearance-frost.png"));
        }
        appearance.SelectLayoutTab();await Task.Delay(100);Capture(appearance,Path.Combine(directory,"appearance-layout.png"));
        appearanceChecks["noBlurAfterTransparentTheme"]=!overlay.BlurAccepted;
        appearance.Close();appearanceChecks["cancelRestoredAppearance"]=original==OverlayAppearance.From(settings);
        var saveWindow=new AppearanceWindow(settings,overlay.PreviewAppearance,coordinator.SaveSettings,()=>overlay.MaterialStatus){Owner=this};saveWindow.Show();
        var large=OverlayThemes.All[3].Appearance with{FontSize=96,Width=360,TwoLines=false,Animations=false,TransitionMs=340};saveWindow.PreviewOptions(large);await Task.Delay(100);saveWindow.Save();
        appearanceChecks["appearanceSavedAndReloaded"]=OverlayAppearance.From(store.Load())==large.Normalize();
        appearanceChecks["largeFontWindowWithinBounds"]=overlay.ActualWidth==360 && overlay.ActualHeight<=640 && overlay.ActualHeight>=96;
        original.Apply(settings);overlay.ApplyStyle();coordinator.SaveSettings();overlay.SetLocked(false);if(!visible)overlay.Hide();
        File.WriteAllText(Path.Combine(directory,"appearance-check.json"),System.Text.Json.JsonSerializer.Serialize(appearanceChecks,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        var candidates=new CandidateWindow(coordinator){Owner=this};candidates.Show();await Task.Delay(100);Capture(candidates,Path.Combine(directory,"candidates.png"));candidates.Close();
        var library=new LibraryWindow(coordinator){Owner=this};library.Show();await Task.Delay(100);Capture(library,Path.Combine(directory,"library.png"));library.Close();
        var s=coordinator.Snapshot;
        File.WriteAllText(Path.Combine(directory,"runtime-check.json"),System.Text.Json.JsonSerializer.Serialize(new{os=Environment.OSVersion.ToString(),source=s?.SessionId,s?.Generation,s?.Revision,state=s?.Status.ToString(),position=coordinator.Position,quality=coordinator.Quality.ToString(),lyrics=coordinator.Document?.IsSynced,groups=coordinator.Document?.Groups.Count,tray=CanRecoverOverlay,hotkeys=hotkeyError,dpi=VisualTreeHelper.GetDpi(this).PixelsPerInchX,screens=Forms.Screen.AllScreens.Select(x=>new{x.Bounds,x.WorkingArea}),capture="WPF live client-area visual tree"},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
    }
    async Task VerifyImportAsync(string file,string directory)
    {
        var checks=new Dictionary<string,object?>();
        var track=coordinator.ActiveTrack;
        checks["liveSession"]=coordinator.Snapshot is not null;checks["networkCandidates"]=coordinator.Candidates.Count;
        checks["automaticallyImportedBeforeFixture"]=coordinator.Binding?.Manual==false;
        checks["automaticSource"]=coordinator.Binding?.Source;
        checks["libraryTracks"]=coordinator.LibraryStatus.Total;
        if(track is not null)
        {
            // Explicit verification mode uses a separate data root and self-authored fixture.
            coordinator.Import(track,File.ReadAllText(file),"自写测试歌词 · QA",false);
            checks["importedTimedGroups"]=coordinator.Document?.Groups.Count;
            coordinator.SetOffset(100);checks["offsetSaved"]=coordinator.Binding?.OffsetMs==100;
            var id=coordinator.Binding?.Id;await coordinator.LookupAsync(false);
            checks["bindingRetainedAfterLookup"]=coordinator.Binding?.Id==id;
            coordinator.ClearCache();checks["manualRetainedAfterCleanup"]=coordinator.Binding?.Manual==true;
            coordinator.SetOffset(0);
        }
        if(!overlay.IsVisible)overlay.Show();overlay.SetLocked(true,false);checks["nativeTransparentFlagEnabled"]=NativeWindow.IsClickThrough(overlay);
        overlay.SetLocked(false);checks["nativeTransparentFlagRestored"]=!NativeWindow.IsClickThrough(overlay);
        checks["trayAvailable"]=CanRecoverOverlay;checks["hotkeysRegistered"]=string.IsNullOrEmpty(hotkeyError);
        Hide();checks["mainHiddenOverlayVisible"]=!IsVisible&&overlay.IsVisible;Restore();coordinator.SaveSettings();
        Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"verification.json"),System.Text.Json.JsonSerializer.Serialize(checks,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
    }
}
