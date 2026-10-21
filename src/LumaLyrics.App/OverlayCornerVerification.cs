using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

public partial class MainWindow
{
    // Check the actual HWND, not just the rounded WPF preview. Uses only original fixture text.
    async Task VerifyOverlayCornersAsync(string directory,Dictionary<string,object?> checks)
    {
        var tile=new DrawingGroup();using(var dc=tile.Open())
        {
            dc.DrawRectangle(Brushes.CornflowerBlue,null,new Rect(0,0,12,12));
            dc.DrawRectangle(Brushes.Coral,null,new Rect(0,0,6,6));dc.DrawRectangle(Brushes.Coral,null,new Rect(6,6,6,6));
        }
        var scene=new Window{Owner=this,Title="浮光歌词 · 圆角合成验证",Left=40,Top=40,Width=1100,Height=440,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,Topmost=true,Background=new DrawingBrush(tile){TileMode=TileMode.Tile,ViewportUnits=BrushMappingMode.Absolute,Viewport=new Rect(0,0,12,12)}};
        scene.Show();await scene.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        var fixture=new AppSettings{OverlayLeft=100,OverlayTop=120};
        var floating=new OverlayWindow(this,new PlayerViewModel{Current="把此刻，轻轻唱成一束光。",Next="下一句，随微风慢慢抵达"},fixture){Owner=scene};
        async Task Settle(){floating.UpdateLayout();await floating.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(80);}
        void Check(string name,bool rounded=true)
        {
            var region=CornerNative.CreateRectRgn(0,0,0,0);
            try
            {
                int kind=CornerNative.GetWindowRgn(NativeWindow.Handle(floating),region);
                CornerNative.GetWindowRect(NativeWindow.Handle(floating),out var bounds);
                int w=bounds.Right-bounds.Left,h=bounds.Bottom-bounds.Top;
                bool Inside(int x,int y)=>CornerNative.PtInRegion(region,x,y);
                bool corners=new[]{(2,2),(w-3,2),(2,h-3),(w-3,h-3)}.All(p=>Inside(p.Item1,p.Item2)!=rounded);
                checks["overlayCorners."+name]=kind!=0 && corners && Inside(w/2,0) && Inside(0,h/2) && Inside(w-1,h/2) && Inside(w/2,h-1);
                checks["overlayCorners."+name+".regionKind"]=kind;
            }
            finally{CornerNative.DeleteObject(region);}
        }
        async Task CheckComposition(string name)
        {
            CornerNative.GetWindowRect(NativeWindow.Handle(floating),out var screen);
            System.Drawing.Bitmap CaptureNative()
            {
                var result=new System.Drawing.Bitmap(screen.Right-screen.Left,screen.Bottom-screen.Top);
                using var graphics=System.Drawing.Graphics.FromImage(result);graphics.CopyFromScreen(screen.Left,screen.Top,0,0,result.Size);return result;
            }
            // The opaque, topmost checker scene owns this entire bounded capture. No desktop or library imagery is used.
            using var composed=CaptureNative();composed.Save(Path.Combine(directory,"overlay-"+name+"-native.png"));
            floating.Hide();await Settle();using var behind=CaptureNative();behind.Save(Path.Combine(directory,"overlay-"+name+"-background.png"));
            int Delta(int x,int y){var a=composed.GetPixel(x,y);var b=behind.GetPixel(x,y);return Math.Max(Math.Abs(a.R-b.R),Math.Max(Math.Abs(a.G-b.G),Math.Abs(a.B-b.B)));}
            int w=composed.Width,h=composed.Height;
            int cornerDelta=new[]{(2,2),(w-3,2),(2,h-3),(w-3,h-3)}.Max(p=>Delta(p.Item1,p.Item2));
            checks["overlayCorners."+name+".compositedCornersClear"]=cornerDelta<=3;
            checks["overlayCorners."+name+".cornerColorDelta"]=cornerDelta;
            floating.Show();await Settle();
        }
        floating.Show();
        try
        {
            foreach(var theme in OverlayThemes.All.Where(t=>t.Appearance.Material is "liquid" or "frost"))
            {
                string material=theme.Appearance.Material;
                var appearance=theme.Appearance with{Radius=48,Animations=false};
                appearance.Apply(fixture);floating.ApplyStyle();await Settle();Check(material+".initial");
                checks["overlayCorners."+material+".roundedBlurFallback"]=!floating.BlurAccepted && floating.MaterialStatus.Contains("圆角");
                await CheckComposition(material);
                VisualVerification.Save(floating.LyricSurface,Path.Combine(directory,"overlay-"+material+"-rounded.png"));
                var bitmap=VisualVerification.Render(floating.LyricSurface);int stride=bitmap.PixelWidth*4;var pixels=new byte[stride*bitmap.PixelHeight];bitmap.CopyPixels(pixels,stride,0);
                checks["overlayCorners."+material+".wpfCornersTransparent"]=new[]{(0,0),(bitmap.PixelWidth-1,0),(0,bitmap.PixelHeight-1),(bitmap.PixelWidth-1,bitmap.PixelHeight-1)}.All(p=>pixels[p.Item2*stride+p.Item1*4+3]==0);
                (appearance with{Width=540,FontSize=52,TwoLines=false,Radius=64}).Apply(fixture);floating.ApplyStyle();await Settle();Check(material+".resizedSingleLine");
                floating.SetLocked(true,false);await Settle();Check(material+".locked");
                floating.SetLocked(false,false);await Settle();Check(material+".unlocked");
                floating.Hide();floating.Show();await Settle();Check(material+".reshown");
                (appearance with{SystemBlur=false}).Apply(fixture);floating.ApplyStyle();await Settle();Check(material+".blurDisabled");
                appearance.Apply(fixture);floating.ApplyStyle();await Settle();Check(material+".blurReenabled");
                (appearance with{Radius=0}).Apply(fixture);floating.ApplyStyle();await Settle();Check(material+".zeroRadius",false);
                (appearance with{Radius=24}).Apply(fixture);floating.ApplyStyle();await Settle();Check(material+".roundedAfterSquare");
                checks["overlayCorners."+material+".roundedAfterSquareDisablesBlur"]=!floating.BlurAccepted && fixture.OverlaySystemBlur;
                await CheckComposition(material+"-roundtrip");
                (appearance with{Transparent=true}).Apply(fixture);floating.ApplyStyle();await Settle();
                checks["overlayCorners."+material+".transparentDisablesBlur"]=!floating.BlurAccepted;
                appearance.Apply(fixture);floating.ApplyStyle();floating.SetInstrumental(true);await Settle();
                checks["overlayCorners."+material+".instrumentalDisablesBlur"]=!floating.BlurAccepted && floating.InstrumentalSuppressed;
                floating.SetInstrumental(false);await Settle();Check(material+".vocalRestoresRounded");
            }
        }
        finally{floating.Close();scene.Close();}
    }
    static class CornerNative
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Rect {internal int Left,Top,Right,Bottom;}
        [DllImport("user32.dll")] internal static extern int GetWindowRgn(nint window,nint region);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint window,out Rect rect);
        [DllImport("gdi32.dll")] internal static extern nint CreateRectRgn(int left,int top,int right,int bottom);
        [DllImport("gdi32.dll")] internal static extern bool PtInRegion(nint region,int x,int y);
        [DllImport("gdi32.dll")] internal static extern bool DeleteObject(nint region);
    }
}
