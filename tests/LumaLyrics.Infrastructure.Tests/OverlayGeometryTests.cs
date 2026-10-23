using System.Text.Json;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.Infrastructure.Tests;

public sealed class OverlayGeometryTests
{
    [Fact] public void CornerProportionSurvivesTallLyricsAndNarrowWindows()
    {
        var a=(new OverlayAppearance{CornerRatio=.8}).Normalize();
        Assert.Equal(48,a.ResolveCornerRadius(820,120),6);
        Assert.Equal(144,a.ResolveCornerRadius(820,360),6);
        Assert.Equal(144,a.ResolveCornerRadius(360,640),6);
        Assert.Equal(0,(a with{CornerRatio=0}).ResolveCornerRadius(820,360));
        Assert.Equal(180,(a with{CornerRatio=1}).ResolveCornerRadius(820,360));
    }
    [Fact] public void LegacyPixelsMigrateOnceAndSurviveSaveSwitchAndReload()
    {
        var settings=JsonSerializer.Deserialize<AppSettings>("""{"OverlayPreset":"液态玻璃","OverlayMaterial":"liquid","OverlayCornerRadius":24,"OverlayFontSize":32,"OverlayPadding":24,"OverlayTwoLines":true}""")!;
        var initial=OverlayAppearance.From(settings);
        double baseline=2*24*.65+32*1.4+8+32*.65*1.45;
        Assert.Equal(24,initial.ResolveCornerRadius(820,baseline),6);
        var edit=new OverlayThemeSession(settings);edit.Select(OverlayThemes.All[1]);edit.Select(OverlayThemes.All[0]);edit.Save(()=>{});
        var loaded=JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Equal(initial.CornerRatio,loaded.OverlayCornerRatio);
        Assert.Equal(initial.CornerRatio,loaded.OverlayThemeProfiles["液态玻璃"].CornerRatio);
        var larger=OverlayAppearance.From(loaded) with{FontSize=80,TwoLines=false};
        Assert.Equal(initial.CornerRatio,larger.Normalize().CornerRatio);
        Assert.Equal(initial.ResolveCornerRadius(820,400),larger.ResolveCornerRadius(820,400));
    }
    [Fact] public void InvalidGeometryCannotProduceNonFiniteOrOversizedClips()
    {
        var a=(new OverlayAppearance{Radius=999,CornerRatio=double.NaN}).Normalize();
        Assert.True(double.IsFinite(a.CornerRatio!.Value));Assert.InRange(a.CornerRatio.Value,0,1);
        foreach(var (w,h) in new[]{(double.NaN,100d),(100d,double.PositiveInfinity),(0d,100d),(-1d,100d)})Assert.Equal(0,a.ResolveCornerRadius(w,h));
        Assert.Equal(50,(a with{CornerRatio=9}).ResolveCornerRadius(100,200));
    }
    [Fact] public void BackgroundLayersEaseInAtSubPercentValues()
    {
        var a=OverlayThemes.All[0].Appearance;
        Assert.Equal(0,(a with{Opacity=0}).BackgroundEffectOpacity());
        Assert.InRange((a with{Opacity=.001}).BackgroundEffectOpacity(),0.00001,.001);
        Assert.InRange((a with{Opacity=.01}).BackgroundEffectOpacity(),.01,.04);
        double previous=0;
        for(int i=0;i<=100;i++)
        {
            double next=(a with{Opacity=i/1000d}).BackgroundEffectOpacity();
            Assert.InRange(next,previous,1);Assert.True(next-previous<.016);previous=next;
        }
        foreach(double opacity in new[]{.1,.16,.64,1})Assert.Equal(1,(a with{Opacity=opacity}).BackgroundEffectOpacity());
        Assert.Equal(0,(a with{Transparent=true,Opacity=1}).BackgroundEffectOpacity());
        Assert.Equal(0,(a with{Opacity=double.NaN}).BackgroundEffectOpacity());
    }
    [Fact] public void ThemeCancelRestoresRatioAndKeepsTranslationPreference()
    {
        var settings=new AppSettings();var original=(OverlayThemes.All[0].Appearance with{CornerRatio=.32}).Normalize();original.Apply(settings);
        var edit=new OverlayThemeSession(settings);edit.Update(edit.Current with{CornerRatio=.91});edit.Select(OverlayThemes.All[1]);
        edit.Update(edit.Current with{CornerRatio=.76});settings.OverlayShowChineseTranslation=true;edit.Cancel();
        Assert.Equal(original,OverlayAppearance.From(settings));Assert.True(settings.OverlayShowChineseTranslation);
    }
}
