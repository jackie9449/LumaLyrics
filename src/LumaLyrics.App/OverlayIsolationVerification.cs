using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

public partial class MainWindow
{
    async Task VerifyOverlayIsolationAsync(string directory,Dictionary<string,object?> checks)
    {
        string originalTheme=settings.UiTheme;var original=OverlayAppearance.From(settings);var profiles=settings.OverlayThemeProfiles;
        string[] keys=["WindowBrush","PanelBrush","TextBrush","MutedBrush","AccentBrush","ControlBrush","BorderBrush","SheenBrush"];
        static string Paint(object? brush)=>brush is LinearGradientBrush gradient?string.Join(";",gradient.GradientStops.Select(s=>$"{s.Color}:{s.Offset}")):brush?.ToString()??"";
        string MainPaint()=>string.Join("|",keys.Select(k=>Paint(FindResource(k))))+"|"+Paint(((Border)Content).Background)+"|"+Paint(PlayButton.Background)+"|"+Paint(LyricsPanel.Background)+"|"+Paint(TrackTitle.Foreground);
        try
        {
            foreach(var app in AppThemes.All)
            {
                SelectUiTheme(app.Id);var parent=new SettingsWindow(coordinator,id=>SelectUiTheme(id)){Owner=this};parent.Show();parent.SelectOverlayTab();
                ShowAppearance(parent);await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                string baseline=MainPaint(),preferences=Paint(parent.Background);var global=keys.Select(k=>Application.Current.Resources[k]).ToArray();
                var editor=appearanceWindow!;
                foreach(var theme in OverlayThemes.All)
                {
                    editor.PreviewTheme(theme);editor.UpdateLayout();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.Render);
                    checks[$"overlayIsolation.{app.Id}.{theme.Tag}.mainPaintStable"]=MainPaint()==baseline && Paint(parent.Background)==preferences;
                    checks[$"overlayIsolation.{app.Id}.{theme.Tag}.applicationChoiceStable"]=settings.UiTheme==app.Id && ThemeManager.Current.Id==app.Id && keys.Select((k,i)=>ReferenceEquals(Application.Current.Resources[k],global[i])).All(v=>v);
                }
                editor.ResetCurrentTheme();editor.Save();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                checks[$"overlayIsolation.{app.Id}.saveDoesNotChangeMain"]=MainPaint()==baseline && store.Load().UiTheme==app.Id;
                ShowAppearance(parent);appearanceWindow!.PreviewTheme(OverlayThemes.All[0]);appearanceWindow.Close();parent.Close();
                checks[$"overlayIsolation.{app.Id}.cancelDoesNotChangeMain"]=MainPaint()==baseline && settings.UiTheme==app.Id;
                if(app.Id=="material")VisualVerification.Save((FrameworkElement)Content,Path.Combine(directory,"main-independent-of-overlay.png"),true);
                settings.OverlayThemeProfiles=profiles;original.Apply(settings);overlay.ApplyStyle();
            }
        }
        finally
        {
            appearanceWindow?.Close();settings.OverlayThemeProfiles=profiles;original.Apply(settings);SelectUiTheme(originalTheme);overlay.ApplyStyle();coordinator.SaveSettings();
        }
    }
}
