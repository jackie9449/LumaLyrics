using LumaLyrics.Infrastructure;
namespace LumaLyrics.Infrastructure.Tests;
public sealed class AppearanceTests
{
    [Fact] public void PreviewRollbackPreservesUnrelatedLibraryAndPlaybackChanges()
    {
        var settings=new AppSettings{GlobalOffsetMs=200,OnlineLookup=true};var before=OverlayAppearance.From(settings);
        OverlayThemes.All[0].Appearance.Apply(settings);settings.OnlineLookup=false;settings.GlobalOffsetMs=700;settings.LibraryPath="custom.musicdb";
        before.Apply(settings);Assert.Equal(before,OverlayAppearance.From(settings));Assert.False(settings.OnlineLookup);Assert.Equal(700,settings.GlobalOffsetMs);Assert.Equal("custom.musicdb",settings.LibraryPath);
    }
    [Fact] public void OldSettingsRetainFontAndTransparentPreset()
    {
        var s=System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{\"OverlayPreset\":\"极简透明\",\"OverlayFontSize\":44,\"OverlayFont\":\"KaiTi\"}")!;
        var a=OverlayAppearance.From(s);Assert.True(a.Transparent);Assert.Equal(44,a.FontSize);Assert.Equal("KaiTi",a.Font);a.Apply(s);Assert.Equal(a,OverlayAppearance.From(s));
    }
    [Fact] public void InvalidSettingsNormalizeWithoutInvalidLayoutOrBrushes()
    {
        var a=new OverlayAppearance{FontSize=double.NaN,Width=double.PositiveInfinity,Opacity=-10,Radius=300,NextScale=double.NaN,Color="not a color",Font="",Material="bad",Alignment="oops",Weight="bad"}.Normalize();
        Assert.Equal(32,a.FontSize);Assert.Equal(820,a.Width);Assert.Equal(0,a.Opacity);Assert.Equal(64,a.Radius);Assert.Equal(.65,a.NextScale);Assert.Equal("#F5F7FA",a.Color);Assert.Equal("card",a.Material);Assert.Equal("Center",a.Alignment);Assert.Equal("SemiBold",a.Weight);
    }
    [Fact] public void EveryThemeSurvivesLocalSaveAndReload()
    {
        string path=Path.Combine(Path.GetTempPath(),"LumaLyrics-appearance-"+Guid.NewGuid().ToString("N"));
        try {var store=new SettingsStore(path);Assert.Equal(8,OverlayThemes.All.Select(t=>t.Name).Distinct().Count());foreach(var theme in OverlayThemes.All){var s=new AppSettings();theme.Appearance.Apply(s);store.Save(s);Assert.Equal(theme.Appearance.Normalize(),OverlayAppearance.From(store.Load()));}}
        finally{Directory.Delete(path,true);}
    }
    [Fact] public void BackgroundTransparencyDoesNotChangeTextOpacity()
    {
        var initial=OverlayThemes.All[2].Appearance;var transparent=initial with{Transparent=true,Opacity=0};var settings=new AppSettings();transparent.Apply(settings);
        Assert.Equal(initial.Color,settings.OverlayColor);Assert.Equal(initial.NextOpacity,settings.OverlayNextOpacity);Assert.False(settings.OverlayShadow);Assert.True(settings.OverlayTransparent);
    }
}
