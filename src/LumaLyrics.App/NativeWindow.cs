using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;
namespace LumaLyrics.App;

internal static class NativeWindow
{
    const int GwlExStyle = -20;
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] static extern nint GetWindowLongPtr(nint h,int i);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")] static extern nint SetWindowLongPtr(nint h,int i,nint v);
    [DllImport("user32.dll")] static extern bool SetWindowPos(nint h,nint after,int x,int y,int w,int height,uint flags);
    [DllImport("user32.dll")] static extern bool GetWindowRect(nint h,out RectNative rect);
    [DllImport("user32.dll")] internal static extern bool RegisterHotKey(nint h,int id,uint modifiers,uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(nint h,int id);
    [StructLayout(LayoutKind.Sequential)] internal struct RectNative { public int Left,Top,Right,Bottom; }
    public static nint Handle(Window w) => new WindowInteropHelper(w).Handle;
    public static bool IsClickThrough(Window w)=>(GetWindowLongPtr(Handle(w),GwlExStyle)&0x20)!=0;
    public static void NoActivate(Window w) { var h=Handle(w); SetWindowLongPtr(h,GwlExStyle,GetWindowLongPtr(h,GwlExStyle) | 0x08000000 | 0x80); }
    public static void ClickThrough(Window w,bool enabled)
    {
        var h=Handle(w); var style=GetWindowLongPtr(h,GwlExStyle);
        SetWindowLongPtr(h,GwlExStyle,enabled ? style | 0x20 : style & ~0x20);
        SetWindowPos(h,0,0,0,0,0,0x37);
    }
    public static (double Left,double Top) Location(Window w) { GetWindowRect(Handle(w),out var r); return (r.Left,r.Top); }
    internal static (int Width,int Height) PixelSize(Window w) => GetWindowRect(Handle(w),out var r)?(r.Right-r.Left,r.Bottom-r.Top):(0,0);
    public static void Place(Window w,double? left,double? top,bool bottom = false,double bottomInset=60)
    {
        var h=Handle(w); GetWindowRect(h,out var r); int width=r.Right-r.Left, height=r.Bottom-r.Top;
        var point=new System.Drawing.Point((int)(left ?? r.Left),(int)(top ?? r.Top));
        var area=Forms.Screen.FromPoint(point).WorkingArea;
        int x=left.HasValue ? (int)left : area.Left+(area.Width-width)/2;
        int y=top.HasValue ? (int)top : bottom ? area.Bottom-height-(int)Math.Round(bottomInset) : area.Top+(area.Height-height)/2;
        x=Math.Clamp(x,area.Left,Math.Max(area.Left,area.Right-width)); y=Math.Clamp(y,area.Top,Math.Max(area.Top,area.Bottom-height));
        SetWindowPos(h,0,x,y,0,0,0x15);
    }
    public static void ClampVisible(Window w) { var p=Location(w); Place(w,p.Left,p.Top); }
}
internal sealed class Hotkeys : IDisposable
{
    readonly nint handle; readonly HwndSource source; readonly Action<int> action;
    public Hotkeys(Window window,Action<int> action) { handle=NativeWindow.Handle(window); source=HwndSource.FromHwnd(handle); this.action=action; source.AddHook(Hook); }
    public string Register(string toggle,string unlock)
    {
        NativeWindow.UnregisterHotKey(handle,1); NativeWindow.UnregisterHotKey(handle,2);
        var errors=new List<string>();
        if (!RegisterOne(1,toggle)) errors.Add($"显示快捷键 {toggle} 注册失败或已占用");
        if (!RegisterOne(2,unlock)) errors.Add($"解锁快捷键 {unlock} 注册失败或已占用");
        return string.Join("；",errors);
    }
    bool RegisterOne(int id,string value)
    {
        try { var gesture=(System.Windows.Input.KeyGesture)new System.Windows.Input.KeyGestureConverter().ConvertFromInvariantString(value)!; uint mod=0x4000; if(gesture.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control))mod|=2; if(gesture.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Alt))mod|=1; if(gesture.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift))mod|=4; if(gesture.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Windows))return false; return NativeWindow.RegisterHotKey(handle,id,mod,(uint)System.Windows.Input.KeyInterop.VirtualKeyFromKey(gesture.Key)); } catch { return false; }
    }
    nint Hook(nint h,int msg,nint w,nint l,ref bool handled) { if(msg==0x312) { action((int)w); handled=true; } return 0; }
    public void Dispose() { NativeWindow.UnregisterHotKey(handle,1); NativeWindow.UnregisterHotKey(handle,2); source.RemoveHook(Hook); }
}
