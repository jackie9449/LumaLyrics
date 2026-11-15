using System.Text.Json;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.Infrastructure.Tests;

public sealed class LanguageFontTests
{
    [Fact] public void LegacyFontMigratesToSystemLanguageWithoutLosingUserChoice()
    {
        var settings=JsonSerializer.Deserialize<AppSettings>("{\"OverlayFont\":\"KaiTi\"}")!;
        var a=OverlayAppearance.From(settings);
        Assert.Equal("KaiTi",a.Font);Assert.Equal(LyricFontLanguages.SystemLanguage,a.FontLanguage);Assert.Empty(a.LanguageFonts.Fonts);
        a.Apply(settings);Assert.Equal(a,OverlayAppearance.From(JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!));
    }
    [Fact] public void NormalizeRejectsInvalidAndDuplicateLanguagesAndUnsafeFamilyReferences()
    {
        var a=new OverlayAppearance{FontLanguage="en-US",LanguageFonts=new LanguageFontPreferences().Set("en","Arial").Set("unknown","X").Set("fr-FR","Arial").Set("ja","https://invalid/font").Set("zh-TW","MingLiU")}.Normalize();
        Assert.Equal("en",a.FontLanguage);Assert.Equal(2,a.LanguageFonts.Fonts.Count);Assert.Equal("Arial",a.LanguageFonts.Fonts["fr"]);Assert.Equal("MingLiU",a.LanguageFonts.Fonts["zh-Hant"]);
    }
    [Fact] public void ThemeDraftsAreImmutableAndCancelRestoresEveryFont()
    {
        var settings=new AppSettings{OverlayFontLanguage="zh-Hans",OverlayLanguageFonts=new LanguageFontPreferences().Set("en","Arial")};
        var before=OverlayAppearance.From(settings);var edit=new OverlayThemeSession(settings);
        edit.Update(edit.Current with{LanguageFonts=edit.Current.LanguageFonts.Set("fr","Georgia")});
        edit.Select(OverlayThemes.All[1]);edit.Update(edit.Current with{LanguageFonts=edit.Current.LanguageFonts.Set("en","Verdana")});
        Assert.Equal("Arial",before.LanguageFonts.Fonts["en"]);Assert.False(before.LanguageFonts.Fonts.ContainsKey("fr"));
        edit.Cancel();Assert.Equal(before,OverlayAppearance.From(settings));Assert.Empty(settings.OverlayThemeProfiles);
    }
    [Fact] public void SaveAndReloadRetainsIndependentThemeFonts()
    {
        var settings=new AppSettings{OverlayFontLanguage="en"};var edit=new OverlayThemeSession(settings);
        edit.Update(edit.Current with{LanguageFonts=edit.Current.LanguageFonts.Set("ja","Yu Gothic")});string first=edit.Current.Preset;
        edit.Select(OverlayThemes.All[0]);edit.Update(edit.Current with{LanguageFonts=edit.Current.LanguageFonts.Set("ja","Meiryo")});edit.Save(()=>{});
        var loaded=JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Equal("Yu Gothic",loaded.OverlayThemeProfiles[first].LanguageFonts.Fonts["ja"]);
        Assert.Equal("Meiryo",loaded.OverlayThemeProfiles[OverlayThemes.All[0].Name].LanguageFonts.Fonts["ja"]);
        Assert.Equal(OverlayAppearance.From(settings),OverlayAppearance.From(loaded));
    }
    [Fact] public void AdditionalRowsKeepUserInsertionOrderAcrossReload()
    {
        var a=new OverlayAppearance{FontLanguage="zh-Hans",LanguageFonts=new LanguageFontPreferences().Set("ja","Meiryo").Set("fr","Arial").Set("en","Georgia")}.Normalize();
        var restored=JsonSerializer.Deserialize<OverlayAppearance>(JsonSerializer.Serialize(a))!.Normalize();
        Assert.Equal(new[]{"ja","fr","en"},restored.LanguageFonts.Languages);
        Assert.Equal(a,restored);Assert.Equal(a.GetHashCode(),restored.GetHashCode());
        var changed=restored.LanguageFonts.Remove("fr").Set("fr","Verdana");Assert.Equal(new[]{"ja","en","fr"},changed.Languages);
    }
    [Theory][InlineData("zh-TW","zh-Hant")][InlineData("zh-HK","zh-Hant")][InlineData("zh-CN","zh-Hans")][InlineData("es-MX","es")][InlineData("nb-NO","no")]
    public void RegionalTagsHaveStableRows(string input,string expected)=>Assert.Equal(expected,LyricFontLanguages.NormalizeTag(input));
    [Fact] public void CatalogHasAllRequestedLanguagesAndNoDuplicateTags()
    {
        foreach(string tag in new[]{"zh-Hans","zh-Hant","en","ja","fr","es","de","ko"})Assert.Contains(LyricFontLanguages.All,l=>l.Tag==tag);
        Assert.Equal(LyricFontLanguages.All.Count,LyricFontLanguages.All.Select(l=>l.Tag).Distinct().Count());
    }
}
