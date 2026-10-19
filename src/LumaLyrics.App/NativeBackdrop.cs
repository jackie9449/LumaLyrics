using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

// Accent policy is a compatibility path for Windows 10, not Windows 11's Mica API.
// It is optional, bounded to this HWND, and must be disabled for a truly clear background.
internal sealed class NativeBackdrop : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] struct Accent {public int State;public int Flags;public uint Color;public int Animation;}
    [StructLayout(LayoutKind.Sequential)] struct AttributeData {public int Attribute;public nint Data;public nuint Size;}
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] static extern bool SetWindowCompositionAttribute(nint hwnd,ref AttributeData data);
    [DllImport("user32.dll")] static extern int SetWindowRgn(nint hwnd,nint region,bool redraw);
    [DllImport("gdi32.dll")] static extern nint CreateRoundRectRgn(int l,int t,int r,int b,int ellipseWidth,int ellipseHeight);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(nint obj);
    readonly Window window;
    bool? enabled;
    public string Status {get;private set;}="背景模糊未启用";
    public bool Accepted {get;private set;}
    public NativeBackdrop(Window window)=>this.window=window;
    public void Apply(OverlayAppearance a,bool force=false)
    {
        bool requested=a.SystemBlur && !a.Transparent && a.Opacity>0 && a.Material is "liquid" or "frost";
        if(NativeWindow.Handle(window)==0)return;
        // Apply the native silhouette before enabling blur, which is composed outside WPF's alpha mask.
        bool rounded=RoundRegion(a.Radius);
        bool allowed=requested && rounded && !SystemParameters.HighContrast && TransparencyAllowed();
        if(force || enabled!=allowed)
        {
            try
            {
                // Blur (rather than acrylic) avoids Windows 10 acrylic's drag-time stalls.
                var accent=new Accent{State=allowed?3:0};var ptr=Marshal.AllocHGlobal(Marshal.SizeOf<Accent>());
                try {Marshal.StructureToPtr(accent,ptr,false);var data=new AttributeData{Attribute=19,Data=ptr,Size=(nuint)Marshal.SizeOf<Accent>()};Accepted=SetWindowCompositionAttribute(NativeWindow.Handle(window),ref data)&&allowed;}
                finally{Marshal.FreeHGlobal(ptr);}
            }
            catch(Exception ex) when(ex is EntryPointNotFoundException or DllNotFoundException or ExternalException){Accepted=false;}
            enabled=allowed;
        }
        Status=!requested?"背景模糊未启用":Accepted?"已请求系统背景模糊 · 效果由系统决定":"使用半透明材质 · 系统模糊不可用";
    }
    bool RoundRegion(double radius)
    {
        var handle=NativeWindow.Handle(window);if(handle==0)return false;
        var (width,height)=NativeWindow.PixelSize(window);if(width<=0 || height<=0)return false;
        var dpi=System.Windows.Media.VisualTreeHelper.GetDpi(window);
        // Never expand this region: transparent WPF pixels still receive native blur inside it.
        // Use the HWND's physical bounds, not rounded DIP dimensions, especially after a DPI change.
        int diameterX=(int)Math.Ceiling(Math.Min(radius*2*dpi.DpiScaleX,width));
        int diameterY=(int)Math.Ceiling(Math.Min(radius*2*dpi.DpiScaleY,height));
        // GDI excludes the far outline row/column of a rounded rectangle.
        var region=CreateRoundRectRgn(0,0,width+1,height+1,diameterX,diameterY);
        if(region==0)return false;
        if(SetWindowRgn(handle,region,true)!=0)return true; // Success transfers ownership to Windows.
        DeleteObject(region);return false;
    }
    static bool TransparencyAllowed()
    {
        try {using var key=Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");return key?.GetValue("EnableTransparency") is not int v || v!=0;}
        catch(System.Security.SecurityException){return false;}
    }
    public void Dispose(){Apply(new(){SystemBlur=false},true);}
}
