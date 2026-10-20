using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
    static Brush Sheen(string accent)
    {
        var brush=new LinearGradientBrush{StartPoint=new(0,0),EndPoint=new(1,1)};
        brush.GradientStops.Add(new(Colors.White,0));brush.GradientStops.Add(new(Color.FromArgb(0,255,255,255),.4));
        brush.GradientStops.Add(new(Color.FromArgb(0,255,255,255),.8));brush.GradientStops.Add(new(ColorOf(accent),1));
        brush.Freeze();return brush;
    }

    public static void Apply(string? id,ArtworkPalette? artwork=null,bool animate=false)
    {
        var theme=AppThemes.Find(id);
        animate=animate && theme.Id is ("liquid" or "frost") && Current.Id==theme.Id && SystemParameters.ClientAreaAnimation;
        Current = artwork?.ApplyTo(theme) ?? theme;
        Populate(System.Windows.Application.Current.Resources,Current,animate);
    }

    static void Populate(ResourceDictionary resources,AppTheme t,bool animate=false)
    {
        foreach (var (key, color) in new (string, string)[] {
            ("TextBrush",t.Text), ("MutedBrush",t.Muted), ("AccentBrush",t.Accent),
            ("AccentTextBrush",t.AccentText), ("AccentSoftBrush",t.AccentSoft),
            ("RaisedBrush",t.Raised), ("ControlBrush",t.Control), ("HoverBrush",t.Hover),
            ("BorderBrush",t.Border), ("GlowBrush",t.Glow), ("SecondaryGlowBrush",t.GlowSecondary),
            ("ScrollThumbBrush",t.Id=="material"?"#A79AB9":t.IsDark?t.Muted:t.Accent) })
            ReplaceBrush(resources,key,Solid(color),animate && key is not ("TextBrush" or "MutedBrush" or "AccentBrush" or "AccentTextBrush" or "ScrollThumbBrush"));
        ReplaceBrush(resources,"WindowBrush",t.Id=="material"?Solid(t.WindowStart):Gradient(t.WindowStart,t.WindowEnd),animate);
        ReplaceBrush(resources,"PanelBrush",t.Id=="liquid"?Gradient(t.Panel,t.PanelEnd):Solid(t.Panel),animate);
        ReplaceBrush(resources,"ArtworkBrush",t.Id=="material"?Solid("#EADDFF"):Gradient(t.Glow,t.WindowEnd),animate);
        ReplaceBrush(resources,"SheenBrush",Sheen(t.Accent),animate);
        resources["PanelRadius"] = new CornerRadius(t.Radius);
        resources["SurfaceSheenOpacity"] = t.Id == "liquid" ? .16 : t.Id == "frost" ? .09 : 0d;
        resources["AmbientOpacity"] = t.Id == "liquid" ? .3 : t.Id == "frost" ? .38 : 0d;
    }

    static void ReplaceBrush(ResourceDictionary resources,string key,Brush target,bool animate)
    {
        if(animate && resources[key] is Brush previous)
        {
            target=target.Clone();
            void Transition(Animatable element,DependencyProperty property,Color from,Color to)
            {
                if(from==to)return;
                // A new WPF clock starts on the next render tick. Keep the visible
                // starting color as its base value so rapid updates cannot flash the target.
                element.SetValue(property,from);
                var animation=new ColorAnimation(from,to,TimeSpan.FromMilliseconds(650)){
                    EasingFunction=new CubicEase{EasingMode=EasingMode.EaseInOut},FillBehavior=FillBehavior.HoldEnd
                };
                animation.Completed+=(_,_)=>{element.SetValue(property,to);element.BeginAnimation(property,null);};
                element.BeginAnimation(property,animation);
            }
            if(previous is SolidColorBrush before && target is SolidColorBrush after)
                Transition(after,SolidColorBrush.ColorProperty,before.Color,after.Color);
            else if(previous is LinearGradientBrush oldGradient && target is LinearGradientBrush newGradient)
                for(int i=0;i<Math.Min(oldGradient.GradientStops.Count,newGradient.GradientStops.Count);i++)
                    Transition(newGradient.GradientStops[i],GradientStop.ColorProperty,oldGradient.GradientStops[i].Color,newGradient.GradientStops[i].Color);
        }
        resources[key]=target;
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
