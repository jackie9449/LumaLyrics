using System.Windows;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

// Compatibility host used by isolated renderer verification. Application entries use SettingsWindow.
public sealed class AppearanceWindow:Window
{
    readonly AppearanceEditor editor;
    bool closing;
    public AppearanceWindow(AppSettings settings,Action<OverlayAppearance?> previewChanged,Action saveSettings,Func<string> materialStatus)
    {
        Title="浮光歌词 · 悬浮歌词外观";Width=1120;Height=850;MinWidth=640;MinHeight=520;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        ThemeManager.Bind(this,BackgroundProperty,"WindowBrush");ThemeManager.Bind(this,ForegroundProperty,"TextBrush");
        editor=new(settings,previewChanged,saveSettings,materialStatus);
        Content=new System.Windows.Controls.ScrollViewer{Content=editor,Padding=new(24),VerticalScrollBarVisibility=System.Windows.Controls.ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=System.Windows.Controls.ScrollBarVisibility.Disabled};
        editor.Completed+=()=>{if(!closing)Close();};Closing+=(_,_)=>{closing=true;editor.Close();};
        Loaded+=(_,_)=>{var area=SystemParameters.WorkArea;MaxWidth=area.Width;MaxHeight=area.Height;NativeWindow.ClampVisible(this);};
    }
    internal bool Saved=>editor.Saved;
    internal InstalledFontPicker FontPicker=>editor.FontPicker;
    internal OverlaySurface PreviewSurface=>editor.PreviewSurface;
    internal void PreviewTheme(OverlayTheme theme)=>editor.PreviewTheme(theme);
    internal void PreviewOptions(OverlayAppearance appearance)=>editor.PreviewOptions(appearance);
    internal void ResetCurrentTheme()=>editor.ResetCurrentTheme();
    internal void SelectLayoutTab()=>editor.SelectLayoutTab();
    internal Dictionary<string,bool> VerifyControls()=>editor.VerifyControls();
    public void Save()=>editor.Save();
}
