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
        new("frost", "雾白", "漫射柔光 · 留白与悬浮纸片", "FROST / DAYLIGHT", false,
            "#F0F4F3", "#E8EFED", "#F5FFFFFF", "#FFFFFF",
            "#E6EEEB", "#D8E5DF", "#253B34", "#4C6157", "#365D4C",
            "#FFFFFF", "#DBE9E1", "#C8D6D0", "#CEDDD6", "#DEE8E2", 28),
        new("material", "柔塑", "柔和色块 · 胶囊与不对称曲线", "MATERIAL / SOFT FORM", false,
            "#F8F3F1", "#F8F3F1", "#F4E8ED", "#FFFBFA",
            "#EEE3E8", "#E6D4DD", "#332B32", "#66535F", "#76516D",
            "#FFFFFF", "#E9D3E2", "#DCCBD3", "#EBD6DC", "#DDE8E0", 32),
        new("paper", "纸间", "唱片内页 · 衬线标题与编辑式留白", "PAPER / LINER NOTES", false,
            "#F4F0E7", "#F4F0E7", "#FCFAF4", "#FFFDFA",
            "#EEE8DC", "#E5DDCF", "#302E29", "#625C50", "#914330",
            "#FFFCF5", "#EFE1D2", "#CFC7B8", "#E8D9C5", "#E8D9C5", 0),
        new("studio", "夜航", "录音室秩序 · 精密刻度与哑光面板", "STUDIO / AFTER HOURS", true,
            "#111719", "#111719", "#192124", "#202A2D",
            "#263235", "#344346", "#E8ECE8", "#ADB9B5", "#D7B98A",
            "#232521", "#394035", "#3B494A", "#2C3C3E", "#2C3C3E", 4),
        new("bauhaus", "构成", "几何秩序 · 大胆字重与平面装帧", "FORM / BAUHAUS", false,
            "#F3F2EA", "#F3F2EA", "#FFFEF9", "#FFFEF9",
            "#E5E5DA", "#DCDDD1", "#242A2B", "#555E5B", "#294EB4",
            "#FFFFFF", "#E0E7F9", "#A9B0A9", "#E9C65F", "#C7634D", 2)
    ];

    public static AppTheme Find(string? id) => All.FirstOrDefault(t => t.Id == id) ?? All[0];
}
