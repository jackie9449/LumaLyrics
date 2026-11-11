using LumaLyrics.Infrastructure;
namespace LumaLyrics.Infrastructure.Tests;

public sealed class InterfaceLanguageSettingsTests : IDisposable
{
    readonly string directory=Path.Combine(Path.GetTempPath(),"LumaLyrics-language-"+Guid.NewGuid().ToString("N"));
    [Theory]
    [InlineData("zh-Hans")][InlineData("zh-Hant")][InlineData("en")][InlineData("ja")]
    public void InterfaceLanguageSurvivesRestartIndependentlyOfLyricsAndAppearance(string language)
    {
        new SettingsStore(directory).Save(new(){UiLanguage=language,ChineseScript="traditional",ShowChineseTranslation=false,OverlayShowChineseTranslation=true,OverlayFontSize=39,UiTheme="paper",GlobalOffsetMs=175});
        var restored=new SettingsStore(directory).Load();
        Assert.Equal(language,restored.UiLanguage);Assert.Equal("traditional",restored.ChineseScript);
        Assert.False(restored.ShowChineseTranslation);Assert.True(restored.OverlayShowChineseTranslation);
        Assert.Equal(39,restored.OverlayFontSize);Assert.Equal("paper",restored.UiTheme);Assert.Equal(175,restored.GlobalOffsetMs);
    }
    [Fact]
    public void ExistingProfilesKeepSimplifiedInterfaceWithoutResettingLyricPreference()
    {
        var store=new SettingsStore(directory);File.WriteAllText(Path.Combine(directory,"settings.json"),"{\"ChineseScript\":\"traditional\",\"GlobalOffsetMs\":325}");
        var settings=store.Load();Assert.Equal("zh-Hans",settings.UiLanguage);Assert.Equal("traditional",settings.ChineseScript);Assert.Equal(325,settings.GlobalOffsetMs);
    }
    public void Dispose(){if(Directory.Exists(directory))Directory.Delete(directory,true);}
}
