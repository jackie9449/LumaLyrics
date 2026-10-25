using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace LumaLyrics.App;

public sealed class ThemeBackdrop : FrameworkElement
{
    static DependencyProperty ResourceProperty(string name,Type type,object value)=>DependencyProperty.Register(name,type,typeof(ThemeBackdrop),new FrameworkPropertyMetadata(value,FrameworkPropertyMetadataOptions.AffectsRender));
    internal static readonly DependencyProperty ThemeProperty=ResourceProperty("Theme",typeof(string),"liquid");
    static readonly DependencyProperty InkProperty=ResourceProperty("Ink",typeof(Brush),Brushes.Gray);
    static readonly DependencyProperty AccentProperty=ResourceProperty("Accent",typeof(Brush),Brushes.Gray);
    static readonly DependencyProperty GlowProperty=ResourceProperty("Glow",typeof(Brush),Brushes.Gray);
    public ThemeBackdrop()
    {
        IsHitTestVisible=false;ClipToBounds=true;
        SetResourceReference(ThemeProperty,"ThemeId");SetResourceReference(InkProperty,"TextBrush");
        SetResourceReference(AccentProperty,"AccentBrush");SetResourceReference(GlowProperty,"GlowBrush");
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);double w=ActualWidth,h=ActualHeight;if(w<1 || h<1)return;
        var ink=(Brush)GetValue(InkProperty);var accent=(Brush)GetValue(AccentProperty);var glow=(Brush)GetValue(GlowProperty);
        dc.PushClip(new RectangleGeometry(new Rect(0,0,w,h)));
        switch((string)GetValue(ThemeProperty))
        {
            case "frost":
                dc.PushOpacity(.22);dc.DrawRoundedRectangle(Brushes.White,new Pen(Brushes.White,1),new Rect(w*.60,-90,w*.65,210),90,90);dc.Pop();
                dc.PushOpacity(.16);dc.DrawLine(new Pen(Brushes.White,1),new(w*.52,0),new(w*.22,h));dc.Pop();break;
            case "material":
                dc.PushOpacity(.38);dc.DrawRoundedRectangle(glow,null,new Rect(w-264,-120,360,220),100,100);dc.Pop();
                dc.PushOpacity(.12);dc.DrawEllipse(accent,null,new(w-65,70),44,44);dc.Pop();break;
            case "paper":
                dc.PushOpacity(.012);for(double y=7;y<h;y+=7)dc.DrawLine(new Pen(ink,.5),new(0,y),new(w,y));dc.Pop();
                dc.PushOpacity(.22);dc.DrawLine(new Pen(ink,.6),new(24,h-14),new(w-24,h-14));dc.Pop();break;
            case "studio":
                dc.PushOpacity(.035);var grid=new Pen(ink,.6);
                for(double x=24;x<w;x+=48)dc.DrawLine(grid,new(x,0),new(x,h));
                for(double y=24;y<h;y+=48)dc.DrawLine(grid,new(0,y),new(w,y));dc.Pop();
                dc.PushOpacity(.24);for(int i=0;i<24;i++)dc.DrawLine(new Pen(accent,1),new(w-218+i*7,20),new(w-218+i*7,i%4==0?29:24));dc.Pop();break;
            case "bauhaus":
                dc.PushOpacity(.22);dc.DrawRectangle(glow,null,new Rect(w-184,0,82,13));dc.DrawRectangle(accent,null,new Rect(w-102,0,102,13));dc.Pop();
                dc.PushOpacity(.075);dc.DrawEllipse(accent,null,new(w+4,36),96,96);dc.Pop();break;
        }
        dc.Pop();
    }
}

// Theme gallery previews are original vectors, not tinted screenshots. Their
// shapes and typography communicate each theme even before it is selected.
internal sealed class ThemePreview(LumaLyrics.Infrastructure.AppTheme theme) : FrameworkElement
{
    static Brush Brush(string color){var b=new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));b.Freeze();return b;}
    protected override void OnRender(DrawingContext dc)
    {
        double w=ActualWidth,h=ActualHeight;if(w<1 || h<1)return;
        var d=ThemeDesign.For(theme.Id);var text=Brush(theme.Text);var accent=Brush(theme.Accent);var rule=new Pen(Brush(theme.Border),.7);
        dc.PushClip(new RectangleGeometry(new Rect(0,0,w,h),8,8));dc.DrawRectangle(Brush(theme.WindowStart),null,new Rect(0,0,w,h));
        if(theme.Id=="studio")
        {dc.PushOpacity(.08);for(double x=0;x<w;x+=20)dc.DrawLine(rule,new(x,0),new(x,h));dc.Pop();}
        dc.DrawRoundedRectangle(Brush(theme.Raised),null,new Rect(8,9,23,h-18),Math.Min(12,d.Control.TopLeft),Math.Min(12,d.Control.TopLeft));
        for(int i=0;i<3;i++)dc.DrawRoundedRectangle(i==0?accent:Brush(theme.Control),null,new Rect(14,18+i*22,11,11),Math.Min(5,d.Control.TopLeft),Math.Min(5,d.Control.TopLeft));
        double cover=Math.Min(70,h-38);double x0=43,y0=23;
        dc.DrawRoundedRectangle(Brush(theme.Raised),rule,new Rect(x0-3,y0-3,cover+6,cover+6),d.ArtworkRadius*.22,d.ArtworkRadius*.22);
        dc.PushClip(new RectangleGeometry(new Rect(x0,y0,cover,cover),d.ArtworkRadius*.22,d.ArtworkRadius*.22));
        dc.DrawRectangle(Brush(theme.Glow),null,new Rect(x0,y0,cover,cover));
        dc.DrawEllipse(Brush(theme.GlowSecondary),null,new(x0+cover*.72,y0+cover*.30),cover*.5,cover*.5);
        dc.DrawRectangle(accent,null,new Rect(x0,y0+cover*.67,cover,cover*.33));dc.Pop();
        double px=x0+cover+13,pw=Math.Max(25,w-px-12);
        dc.DrawRoundedRectangle(Brush(theme.Panel),theme.Id is "studio" or "bauhaus"?rule:null,new Rect(px,12,pw,h-24),Math.Min(14,d.Panel.TopLeft),Math.Min(14,d.Panel.TopLeft));
        double offset=theme.Id=="paper"?0:10;
        if(theme.Id=="paper")dc.DrawLine(rule,new(px-4,12),new(px-4,h-12));
        var title=new FormattedText(theme.Id=="paper"?"Liner notes":"LUMA",CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface(new FontFamily(d.DisplayFont),FontStyles.Normal,d.Weight,FontStretches.Normal),theme.Id=="bauhaus"?15:12,text,VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(title,new(px+offset,21));
        for(int i=0;i<3;i++)dc.DrawRoundedRectangle(i==0?accent:Brush(theme.Control),null,new Rect(px+offset,48+i*11,Math.Max(14,pw-offset-14-i*6),i==0?4:3),theme.Id=="material"?2:0,theme.Id=="material"?2:0);
        if(theme.Id=="bauhaus"){dc.DrawRectangle(accent,null,new Rect(w-34,12,22,5));dc.DrawEllipse(Brush(theme.Glow),null,new(w-22,h-24),6,6);}
        dc.Pop();
    }
}

internal sealed class ArtworkFrame : System.Windows.Controls.Border
{
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo){base.OnRenderSizeChanged(sizeInfo);RefreshClip();}
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {base.OnPropertyChanged(e);if(e.Property==CornerRadiusProperty)RefreshClip();}
    void RefreshClip(){if(ActualWidth>0 && ActualHeight>0)Clip=new RectangleGeometry(new Rect(RenderSize),CornerRadius.TopLeft,CornerRadius.TopLeft);}
}

internal sealed class ArtworkImage : System.Windows.Controls.Image
{
    internal static readonly DependencyProperty CornersProperty=DependencyProperty.Register("Corners",typeof(CornerRadius),typeof(ArtworkImage),new PropertyMetadata(new CornerRadius(0),(o,_)=>((ArtworkImage)o).RefreshClip()));
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo){base.OnRenderSizeChanged(sizeInfo);RefreshClip();}
    void RefreshClip(){if(ActualWidth>0 && ActualHeight>0){var corners=(CornerRadius)GetValue(CornersProperty);Clip=new RectangleGeometry(new Rect(RenderSize),corners.TopLeft,corners.TopLeft);}}
}
