using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;
using LumaLyrics.Infrastructure;
using System.ComponentModel;
namespace LumaLyrics.App;

// The OS backdrop is clipped in its own compositor visual, behind the layered lyric text.
internal sealed class NativeBackdrop : IDisposable
{
    [DllImport("user32.dll")] static extern int SetWindowRgn(nint hwnd,nint region,bool redraw);
    [DllImport("gdi32.dll")] static extern nint CreateRoundRectRgn(int l,int t,int r,int b,int ellipseWidth,int ellipseHeight);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(nint obj);
    readonly Window window;
    DesktopGlassHost? host;
    OverlayAppearance appearance=new();
    bool allowed,disposed,unavailable;
    readonly UserPreferenceChangedEventHandler preferencesChanged;
    readonly DependencyPropertyDescriptor opacityProperty=DependencyPropertyDescriptor.FromProperty(UIElement.OpacityProperty,typeof(Window));
    public string Status {get;private set;}="背景模糊未启用";
    public bool Accepted {get;private set;}
    public NativeBackdrop(Window window)
    {
        this.window=window;opacityProperty.AddValueChanged(window,OpacityChanged);
        preferencesChanged=(_,_)=>{if(!disposed && !window.Dispatcher.HasShutdownStarted)window.Dispatcher.BeginInvoke(new Action(()=>{if(!disposed)Apply(appearance,true);}));};
        SystemEvents.UserPreferenceChanged+=preferencesChanged;
    }
    void OpacityChanged(object? sender,EventArgs e)=>Refresh();
    internal bool HostVisible=>host?.IsVisible==true;
    internal bool HostInputTransparent=>host?.IsInputTransparent==true;
    internal nint HostHandle=>host?.Handle??0;
    public void Apply(OverlayAppearance a,bool force=false)
    {
        appearance=a;
        bool requested=a.SystemBlur && !a.Transparent && a.Opacity>0 && a.Material is "liquid" or "frost";
        bool nextAllowed=requested && !SystemParameters.HighContrast && TransparencyAllowed();
        if(force || nextAllowed!=allowed)unavailable=false;allowed=nextAllowed;
        Refresh();
        Status=!requested?"背景模糊未启用":!allowed?"使用半透明材质 · 系统透明效果不可用":Accepted?"实时圆角背景柔化 · 文字保持清晰":window.IsVisible?"使用半透明材质 · 系统玻璃不可用":"显示悬浮歌词后预览实时玻璃";
    }
    public void Refresh()
    {
        if(disposed)return;
        if(unavailable || !allowed || !window.IsVisible || NativeWindow.Handle(window)==0){host?.Hide();Accepted=false;return;}
        try
        {
            host??=new(window);host.Update(appearance);Accepted=host.IsVisible;
        }
        catch(Exception ex) when(ex is EntryPointNotFoundException or DllNotFoundException or ExternalException or TypeLoadException or NotSupportedException or InvalidCastException)
        {host?.Dispose();host=null;Accepted=false;unavailable=true;}
    }
    public void RoundRegion(double radius)
    {
        var handle=NativeWindow.Handle(window);if(handle==0)return;
        var dpi=System.Windows.Media.VisualTreeHelper.GetDpi(window);
        // HRGN has integer-only coverage. Keep its cutoff outside the antialiased WPF silhouette.
        const int guard=2;
        var region=CreateRoundRectRgn(-guard,-guard,(int)Math.Ceiling(window.ActualWidth*dpi.DpiScaleX)+guard+1,(int)Math.Ceiling(window.ActualHeight*dpi.DpiScaleY)+guard+1,(int)Math.Ceiling(radius*2*dpi.DpiScaleX)+guard*2,(int)Math.Ceiling(radius*2*dpi.DpiScaleY)+guard*2);
        if(region!=0 && SetWindowRgn(handle,region,true)==0)DeleteObject(region); // Successful SetWindowRgn transfers ownership to Windows.
    }
    static bool TransparencyAllowed()
    {
        try {using var key=Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");return key?.GetValue("EnableTransparency") is not int v || v!=0;}
        catch(System.Security.SecurityException){return false;}
    }
    public void Dispose(){if(disposed)return;disposed=true;SystemEvents.UserPreferenceChanged-=preferencesChanged;opacityProperty.RemoveValueChanged(window,OpacityChanged);host?.Dispose();host=null;Accepted=false;}
}
