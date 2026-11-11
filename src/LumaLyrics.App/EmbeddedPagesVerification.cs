using System.IO;
using System.Windows;
using LumaLyrics.Infrastructure;

namespace LumaLyrics.App;

public partial class MainWindow
{
    async Task VerifyEmbeddedPagesAsync(string directory,Dictionary<string,object?> checks)
    {
        foreach(var theme in AppThemes.All)
        {
            SelectUiTheme(theme.Id,false);
            SettingsClicked(this,new());
            var preferences=settingsWindow!;
            await Task.Delay(100);UpdateLayout();
            checks[$"embedded.{theme.Id}.settings"]=embeddedPageHost.IsVisible && !preferences.IsVisible && !LyricsPage.IsVisible;
            preferences.SelectOverlayTab();await Task.Delay(100);UpdateLayout();
            checks[$"embedded.{theme.Id}.appearance"]=preferences.OverlayEditor?.IsVisible==true;
            preferences.SelectLibraryTab();await Task.Delay(100);UpdateLayout();
            checks[$"embedded.{theme.Id}.library"]=preferences.Library.IsVisible && preferences.Library.IsSubscribed;
            VisualVerification.Save((FrameworkElement)Content,Path.Combine(directory,$"embedded-settings-{theme.Id}.png"),true);
            DiagnosticsClicked(this,new());await Task.Delay(100);UpdateLayout();
            checks[$"embedded.{theme.Id}.diagnostics"]=embeddedPage is DiagnosticsWindow && !embeddedPage.IsVisible && settingsWindow is null && !preferences.Library.IsSubscribed;
            VisualVerification.Save((FrameworkElement)Content,Path.Combine(directory,$"embedded-diagnostics-{theme.Id}.png"),true);
            ShowLyricsPage();
            checks[$"embedded.{theme.Id}.return"]=LyricsPage.IsVisible && embeddedPageHost.Content is null;
        }
    }
}
