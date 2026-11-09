using LumaLyrics.Infrastructure;
namespace LumaLyrics.Infrastructure.Tests;
public sealed class OverlayScreenLayoutTests
{
    [Theory]
    [InlineData(-500,-200,900,200,0,0,800,600)]
    [InlineData(700,590,900,200,0,0,800,600)]
    [InlineData(-2200,-900,1800,1200,-1920,-200,1600,900)]
    public void ConstrainsOversizedAndOffscreenWindows(double x,double y,double w,double h,double left,double top,double width,double height)
    {
        var area=new OverlayBounds(left,top,width,height);var result=OverlayScreenLayout.Contain(new(x,y,w,h),area);
        Assert.True(result.Left>=area.Left&&result.Top>=area.Top&&result.Right<=area.Right&&result.Bottom<=area.Bottom);
        Assert.Equal(Math.Min(w,width),result.Width);Assert.Equal(Math.Min(h,height),result.Height);
    }
    [Fact]public void CenterUsesCurrentNegativeCoordinateMonitor()
    {
        var area=new OverlayBounds(-1920,-200,1920,1040);var box=new OverlayBounds(-1700,460,820,110);
        var centered=box with{Left=OverlayScreenLayout.CenteredLeft(box.Width,area)};
        Assert.Equal(-1370,centered.Left);Assert.Equal(box.Top,centered.Top);Assert.Equal(box.Height,centered.Height);
    }
    [Theory]
    [InlineData(200,200,900,120)]
    [InlineData(200,870,900,120)]
    [InlineData(0,0,1200,1000)]
    [InlineData(0,0,360,100)]
    [InlineData(0,900,360,100)]
    public void PopupsStayInsideWorkAreaWithoutOverlapping(double x,double y,double width,double height)
    {
        var area=new OverlayBounds(0,0,1200,1000);var surface=new OverlayBounds(x,y,width,height);
        var (f,p)=OverlayScreenLayout.Controls(surface,new(0,0,520,44),new(0,0,154,56),area);
        foreach(var box in new[]{f,p})Assert.True(box.Left>=area.Left&&box.Top>=area.Top&&box.Right<=area.Right&&box.Bottom<=area.Bottom);
        Assert.False(f.Overlaps(p));
        if(surface.Top==area.Top&&surface.Height<area.Height/2)Assert.False(f.Overlaps(surface));
        if(surface.Bottom+62<=area.Bottom)Assert.Equal(surface.Bottom+6,p.Top);
        else if(surface.Top>=62)Assert.Equal(surface.Top-62,p.Top);
    }
}
