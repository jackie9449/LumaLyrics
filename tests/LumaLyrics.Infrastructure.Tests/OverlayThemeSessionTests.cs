using System.Text.Json;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.Infrastructure.Tests;

public sealed class OverlayThemeSessionTests
{
    static OverlayAppearance Custom(OverlayAppearance a, int n) => a with
    {
        Font = "Test Font " + n, FontSize = 35 + n, Weight = "Bold", Alignment = n % 2 == 0 ? "Left" : "Right",
        Color = "#D9B8FA", Background = "#202B40", Accent = "#B8DFFF", Opacity = .31 + n * .03,
        Width = 860 + n * 20, Radius = 15 + n, Padding = 17 + n, LineGap = 3 + n,
        NextScale = .55, NextOpacity = .45, ShadowStrength = .35, GlassIntensity = .4 + n * .02,
        TwoLines = n % 2 == 0, Shadow = false, AdaptiveContrast = false, Transparent = n == 7,
        SystemBlur = false, Border = false, Animations = false, TransitionMs = 300 + n * 10
    };

    [Fact] public void SwitchingRestoresEveryFieldOfEachVisitedTheme()
    {
        var settings = new AppSettings(); var edit = new OverlayThemeSession(settings);
        var expected = new Dictionary<string, OverlayAppearance>();
        foreach (var (theme, index) in OverlayThemes.All.Select((t, i) => (t, i)))
        {
            edit.Select(theme); edit.Update(Custom(edit.Current, index)); expected[theme.Name] = edit.Current;
        }
        foreach (var theme in OverlayThemes.All.Reverse())
        {
            edit.Select(theme); Assert.Equal(expected[theme.Name], edit.Current);
            Assert.Equal(expected[theme.Name], OverlayAppearance.From(settings));
        }
        Assert.Empty(settings.OverlayThemeProfiles);
    }

    [Fact] public void SavingAndRestartingRestoresAllThemeProfilesFromDisk()
    {
        string path = Path.Combine(Path.GetTempPath(), "LumaLyrics-theme-profiles-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SettingsStore(path); var settings = new AppSettings(); var edit = new OverlayThemeSession(settings);
            var expected = new Dictionary<string, OverlayAppearance>();
            foreach (var (theme, index) in OverlayThemes.All.Select((t, i) => (t, i)))
            {
                edit.Select(theme); edit.Update(Custom(edit.Current, index)); expected[theme.Name] = edit.Current;
            }
            edit.Save(() => store.Save(settings));
            var reloaded = new SettingsStore(path).Load(); var restarted = new OverlayThemeSession(reloaded);
            Assert.Equal(expected.Count, reloaded.OverlayThemeProfiles.Count);
            foreach (var theme in OverlayThemes.All) { restarted.Select(theme); Assert.Equal(expected[theme.Name], restarted.Current); }
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }

    [Fact] public void LegacyActiveCustomizationBecomesTheInitialThemeDraft()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("""{"OverlayPreset":"液态玻璃","OverlayMaterial":"liquid","OverlayFont":"KaiTi","OverlayFontSize":44,"OverlayCornerRadius":41,"OverlayBackgroundOpacity":0.27,"OverlayColor":"#B8DFFF"}""")!;
        var before = OverlayAppearance.From(settings); var edit = new OverlayThemeSession(settings);
        edit.Select(OverlayThemes.All[1]); edit.Select(OverlayThemes.All[0]);
        Assert.Equal(before, edit.Current); edit.Save(() => { });
        Assert.Equal(before, settings.OverlayThemeProfiles["液态玻璃"]);
    }

    [Fact] public void ActiveFlatSettingsTakePrecedenceOverAnOlderSavedProfile()
    {
        var settings = new AppSettings(); var theme = OverlayThemes.All[0];
        settings.OverlayThemeProfiles[theme.Name] = theme.Appearance;
        var newer = Custom(theme.Appearance, 2); newer.Apply(settings);
        var edit = new OverlayThemeSession(settings); edit.Select(OverlayThemes.All[1]); edit.Select(theme);
        Assert.Equal(newer, edit.Current);
    }

    [Fact] public void DirectOverlayResizingIsRememberedWhenSwitchingAndSaving()
    {
        var settings = new AppSettings(); var edit = new OverlayThemeSession(settings);
        edit.Select(OverlayThemes.All[0]); settings.OverlayWidth = 1040;
        edit.Select(OverlayThemes.All[1]); settings.OverlayWidth = 760;
        edit.Select(OverlayThemes.All[0]); Assert.Equal(1040, edit.Current.Width);
        settings.OverlayWidth = 1120; edit.Save(() => { });
        Assert.Equal(1120, settings.OverlayThemeProfiles["液态玻璃"].Width);
        Assert.Equal(760, settings.OverlayThemeProfiles["雾面毛玻璃"].Width);
    }

    [Fact] public void CancelDiscardsAllThemeDraftsAndPreservesUnrelatedSettings()
    {
        var settings = new AppSettings(); var liquid = Custom(OverlayThemes.All[0].Appearance, 0);
        settings.OverlayThemeProfiles[liquid.Preset] = liquid;
        var savedProfiles = settings.OverlayThemeProfiles; var before = OverlayAppearance.From(settings);
        var edit = new OverlayThemeSession(settings);
        foreach (var theme in OverlayThemes.All) { edit.Select(theme); edit.Update(Custom(edit.Current, 3)); }
        settings.UiTheme = "frost"; settings.GlobalOffsetMs = 750; settings.LibraryPath = "other.musicdb";
        settings.OverlayLeft = 120; settings.OverlayVisible = false;
        edit.Cancel();
        Assert.Equal(before, OverlayAppearance.From(settings)); Assert.Same(savedProfiles, settings.OverlayThemeProfiles);
        Assert.Equal(liquid, settings.OverlayThemeProfiles[liquid.Preset]); Assert.Single(settings.OverlayThemeProfiles);
        Assert.Equal("frost", settings.UiTheme); Assert.Equal(750, settings.GlobalOffsetMs);
        Assert.Equal("other.musicdb", settings.LibraryPath); Assert.Equal(120, settings.OverlayLeft); Assert.False(settings.OverlayVisible);
        var reopened = new OverlayThemeSession(settings); reopened.Select(OverlayThemes.All[0]); Assert.Equal(liquid, reopened.Current);
        reopened.Select(OverlayThemes.All[1]); Assert.Equal(OverlayThemes.All[1].Appearance.Opacity, reopened.Current.Opacity);
    }

    [Fact] public void ResetOnlyReplacesCurrentMaterialAndRetainsTypography()
    {
        var settings = new AppSettings(); var edit = new OverlayThemeSession(settings);
        edit.Select(OverlayThemes.All[0]); edit.Update(Custom(edit.Current, 0)); var liquid = edit.Current;
        edit.Select(OverlayThemes.All[1]); edit.Update(Custom(edit.Current, 1)); var frost = edit.Current;
        edit.ResetCurrent(); var reset = edit.Current;
        Assert.Equal(OverlayThemes.All[1].Appearance.Background, reset.Background);
        Assert.Equal(OverlayThemes.All[1].Appearance.Opacity, reset.Opacity);
        Assert.Equal(OverlayThemes.All[1].Appearance.Radius, reset.Radius);
        Assert.Equal(frost.Font, reset.Font); Assert.Equal(frost.FontSize, reset.FontSize); Assert.Equal(frost.Width, reset.Width);
        edit.Select(OverlayThemes.All[0]); Assert.Equal(liquid, edit.Current);
        edit.Select(OverlayThemes.All[1]); Assert.Equal(reset, edit.Current);
        edit.Cancel(); Assert.Empty(settings.OverlayThemeProfiles);
    }

    [Fact] public void FirstVisitInheritsTypographyButReturningRestoresThatThemesOwnTypography()
    {
        var settings = new AppSettings(); var edit = new OverlayThemeSession(settings);
        edit.Select(OverlayThemes.All[0]); edit.Update(Custom(edit.Current, 2)); var liquid = edit.Current;
        edit.Select(OverlayThemes.All[1]);
        Assert.Equal(liquid.Font, edit.Current.Font); Assert.Equal(liquid.FontSize, edit.Current.FontSize);
        Assert.Equal(liquid.Alignment, edit.Current.Alignment); Assert.Equal(liquid.Width, edit.Current.Width);
        Assert.Equal(OverlayThemes.All[1].Appearance.Opacity, edit.Current.Opacity);
        edit.Update(Custom(edit.Current, 5)); edit.Select(OverlayThemes.All[0]); Assert.Equal(liquid, edit.Current);
        edit.Select(OverlayThemes.All[0]); Assert.Equal(liquid, edit.Current);
    }

    [Fact] public void FailedSaveRestoresSavedProfilesAndCanBeRetriedOrCancelled()
    {
        var settings = new AppSettings(); var savedProfiles = settings.OverlayThemeProfiles;
        var before = OverlayAppearance.From(settings); var edit = new OverlayThemeSession(settings);
        edit.Select(OverlayThemes.All[0]); edit.Update(Custom(edit.Current, 2)); var changed = edit.Current;
        Assert.Throws<IOException>(() => edit.Save(() => throw new IOException("test write failure")));
        Assert.Same(savedProfiles, settings.OverlayThemeProfiles); Assert.Empty(savedProfiles); Assert.Equal(changed, edit.Current);
        edit.Cancel(); Assert.Equal(before, OverlayAppearance.From(settings));
        var retry = new OverlayThemeSession(settings); retry.Select(OverlayThemes.All[0]); retry.Update(changed); retry.Save(() => { });
        Assert.Equal(changed, settings.OverlayThemeProfiles[changed.Preset]); Assert.Empty(savedProfiles);
    }

    [Fact] public void NullLegacyAndInvalidProfileValuesDoNotBreakSelection()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("""{"OverlayThemeProfiles":{"雾面毛玻璃":{"Preset":"wrong","Opacity":-1,"Radius":999,"Color":"bad"},"极简透明":{"Transparent":true},"液态玻璃":null}}""")!;
        var edit = new OverlayThemeSession(settings); edit.Select(OverlayThemes.All[1]);
        Assert.Equal("雾面毛玻璃", edit.Current.Preset); Assert.Equal(0, edit.Current.Opacity);
        Assert.Equal(64, edit.Current.Radius); Assert.Equal("#F5F7FA", edit.Current.Color);
        edit.Select(OverlayThemes.All[7]); Assert.True(edit.Current.Transparent); Assert.Equal("纯净透明", edit.Current.Preset);
        edit.Select(OverlayThemes.All[0]); Assert.Equal(OverlayThemes.All[0].Appearance.Opacity, edit.Current.Opacity);
        var missing = JsonSerializer.Deserialize<AppSettings>("""{"OverlayThemeProfiles":null}""")!;
        var fallback = new OverlayThemeSession(missing); fallback.Select(OverlayThemes.All[0]); fallback.Save(() => { });
        Assert.Equal(2, missing.OverlayThemeProfiles.Count);
    }

}
