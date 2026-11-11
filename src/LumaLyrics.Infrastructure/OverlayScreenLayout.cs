namespace LumaLyrics.Infrastructure;

public readonly record struct OverlayBounds(double Left,double Top,double Width,double Height)
{
    public double Right=>Left+Width;
    public double Bottom=>Top+Height;
    public bool Overlaps(OverlayBounds other)=>Left<other.Right&&Right>other.Left&&Top<other.Bottom&&Bottom>other.Top;
}
public static class OverlayScreenLayout
{
    public static OverlayBounds Contain(OverlayBounds requested,OverlayBounds area)
    {
        if(area.Width<=0||area.Height<=0)throw new ArgumentOutOfRangeException(nameof(area));
        double width=Math.Clamp(requested.Width,1,area.Width),height=Math.Clamp(requested.Height,1,area.Height);
        return new(Math.Clamp(requested.Left,area.Left,area.Right-width),Math.Clamp(requested.Top,area.Top,area.Bottom-height),width,height);
    }
    public static double CenteredLeft(double width,OverlayBounds area)=>area.Left+(area.Width-Math.Clamp(width,1,area.Width))/2;
    public static OverlayBounds Toolbar(OverlayBounds surface,OverlayBounds toolbar,OverlayBounds area,double gap=6)
    {
        double width=Math.Min(toolbar.Width,surface.Width);
        double left=surface.Left+(surface.Width-width)/2;
        double top=surface.Top-toolbar.Height-gap;
        if(top<area.Top)top=surface.Bottom+gap;
        return Contain(toolbar with{Left=left,Top=top,Width=width},area);
    }
}
