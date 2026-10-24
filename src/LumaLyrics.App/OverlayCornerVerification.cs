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
        static System.Drawing.Bitmap Capture(CornerNative.Rect screen)
        {
            var image=new System.Drawing.Bitmap(screen.Right-screen.Left,screen.Bottom-screen.Top);
            using var graphics=System.Drawing.Graphics.FromImage(image);graphics.CopyFromScreen(screen.Left,screen.Top,0,0,image.Size);return image;
        }
        static (double[] Mean,double Variance) Interior(System.Drawing.Bitmap image)
        {
            var sum=new double[3];var squares=new double[3];int count=0;
            for(int y=image.Height/3;y<image.Height*2/3;y+=2)for(int x=image.Width/3;x<image.Width*2/3;x+=2)
            {var color=image.GetPixel(x,y);double[] values=[color.R,color.G,color.B];for(int i=0;i<3;i++){sum[i]+=values[i];squares[i]+=values[i]*values[i];}count++;}
            var mean=sum.Select(v=>v/count).ToArray();return(mean,Enumerable.Range(0,3).Sum(i=>Math.Max(0,squares[i]/count-mean[i]*mean[i])));
        }
        static double ColorDelta(double[] a,double[] b)=>a.Zip(b,(x,y)=>Math.Abs(x-y)).Max();
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
            var active=CornerNative.GetForegroundWindow();
            // The opaque, topmost checker scene owns this entire bounded capture. No desktop or library imagery is used.
            using var composed=Capture(screen);composed.Save(Path.Combine(directory,"overlay-"+name+"-native.png"));
            var samples=new List<(double[] Mean,double Variance)>();
            floating.LyricSurface.Opacity=0;
            try
            {
                await Settle();
                // Every frame must meet the original thresholds; never retry until a good frame appears.
                for(int frame=0;frame<4;frame++)
                {
                    using var substrate=Capture(screen);samples.Add(Interior(substrate));
                    if(frame==0 || frame==3)substrate.Save(Path.Combine(directory,"overlay-"+name+(frame==0?"-substrate-first.png":"-substrate.png")));
                    if(frame<3)await Task.Delay(50);
                }
            }
            finally{floating.LyricSurface.Opacity=1;await Settle();}
            floating.Hide();await Settle();using var behind=Capture(screen);behind.Save(Path.Combine(directory,"overlay-"+name+"-background.png"));
            checks["overlayCorners."+name+".hostHidesWithWindow"]=!floating.GlassHostVisible;
            int Delta(int x,int y){var a=composed.GetPixel(x,y);var b=behind.GetPixel(x,y);return Math.Max(Math.Abs(a.R-b.R),Math.Max(Math.Abs(a.G-b.G),Math.Abs(a.B-b.B)));}
            int w=composed.Width,h=composed.Height;
            int cornerDelta=new[]{(2,2),(w-3,2),(2,h-3),(w-3,h-3)}.Max(p=>Delta(p.Item1,p.Item2));
            checks["overlayCorners."+name+".compositedCornersClear"]=cornerDelta<=3;
            checks["overlayCorners."+name+".cornerColorDelta"]=cornerDelta;
            floating.Show();await Settle();
            var original=Interior(behind);
            var measurements=samples.Select(s=>new[]{s.Variance/Math.Max(1,original.Variance),ColorDelta(s.Mean,original.Mean)}).ToArray();
            double ratio=measurements.Max(s=>s[0]),colorDelta=measurements.Max(s=>s[1]);
            checks["overlayCorners."+name+".interiorActuallyBlurred"]=floating.BlurAccepted && ratio<.35;
            checks["overlayCorners."+name+".backdropKeepsSceneColor"]=colorDelta<24;
            checks["overlayCorners."+name+".varianceRatio"]=ratio;
            checks["overlayCorners."+name+".sceneColorDelta"]=colorDelta;
            checks["overlayCorners."+name+".consecutiveFrames"]=measurements;
            checks["overlayCorners."+name+".doesNotActivate"]=active==CornerNative.GetForegroundWindow() && active!=floating.GlassHostHandle && active!=NativeWindow.Handle(floating);
        }
        async Task CheckFade(string material)
        {
            CornerNative.GetWindowRect(NativeWindow.Handle(floating),out var bounds);
            var active=CornerNative.GetForegroundWindow();double oldTextOpacity=floating.LyricSurface.Opacity;
            try
            {
                floating.LyricSurface.Opacity=0;floating.Opacity=1;await Settle();
                float fullOpacity=floating.GlassHostOpacity;using var full=Capture(bounds);var fullStats=Interior(full);
                full.Save(Path.Combine(directory,"glass-"+material+"-full.png"));
                CornerNative.GetLayeredWindowAttributes(floating.GlassHostHandle,out _,out byte fullAlpha,out _);
                floating.Opacity=.25;await Settle();float fadedOpacity=floating.GlassHostOpacity;using var faded=Capture(bounds);var fadedStats=Interior(faded);
                faded.Save(Path.Combine(directory,"glass-"+material+"-quarter.png"));
                CornerNative.GetLayeredWindowAttributes(floating.GlassHostHandle,out _,out byte fadedAlpha,out _);
                floating.Opacity=0;await Settle();float offOpacity=floating.GlassHostOpacity;using var clear=Capture(bounds);var original=Interior(clear);
                clear.Save(Path.Combine(directory,"glass-"+material+"-clear.png"));
                double fullRatio=fullStats.Variance/Math.Max(1,original.Variance),fadedRatio=fadedStats.Variance/Math.Max(1,original.Variance);
                checks["overlayCorners."+material+".substrateFollowsWindowFade"]=fullOpacity>0 && Math.Abs(fadedOpacity-fullOpacity*.25)<.00001 && offOpacity==0 && fullRatio<.35 && fadedRatio>fullRatio+.1 && fadedRatio<.98;
                checks["overlayCorners."+material+".nativeLayerAlphaStaysOpaque"]=fullAlpha==255 && fadedAlpha==255;
                checks["overlayCorners."+material+".fadeKeepsSceneColor"]=ColorDelta(fullStats.Mean,original.Mean)<24 && ColorDelta(fadedStats.Mean,original.Mean)<24;
                checks["overlayCorners."+material+".fadeVarianceRatios"]=new[]{fullRatio,fadedRatio,1d};
                checks["overlayCorners."+material+".fadeDoesNotActivate"]=active==CornerNative.GetForegroundWindow();
            }
            finally{floating.Opacity=1;floating.LyricSurface.Opacity=oldTextOpacity;await Settle();}
        }
        floating.Show();
        try
        {
            foreach(var theme in OverlayThemes.All.Where(t=>t.Appearance.Material is "liquid" or "frost"))
            {
                string material=theme.Appearance.Material;
                var appearance=theme.Appearance with{CornerRatio=.8,Animations=false};
                appearance.Apply(fixture);floating.ApplyStyle();await Settle();Check(material+".initial");
                checks["overlayCorners."+material+".roundedCompositedBackdrop"]=floating.BlurAccepted && floating.GlassHostVisible && floating.GlassHostInputTransparent;
                checks["overlayCorners."+material+".hostSharesOwnerWithoutRedirection"]=CornerNative.GetWindow(floating.GlassHostHandle,4)==NativeWindow.Handle(scene) && (CornerNative.GetWindowLongPtr(floating.GlassHostHandle,-20)&0x00200000)!=0;
                await CheckComposition(material);
                VisualVerification.Save(floating.LyricSurface,Path.Combine(directory,"overlay-"+material+"-rounded.png"));
                var bitmap=VisualVerification.Render(floating.LyricSurface);int stride=bitmap.PixelWidth*4;var pixels=new byte[stride*bitmap.PixelHeight];bitmap.CopyPixels(pixels,stride,0);
                checks["overlayCorners."+material+".wpfCornersTransparent"]=new[]{(0,0),(bitmap.PixelWidth-1,0),(0,bitmap.PixelHeight-1),(bitmap.PixelWidth-1,bitmap.PixelHeight-1)}.All(p=>pixels[p.Item2*stride+p.Item1*4+3]==0);
                (appearance with{Width=540,FontSize=52,TwoLines=false,CornerRatio=1}).Apply(fixture);floating.ApplyStyle();await Settle();Check(material+".resizedSingleLine");
                floating.SetLocked(true,false);await Settle();Check(material+".locked");
                CornerNative.GetWindowRect(NativeWindow.Handle(floating),out var inputBounds);
                var hit=CornerNative.WindowFromPoint(new(){X=(inputBounds.Left+inputBounds.Right)/2,Y=(inputBounds.Top+inputBounds.Bottom)/2});
                checks["overlayCorners."+material+".lockedInputReachesUnderlyingWindow"]=hit==NativeWindow.Handle(scene);
                floating.SetLocked(false,false);await Settle();Check(material+".unlocked");
                floating.Hide();floating.Show();await Settle();Check(material+".reshown");
                (appearance with{SystemBlur=false}).Apply(fixture);floating.ApplyStyle();await Settle();Check(material+".blurDisabled");
                appearance.Apply(fixture);floating.ApplyStyle();await Settle();Check(material+".blurReenabled");
                (appearance with{CornerRatio=0}).Apply(fixture);floating.ApplyStyle();await Settle();Check(material+".zeroRadius",false);
                (appearance with{CornerRatio=.4}).Apply(fixture);floating.ApplyStyle();await Settle();Check(material+".roundedAfterSquare");
                checks["overlayCorners."+material+".roundedAfterSquareKeepsBlur"]=floating.BlurAccepted && fixture.OverlaySystemBlur;
                await CheckComposition(material+"-roundtrip");
                await CheckFade(material);
                (appearance with{Transparent=true}).Apply(fixture);floating.ApplyStyle();await Settle();
                checks["overlayCorners."+material+".transparentDisablesBlur"]=!floating.BlurAccepted && !floating.GlassHostVisible;
                appearance.Apply(fixture);floating.ApplyStyle();floating.SetInstrumental(true);await Settle();
                checks["overlayCorners."+material+".instrumentalDisablesBlur"]=!floating.BlurAccepted && !floating.GlassHostVisible && floating.InstrumentalSuppressed;
                floating.SetInstrumental(false);await Settle();Check(material+".vocalRestoresRounded");
            }
        }
        finally{floating.Close();scene.Close();}
    }
    static class CornerNative
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Rect {internal int Left,Top,Right,Bottom;}
        [StructLayout(LayoutKind.Sequential)] internal struct PixelPoint {internal int X,Y;}
        [DllImport("user32.dll")] internal static extern int GetWindowRgn(nint window,nint region);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint window,out Rect rect);
        [DllImport("user32.dll")] internal static extern nint GetWindow(nint window,uint command);
        [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
        [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] internal static extern nint GetWindowLongPtr(nint window,int index);
        [DllImport("user32.dll")] internal static extern nint WindowFromPoint(PixelPoint point);
        [DllImport("user32.dll")] internal static extern bool GetLayeredWindowAttributes(nint window,out uint key,out byte alpha,out uint flags);
        [DllImport("gdi32.dll")] internal static extern nint CreateRectRgn(int left,int top,int right,int bottom);
        [DllImport("gdi32.dll")] internal static extern bool PtInRegion(nint region,int x,int y);
        [DllImport("gdi32.dll")] internal static extern bool DeleteObject(nint region);
    }
}
