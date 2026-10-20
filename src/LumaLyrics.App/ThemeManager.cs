using System.Windows;
using System.Windows.Media;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

internal static class ThemeManager
{
    public static AppTheme Current { get; private set; } = AppThemes.All[0];
    static Color ColorOf(string value) => (Color)ColorConverter.ConvertFromString(value);
    static Brush Solid(string value) { var brush = new SolidColorBrush(ColorOf(value)); brush.Freeze(); return brush; }
    static Brush Gradient(string start, string end)
    {
        var brush = new LinearGradientBrush(ColorOf(start), ColorOf(end), new Point(0, 0), new Point(1, 1));
        brush.Freeze(); return brush;
    }

    public static void Apply(string? id)
    {
        Current = AppThemes.Find(id);
        Populate(System.Windows.Application.Current.Resources,Current);
    }

    static void Populate(ResourceDictionary resources,AppTheme t)
    {
        foreach (var (key, color) in new (string, string)[] {
            ("TextBrush",t.Text), ("MutedBrush",t.Muted), ("AccentBrush",t.Accent),
            ("AccentTextBrush",t.AccentText), ("AccentSoftBrush",t.AccentSoft),
            ("RaisedBrush",t.Raised), ("ControlBrush",t.Control), ("HoverBrush",t.Hover),
            ("BorderBrush",t.Border), ("GlowBrush",t.Glow), ("SecondaryGlowBrush",t.GlowSecondary),
            ("ScrollThumbBrush",t.IsDark?"#617F98":t.Id=="material"?"#A79AB9":"#8CA899") })
            resources[key] = Solid(color);
        resources["WindowBrush"] = t.Id == "material" ? Solid(t.WindowStart) : Gradient(t.WindowStart,t.WindowEnd);
        resources["PanelBrush"] = t.Id == "liquid" ? Gradient(t.Panel,"#E5152537") : Solid(t.Panel);
        resources["ArtworkBrush"] = t.Id == "material" ? Solid("#EADDFF") : Gradient(t.Glow,t.WindowEnd);
        resources["PanelRadius"] = new CornerRadius(t.Radius);
        resources["SurfaceSheenOpacity"] = t.Id == "liquid" ? .16 : t.Id == "frost" ? .09 : 0d;
        resources["AmbientOpacity"] = t.Id == "liquid" ? .3 : t.Id == "frost" ? .38 : 0d;
    }

    // These local resources never change when Application.Resources is replaced.
    // Both the floating toolbar and its editor belong to the floating appearance.
    public static void ApplyOverlayResources(FrameworkElement target,OverlayAppearance appearance)
    {
        var background=ColorOf(appearance.Background);
        bool light=(background.R*.2126+background.G*.7152+background.B*.0722)>160;
        Populate(target.Resources,AppThemes.Find(light?"frost":"liquid"));
        target.Resources["AccentBrush"]=Solid(appearance.Accent);
        var accent=ColorOf(appearance.Accent);
        target.Resources["AccentTextBrush"]=Solid(accent.R*.2126+accent.G*.7152+accent.B*.0722>160?"#182630":"#FFFFFF");
    }

    public static void Bind(FrameworkElement element, DependencyProperty property, string resource) => element.SetResourceReference(property,resource);
}
