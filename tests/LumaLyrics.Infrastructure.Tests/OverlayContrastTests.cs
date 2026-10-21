using LumaLyrics.Infrastructure;
namespace LumaLyrics.Infrastructure.Tests;

public sealed class OverlayContrastTests
{
    [Fact] public void HysteresisAvoidsFlippingOnMediumBackgrounds()
    {
        Assert.True(OverlayContrast.UseDarkText(.9,false));Assert.False(OverlayContrast.UseDarkText(.2,true));
        Assert.False(OverlayContrast.UseDarkText(.6,false));Assert.True(OverlayContrast.UseDarkText(.6,true));
        Assert.True(OverlayContrast.UseDarkText(double.NaN,true));Assert.False(OverlayContrast.UseDarkText(double.NaN,false));
    }
    [Fact] public void AdaptivePaintKeepsUserPreferencesAndLayoutUnmodified()
    {
        var chosen=OverlayThemes.All[0].Appearance with{Font="Consolas",Width=620};var dark=OverlayContrast.Paint(chosen,true);
        Assert.Equal("#202832",dark.Color);Assert.False(dark.Shadow);Assert.Equal(chosen.Font,dark.Font);Assert.Equal(620,dark.Width);
        Assert.Equal("#F7F9FC",chosen.Color);Assert.True(chosen.Shadow);Assert.True(chosen.AdaptiveContrast);
    }
    [Fact] public void FixedColorsOtherMaterialsAndTransparentModeAreRespected()
    {
        var liquid=OverlayThemes.All[0].Appearance;
        foreach(var appearance in new[]{liquid with{AdaptiveContrast=false,Color="#ABCDEF"},liquid with{Transparent=true},OverlayThemes.All[1].Appearance})Assert.Equal(appearance,OverlayContrast.Paint(appearance,true));
    }
}
