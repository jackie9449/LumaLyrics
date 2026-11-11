using LumaLyrics.Infrastructure;

namespace LumaLyrics.Infrastructure.Tests;

public sealed class ChineseScriptSettingsTests : IDisposable
{
    readonly string directory = Path.Combine(Path.GetTempPath(), "LumaLyrics-script-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void NewProfileKeepsOriginalChineseScript()
    {
        var settings = new SettingsStore(directory).Load();
        Assert.Equal("original", settings.ChineseScript);
        Assert.False(settings.ShowChineseTranslation);
        Assert.False(settings.OverlayShowChineseTranslation);
    }

    [Theory]
    [InlineData("simplified", true, false)]
    [InlineData("traditional", false, true)]
    [InlineData("original", true, true)]
    public void ScriptChoiceSurvivesRestartWithoutChangingVisibilityOrPlaybackSettings(string script, bool main, bool floating)
    {
        var store = new SettingsStore(directory);
        store.Save(new AppSettings
        {
            ChineseScript = script,
            ShowChineseTranslation = main,
            OverlayShowChineseTranslation = floating,
            UiTheme = "frost",
            GlobalOffsetMs = -325,
            OverlayColor = "#ABCDEF",
            OnlineLookup = false
        });

        var reloaded = new SettingsStore(directory).Load();
        Assert.Equal(script, reloaded.ChineseScript);
        Assert.Equal(main, reloaded.ShowChineseTranslation);
        Assert.Equal(floating, reloaded.OverlayShowChineseTranslation);
        Assert.Equal("frost", reloaded.UiTheme);
        Assert.Equal(-325, reloaded.GlobalOffsetMs);
        Assert.Equal("#ABCDEF", reloaded.OverlayColor);
        Assert.False(reloaded.OnlineLookup);
    }

    [Fact]
    public void LegacyProfileUsesOriginalScriptAndPreservesExistingPreferences()
    {
        var store = new SettingsStore(directory);
        File.WriteAllText(Path.Combine(directory, "settings.json"), """
            {
              "ShowChineseTranslation": true,
              "OverlayShowChineseTranslation": false,
              "UiTheme": "frost",
              "GlobalOffsetMs": 450,
              "OverlayFontSize": 38,
              "OnlineLookup": false
            }
            """);

        var settings = store.Load();
        Assert.Equal("original", settings.ChineseScript);
        Assert.True(settings.ShowChineseTranslation);
        Assert.False(settings.OverlayShowChineseTranslation);
        Assert.Equal("frost", settings.UiTheme);
        Assert.Equal(450, settings.GlobalOffsetMs);
        Assert.Equal(38, settings.OverlayFontSize);
        Assert.False(settings.OnlineLookup);

        store.Save(settings);
        var reloaded = new SettingsStore(directory).Load();
        Assert.Equal("original", reloaded.ChineseScript);
        Assert.True(reloaded.ShowChineseTranslation);
        Assert.False(reloaded.OverlayShowChineseTranslation);
        Assert.Equal(450, reloaded.GlobalOffsetMs);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
