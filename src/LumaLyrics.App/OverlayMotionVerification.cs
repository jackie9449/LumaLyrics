using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

public partial class MainWindow
{
    [DllImport("user32.dll",EntryPoint="SendMessageW")] static extern nint MotionMessage(nint handle,uint message,nint w,nint l);
    [DllImport("user32.dll",EntryPoint="GetWindowRect")] static extern bool MotionBounds(nint handle,out NativeWindow.RectNative bounds);
    async Task VerifyOverlayMotionAsync(Dictionary<string,object?> checks)
    {
        var original=OverlayAppearance.From(settings);var location=NativeWindow.Location(overlay);bool visible=overlay.IsVisible;
        string current=vm.Current,next=vm.Next;var area=SystemParameters.WorkArea;
        var fixture=new Window{WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,Topmost=true,Left=area.Left,Top=area.Top,Width=area.Width,Height=area.Height,Background=Brushes.White,Content=GlassPreviewScene.CreateWallpaper(2)};
        try
        {
            fixture.Show();vm.Current="随光移动，让旋律轻盈停留。";vm.Next="原创文字 · 拖动性能验证";
            (OverlayThemes.All[0].Appearance with{Width=640}).Apply(settings);overlay.Show();overlay.ApplyStyle();NativeWindow.Place(overlay,80,240);
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(100);
            var before=overlay.MovementWork;var samples=new List<double>();var handle=NativeWindow.Handle(overlay);
            MotionMessage(handle,0x231,0,0);
            checks["overlayMotion.nativeMoveStateStarts"]=overlay.InteractiveMove;
            for(int i=0;i<32;i++)
            {
                var watch=Stopwatch.StartNew();NativeWindow.Place(overlay,80+i*3,240+(i%7)*2);
                await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.Render);samples.Add(watch.Elapsed.TotalMilliseconds);
            }
            var moved=overlay.MovementWork;MotionMessage(handle,0x232,0,0);
            checks["overlayMotion.nativeMoveStateEnds"]=!overlay.InteractiveMove;
            checks["overlayMotion.noDesktopReadbacksDuringMove"]=moved.Samples==before.Samples;
            checks["overlayMotion.reusesUnchangedGeometry"]=moved.Geometry==before.Geometry;
            checks["overlayMotion.reusesForegroundRegion"]=moved.Regions==before.Regions;
            checks["overlayMotion.moveSamples"]=samples.Count;
            checks["overlayMotion.medianStepMs"]=samples.Order().ElementAt(samples.Count/2);
            checks["overlayMotion.maxStepMs"]=samples.Max();
            checks["overlayMotion.desktopReads"]=moved.Samples-before.Samples;
            checks["overlayMotion.geometryWrites"]=moved.Geometry-before.Geometry;
            checks["overlayMotion.nativePositionWrites"]=moved.Positions-before.Positions;
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(100);
            MotionBounds(handle,out var foreground);MotionBounds(overlay.GlassHostHandle,out var substrate);
            checks["overlayMotion.substrateExactlyFollowsForeground"]=foreground.Left==substrate.Left && foreground.Top==substrate.Top && foreground.Right==substrate.Right && foreground.Bottom==substrate.Bottom;
            checks["overlayMotion.keepsRealBlurAndInputTransparency"]=overlay.GlassHostVisible && overlay.GlassHostInputTransparent;
            checks["overlayMotion.preservesDraggedPosition"]=Math.Abs(settings.OverlayLeft!.Value-foreground.Left)<1 && Math.Abs(settings.OverlayTop!.Value-foreground.Top)<1;
        }
        finally
        {
            MotionMessage(NativeWindow.Handle(overlay),0x232,0,0);original.Apply(settings);vm.Current=current;vm.Next=next;
            overlay.ApplyStyle();if(!visible)overlay.Hide();fixture.Close();NativeWindow.Place(overlay,location.Left,location.Top);
        }
    }
}
