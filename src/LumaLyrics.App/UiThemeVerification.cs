using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

public partial class MainWindow
{
    // Explicit --verify-ui mode uses an isolated data directory and never starts media/network services.
    async Task VerifyUiThemesAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        void CaptureMain(string file)=>VisualVerification.Save((FrameworkElement)Content,Path.Combine(directory,file),true);
        var checks=new Dictionary<string,object?>();
        if(Environment.GetCommandLineArgs().Contains("--verify-tray-icon"))
        {
            await VerifyTrayIconAsync(directory,checks);
            File.WriteAllText(Path.Combine(directory,"ui-check.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));return;
        }
        if(Environment.GetCommandLineArgs().Contains("--verify-responsive-layout"))
        {
            await VerifyResponsiveLayoutAsync(directory,checks);await VerifyIntegratedAppearanceAsync(directory,checks);
            File.WriteAllText(Path.Combine(directory,"ui-check.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));return;
        }
        if(Environment.GetCommandLineArgs().Contains("--verify-glass-backdrop"))
        {
            await VerifyOverlayCornersAsync(directory,checks);
            File.WriteAllText(Path.Combine(directory,"ui-check.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));return;
        }
        if(Environment.GetCommandLineArgs().Contains("--verify-application-design"))
        {
            await VerifyApplicationDesignAsync(directory,checks);
            File.WriteAllText(Path.Combine(directory,"ui-check.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));return;
        }
        if(Environment.GetCommandLineArgs().Contains("--verify-queue-reordering"))
        {
            await VerifyQueueReorderingAsync(directory,checks);
            File.WriteAllText(Path.Combine(directory,"ui-check.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));return;
        }
        if(Environment.GetCommandLineArgs().Contains("--verify-playback-sidebar"))
        {
            await VerifyPlaybackSidebarAsync(directory,checks);await VerifyQueueNavigationAsync(checks);await VerifyNativeHandoffReadinessAsync(checks);
            File.WriteAllText(Path.Combine(directory,"ui-check.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));return;
        }
        if(Environment.GetCommandLineArgs().Contains("--verify-playback-queue"))
        {
            await VerifyPlaybackQueueAsync(directory,checks);
            File.WriteAllText(Path.Combine(directory,"ui-check.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));return;
        }
        if(Environment.GetCommandLineArgs().Contains("--verify-overlay-adjustments"))
        {
            await VerifyOverlayAdjustmentsAsync(directory,checks);
            File.WriteAllText(Path.Combine(directory,"ui-check.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));return;
        }
        if(Environment.GetCommandLineArgs().Contains("--verify-translations"))
        {
            await VerifyTranslationsAsync(directory,checks);
            File.WriteAllText(Path.Combine(directory,"ui-check.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));return;
        }
        if(Environment.GetCommandLineArgs().Contains("--verify-library-browser"))
        {
            await VerifyMusicLibraryAsync(directory,checks);
            File.WriteAllText(Path.Combine(directory,"ui-check.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));return;
        }
        if(Environment.GetCommandLineArgs().Contains("--verify-overlay-motion"))
        {
            await VerifyOverlayMotionAsync(checks);
            File.WriteAllText(Path.Combine(directory,"ui-check.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));return;
        }
        if(Environment.GetCommandLineArgs().Contains("--verify-overlay-isolation"))
        {
            await VerifyOverlayIsolationAsync(directory,checks);
            File.WriteAllText(Path.Combine(directory,"ui-check.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));return;
        }
        if(Environment.GetCommandLineArgs().Contains("--verify-overlay-corners"))
        {
            await VerifyOverlayCornersAsync(directory,checks);
            File.WriteAllText(Path.Combine(directory,"ui-check.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));return;
        }
        if(Environment.GetCommandLineArgs().Contains("--verify-appearance-interactions"))
        {
            await VerifyAppearanceInteractionsAsync(directory,checks);
            File.WriteAllText(Path.Combine(directory,"ui-check.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));return;
        }
        if(Environment.GetCommandLineArgs().Contains("--verify-glass-materials"))
        {
            await VerifyGlassVisualsAsync(directory,checks);
            File.WriteAllText(Path.Combine(directory,"ui-check.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));return;
        }
        var originalTheme=settings.UiTheme;var originalOverlay=OverlayAppearance.From(settings);var originalProfiles=settings.OverlayThemeProfiles;
        overlay.ApplyStyle();
        var overlayChrome=overlay.FindResource("RaisedBrush").ToString();
        var toolbarChrome=overlay.Toolbar.Background.ToString();
        checks["mainHasNoThemeSelectors"]=!Descendants<RadioButton>(this).Any();
        Width=1220;Height=820;await Task.Delay(120);
        SelectUiTheme("liquid");CaptureMain("empty-liquid.png");
        var fixture="[00:00.00]微风轻轻推开了窗\n[00:12.00]城市在远处慢慢发亮\n[00:24.00]把此刻，轻轻唱成一束光。\n[00:36.00]让旋律停留在你身旁\n[00:48.00]那些未曾说出口的向往\n[01:00.00]都随着音乐，去往远方\n[01:12.00]留一束光，给正在听的你\n[01:24.00]下一句，随微风慢慢抵达";
        coordinator.Import(new Track("微光之间","浮光","自写文字 · 界面预览",96000),fixture,"UI verification fixture",false);
        checks["localImportRendersLyrics"]=displayed?.Groups.Count==8 && lyricBlocks.Count==8;
        // Original geometric artwork tests the actual PNG cache and WPF decoder;
        // no personal library images or external services are used in verification.
        var artworkDrawing=new DrawingVisual();
        using(var canvas=artworkDrawing.RenderOpen())
        {
            canvas.DrawRectangle(new LinearGradientBrush(Color.FromRgb(16,30,64),Color.FromRgb(52,91,109),35),null,new Rect(0,0,1200,1200));
            canvas.DrawEllipse(new LinearGradientBrush(Color.FromRgb(224,207,170),Color.FromRgb(114,179,181),65),null,new Point(770,470),400,400);
            canvas.DrawEllipse(new SolidColorBrush(Color.FromRgb(18,40,68)),null,new Point(615,340),377,377);
            canvas.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(80,232,233,221)),2),new Point(100,895),new Point(1100,895));
            var text=new FormattedText("L U M A  /  微光之间",System.Globalization.CultureInfo.GetCultureInfo("zh-CN"),FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),44,Brushes.White,1);
            canvas.DrawText(text,new Point(100,960));
        }
        var bitmap=new RenderTargetBitmap(1200,1200,96,96,PixelFormats.Pbgra32);bitmap.Render(artworkDrawing);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var artworkBytes=new MemoryStream();encoder.Save(artworkBytes);
        using(var cache=new ArtworkCache(store.Root))
        {
            var track=coordinator.ActiveTrack!;var sourcePath=Path.Combine(store.Root,"cache","ui-fixture.png");File.WriteAllBytes(sourcePath,artworkBytes.ToArray());
            cache.Sync(new("ui-fixture",1,[new("UI",track)],"original-artwork"),new Dictionary<string,AppleArtworkSource>{{"UI",new("UI","original-artwork",null,sourcePath)}});
            cache.Save(cache.GetQueue().Single(),artworkBytes.ToArray());var cached=cache.Find(track)!;await coordinator.RefreshArtworkAsync();
            checks["artwork.highResolutionCache"]=cached.HighResolution && cache.GetProgress().HighResolution==1;
            checks["artwork.wpfDecodesCachedCover"]=Artwork.Source is BitmapImage image && image.PixelWidth==1200 && image.IsFrozen && CoverPlaceholder.Visibility==Visibility.Collapsed;
        }
        vm.Title="微光之间";vm.Artist="浮光  /  自写文字 · 界面预览";vm.Time="00:24 / 01:36";
        vm.Status="本地歌词 · 静态浏览";vm.Quality="界面预览 · 示例文字";vm.Duration=96000;vm.Position=24000;
        currentIndex=2;LyricLines.Margin=new(0,32,10,60);
        foreach(var theme in AppThemes.All)
        {
            var preferences=new SettingsWindow(coordinator,id=>SelectUiTheme(id)){Owner=this};
            preferences.Show();preferences.SelectApplicationTheme(theme.Id);
            await Task.Delay(100);StyleLyricLines(-1,2);UpdateLayout();
            checks[theme.Id+".settingsThemeApplies"]=ThemeManager.Current.Id==theme.Id;
            VisualVerification.Save(preferences,Path.Combine(directory,"settings-"+theme.Id+".png"));
            preferences.SelectOverlayTab();await Task.Delay(40);
            VisualVerification.Save(preferences,Path.Combine(directory,"settings-overlay-"+theme.Id+".png"));
            preferences.Close();
            checks[theme.Id+".selectionSaved"]=store.Load().UiTheme==theme.Id;
            checks[theme.Id+".activeLyricColor"]=((SolidColorBrush)lyricBlocks[2].Foreground).Color==(Color)ColorConverter.ConvertFromString(theme.Text);
            checks[theme.Id+".headingInheritsTheme"]=TrackTitle.Foreground==FindResource("TextBrush");
            checks[theme.Id+".artworkClipped"]=ArtworkBorder.Clip is RectangleGeometry;
            checks[theme.Id+".overlayUnchanged"]=originalOverlay==OverlayAppearance.From(settings);
            checks[theme.Id+".overlayChromeIndependent"]=overlayChrome==overlay.FindResource("RaisedBrush").ToString();
            checks[theme.Id+".overlayToolbarIndependent"]=toolbarChrome==overlay.Toolbar.Background.ToString();
            CaptureMain("main-"+theme.Id+".png");
            var appearance=new AppearanceWindow(settings,overlay.PreviewAppearance,coordinator.SaveSettings,()=>overlay.MaterialStatus){Owner=this};
            appearance.Show();await Task.Delay(80);
            checks[theme.Id+".editorInheritsAppPalette"]=!appearance.Resources.Contains("WindowBrush") && appearance.Background==FindResource("WindowBrush") && appearance.Foreground==FindResource("TextBrush");
            var previewAppearance=OverlayAppearance.From(settings);var previewFont=appearance.PreviewSurface.RenderedFont;
            SelectUiTheme(theme.Id=="frost"?"material":"frost",false);
            checks[theme.Id+".editorFollowsAppWithoutChangingPreview"]=appearance.Background==FindResource("WindowBrush") && appearance.Foreground==FindResource("TextBrush") && OverlayAppearance.From(settings)==previewAppearance && appearance.PreviewSurface.RenderedFont==previewFont;
            SelectUiTheme(theme.Id,false);
            checks[theme.Id+".appearanceControls"]=appearance.VerifyControls();
            VisualVerification.Save(appearance,Path.Combine(directory,"appearance-"+theme.Id+".png"));
            appearance.PreviewTheme(OverlayThemes.All[0]);appearance.Close();
            checks[theme.Id+".appearanceCancelRestores"]=originalOverlay==OverlayAppearance.From(settings) && settings.UiTheme==theme.Id;
            var dialog=new ImportWindow(coordinator,null){Owner=this};dialog.Show();await Task.Delay(60);
            checks[theme.Id+".dialogInheritsTheme"]=dialog.Foreground==FindResource("TextBrush");
            VisualVerification.Save(dialog,Path.Combine(directory,"import-"+theme.Id+".png"));dialog.Close();
        }
        var saveWindow=new AppearanceWindow(settings,overlay.PreviewAppearance,coordinator.SaveSettings,()=>overlay.MaterialStatus){Owner=this};
        saveWindow.Show();saveWindow.PreviewTheme(OverlayThemes.All[1]);saveWindow.Save();
        checks["appearanceSavePersists"]=saveWindow.Saved && OverlayAppearance.From(store.Load()).Preset==OverlayThemes.All[1].Name;
        settings.OverlayThemeProfiles=originalProfiles;originalOverlay.Apply(settings);overlay.ApplyStyle();coordinator.SaveSettings();
        foreach(var (width,height) in new[]{(1220d,820d),(980d,700d),(820d,640d)})
        {
            Width=width;Height=height;await Task.Delay(250);UpdateLayout();
            var bodyBounds=new Rect(0,0,Body.ActualWidth,Body.ActualHeight);
            Rect Bounds(FrameworkElement element)=>element.TransformToAncestor(Body).TransformBounds(new Rect(0,0,element.ActualWidth,element.ActualHeight));
            var lyrics=Bounds(LyricsPanel);var info=Bounds(InfoPanel);
            bool Inside(Rect rect)=>rect.Left>=-.5 && rect.Top>=-.5 && rect.Right<=bodyBounds.Right+.5 && rect.Bottom<=bodyBounds.Bottom+.5;
            checks[$"layout.{width}.panelsFit"]=Inside(lyrics) && Inside(info) && Rect.Intersect(lyrics,info).IsEmpty;
            checks[$"layout.{width}.playbackVisible"]=PlaybackControls.IsVisible && SeekSlider.IsVisible && Inside(Bounds(PlaybackControls)) && Inside(Bounds(SeekSlider));
            CaptureMain($"layout-{width}.png");
        }
        LaterClicked(this,new());checks["offsetButtonWorks"]=coordinator.Binding?.OffsetMs==100;
        ResetOffsetClicked(this,new());checks["offsetResetWorks"]=coordinator.Binding?.OffsetMs==0;
        var instrumental=new LyricsCandidate(900001,new("静静的海面","浮光","自写测试",180000),null,null,true);
        coordinator.SelectCandidate(instrumental.Track,instrumental);Tick();
        checks["instrumental.noLyricLines"]=lyricBlocks.Count==0 && LyricLines.Children.Count==0;
        checks["instrumental.noPlaceholder"]=EmptyState.Visibility==Visibility.Collapsed && LyricsScroll.Visibility==Visibility.Collapsed && ReturnButton.Visibility==Visibility.Collapsed;
        checks["instrumental.emptyFloatingText"]=vm.Current.Length==0 && vm.Next.Length==0;
        Width=1220;Height=820;await Task.Delay(80);CaptureMain("instrumental-blank.png");
        checks["instrumental.noFloatingSurface"]=overlay.InstrumentalSuppressed;
        checks["instrumental.noOutgoingLyric"]=overlay.LyricSurface.TransitionState.Text.Length==0 && overlay.LyricSurface.TransitionState.OutgoingText.Length==0;
        checks["artwork.manualTrackChangeClearsOldCover"]=Artwork.Source is null && CoverPlaceholder.Visibility==Visibility.Visible;
        coordinator.Import(new("微光之间","浮光","自写文字 · 界面预览",96000),fixture,"UI verification fixture",false);Tick();
        checks["instrumental.vocalSongRestores"]=lyricBlocks.Count==8 && LyricsScroll.Visibility==Visibility.Visible && !overlay.InstrumentalSuppressed;
        var libraryWindow=new LibraryWindow(coordinator){Owner=this};libraryWindow.Library.SourcePath.Text="Apple Music / Library.musicdb（原创界面示例）";libraryWindow.Show();await Task.Delay(60);
        checks["library.separateArtworkProgress"]=Descendants<TextBlock>(libraryWindow).Any(t=>t.Text.Contains("高清")) && Descendants<TextBlock>(libraryWindow).Any(t=>t.Text.Contains("动态歌词"));
        VisualVerification.Save(libraryWindow,Path.Combine(directory,"library-assets.png"));libraryWindow.Close();
        await VerifyArtworkThemesAsync(directory,checks);
        await VerifyPersonalLibraryAsync(directory,checks);
        Width=1220;Height=820;SelectUiTheme(originalTheme);originalOverlay.Apply(settings);coordinator.SaveSettings();
        var overlayChecks=await VisualVerification.RunAsync(this,directory);
        checks["overlayRegression"]=overlayChecks;
        await VerifyOverlayCornersAsync(directory,checks);
        await VerifyThemeProfilesAsync(directory,checks);
        await VerifyAppearanceInteractionsAsync(directory,checks);
        await VerifyGlassVisualsAsync(directory,checks);
        await VerifyOverlayMotionAsync(checks);
        await VerifyOverlayIsolationAsync(directory,checks);
        await VerifyMusicLibraryAsync(directory,checks);
        await VerifyPlaybackQueueAsync(directory,checks);
        await VerifyPlaybackSidebarAsync(directory,checks);await VerifyQueueNavigationAsync(checks);await VerifyNativeHandoffReadinessAsync(checks);
        await VerifyTranslationsAsync(directory,checks);
        await VerifyOverlayAdjustmentsAsync(directory,checks);
        await VerifyQueueReorderingAsync(directory,checks);
        await VerifyApplicationDesignAsync(directory,checks);
        await VerifyResponsiveLayoutAsync(directory,checks);await VerifyIntegratedAppearanceAsync(directory,checks);
        await VerifyTrayIconAsync(directory,checks);
        await VerifyOriginalLyricsAsync(directory,checks);await VerifyNativeSurfaceAsync(checks);
        File.WriteAllText(Path.Combine(directory,"ui-check.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));
    }
    static IEnumerable<T> Descendants<T>(DependencyObject parent) where T:DependencyObject
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {
            var child=VisualTreeHelper.GetChild(parent,i);if(child is T typed)yield return typed;
            foreach(var descendant in Descendants<T>(child))yield return descendant;
        }
    }
}
