namespace LumaLyrics.Infrastructure;

public sealed record AppTheme(
    string Id, string Name, string Caption, string EnglishName, bool IsDark,
    string WindowStart, string WindowEnd, string Panel, string Raised,
    string Control, string Hover, string Text, string Muted, string Accent,
    string AccentText, string AccentSoft, string Border, string Glow, string GlowSecondary,
    double Radius)
{
    public string PanelEnd { get; init; } = "#E5152537";
}

public static class AppThemes
{
    public const string Default = "liquid";
    public static IReadOnlyList<AppTheme> All { get; } = [
        new("liquid", "液态玻璃", "封面取色 · 深色流光", "LIQUID GLASS", true,
            "#0D1929", "#1B3044", "#C91B2E43", "#D9253D53",
            "#293E53", "#38546D", "#F1F7FC", "#ADBECE", "#A7DDF4",
            "#102F40", "#334F65", "#557C9CB4", "#477796", "#495B83", 26),
        new("frost", "毛玻璃", "封面取色 · 浅色柔雾", "FROSTED GLASS", false,
            "#E6EEEA", "#D7E4DE", "#DEF3F7F4", "#F3F8F5",
            "#DFE9E3", "#CFDFD5", "#243D33", "#576E61", "#35634E",
            "#FFFFFF", "#D6E6DB", "#7FA4BAAC", "#B7D1C4", "#CDD6C2", 22),
        new("material", "谷歌平面", "浅丁香调 · 纯粹秩序", "MATERIAL DESIGN", false,
            "#F6F2FA", "#F6F2FA", "#FFFBFE", "#F0EAF5",
            "#EAE3F0", "#DED4EA", "#292432", "#6E6478", "#6750A4",
            "#FFFFFF", "#EADDFF", "#DCD4E5", "#EADDFF", "#E8DEF0", 30)
    ];

    public static AppTheme Find(string? id) => All.FirstOrDefault(t => t.Id == id) ?? All[0];
}
