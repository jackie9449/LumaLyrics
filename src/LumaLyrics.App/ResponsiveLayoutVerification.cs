using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using LumaLyrics.Infrastructure;

namespace LumaLyrics.App;

public partial class MainWindow
{
    async Task VerifyResponsiveLayoutAsync(string directory,Dictionary<string,object?> checks)
    {
        double width=Width,height=Height;var state=WindowState;bool queue=settings.ShowPlaybackQueue;
        string theme=settings.UiTheme;var browsing=manualUntil;
        async Task Settle(){await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(45);UpdateLayout();}
        Rect Bounds(FrameworkElement element)=>element.TransformToAncestor(Body).TransformBounds(new Rect(element.RenderSize));
        bool Inside(Rect rect)=>rect.Left>=-.5 && rect.Top>=-.5 && rect.Right<=Body.ActualWidth+.5 && rect.Bottom<=Body.ActualHeight+.5;
        try
        {
            WindowState=WindowState.Normal;ShowLyricsPage();SetPlaybackQueueVisible(true);
            coordinator.Import(new("微光之间","浮光工作室","原创布局样例",96000),"[00:00]微风轻轻推开了窗\n[00:12]城市在远处慢慢发亮\n[00:24]把此刻，轻轻唱成一束光。\n[00:36]让旋律停留在你身旁\n[00:48]每一句，都有回响。", "原创布局验证",false);
            currentIndex=2;
            foreach(var app in AppThemes.All)
            {
                SelectUiTheme(app.Id,false);
                double baseline=0;
                foreach(var size in new[]{(820d,640d),(980d,700d),(1220d,820d),(1440d,900d),(1920d,1080d),(2560d,1440d),(1920d,640d)})
                {
                    Width=size.Item1;Height=size.Item2;await Settle();
                    string key=$"responsive.{app.Id}.{size.Item1:0}x{size.Item2:0}";
                    checks[key+".panelsAndTransportFit"]=Inside(Bounds(InfoPanel)) && Inside(Bounds(LyricsPanel)) && Inside(Bounds(PlaybackControls)) && Inside(Bounds(SeekSlider)) && Rect.Intersect(Bounds(InfoPanel),Bounds(LyricsPanel)).IsEmpty;
                    checks[key+".geometry"]=new{Body=Body.RenderSize.ToString(),Info=Bounds(InfoPanel).ToString(),Lyrics=Bounds(LyricsPanel).ToString(),Transport=Bounds(PlaybackControls).ToString(),Seek=Bounds(SeekSlider).ToString(),Cover=Bounds(ArtworkBorder).ToString()};
                    checks[key+".squareCoverRemainsVisible"]=ArtworkBorder.IsVisible && Math.Abs(ArtworkBorder.ActualWidth-ArtworkBorder.ActualHeight)<1 && Inside(Bounds(ArtworkBorder));
                    checks[key+".boundedReadableLyrics"]=LyricContent.ActualWidth<=820.5 && lyricBlocks[2].FontSize>=27 && lyricBlocks[2].FontSize<=38.1;
                    if(size==(1220d,820d))baseline=ArtworkBorder.ActualWidth;
                    if(size==(1920d,1080d))
                    {
                        double share=ArtworkBorder.ActualWidth/(ArtworkBorder.ActualWidth+LyricsPanel.ActualWidth);
                        checks[$"responsive.{app.Id}.largeCoverGrowsAndBalances"]=ArtworkBorder.ActualWidth>baseline*1.45 && share is >.25 and <.48;
                        VisualVerification.Save((FrameworkElement)Content,Path.Combine(directory,$"responsive-{app.Id}-large.png"),true);
                    }
                    if(size==(820d,640d))VisualVerification.Save((FrameworkElement)Content,Path.Combine(directory,$"responsive-{app.Id}-compact.png"),true);
                }
                Width=1220;Height=820;await Settle();double before=ArtworkBorder.ActualWidth;
                Width=1230;await Settle();double after=ArtworkBorder.ActualWidth;
                checks[$"responsive.{app.Id}.smallResizeIsContinuous"]=after>before && after-before<10;
                manualUntil=DateTime.UtcNow.AddMinutes(1);var retained=manualUntil;Width=1260;await Settle();
                checks[$"responsive.{app.Id}.resizeRetainsManualBrowsing"]=manualUntil==retained;
            }
            WindowState=WindowState.Maximized;await Settle();
            checks["responsive.maximizedKeepsBalancedBounds"]=Inside(Bounds(ArtworkBorder)) && Inside(Bounds(PlaybackControls)) && ArtworkBorder.ActualWidth>=160 && LyricContent.ActualWidth<=820.5;
            VisualVerification.Save((FrameworkElement)Content,Path.Combine(directory,"responsive-maximized.png"),true);
        }
        finally{WindowState=WindowState.Normal;Width=width;Height=height;manualUntil=browsing;SetPlaybackQueueVisible(queue);SelectUiTheme(theme,false);WindowState=state;await Settle();}
    }

    async Task VerifyIntegratedAppearanceAsync(string directory,Dictionary<string,object?> checks)
    {
        var original=OverlayAppearance.From(settings);var profiles=settings.OverlayThemeProfiles;string app=settings.UiTheme;
        bool visible=overlay.IsVisible,locked=overlay.IsLocked,pref=settings.OverlayVisible;
        async Task Settle(){await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(70);}
        static Slider Size(AppearanceEditor editor)=>Descendants<Slider>(editor).Single(s=>AutomationProperties.GetName(s)=="主歌词字号");
        static string PreviewPixels(AppearanceEditor editor)
        {
            var bitmap=VisualVerification.Render(editor.PreviewSurface,true);int stride=bitmap.PixelWidth*4;byte[] pixels=new byte[stride*bitmap.PixelHeight];bitmap.CopyPixels(pixels,stride,0);
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pixels));
        }
        try
        {
            overlay.Hide();overlay.SetLocked(true,false);settings.OverlayVisible=false;
            foreach(var theme in AppThemes.All)
            {
                SelectUiTheme(theme.Id,false);ShowAppearance();await Settle();
                var parent=settingsWindow!;var editor=appearanceWindow!;
                bool typography=Descendants<Slider>(editor).Any(s=>AutomationProperties.GetName(s)=="主歌词字号") && editor.FontPicker.IsVisible;
                editor.SelectLayoutTab();await Settle();
                bool layout=new[]{"圆角比例","背景不透明度","桌面歌词宽度","卡片留白"}.All(name=>Descendants<Slider>(editor).Any(s=>AutomationProperties.GetName(s)==name));
                Descendants<TabControl>(editor).Single().SelectedIndex=0;await Settle();
                checks[$"integrated.{theme.Id}.allControlsLiveInsideSettings"]=ReferenceEquals(Window.GetWindow(editor),parent) && typography && layout && !Application.Current.Windows.OfType<AppearanceWindow>().Any();
                checks[$"integrated.{theme.Id}.modelessPreviewRetainsPreferences"]=overlay.AppearanceEditing && overlay.CanDrag && overlay.IsVisible && !settings.OverlayVisible && overlay.IsLocked;
                var parentPaint=parent.Background;var mainPaint=LyricsPanel.Background;
                foreach(var floating in OverlayThemes.All)
                {
                    editor.PreviewTheme(floating);await Settle();
                    checks[$"integrated.{theme.Id}.{floating.Tag}.editorControlsKeepAppChrome"]=editor.Foreground==parent.Foreground && editor.FindResource("ControlBrush")==FindResource("ControlBrush") && editor.FindResource("AccentBrush")==FindResource("AccentBrush") && Descendants<Button>(editor).All(button=>button.Template==FindResource("AppActionTemplate"));
                    if(floating.Tag=="FROST")VisualVerification.Save((FrameworkElement)parent.Content,Path.Combine(directory,$"settings-frost-preview-{theme.Id}.png"),true);
                }
                editor.PreviewTheme(OverlayThemes.All[1]);Size(editor).Value=42;
                checks[$"integrated.{theme.Id}.previewThemeKeepsSettingsPalette"]=parent.Background==parentPaint && LyricsPanel.Background==mainPaint && settings.UiTheme==theme.Id && editor.Foreground==parent.Foreground && editor.FindResource("WindowBrush")==parent.Background;
                await Settle();var appearance=OverlayAppearance.From(settings);string pixels=PreviewPixels(editor);
                SelectUiTheme(theme.Id=="frost"?"material":"frost",false);await Settle();
                checks[$"integrated.{theme.Id}.appChangeUpdatesEditorAndRetainsPreviewPixels"]=editor.Foreground==parent.Foreground && parent.Background==FindResource("WindowBrush") && OverlayAppearance.From(settings)==appearance && PreviewPixels(editor)==pixels;
                SelectUiTheme(theme.Id,false);await Settle();
                editor.Save();await Settle();var saved=OverlayAppearance.From(settings);
                checks[$"integrated.{theme.Id}.saveStaysInSettingsAndEndsPreview"]=parent.IsVisible && appearanceWindow is null && !overlay.AppearanceEditing && !overlay.IsVisible && OverlayAppearance.From(store.Load())==saved;
                ShowAppearance();await Settle();editor=appearanceWindow!;Size(editor).Value=55;editor.Close();await Settle();
                checks[$"integrated.{theme.Id}.cancelRestoresSavedSession"]=OverlayAppearance.From(settings)==saved && parent.IsVisible && !overlay.AppearanceEditing;
                ShowAppearance();await Settle();editor=appearanceWindow!;Size(editor).Value=56;parent.SelectApplicationTab();await Settle();
                checks[$"integrated.{theme.Id}.leavingPageCancelsDraft"]=OverlayAppearance.From(settings)==saved && appearanceWindow is null && !overlay.AppearanceEditing;
                parent.SelectOverlayTab();await Settle();editor=appearanceWindow!;
                foreach(double size in new[]{1120d,840d,640d})
                {
                    parent.Width=size;await Settle();
                    checks[$"integrated.{theme.Id}.{size:0}.fitsSettingsViewport"]=editor.ActualWidth<=parent.ActualWidth-146 && editor.ActualWidth>=350;
                    var sectionTabs=Descendants<TabControl>(editor).Single();
                    checks[$"integrated.{theme.Id}.{size:0}.horizontalSectionsFit"]=sectionTabs.TabStripPlacement==Dock.Top && Descendants<TabItem>(sectionTabs).All(item=>item.ActualWidth>70 && item.ActualWidth<=sectionTabs.ActualWidth);
                    if(size==640)VisualVerification.Save((FrameworkElement)parent.Content,Path.Combine(directory,$"settings-compact-{theme.Id}.png"),true);
                }
                parent.Width=1120;await Settle();
                VisualVerification.Save((FrameworkElement)parent.Content,Path.Combine(directory,$"settings-integrated-{theme.Id}.png"),true);
                parent.SelectLibraryTab();await Settle();
                var library=parent.Library;library.SourcePath.Text="Apple Music / Library.musicdb（原创界面示例）";
                checks[$"integrated.{theme.Id}.libraryManagementLivesInsideSettings"]=library.IsVisible && library.IsSubscribed && ReferenceEquals(Window.GetWindow(library),parent) && !Application.Current.Windows.OfType<LibraryWindow>().Any() && Descendants<Button>(library).Count()==4;
                VisualVerification.Save((FrameworkElement)parent.Content,Path.Combine(directory,$"settings-library-{theme.Id}.png"),true);
                parent.Width=640;await Settle();
                checks[$"integrated.{theme.Id}.compactLibraryControlsStayWithinViewport"]=Descendants<Button>(library).All(button=>{var rect=button.TransformToAncestor(library).TransformBounds(new Rect(button.RenderSize));return rect.Left>=0 && rect.Right<=library.ActualWidth+.5;});
                VisualVerification.Save((FrameworkElement)parent.Content,Path.Combine(directory,$"settings-library-compact-{theme.Id}.png"),true);
                parent.Width=1120;await Settle();
                parent.SelectApplicationTab();await Settle();
                checks[$"integrated.{theme.Id}.hiddenLibraryDetachesProgress"]=!library.IsSubscribed;
                LibraryClicked(this,new());await Settle();
                checks[$"integrated.{theme.Id}.cacheShortcutReusesSettings"]=ReferenceEquals(settingsWindow,parent) && parent.Library.IsVisible && parent.Library.IsSubscribed && Application.Current.Windows.OfType<SettingsWindow>().Count()==1;
                parent.Close();await Settle();
                checks[$"integrated.{theme.Id}.closedLibraryDetachesProgress"]=!library.IsSubscribed;
                checks[$"integrated.{theme.Id}.closingSettingsCleansPreview"]=appearanceWindow is null && !overlay.AppearanceEditing && !overlay.IsVisible && overlay.IsLocked;
            }
        }
        finally
        {
            settingsWindow?.Close();settings.OverlayThemeProfiles=profiles;original.Apply(settings);settings.OverlayVisible=pref;
            overlay.SetLocked(locked,false);overlay.ApplyStyle();if(visible)overlay.Show();else overlay.Hide();SelectUiTheme(app,false);coordinator.SaveSettings();
        }
    }
}
