using LumaLyrics.Infrastructure;
namespace LumaLyrics.Infrastructure.Tests;

public sealed class AppThemeTests
{
    [Fact] public void OldSettingsUseLiquidGlassWithoutChangingOverlay()
    {
        var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{\"OverlayPreset\":\"暖纸留白\",\"OverlayFontSize\":44}")!;
        Assert.Equal("liquid", settings.UiTheme);
        Assert.Equal("暖纸留白", settings.OverlayPreset);
        Assert.Equal(44, settings.OverlayFontSize);
        Assert.True(settings.ShowPlaybackQueue);
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("future-theme")]
    public void UnknownThemesResolveToUsableDefault(string? id) => Assert.Equal("liquid", AppThemes.Find(id).Id);

    [Fact] public void ThemeSelectionSurvivesRestartAndDoesNotChangeLyricAppearance()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LumaLyrics-ui-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SettingsStore(directory);
            var settings = new AppSettings { GlobalOffsetMs = 450, OnlineLookup = false };
            OverlayThemes.All[5].Appearance.Apply(settings);
            var overlay = OverlayAppearance.From(settings);
            foreach (var theme in AppThemes.All)
            {
                settings.UiTheme = theme.Id; store.Save(settings);
                var loaded = store.Load();
                Assert.Equal(theme, AppThemes.Find(loaded.UiTheme));
                Assert.Equal(overlay, OverlayAppearance.From(loaded));
                Assert.Equal(450, loaded.GlobalOffsetMs); Assert.False(loaded.OnlineLookup);
            }
        }
        finally { Directory.Delete(directory, true); }
    }
    [Theory]
    [InlineData("liquid")] [InlineData("frost")] [InlineData("material")]
    [InlineData("paper")] [InlineData("studio")] [InlineData("bauhaus")]
    public void EveryFloatingPresetAndPreviewRollbackPreserveApplicationTheme(string applicationTheme)
    {
        var settings=new AppSettings{UiTheme=applicationTheme,GlobalOffsetMs=450};
        var original=OverlayAppearance.From(settings);
        foreach(var preset in OverlayThemes.All)
        {
            preset.Appearance.Apply(settings);
            var loaded=System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(settings))!;
            Assert.Equal(applicationTheme,loaded.UiTheme);
            Assert.Equal(preset.Appearance.Normalize(),OverlayAppearance.From(loaded));
            original.Apply(settings);
            Assert.Equal(applicationTheme,settings.UiTheme);
            Assert.Equal(original,OverlayAppearance.From(settings));
            Assert.Equal(450,settings.GlobalOffsetMs);
        }
    }
}
