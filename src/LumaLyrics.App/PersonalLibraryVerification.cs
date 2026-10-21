using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

public partial class MainWindow
{
    async Task VerifyPersonalLibraryAsync(string directory,Dictionary<string,object?> checks)
    {
        coordinator.SaveSettings();
        var preferences=new SettingsWindow(coordinator,id=>SelectUiTheme(id)){Owner=this};preferences.Show();preferences.SelectPersonalTab();await Task.Delay(80);
        var personal=preferences.PersonalLibrary;
        checks["personal.settingsTabAvailable"]=Descendants<TabItem>(preferences).Any(t=>t.Header?.ToString()=="个人资料");
        checks["personal.currentRootShown"]=Descendants<TextBox>(personal).Any(t=>t.IsReadOnly && t.Text==store.Root);
        checks["personal.exportAndRestoreControls"]=Descendants<Button>(personal).Count(b=>b.Content?.ToString()?.Contains("导出")==true||b.Content?.ToString()?.Contains("恢复")==true)==4;
        checks["personal.inheritsTheme"]=preferences.Background==FindResource("WindowBrush") && preferences.Foreground==FindResource("TextBrush");
        VisualVerification.Save(preferences,Path.Combine(directory,"settings-personal.png"));
        var originalTheme=settings.UiTheme;var originalAppearance=OverlayAppearance.From(settings);
        string prefix=store.Root+"-personal-"+Guid.NewGuid().ToString("N");
        var manifest=await personal.ExportToAsync(prefix+"-files");
        checks["personal.exportButtonUsesIsolatedRoot"]=manifest is not null && manifest.Summary.LyricFiles>0 && Directory.Exists(prefix+"-files/lyrics") && personal.DataRoot==store.Root;
        checks["personal.exportRestoresButtons"]=!personal.IsBusy && Descendants<Button>(personal).Where(b=>b.Visibility==Visibility.Visible).All(b=>b.IsEnabled);
        var zip=await personal.ExportToAsync(prefix+".lumalibrary.zip",true);
        checks["personal.zipExportCompletes"]=zip is not null && File.Exists(prefix+".lumalibrary.zip");
        var restored=await personal.RestoreFromAsync(prefix+".lumalibrary.zip",prefix+"-restored");
        checks["personal.restoreButtonCreatesNewRoot"]=restored is not null && File.Exists(prefix+"-restored/lyrics.db") && personal.DataRoot==store.Root;
        var restoredSettings=new SettingsStore(prefix+"-restored").Load();
        checks["personal.restorePreservesAppearance"]=OverlayAppearance.From(restoredSettings)==originalAppearance && restoredSettings.UiTheme==originalTheme;
        checks["personal.exportedFolderProtectedFromGit"]=File.ReadAllText(prefix+"-files/.gitignore").Trim()=="*";
        checks["personal.restoredFolderProtectedFromGit"]=File.ReadAllText(prefix+"-restored/.gitignore").Trim()=="*";
        checks["personal.activeSettingsUnchanged"]=settings.UiTheme==originalTheme && OverlayAppearance.From(settings)==originalAppearance;
        VisualVerification.Save(preferences,Path.Combine(directory,"settings-personal-export.png"));preferences.Close();
    }
}
