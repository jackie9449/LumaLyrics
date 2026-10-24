using System.ComponentModel;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using LumaLyrics.Infrastructure;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using WinRT;
namespace LumaLyrics.App;

// A separate compositor substrate lets WPF keep sharp, layered text without the square
// Accent-blur rectangle. HostBackdropBrush samples behind the window inside the OS;
// this class never captures, reads, stores or transmits desktop pixels.
internal sealed class DesktopGlassHost : IDisposable
{
    const string ClassName="LumaLyrics.CompositedGlass.v1";
    static readonly WindowProcedure procedure=WindowProc;
    static nint queueController;
    static bool registered;
    readonly Window foreground;
    nint handle;
    Compositor? compositor;
    DesktopWindowTarget? target;
    SpriteVisual? surface;
    CompositionBackdropBrush? brush;
    CompositionRoundedRectangleGeometry? geometry;
    CompositionGeometricClip? clip;
    (int Width,int Height,int RadiusX,int RadiusY) regionSize;
    (int Width,int Height,float RadiusX,float RadiusY) visualSize;
    (int Left,int Top,int Width,int Height)? placement;
    float opacity=1;
    internal long GeometryUpdates {get;private set;}
    internal long PositionUpdates {get;private set;}
    public bool IsVisible=>handle!=0 && IsWindowVisible(handle);
    internal bool IsInputTransparent=>handle!=0 && (GetWindowLongPtr(handle,-20)&0x80020)==0x80020;
    internal nint Handle=>handle;
    internal float Opacity=>surface?.Opacity??0;
    public DesktopGlassHost(Window foreground)=>this.foreground=foreground;

    void Create()
    {
        if(handle!=0)return;
        if(global::Windows.System.DispatcherQueue.GetForCurrentThread() is null && queueController==0)
        {
            var options=new QueueOptions{Size=Marshal.SizeOf<QueueOptions>(),ThreadType=2,ApartmentType=2};
            Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(options,out queueController));
            // One queue controller is retained for the WPF UI thread for the process lifetime.
        }
        if(!registered)
        {
            var cls=new WindowClass{Procedure=Marshal.GetFunctionPointerForDelegate(procedure),Instance=GetModuleHandle(null),Name=ClassName};
            if(RegisterClass(ref cls)==0 && Marshal.GetLastWin32Error()!=1410)throw new Win32Exception();
            registered=true;
        }
        // Render through the compositor, without a legacy GDI redirection bitmap. Keep
        // WS_EX_LAYERED solely for its documented WS_EX_TRANSPARENT input behavior.
        // Sharing the foreground's owner keeps both siblings above it after hide/show,
        // without making the substrate an owned window above the sharp lyric text.
        var owner=foreground.Owner is { } ownedBy?NativeWindow.Handle(ownedBy):0;
        handle=CreateWindowEx(0x082800A0,ClassName,"LumaLyrics glass substrate",0x80000000,0,0,1,1,owner,0,GetModuleHandle(null),0);
        if(handle==0)throw new Win32Exception();
        if(!SetLayeredWindowAttributes(handle,0,255,2))throw new Win32Exception();
        int enabled=1;
        const int useHostBackdropBrush=17;
        bool supported=OperatingSystem.IsWindowsVersionAtLeast(10,0,22000) && DwmSetWindowAttribute(handle,useHostBackdropBrush,ref enabled,4)==0;
        if(!supported)
        {
            var accent=new Accent{State=5};var pointer=Marshal.AllocHGlobal(Marshal.SizeOf<Accent>());
            try
            {
                Marshal.StructureToPtr(accent,pointer,false);
                var data=new AttributeData{Attribute=19,Data=pointer,Size=(nuint)Marshal.SizeOf<Accent>()};
                if(!SetWindowCompositionAttribute(handle,ref data))throw new ExternalException("Host backdrop is unavailable.");
            }
            finally{Marshal.FreeHGlobal(pointer);}
        }
        compositor=new();compositor.As<ICompositorDesktopInterop>().CreateDesktopWindowTarget(handle,true,out var raw);
        try{target=MarshalInterface<DesktopWindowTarget>.FromAbi(raw);}finally{Marshal.Release(raw);}
        brush=compositor.CreateHostBackdropBrush();surface=compositor.CreateSpriteVisual();surface.Brush=brush;
        geometry=compositor.CreateRoundedRectangleGeometry();clip=compositor.CreateGeometricClip(geometry);surface.Clip=clip;target.Root=surface;
    }
    public void Update(OverlayAppearance appearance)
    {
        if(!foreground.IsVisible){Hide();return;}
        Create();
        if(!GetWindowRect(NativeWindow.Handle(foreground),out var bounds))throw new Win32Exception();
        int width=bounds.Right-bounds.Left,height=bounds.Bottom-bounds.Top;if(width<=0 || height<=0){Hide();return;}
        var dpi=VisualTreeHelper.GetDpi(foreground);
        double radius=appearance.ResolveCornerRadius(foreground.ActualWidth,foreground.ActualHeight);
        float rx=(float)Math.Min(radius*dpi.DpiScaleX,width/2d),ry=(float)Math.Min(radius*dpi.DpiScaleY,height/2d);
        var visual=(width,height,rx,ry);
        if(visualSize!=visual)
        {
            GeometryUpdates++;surface!.Size=geometry!.Size=new Vector2(width,height);geometry.CornerRadius=new(rx,ry);visualSize=visual;
        }
        // The native layered alpha stays 255. Combining its fade with a host backdrop
        // can expose the legacy black surface; fade the compositor visual instead.
        float nextOpacity=(float)Math.Clamp((appearance.Material=="frost"?.98:.78)*appearance.BackgroundEffectOpacity()*foreground.Opacity,0,1);
        if(nextOpacity!=opacity){surface!.Opacity=nextOpacity;opacity=nextOpacity;}
        var size=(width,height,(int)Math.Ceiling(rx),(int)Math.Ceiling(ry));
        if(regionSize!=size)
        {
            // The sharp WPF rim covers this one-pixel inset. Keeping the substrate inside
            // the optical silhouette prevents black pixels around a partially covered native edge.
            int inset=rx>0 || ry>0?1:0;
            var region=CreateRoundRectRgn(inset,inset,width+1-inset,height+1-inset,Math.Max(0,size.Item3*2-inset*2),Math.Max(0,size.Item4*2-inset*2));
            if(region!=0 && SetWindowRgn(handle,region,false)==0)DeleteObject(region);
            regionSize=size;
        }
        // Never activate or put the substrate above the sharp WPF lyric window.
        var next=(bounds.Left,bounds.Top,width,height);bool show=!IsVisible;
        if(placement!=next || show)
        {
            // Moving an existing substrate needs neither geometry commits nor repeated z-order changes.
            uint flags=0x0010|0x0200|0x0400|(show?0x0040u:0x0004u);
            if(placement is { } previous && previous.Width==width && previous.Height==height)flags|=0x0001;
            PositionUpdates++;if(!SetWindowPos(handle,NativeWindow.Handle(foreground),bounds.Left,bounds.Top,width,height,flags))throw new Win32Exception();
            placement=next;
        }
    }
    public void Hide(){if(IsVisible)ShowWindow(handle,0);}
    public void Dispose()
    {
        Hide();if(target is not null)target.Root=null;
        surface?.Dispose();clip?.Dispose();geometry?.Dispose();brush?.Dispose();target?.Dispose();compositor?.Dispose();
        surface=null;clip=null;geometry=null;brush=null;target=null;compositor=null;
        if(handle!=0){DestroyWindow(handle);handle=0;}
    }
    static nint WindowProc(nint hwnd,uint message,nint w,nint l)=>message==0x84?-1:DefWindowProc(hwnd,message,w,l);
    delegate nint WindowProcedure(nint h,uint message,nint w,nint l);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct WindowClass {public uint Style;public nint Procedure;public int ClassExtra,WindowExtra;public nint Instance,Icon,Cursor,Background;public string? Menu;public string Name;}
    [StructLayout(LayoutKind.Sequential)] struct NativeRect {public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] struct QueueOptions {public int Size,ThreadType,ApartmentType;}
    [StructLayout(LayoutKind.Sequential)] struct Accent {public int State,Flags;public uint Color;public int Animation;}
    [StructLayout(LayoutKind.Sequential)] struct AttributeData {public int Attribute;public nint Data;public nuint Size;}
    [ComImport,Guid("29E691FA-4567-4DCA-B319-D0F207EB6807"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface ICompositorDesktopInterop {void CreateDesktopWindowTarget(nint hwnd,[MarshalAs(UnmanagedType.Bool)]bool topmost,out nint target);void EnsureOnThread(uint threadId);}
    [DllImport("CoreMessaging.dll")] static extern int CreateDispatcherQueueController(QueueOptions options,out nint controller);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern nint GetModuleHandle(string? module);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern ushort RegisterClass(ref WindowClass cls);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern nint CreateWindowEx(uint extra,string cls,string title,uint style,int x,int y,int width,int height,nint parent,nint menu,nint instance,nint param);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern nint DefWindowProc(nint h,uint m,nint w,nint l);
    [DllImport("user32.dll",SetLastError=true)] static extern bool SetLayeredWindowAttributes(nint h,uint key,byte alpha,uint flags);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] static extern nint GetWindowLongPtr(nint h,int index);
    [DllImport("user32.dll",SetLastError=true)] static extern bool SetWindowPos(nint h,nint after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")] static extern bool GetWindowRect(nint h,out NativeRect rect);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(nint h);
    [DllImport("user32.dll")] static extern bool ShowWindow(nint h,int command);
    [DllImport("user32.dll")] static extern bool DestroyWindow(nint h);
    [DllImport("user32.dll")] static extern bool SetWindowCompositionAttribute(nint h,ref AttributeData data);
    [DllImport("user32.dll")] static extern int SetWindowRgn(nint h,nint region,bool redraw);
    [DllImport("gdi32.dll")] static extern nint CreateRoundRectRgn(int l,int t,int r,int b,int rx,int ry);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(nint region);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(nint h,int attribute,ref int value,int size);
}
