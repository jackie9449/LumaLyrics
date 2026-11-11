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
    [InlineData(0,0,360,100)]
    [InlineData(0,900,360,100)]
    [InlineData(840,0,360,100)]
    [InlineData(840,900,360,100)]
    [InlineData(100,400,820,100)]
    [InlineData(0,0,1200,1000)]
    public void UnifiedToolbarIsContainedAndCentered(double x,double y,double width,double height)
    {
        var area=new OverlayBounds(0,0,1200,1000);
        var surface=new OverlayBounds(x,y,width,height);
        var result=OverlayScreenLayout.Toolbar(surface,new(0,0,Math.Min(720,width),110),area);
        Assert.True(result.Left>=area.Left&&result.Top>=area.Top&&result.Right<=area.Right&&result.Bottom<=area.Bottom);
        Assert.Equal(surface.Left+surface.Width/2,result.Left+result.Width/2);
        if(y>=116)Assert.Equal(y-116,result.Top);
        else if(surface.Bottom+116<=area.Bottom)Assert.Equal(surface.Bottom+6,result.Top);
    }
}
