using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
        var originalTheme=settings.UiTheme;var originalOverlay=OverlayAppearance.From(settings);
        overlay.ApplyStyle();
        var overlayChrome=overlay.FindResource("RaisedBrush").ToString();
        var toolbarChrome=overlay.Toolbar.Background.ToString();
        checks["mainHasNoThemeSelectors"]=!Descendants<RadioButton>(this).Any();
        Width=1220;Height=820;await Task.Delay(120);
        SelectUiTheme("liquid");CaptureMain("empty-liquid.png");
        var fixture="[00:00.00]微风轻轻推开了窗\n[00:12.00]城市在远处慢慢发亮\n[00:24.00]把此刻，轻轻唱成一束光。\n[00:36.00]让旋律停留在你身旁\n[00:48.00]那些未曾说出口的向往\n[01:00.00]都随着音乐，去往远方\n[01:12.00]留一束光，给正在听的你\n[01:24.00]下一句，随微风慢慢抵达";
        coordinator.Import(new Track("微光之间","浮光","自写文字 · 界面预览",96000),fixture,"UI verification fixture",false);
        checks["localImportRendersLyrics"]=displayed?.Groups.Count==8 && lyricBlocks.Count==8;
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
            var appearance=new AppearanceWindow(settings,overlay.ApplyStyle,coordinator.SaveSettings,()=>overlay.MaterialStatus){Owner=this};
            appearance.Show();await Task.Delay(80);
            checks[theme.Id+".editorHasLocalPalette"]=appearance.Resources.Contains("WindowBrush") && appearance.Resources.Contains("TextBrush");
            var editorBackground=appearance.Background;var editorForeground=appearance.Foreground;
            SelectUiTheme(theme.Id=="frost"?"material":"frost",false);
            checks[theme.Id+".editorPaletteIndependent"]=appearance.Background==editorBackground && appearance.Foreground==editorForeground;
            SelectUiTheme(theme.Id,false);
            checks[theme.Id+".appearanceControls"]=appearance.VerifyControls();
            VisualVerification.Save(appearance,Path.Combine(directory,"appearance-"+theme.Id+".png"));
            appearance.PreviewTheme(OverlayThemes.All[0]);appearance.Close();
            checks[theme.Id+".appearanceCancelRestores"]=originalOverlay==OverlayAppearance.From(settings) && settings.UiTheme==theme.Id;
            var dialog=new ImportWindow(coordinator,null){Owner=this};dialog.Show();await Task.Delay(60);
            checks[theme.Id+".dialogInheritsTheme"]=dialog.Foreground==FindResource("TextBrush");
            VisualVerification.Save(dialog,Path.Combine(directory,"import-"+theme.Id+".png"));dialog.Close();
        }
        var saveWindow=new AppearanceWindow(settings,overlay.ApplyStyle,coordinator.SaveSettings,()=>overlay.MaterialStatus){Owner=this};
        saveWindow.Show();saveWindow.PreviewTheme(OverlayThemes.All[1]);saveWindow.Save();
        checks["appearanceSavePersists"]=saveWindow.Saved && OverlayAppearance.From(store.Load()).Preset==OverlayThemes.All[1].Name;
        originalOverlay.Apply(settings);overlay.ApplyStyle();coordinator.SaveSettings();
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
        Width=1220;Height=820;SelectUiTheme(originalTheme);originalOverlay.Apply(settings);coordinator.SaveSettings();
        var overlayChecks=await VisualVerification.RunAsync(this,directory);
        checks["overlayRegression"]=overlayChecks;
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
