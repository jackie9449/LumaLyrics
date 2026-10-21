using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

public partial class MainWindow
{
    async Task VerifyThemeProfilesAsync(string directory, Dictionary<string, object?> checks)
    {
        var original = OverlayAppearance.From(settings); var originalProfiles = settings.OverlayThemeProfiles;
        AppearanceWindow? restarted = null;
        async Task Settle() { await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); await Task.Delay(80); }
        static void Select(AppearanceWindow editor, OverlayTheme theme)
        {
            var list = Descendants<ListBox>(editor).Single(x => x.Items.Cast<object>().Any(i => i is ListBoxItem { Tag: OverlayTheme }));
            list.SelectedItem = list.Items.Cast<ListBoxItem>().Single(i => ((OverlayTheme)i.Tag).Name == theme.Name);
        }
        static Slider Slider(AppearanceWindow editor, string name) => Descendants<Slider>(editor).Single(s => AutomationProperties.GetName(s) == name);
        static TextBox Color(AppearanceWindow editor, string name) => Descendants<TextBox>(editor).Single(s => AutomationProperties.GetName(s) == name);
        static void Customize(AppearanceWindow editor, int index, double size, double radius, double opacity, double width, string background)
        {
            Descendants<TabControl>(editor).Single().SelectedIndex = 0; editor.UpdateLayout();
            editor.FontPicker.SelectedIndex = index; Slider(editor, "主歌词字号").Value = size;
            Color(editor, "文字颜色").Text = index == 1 ? "#493D34" : "#D9B8FA";
            editor.SelectLayoutTab(); editor.UpdateLayout();
            Slider(editor, "圆角").Value = radius; Slider(editor, "背景不透明度").Value = opacity;
            Slider(editor, "桌面歌词宽度").Value = width; Color(editor, "背景底色").Text = background;
        }
        static void Reset(AppearanceWindow editor) => Descendants<Button>(editor).Single(b => b.Content as string == "重置当前主题").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            settings.OverlayThemeProfiles = [];
            var legacy = OverlayThemes.All[0].Appearance with { Radius = 37, Opacity = .29, FontSize = 43, Background = "#203040" };
            legacy.Apply(settings); ShowAppearance(this); await Settle(); var editor = appearanceWindow!;
            Select(editor, OverlayThemes.All[1]); Select(editor, OverlayThemes.All[0]);
            checks["themeMemory.legacyActiveCustomizationRestored"] = OverlayAppearance.From(settings) == legacy;
            Customize(editor, 0, 43, 37, .29, 980, "#203040");
            overlay.Width = 1020; overlay.UpdateLayout(); await Settle();
            checks["themeMemory.directResizeUpdatesLiveWidth"] = settings.OverlayWidth == 1020;
            var liquid = OverlayAppearance.From(settings);
            Select(editor, OverlayThemes.All[1]); Customize(editor, 1, 34, 18, .71, 740, "#E6DDD0"); var frost = OverlayAppearance.From(settings);
            Select(editor, OverlayThemes.All[2]); Customize(editor, 2, 39, 44, .88, 900, "#CCEEDD"); var material = OverlayAppearance.From(settings);
            var expected = new[] { liquid, frost, material };
            for (int i = 0; i < expected.Length; i++)
            {
                Select(editor, OverlayThemes.All[i]); await Settle();
                checks[$"themeMemory.switchRestoresAllFields.{i}"] = OverlayAppearance.From(settings) == expected[i];
                checks[$"themeMemory.switchRestoresPreviewFont.{i}"] = editor.PreviewSurface.RenderedFont.StartsWith(expected[i].Font + ",", StringComparison.Ordinal);
                if (i == 0) checks["themeMemory.switchRestoresDirectlyResizedWidth"] = Slider(editor, "桌面歌词宽度").Value == 1020;
                if (i < 2) VisualVerification.Save(editor, Path.Combine(directory, i == 0 ? "theme-memory-liquid.png" : "theme-memory-frost.png"));
            }
            checks["themeMemory.unsavedDraftsDoNotReplaceProfiles"] = settings.OverlayThemeProfiles.Count == 0;
            editor.Save(); await Settle(); var persisted = store.Load();
            checks["themeMemory.savePersistsEveryVisitedTheme"] = persisted.OverlayThemeProfiles.Count == 3 && expected.All(a => persisted.OverlayThemeProfiles.GetValueOrDefault(a.Preset) == a);
            ShowAppearance(this); await Settle(); editor = appearanceWindow!;
            for (int i = 0; i < expected.Length; i++)
            {
                Select(editor, OverlayThemes.All[i]); checks[$"themeMemory.reopenRestores.{i}"] = OverlayAppearance.From(settings) == expected[i];
            }
            Select(editor, OverlayThemes.All[1]); Reset(editor); var reset = OverlayAppearance.From(settings);
            checks["themeMemory.resetUsesPresetMaterial"] = reset.Background == OverlayThemes.All[1].Appearance.Background && reset.Radius == OverlayThemes.All[1].Appearance.Radius && reset.Opacity == OverlayThemes.All[1].Appearance.Opacity;
            checks["themeMemory.resetRetainsTypography"] = reset.Font == frost.Font && reset.FontSize == frost.FontSize && reset.Width == frost.Width;
            Select(editor, OverlayThemes.All[0]); checks["themeMemory.resetLeavesOtherThemeIntact"] = OverlayAppearance.From(settings) == liquid;
            Customize(editor, 3, 52, 59, .91, 1100, "#334455"); editor.Close(); await Settle(); persisted = store.Load();
            checks["themeMemory.cancelRestoresSavedActiveTheme"] = OverlayAppearance.From(settings) == material && OverlayAppearance.From(persisted) == material;
            checks["themeMemory.cancelDiscardsAllThemeEditsAndReset"] = expected.All(a => persisted.OverlayThemeProfiles.GetValueOrDefault(a.Preset) == a);
            ShowAppearance(this); await Settle(); editor = appearanceWindow!;
            Select(editor, OverlayThemes.All[1]); checks["themeMemory.cancelledResetDoesNotSurviveReopen"] = OverlayAppearance.From(settings) == frost;
            Reset(editor); reset = OverlayAppearance.From(settings); editor.Save(); await Settle();
            persisted = store.Load();
            checks["themeMemory.savedResetOnlyAffectsCurrentTheme"] = persisted.OverlayThemeProfiles.GetValueOrDefault(frost.Preset) == reset && persisted.OverlayThemeProfiles.GetValueOrDefault(liquid.Preset) == liquid && persisted.OverlayThemeProfiles.GetValueOrDefault(material.Preset) == material;
            // A fresh SettingsStore and editor exercise restored settings without sharing the previous edit session.
            var reloaded = new SettingsStore(store.Root).Load();
            restarted = new(reloaded, _ => { }, () => store.Save(reloaded), () => "主题记忆 · 隔离验证") { Owner = this };
            restarted.Show(); await Settle(); var restored = new[] { liquid, reset, material };
            for (int i = 0; i < restored.Length; i++)
            {
                Select(restarted, OverlayThemes.All[i]); checks[$"themeMemory.freshSettingsRestore.{i}"] = OverlayAppearance.From(reloaded) == restored[i];
            }
            restarted.Close(); restarted = null;
        }
        finally
        {
            restarted?.Close(); appearanceWindow?.Close(); settings.OverlayThemeProfiles = originalProfiles;
            original.Apply(settings); overlay.ApplyStyle(); coordinator.SaveSettings();
        }
    }
}
