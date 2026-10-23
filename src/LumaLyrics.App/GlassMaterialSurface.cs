using System.Windows;
using System.Windows.Media;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

// Foreground optics only: the compositor (or the local preview scene) supplies the
// real backdrop. Keep the central reading area quiet and let the curved edge carry light.
internal sealed class GlassMaterialSurface : FrameworkElement
{
    OverlayAppearance appearance=new();
    Point light=new(.25,0);
    bool active,reduceMotion;
    static readonly Brush grain=CreateGrain();
    internal void Apply(OverlayAppearance value,bool reduced){appearance=value;reduceMotion=reduced;InvalidateVisual();}
    internal void SetInteraction(Point point,bool value)
    {
        var next=reduceMotion || !appearance.Animations?new Point(.25,0):new Point(Math.Clamp(point.X,0,1),Math.Clamp(point.Y,0,1));
        if(active==value && (next-light).Length<.008)return;light=next;active=value;InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        var a=appearance;if(a.Transparent || a.Opacity<=0 || ActualWidth<=0 || ActualHeight<=0)return;
        var bounds=new Rect(0,0,ActualWidth,ActualHeight);double radius=a.ResolveCornerRadius(bounds.Width,bounds.Height);
        var shape=new RectangleGeometry(bounds,radius,radius);dc.PushClip(shape);
        dc.DrawRoundedRectangle(Solid(a.Background,a.Opacity),null,bounds,radius,radius);
        // Every optical layer must also disappear continuously as the background reaches zero.
        dc.PushOpacity(a.BackgroundEffectOpacity());
        bool liquid=a.Material=="liquid",frost=a.Material=="frost";
        if(liquid)
        {
            // Wide, soft reflection and a neutral meniscus rather than a colored neon glow.
            var reflection=new RadialGradientBrush(Colors.White,Colors.Transparent){Center=new(light.X,-.2),GradientOrigin=new(light.X,-.2),RadiusX=.85,RadiusY=2.3,Opacity=(.07+(active?.055:0))*a.GlassIntensity};
            dc.DrawRectangle(reflection,null,bounds);
            dc.DrawRectangle(Gradient(("#16FFFFFF",0),("#00FFFFFF",.32),("#00000000",.7),("#10060E1D",1)),null,bounds);
            dc.DrawRoundedRectangle(null,new Pen(Gradient(("#00FFFFFF",0),("#00FFFFFF",.4),("#16030B1B",1)),3),Inset(bounds,1.5),Math.Max(0,radius-1.5),Math.Max(0,radius-1.5));
            if(a.Border)
            {
                var rim=Gradient(("#F2FFFFFF",0),("#66FFFFFF",.15),("#0DFFFFFF",.46),("#A8FFFFFF",1));rim.Opacity=.46+.5*a.GlassIntensity;
                dc.DrawRoundedRectangle(null,new Pen(rim,1),Inset(bounds,.5),Math.Max(0,radius-.5),Math.Max(0,radius-.5));
                dc.DrawRoundedRectangle(null,new Pen(Gradient(("#33FFFFFF",0),("#00FFFFFF",.36),("#12010B1B",1)),1),Inset(bounds,1.7),Math.Max(0,radius-1.7),Math.Max(0,radius-1.7));
            }
        }
        else if(frost)
        {
            dc.DrawRectangle(Gradient(("#20FFFFFF",0),("#04FFFFFF",.55),("#0BFFFFFF",1)),null,bounds);
            dc.PushOpacity(.22+.22*a.GlassIntensity);dc.DrawRectangle(grain,null,bounds);dc.Pop();
            if(a.Border)dc.DrawRoundedRectangle(null,new Pen(Gradient(("#9CFFFFFF",0),("#18FFFFFF",.5),("#20A6AEB8",1)),1),Inset(bounds,.5),Math.Max(0,radius-.5),Math.Max(0,radius-.5));
        }
        else
        {
            if(a.Material=="aurora")
            {dc.PushOpacity(a.GlassIntensity*.43);dc.DrawRectangle(Gradient((a.Accent,0),("#778CE1",.42),("#469AAB",1)),null,bounds);dc.Pop();}
            else if(a.Material is "material" or "paper")
                dc.DrawRectangle(new RadialGradientBrush(OverlaySurface.ColorOf(a.Accent),Colors.Transparent){Center=new(.98,1),GradientOrigin=new(.98,1),RadiusX=.8,RadiusY=1.5,Opacity=a.GlassIntensity*.16},null,bounds);
            if(a.Border)dc.DrawRoundedRectangle(null,new Pen(Solid("#FFFFFF",.16),1),Inset(bounds,.5),Math.Max(0,radius-.5),Math.Max(0,radius-.5));
        }
        dc.Pop();dc.Pop();
    }
    static Rect Inset(Rect r,double value)=>new(value,value,Math.Max(0,r.Width-value*2),Math.Max(0,r.Height-value*2));
    static SolidColorBrush Solid(string value,double opacity){var brush=new SolidColorBrush(OverlaySurface.ColorOf(value)){Opacity=opacity};brush.Freeze();return brush;}
    static LinearGradientBrush Gradient(params (string Color,double Position)[] stops)
    {var brush=new LinearGradientBrush{StartPoint=new(0,0),EndPoint=new(1,1)};foreach(var stop in stops)brush.GradientStops.Add(new(OverlaySurface.ColorOf(stop.Color),stop.Position));return brush;}
    static Brush CreateGrain()
    {
        var drawing=new DrawingGroup();using(var dc=drawing.Open())
            for(int i=0;i<38;i++){int x=(i*29+7)%64,y=(i*43+11)%64;dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(15,255,255,255)),null,new Rect(x,y,1,1));dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(7,0,0,0)),null,new Rect((x+13)%64,(y+19)%64,1,1));}
        var brush=new DrawingBrush(drawing){TileMode=TileMode.Tile,ViewportUnits=BrushMappingMode.Absolute,Viewport=new Rect(0,0,64,64),ViewboxUnits=BrushMappingMode.Absolute,Viewbox=new Rect(0,0,64,64)};brush.Freeze();return brush;
    }
}
