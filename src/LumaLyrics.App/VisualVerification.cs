using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

// Opt-in, isolated-data verification only. Sample sentences are self-authored and never enter the lyrics database.
internal static class VisualVerification
{
    [DllImport("gdi32.dll")] static extern nint CreateRectRgn(int left,int top,int right,int bottom);
    [DllImport("user32.dll")] static extern int GetWindowRgn(nint window,nint region);
    [DllImport("gdi32.dll")] static extern int GetRgnBox(nint region,out NativeWindow.RectNative bounds);
    [DllImport("gdi32.dll")] [return:MarshalAs(UnmanagedType.Bool)] static extern bool PtInRegion(nint region,int x,int y);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(nint obj);

    internal static RenderTargetBitmap Render(FrameworkElement visual)
    {
        visual.UpdateLayout();var dpi=VisualTreeHelper.GetDpi(visual);
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth*dpi.DpiScaleX),(int)Math.Ceiling(visual.ActualHeight*dpi.DpiScaleY),96*dpi.DpiScaleX,96*dpi.DpiScaleY,PixelFormats.Pbgra32);
        var drawing=new DrawingVisual();using(var dc=drawing.RenderOpen())dc.DrawRectangle(new VisualBrush(visual),null,new Rect(0,0,visual.ActualWidth,visual.ActualHeight));bitmap.Render(drawing);return bitmap;
    }
    internal static void Save(FrameworkElement visual,string path)
    {var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(Render(visual)));using var file=File.Create(path);png.Save(file);}
    internal static async Task<Dictionary<string,Dictionary<string,bool>>> CheckOverlayCornersAsync(OverlayWindow overlay,AppSettings settings,string directory)
    {
        var checks=new Dictionary<string,Dictionary<string,bool>>();
        var saved=OverlayAppearance.From(settings);var left=settings.OverlayLeft;var top=settings.OverlayTop;
        bool visible=overlay.IsVisible,locked=overlay.IsLocked;
        try
        {
            if(!visible)overlay.Show();
            foreach(var tag in new[]{"GLASS","FROST","CLASSIC","CLEAR"})
            foreach(double radius in new double[]{0,8,24,64})
            foreach(bool lockWindow in new[]{false,true})
            {
                var appearance=OverlayThemes.All.Single(t=>t.Tag==tag).Appearance with{Radius=radius,Width=lockWindow?360:820,TwoLines=!lockWindow,Animations=false};
                appearance.Apply(settings);overlay.ApplyStyle();overlay.SetLocked(lockWindow,false);
                await overlay.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                string name=$"{tag.ToLowerInvariant()}-r{radius}-{(lockWindow?"locked":"unlocked")}";
                checks[name]=CheckWindowSilhouette(overlay,radius);
                if(tag=="GLASS" && radius==64)Save(overlay,Path.Combine(directory,$"rounded-window-{(lockWindow?"locked":"unlocked")}.png"));
            }
        }
        finally
        {
            saved.Apply(settings);overlay.SetLocked(locked,false);overlay.RestorePlacement(left,top);
            if(!visible)overlay.Hide();
        }
        return checks;
    }
    static Dictionary<string,bool> CheckWindowSilhouette(OverlayWindow overlay,double radius)
    {
        var checks=new Dictionary<string,bool>();var (width,height)=NativeWindow.PixelSize(overlay);
        var region=CreateRectRgn(0,0,0,0);
        try
        {
            bool hasRegion=region!=0 && GetWindowRgn(NativeWindow.Handle(overlay),region)>0;
            checks["nativeRegionPresent"]=hasRegion;
            NativeWindow.RectNative bounds=default;
            bool hasBounds=hasRegion && GetRgnBox(region,out bounds)>0;
            checks["nativeRegionMatchesWindowBounds"]=hasBounds && bounds.Left==0 && bounds.Top==0 && bounds.Right==width && bounds.Bottom==height;
            checks["nativeCenterIncluded"]=hasRegion && PtInRegion(region,width/2,height/2);
            var dpi=VisualTreeHelper.GetDpi(overlay);
            // Probe the top and bottom shoulders as well as all four extreme corners.
            // The former outward guard included these transparent shoulder pixels in native blur.
            int shoulder=(int)(Math.Min(radius*dpi.DpiScaleX,width/2d)*.25);
            var points=new[]{(0,0),(width-1,0),(0,height-1),(width-1,height-1),(shoulder,0),(width-1-shoulder,0),(shoulder,height-1),(width-1-shoulder,height-1)};
            checks["nativeCornersExcluded"]=hasRegion && (radius==0 || points.All(p=>!PtInRegion(region,p.Item1,p.Item2)));
            var bitmap=Render(overlay);int stride=bitmap.PixelWidth*4;var pixels=new byte[stride*bitmap.PixelHeight];bitmap.CopyPixels(pixels,stride,0);
            checks["renderSizeMatchesWindow"]=bitmap.PixelWidth==width && bitmap.PixelHeight==height;
            checks["wholeWindowCornersTransparent"]=radius==0 || points.All(p=>p.Item1>=0 && p.Item1<bitmap.PixelWidth && p.Item2>=0 && p.Item2<bitmap.PixelHeight && pixels[p.Item2*stride+p.Item1*4+3]==0);
        }
        finally{if(region!=0)DeleteObject(region);}
        return checks;
    }
    internal static async Task<Dictionary<string,object?>> RunAsync(Window owner,string directory)
    {
        var checks=new Dictionary<string,object?>();
        var surface=new OverlaySurface{DataContext=new PlayerViewModel{Current="把此刻，轻轻唱成一束光。",Next="让旋律停留在你身旁"}};
        var panel=new StackPanel{Margin=new(26)};
        panel.Children.Add(new TextBlock{Text="切换与留白 · 自写文字演示",FontSize=17,Foreground=Brushes.WhiteSmoke,Margin=new(0,0,0,22)});
        panel.Children.Add(surface);
        panel.Children.Add(new TextBlock{Text="播放进度 · 样式演示",Foreground=Brushes.LightGray,Margin=new(0,24,0,6)});
        var slider=new Slider{Minimum=0,Maximum=100,Value=46};panel.Children.Add(slider);
        var window=new Window{Title="浮光歌词 · 动效验证",Owner=owner,Width=880,SizeToContent=SizeToContent.Height,Background=new SolidColorBrush(Color.FromRgb(37,43,60)),Content=panel,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var options=OverlayThemes.All[0].Appearance with{TransitionMs=220};surface.Apply(options);window.Show();
        try
        {
            await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            Save(window,Path.Combine(directory,"motion-start.png"));
            surface.CurrentText="让旋律停留在你身旁";surface.NextText="下一句，随微风慢慢抵达。";
            await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.Render);
            await Task.Delay(70);
            var middle=surface.TransitionState;
            checks["systemAnimationsEnabled"]=SystemParameters.ClientAreaAnimation;
            checks["crossfadeInProgress"]=!SystemParameters.ClientAreaAnimation || middle.Opacity is >0 and <1 && middle.OutgoingOpacity>0 && middle.OutgoingText.Length>0;
            Save(window,Path.Combine(directory,"motion-middle.png"));
            await Task.Delay(260);
            checks["transitionClearedOutgoing"]=surface.TransitionState is{Opacity:1,OutgoingOpacity:0,OutgoingText:"",Text:"让旋律停留在你身旁"};
            Save(window,Path.Combine(directory,"motion-end.png"));
            // Interruption must discard the obsolete layer and finish on the latest lyric.
            surface.CurrentText="微风穿过安静的窗。";await Task.Delay(45);surface.CurrentText="下一句，随微风慢慢抵达。";await Task.Delay(300);
            checks["rapidChangesEndOnLatestText"]=surface.TransitionState is{Opacity:1,OutgoingText:"",Text:"下一句，随微风慢慢抵达。"};
            surface.Apply(options,true);surface.CurrentText="减少动画时，立即显示新的一句。";await Task.Delay(30);
            checks["reduceMotionSkipsTransition"]=surface.TransitionState is{Opacity:1,OutgoingText:"",Text:"减少动画时，立即显示新的一句。"};
            surface.Apply(options with{Animations=false});surface.CurrentText="关闭切换动画，同样即时生效。";await Task.Delay(30);
            checks["animationSwitchSkipsTransition"]=surface.TransitionState is{Opacity:1,OutgoingText:"",Text:"关闭切换动画，同样即时生效。"};
            surface.Apply(options with{Material="card",Background="#FFFFFF",Color="#202B40",Opacity=1,GlassIntensity=0,Shadow=false,Border=false,TwoLines=false,Animations=false});surface.CurrentText="平滑圆角";await Task.Delay(30);
            var corner=Render(surface);int stride=corner.PixelWidth*4;byte[] pixels=new byte[stride*corner.PixelHeight];corner.CopyPixels(pixels,stride,0);
            int partial=0;for(int y=0;y<Math.Min(40,corner.PixelHeight);y++)for(int x=0;x<Math.Min(40,corner.PixelWidth);x++){byte alpha=pixels[y*stride+x*4+3];if(alpha is >0 and <255)partial++;}
            checks["cornerHasFractionalAlpha"]=partial>10;checks["cornerFractionalPixels"]=partial;
            Save(surface,Path.Combine(directory,"rounded-edge.png"));
            checks["singleLineTextCentered"]=Math.Abs(surface.CurrentCenterY-surface.ActualHeight/2)<1;
            slider.ApplyTemplate();var track=(System.Windows.Controls.Primitives.Track)slider.Template.FindName("PART_Track",slider);
            checks["sharedSliderTemplateLoaded"]=track is not null && slider.IsMoveToPointEnabled;
            slider.Value=73;checks["sliderValueStillWorks"]=slider.Value==73;
        }
        finally{window.Close();}
        return checks;
    }
}
