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

    public static void Apply(string? id,ArtworkPalette? artwork=null,bool animate=false,ResourceDictionary? applicationScope=null)
    {
        var theme=AppThemes.Find(id);
        animate=animate && theme.Id is ("liquid" or "frost") && Current.Id==theme.Id && SystemParameters.ClientAreaAnimation;
        Current = artwork?.ApplyTo(theme) ?? theme;
        Populate(System.Windows.Application.Current.Resources,Current,animate);
        // Give the main view an explicit palette scope while sharing the exact application
        // brushes with owned settings/dialogs, including their in-progress cover animations.
        if(applicationScope is not null)
            foreach(string key in new[]{"TextBrush","MutedBrush","AccentBrush","AccentTextBrush","AccentSoftBrush","RaisedBrush","ControlBrush","HoverBrush","BorderBrush","GlowBrush","SecondaryGlowBrush","ScrollThumbBrush","WindowBrush","PanelBrush","ArtworkBrush","SheenBrush","PanelRadius","SurfaceSheenOpacity","AmbientOpacity","ControlRadius","InputRadius","ItemRadius","CardRadius","InputBrush","RailBrush","PressedBrush","PrimaryFeedbackBrush","SwitchKnobBrush"})
                applicationScope[key]=System.Windows.Application.Current.Resources[key];
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
        ReplaceBrush(resources,"WindowBrush",t.Id is "liquid" or "frost"?Gradient(t.WindowStart,t.WindowEnd):Solid(t.WindowStart),animate);
        ReplaceBrush(resources,"PanelBrush",t.Id=="liquid"?Gradient(t.Panel,t.PanelEnd):Solid(t.Panel),animate);
        ReplaceBrush(resources,"ArtworkBrush",t.Id=="material"?Solid(t.AccentSoft):Gradient(t.Glow,t.WindowEnd),animate);
        ReplaceBrush(resources,"SheenBrush",Sheen(t.Accent),animate);
        var design=ThemeDesign.For(t.Id);
        resources["ThemeId"]=t.Id;resources["DisplayFont"]=new FontFamily(design.DisplayFont);
        resources["LyricFont"]=new FontFamily(t.Id is "paper" or "studio"?design.DisplayFont:"Segoe UI Variable, Microsoft YaHei UI");
        resources["LabelFont"]=new FontFamily(t.Id=="studio"?"Consolas, Microsoft YaHei UI":"Segoe UI Variable, Microsoft YaHei UI");
        resources["HeadlineSize"]=design.Headline;resources["HeadlineWeight"]=design.Weight;
        resources["PanelRadius"] = design.Panel;
        resources["SurfaceSheenOpacity"] = t.Id == "liquid" ? .16 : t.Id == "frost" ? .035 : 0d;
        resources["AmbientOpacity"] = t.Id == "liquid" ? .3 : t.Id == "frost" ? .12 : 0d;
        resources["ControlRadius"]=design.Control;resources["InputRadius"]=design.Input;
        resources["ItemRadius"]=t.Id is "paper" or "studio" or "bauhaus"?design.Control:new CornerRadius(t.Id=="liquid"?12:16);
        resources["CardRadius"]=design.Card;
        resources["ArtworkRadius"]=new CornerRadius(design.ArtworkRadius);
        resources["ArtworkMat"]=design.ArtworkMat;
        resources["NavButtonWidth"]=design.NavWidth;resources["NavButtonHeight"]=design.NavHeight;
        resources["NavLabelVisibility"]=design.NavLabels?Visibility.Visible:Visibility.Collapsed;
        resources["NavIconOffset"]=design.NavLabels?-7d:0d;
        resources["NavLabelMargin"]=new Thickness(0,32,0,0);
        resources["AlbumCardPadding"]=design.CardPadding;resources["AlbumCardBorder"]=design.CardBorder;
        resources["AlbumArtworkRadius"]=new CornerRadius(t.Id switch{"liquid"=>14,"frost"=>16,"material"=>20,"studio"=>2,_=>0});
        resources["AlbumImagePadding"]=new Thickness(t.Id switch{"frost"=>5,"paper"=>6,"studio"=>2,_=>0});
        resources["AlbumTitleSize"]=t.Id=="paper"?16d:t.Id=="studio"?13d:14d;
        resources["ThumbRadius"]=new CornerRadius(t.Id is "paper" or "bauhaus"?0:t.Id=="studio"?2:10);
        resources["ActionBorder"]=new Thickness(t.Id=="bauhaus"?1.5:1);
        resources["ButtonFaceInset"]=t.Id=="bauhaus"?new Thickness(0,0,3,3):new Thickness(0);
        resources["ButtonBaselineVisibility"]=t.Id is "paper" or "studio"?Visibility.Visible:Visibility.Collapsed;
        resources["ButtonPlateVisibility"]=t.Id=="bauhaus"?Visibility.Visible:Visibility.Collapsed;
        resources["SelectionMarkerVisibility"]=t.Id is "paper" or "studio"?Visibility.Visible:Visibility.Collapsed;
        ReplaceBrush(resources,"CardBrush",t.Id=="liquid"?Gradient(t.Panel,t.PanelEnd):Solid(t.Id=="paper"?t.WindowStart:t.Panel),animate);
        ReplaceBrush(resources,"ArtworkFrameBrush",t.Id=="liquid"?Gradient(t.Glow,t.WindowEnd):Solid(t.Id is "frost" or "paper"?t.Raised:t.Id=="studio"?"#0A1012":t.Panel),animate);
        resources["TransportRadius"]=t.Id=="liquid"?new CornerRadius(16):design.Card;
        resources["AlbumHeaderRadius"]=t.Id=="liquid"?new CornerRadius(18):design.Card;
        resources["QueueCardRadius"]=t.Id=="liquid"?new CornerRadius(16):design.Control;
        resources["CompactQueueRadius"]=t.Id=="liquid"?new CornerRadius(12):design.Control;
        resources["PreviewCardRadius"]=t.Id=="liquid"?new CornerRadius(14):design.Card;
        resources["LocationCardRadius"]=t.Id=="liquid"?new CornerRadius(16):design.Card;
        resources["DetailArtworkRadius"]=t.Id=="liquid"?new CornerRadius(12):new CornerRadius(design.ArtworkRadius*.5);
        resources["QueueArtworkRadius"]=t.Id=="liquid"?new CornerRadius(7):new CornerRadius(Math.Min(10,design.ArtworkRadius*.5));
        resources["QueueBorderRadius"]=t.Id=="liquid"?new CornerRadius(8):(CornerRadius)resources["QueueArtworkRadius"];
        resources["CurrentArtworkRadius"]=t.Id=="liquid"?new CornerRadius(9):(CornerRadius)resources["QueueArtworkRadius"];
        resources["DialogHeadlineSize"]=t.Id=="liquid"?24d:design.Headline;
        resources["NavigationBrush"]=Solid(t.Id switch{"liquid" or "paper"=>"#00000000","studio"=>"#101719","material"=>t.Control,_=>t.Raised});
        resources["HeaderRuleVisibility"]=t.Id is "paper" or "studio" or "bauhaus"?Visibility.Visible:Visibility.Collapsed;
        resources["FrameRuleBrush"]=Solid(t.Id is "paper" or "studio" or "bauhaus"?t.Border:"#00000000");
        ReplaceBrush(resources,"InputBrush",Solid(t.Id=="liquid"?t.WindowStart:t.Raised),animate);
        ReplaceBrush(resources,"RailBrush",t.Id=="liquid"?Gradient(t.Control,t.WindowEnd):Solid(t.Id=="material"?t.AccentSoft:t.Raised),animate);
        resources["PressedBrush"]=Solid(t.IsDark?"#16000000":"#12000000");
        resources["PrimaryFeedbackBrush"]=Solid(ColorOf(t.AccentText).R>160?"#28FFFFFF":"#16000000");
        resources["SwitchKnobBrush"]=Solid(t.Muted);
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
    // Floating windows and their toolbar retain their palette; Settings/editor chrome inherits the app.
    public static void ApplyOverlayResources(FrameworkElement target,OverlayAppearance appearance)
    {
        // Floating surfaces keep their original component styles as well as their local palette.
        AppMotion.SetEnabled(target,false);
        if(!target.Resources.MergedDictionaries.Any(d=>d.Source?.OriginalString.Contains("UiStyles.xaml")==true))
        {
            target.Resources.MergedDictionaries.Add(new(){Source=new Uri("/LumaLyrics;component/SliderStyles.xaml",UriKind.Relative)});
            target.Resources.MergedDictionaries.Add(new(){Source=new Uri("/LumaLyrics;component/UiStyles.xaml",UriKind.Relative)});
        }
        var background=ColorOf(appearance.Background);
        bool light=(background.R*.2126+background.G*.7152+background.B*.0722)>160;
        Populate(target.Resources,light?OverlayLightPalette:AppThemes.Find("liquid"));
        target.Resources["AccentBrush"]=Solid(appearance.Accent);
        var accent=ColorOf(appearance.Accent);
        target.Resources["AccentTextBrush"]=Solid(accent.R*.2126+accent.G*.7152+accent.B*.0722>160?"#182630":"#FFFFFF");
    }

    // The floating toolbar's established light palette is independent of redesigned
    // application themes. App theme changes must not recolor an existing overlay.
    static readonly AppTheme OverlayLightPalette=new("frost","毛玻璃","","",false,
        "#E6EEEA","#D7E4DE","#DEF3F7F4","#F3F8F5","#DFE9E3","#CFDFD5","#243D33","#576E61","#35634E",
        "#FFFFFF","#D6E6DB","#7FA4BAAC","#B7D1C4","#CDD6C2",22);

    public static void Bind(FrameworkElement element, DependencyProperty property, string resource) => element.SetResourceReference(property,resource);
    internal static void BindDisplay(System.Windows.Controls.TextBlock heading)
    {Bind(heading,System.Windows.Controls.TextBlock.FontFamilyProperty,"DisplayFont");Bind(heading,System.Windows.Controls.TextBlock.FontWeightProperty,"HeadlineWeight");}
}
