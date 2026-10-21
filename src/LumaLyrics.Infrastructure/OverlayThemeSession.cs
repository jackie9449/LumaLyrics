namespace LumaLyrics.Infrastructure;

// Keep drafts separate from persisted profiles: Cancel must discard changes to every visited theme.
public sealed class OverlayThemeSession
{
    readonly AppSettings settings;
    readonly OverlayAppearance original;
    readonly Dictionary<string, OverlayAppearance> drafts = new(StringComparer.Ordinal);
    // The overlay can also resize directly, so capture its live width rather than a stale panel snapshot.
    public OverlayAppearance Current => OverlayAppearance.From(settings);

    public OverlayThemeSession(AppSettings settings)
    {
        this.settings = settings;
        if (settings.OverlayThemeProfiles is not null)
            foreach (var (name, appearance) in settings.OverlayThemeProfiles)
            {
                if (string.IsNullOrWhiteSpace(name) || appearance is null) continue;
                string key = name == "极简透明" ? "纯净透明" : name;
                drafts[key] = (appearance with { Preset = key }).Normalize();
            }
        // Flat fields remain authoritative for the active theme, including upgrades from older versions.
        original = OverlayAppearance.From(settings);
        drafts[original.Preset] = original;
    }

    public void Update(OverlayAppearance appearance)
    {
        var normalized = appearance.Normalize();
        drafts[normalized.Preset] = normalized;
        normalized.Apply(settings);
    }

    public void Select(OverlayTheme theme)
    {
        RememberCurrent();
        Update(drafts.TryGetValue(theme.Name, out var saved) ? saved : DefaultsWithTypography(theme));
    }

    void RememberCurrent()
    {
        var current = Current;
        drafts[current.Preset] = current;
    }

    public void ResetCurrent()
    {
        var theme = OverlayThemes.All.FirstOrDefault(t => t.Name == Current.Preset) ?? OverlayThemes.All[0];
        Update(DefaultsWithTypography(theme));
    }

    OverlayAppearance DefaultsWithTypography(OverlayTheme theme) => theme.Appearance with
    {
        Preset = theme.Name, Font = Current.Font, FontSize = Current.FontSize, Weight = Current.Weight,
        Width = Current.Width, TwoLines = Current.TwoLines, Alignment = Current.Alignment,
        Animations = Current.Animations, TransitionMs = Current.TransitionMs
    };

    public void Save(Action persist)
    {
        RememberCurrent();
        var savedProfiles = settings.OverlayThemeProfiles;
        settings.OverlayThemeProfiles = new(drafts, StringComparer.Ordinal);
        Current.Apply(settings);
        try { persist(); }
        catch { settings.OverlayThemeProfiles = savedProfiles; throw; }
    }

    public void Cancel() => original.Apply(settings);
}
