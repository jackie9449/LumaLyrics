using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

public partial class MainWindow
{
    async Task VerifyOverlayAdjustmentsAsync(string directory,Dictionary<string,object?> checks)
    {
        var tile=new DrawingGroup();using(var dc=tile.Open())
        {
            dc.DrawRectangle(Brushes.CornflowerBlue,null,new Rect(0,0,16,16));
            dc.DrawRectangle(Brushes.Coral,null,new Rect(0,0,8,8));dc.DrawRectangle(Brushes.Coral,null,new Rect(8,8,8,8));
        }
        var scene=new Window{Owner=this,Title="浮光歌词 · 比例与透明度验证",Left=30,Top=30,Width=1120,Height=720,WindowStyle=WindowStyle.None,ShowInTaskbar=false,Topmost=true,Background=new DrawingBrush(tile){TileMode=TileMode.Tile,ViewportUnits=BrushMappingMode.Absolute,Viewport=new Rect(0,0,16,16)}};
        scene.Show();
        var fixture=new AppSettings{OverlayLeft=90,OverlayTop=110};
        var text=new PlayerViewModel{Current="让每一句，轻轻落在心上。",Next="把旋律交给风"};
        var floating=new OverlayWindow(this,text,fixture){Owner=scene};
        async Task Settle(){floating.UpdateLayout();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(85);}
        double MaskRadius()=>((RectangleGeometry)((GeometryDrawing)((DrawingBrush)floating.LyricSurface.Surface.OpacityMask).Drawing).Geometry).RadiusX;
        static byte[] Pixels(FrameworkElement visual,out int width,out int height)
        {
            var image=VisualVerification.Render(visual);width=image.PixelWidth;height=image.PixelHeight;
            var bytes=new byte[width*height*4];image.CopyPixels(bytes,width*4,0);return bytes;
        }
        bool NativeCurve(nint handle,double radius)
        {
            if(handle==0 || radius<10)return false;
            var region=CornerNative.CreateRectRgn(0,0,0,0);
            try
            {
                int kind=CornerNative.GetWindowRgn(handle,region);
                var dpi=VisualTreeHelper.GetDpi(floating);
                bool At(double x,double y)=>CornerNative.PtInRegion(region,(int)(radius*x*dpi.DpiScaleX),(int)(radius*y*dpi.DpiScaleY));
                return kind!=0 && !At(.15,.15) && At(.6,.3);
            }
            finally{CornerNative.DeleteObject(region);}
        }
        floating.Show();
        try
        {
            foreach(var theme in OverlayThemes.All.Where(t=>t.Appearance.Material is "liquid" or "frost"))
            {
                string key="overlayAdjustments."+theme.Appearance.Material;
                var a=theme.Appearance with{CornerRatio=.8,Animations=false,AdaptiveContrast=false,Width=820,FontSize=32};
                a.Apply(fixture);floating.ApplyStyle();await Settle();double shortHeight=floating.ActualHeight,shortRadius=MaskRadius();
                text.Current="让每一句，轻轻落在心上。\n留住此刻的微光。\n让远方也有回响。";
                text.CurrentTranslation="风把歌声带向安静的远方。\n星光为归途点亮一扇窗。";
                await Settle();double tallRadius=MaskRadius();
                checks[key+".radiusScalesWithTranslatedHeight"]=floating.ActualHeight>shortHeight*1.5 && tallRadius>shortRadius*1.5 && Math.Abs(tallRadius-Math.Min(floating.ActualWidth,floating.ActualHeight)*.4)<.1;
                checks[key+".tallForegroundAndGlassRegionsAgree"]=NativeCurve(NativeWindow.Handle(floating),tallRadius) && NativeCurve(floating.GlassHostHandle,tallRadius);
                VisualVerification.Save(floating.LyricSurface,Path.Combine(directory,"overlay-"+theme.Appearance.Material+"-tall-ratio.png"));
                (a with{Width=360,FontSize=60,CornerRatio=1}).Apply(fixture);floating.ApplyStyle();await Settle();
                checks[key+".maxRadiusUsesShorterEdge"]=Math.Abs(MaskRadius()-Math.Min(floating.ActualWidth,floating.ActualHeight)/2)<.1;
                (a with{CornerRatio=0}).Apply(fixture);floating.ApplyStyle();await Settle();
                checks[key+".zeroRatioIsSquare"]=MaskRadius()==0;
                text.Current="让每一句，轻轻落在心上。";text.Next="把旋律交给风";text.CurrentTranslation="";
                a.Apply(fixture);floating.ApplyStyle();await Settle();
                checks[key+".ratioRestoresAfterResize"]=Math.Abs(MaskRadius()-shortRadius)<.1;

                double textOpacity=floating.LyricSurface.CurrentLine.Opacity;double previousMean=-1;byte previousNative=0;
                foreach(double opacity in new[]{0d,.001,.01,.05,.1})
                {
                    var current=a with{Opacity=opacity};current.Apply(fixture);floating.ApplyStyle();await Settle();
                    string suffix=opacity.ToString("0.000",System.Globalization.CultureInfo.InvariantCulture);
                    byte alpha=0;if(opacity>0)CornerNative.GetLayeredWindowAttributes(floating.GlassHostHandle,out _,out alpha,out _);
                    byte expected=(byte)Math.Round(255*(a.Material=="frost"?.98:.78)*current.BackgroundEffectOpacity());
                    checks[key+".nativeAlpha."+suffix]=opacity==0?!floating.GlassHostVisible:alpha==expected && alpha>=previousNative;
                    var pixels=Pixels(floating.LyricSurface.Material,out int w,out int h);
                    double sum=0;int count=0;
                    for(int y=h/3;y<h*2/3;y++)for(int x=w/3;x<w*2/3;x++){sum+=pixels[(y*w+x)*4+3];count++;}
                    double mean=sum/Math.Max(1,count);
                    checks[key+".paintAlpha."+suffix]=mean>=previousMean && (opacity==0?mean==0:opacity<=.01?mean<8:true);
                    checks[key+".textUnaffected."+suffix]=floating.LyricSurface.CurrentLine.Opacity==textOpacity && floating.LyricSurface.CurrentLine.Text==text.Current;
                    previousMean=mean;previousNative=alpha;
                }
                (a with{Opacity=0}).Apply(fixture);floating.ApplyStyle();await Settle();
                checks[key+".zeroClearsAfterMaterialWasVisible"]=!floating.GlassHostVisible && floating.LyricSurface.CurrentLine.IsVisible;
            }
            var settingsCopy=new AppSettings();int saves=0;
            var editor=new AppearanceWindow(settingsCopy,_=>{},()=>saves++,()=>"比例与低透明度检查"){Owner=this};editor.Show();editor.SelectLayoutTab();await Settle();
            try
            {
                var radius=Descendants<Slider>(editor).Single(s=>AutomationProperties.GetName(s)=="圆角比例");
                var opacity=Descendants<Slider>(editor).Single(s=>AutomationProperties.GetName(s)=="背景不透明度");
                radius.Value=.73;opacity.Value=.001;await Settle();
                checks["overlayAdjustments.editorRatioAndSubPercentControls"]=radius.Minimum==0 && radius.Maximum==1 && settingsCopy.OverlayCornerRatio==.73 && settingsCopy.OverlayBackgroundOpacity==.001 && opacity.TickFrequency==.001;
                checks["overlayAdjustments.editorDisplaysPercentages"]=Descendants<TextBlock>(editor).Any(b=>b.Text.Replace(" ","").Contains("73%")) && Descendants<TextBlock>(editor).Any(b=>b.Text.Replace(" ","").Contains("0.1%"));
                VisualVerification.Save(editor,Path.Combine(directory,"appearance-ratio-opacity.png"));editor.Save();
                checks["overlayAdjustments.editorSavesRatio"]=saves==1 && settingsCopy.OverlayThemeProfiles[settingsCopy.OverlayPreset].CornerRatio==.73;
            }
            finally{editor.Close();}
        }
        finally{floating.Close();scene.Close();}
    }
}
