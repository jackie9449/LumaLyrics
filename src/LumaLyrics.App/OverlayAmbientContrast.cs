using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

internal static class OverlayAmbientContrast
{
    internal static double? Brightness(Window window,OverlayAppearance appearance)
    {
        if(!GetWindowRect(NativeWindow.Handle(window),out var r))return null;
        // Eight ephemeral color points outside the lyric window: no screenshot, image,
        // text extraction or stored desktop data. Outside points also avoid feedback.
        (int X,int Y)[] points=[(r.Left-2,r.Top-2),(r.Right+2,r.Top-2),(r.Left-2,r.Bottom+2),(r.Right+2,r.Bottom+2),((r.Left+r.Right)/2,r.Top-2),((r.Left+r.Right)/2,r.Bottom+2),(r.Left-2,(r.Top+r.Bottom)/2),(r.Right+2,(r.Top+r.Bottom)/2)];
        var dc=GetDC(0);if(dc==0)return null;
        try
        {
            double sum=0;int count=0;
            foreach(var point in points){uint color=GetPixel(dc,point.X,point.Y);if(color==0xffffffff)continue;sum+=(color&255)*.2126+((color>>8)&255)*.7152+((color>>16)&255)*.0722;count++;}
            if(count==0)return null;
            var tint=OverlaySurface.ColorOf(appearance.Background);double tintBrightness=tint.R*.2126+tint.G*.7152+tint.B*.0722;
            return (sum/count*(1-appearance.Opacity)+tintBrightness*appearance.Opacity)/255;
        }
        finally{ReleaseDC(0,dc);}
    }
    [StructLayout(LayoutKind.Sequential)] struct Rect {public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")] static extern bool GetWindowRect(nint h,out Rect rect);
    [DllImport("user32.dll")] static extern nint GetDC(nint h);
    [DllImport("user32.dll")] static extern int ReleaseDC(nint h,nint dc);
    [DllImport("gdi32.dll")] static extern uint GetPixel(nint dc,int x,int y);
}
