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
    public static (OverlayBounds Functions,OverlayBounds Playback) Controls(OverlayBounds surface,OverlayBounds functions,OverlayBounds playback,OverlayBounds area,double gap=6)
    {
        double x=surface.Left+(surface.Width-playback.Width)/2;
        var below=playback with{Left=x,Top=surface.Bottom+gap};
        var p=Contain(below.Bottom<=area.Bottom?below:playback with{Left=x,Top=surface.Top-playback.Height-gap},area);
        double preferred=surface.Top-functions.Height-gap;
        if(p.Top<surface.Top)preferred=p.Top-functions.Height-gap;
        var candidates=new[]{preferred,p.Bottom+gap,p.Top-functions.Height-gap,area.Bottom-functions.Height,area.Top}
            .Select(top=>Contain(functions with{Left=surface.Right-functions.Width,Top=top},area)).ToArray();
        foreach(var f in candidates)if(!f.Overlaps(p)&&!f.Overlaps(surface))return(f,p);
        // A surface filling the monitor leaves no room outside it. Keep controls
        // accessible and separate even when they must cover part of that surface.
        foreach(var f in candidates)if(!f.Overlaps(p))return(f,p);
        return(Contain(functions with{Left=surface.Left,Top=area.Top},area),p);
    }
}
