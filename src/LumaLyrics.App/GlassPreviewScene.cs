using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace LumaLyrics.App;

// An original vector scene, never a copy of the user's desktop or library artwork.
internal sealed class GlassPreviewScene : Border
{
    readonly Wallpaper wallpaper=new();
    readonly OverlaySurface surface;
    internal GlassPreviewScene(OverlaySurface surface)
    {
        this.surface=surface;
        CornerRadius=new(20);var layers=new Grid();Child=layers;layers.Children.Add(wallpaper);
        var scroll=new ScrollViewer{Content=surface,Margin=new(24),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalContentAlignment=VerticalAlignment.Center};
        surface.VerticalAlignment=VerticalAlignment.Center;layers.Children.Add(scroll);surface.AttachPreviewBackdrop(wallpaper);
        SizeChanged+=(_,_)=>{if(ActualWidth>0 && ActualHeight>0)Clip=new RectangleGeometry(new Rect(0,0,ActualWidth,ActualHeight),20,20);};
        SetScene(2);
    }
    internal void SetScene(int scene){wallpaper.Scene=scene;wallpaper.InvalidateVisual();surface.SetPreviewBrightness(scene==0?.22:scene==1?.86:.71);}
    internal static FrameworkElement CreateWallpaper(int scene)=>new Wallpaper{Scene=scene};
    sealed class Wallpaper : FrameworkElement
    {
        internal int Scene;
        protected override void OnRender(DrawingContext dc)
        {
            double w=ActualWidth,h=ActualHeight;if(w<=0 || h<=0)return;
            Color ColorOf(string hex)=>(Color)ColorConverter.ConvertFromString(hex);
            var start=ColorOf(Scene==0?"#111B30":Scene==1?"#DDE8F1":"#9CC8D2");var end=ColorOf(Scene==0?"#33415C":Scene==1?"#F8F0E8":"#D7B9AA");
            dc.DrawRectangle(new LinearGradientBrush(start,end,30),null,new Rect(0,0,w,h));
            dc.PushClip(new RectangleGeometry(new Rect(0,0,w,h)));
            dc.DrawEllipse(new LinearGradientBrush(ColorOf(Scene==0?"#29344E":"#C0DEE6"),ColorOf(Scene==0?"#40506E":"#81AFC1"),70),null,new Point(w*.15,h*.85),w*.72,h*.74);
            dc.DrawEllipse(new LinearGradientBrush(ColorOf(Scene==0?"#5A576F":"#E6D6C9"),ColorOf(Scene==0?"#28334C":"#C3A5A7"),-40),null,new Point(w*.86,h*.18),w*.62,h*.8);
            dc.DrawEllipse(new LinearGradientBrush(ColorOf(Scene==0?"#192941":"#89B8CA"),ColorOf(Scene==0?"#32435C":"#C2D7DF"),40),null,new Point(w*.51,h*1.32),w*.59,h*.98);
            dc.Pop();
        }
    }
}
