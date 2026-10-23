using System.Text.RegularExpressions;
namespace LumaLyrics.Infrastructure;

// A snapshot includes appearance only: canceling a preview must not roll back playback or library settings.
public sealed record OverlayAppearance
{
    public string Preset { get; init; } = "柔和卡片";
    public string Material { get; init; } = "card";
    public string Font { get; init; } = "Microsoft YaHei UI";
    public string Weight { get; init; } = "SemiBold";
    public string Color { get; init; } = "#F5F7FA";
    public string Background { get; init; } = "#101216";
    public string Accent { get; init; } = "#B8A4FF";
    public string Alignment { get; init; } = "Center";
    public double FontSize { get; init; } = 32;
    public double Opacity { get; init; } = .65;
    public double Width { get; init; } = 820;
    public double Radius { get; init; } = 24;
    // Radius is retained only to migrate appearances saved before proportional corners.
    public double? CornerRatio { get; init; }
    public double Padding { get; init; } = 24;
    public double LineGap { get; init; } = 8;
    public double NextScale { get; init; } = .65;
    public double NextOpacity { get; init; } = .65;
    public double ShadowStrength { get; init; } = .8;
    public double GlassIntensity { get; init; } = .55;
    public bool TwoLines { get; init; } = true;
    public bool Shadow { get; init; } = true;
    public bool AdaptiveContrast { get; init; }
    public bool Transparent { get; init; }
    public bool SystemBlur { get; init; } = true;
    public bool Border { get; init; } = true;
    public bool Animations { get; init; } = true;
    public double TransitionMs { get; init; } = 220;

    static double Clamp(double v,double min,double max,double fallback) => double.IsFinite(v)?Math.Clamp(v,min,max):fallback;
    static string Hex(string? v,string fallback) => v is not null && Regex.IsMatch(v,"^#[0-9A-Fa-f]{6}$")?v.ToUpperInvariant():fallback;
    public OverlayAppearance Normalize()
    {
        var a=this with {
        Preset=string.IsNullOrWhiteSpace(Preset)?"柔和卡片":Preset=="极简透明"?"纯净透明":Preset,
        Font=string.IsNullOrWhiteSpace(Font)?"Microsoft YaHei UI":Font,
        Weight=new[]{"Normal","Medium","SemiBold","Bold"}.Contains(Weight)?Weight:"SemiBold",
        Material=new[]{"card","liquid","frost","material","aurora","paper","none"}.Contains(Material)?Material:"card",
        Alignment=new[]{"Left","Center","Right"}.Contains(Alignment)?Alignment:"Center",
        Color=Hex(Color,"#F5F7FA"),Background=Hex(Background,"#101216"),Accent=Hex(Accent,"#B8A4FF"),
        FontSize=Clamp(FontSize,18,96,32),Opacity=Clamp(Opacity,0,1,.65),Width=Clamp(Width,360,1800,820),
        Radius=Clamp(Radius,0,64,24),Padding=Clamp(Padding,8,56,24),LineGap=Clamp(LineGap,0,36,8),
        NextScale=Clamp(NextScale,.45,1,.65),NextOpacity=Clamp(NextOpacity,.15,1,.65),
        ShadowStrength=Clamp(ShadowStrength,0,1,.8),GlassIntensity=Clamp(GlassIntensity,0,1,.55),TransitionMs=Clamp(TransitionMs,120,500,220)
        };
        double baseline=Math.Max(90,2*Math.Max(12,a.Padding*.65)+a.FontSize*1.4+(a.TwoLines?a.LineGap+a.FontSize*a.NextScale*1.45:0));
        double legacy=Math.Clamp(a.Radius*2/Math.Min(a.Width,baseline),0,1);
        return a with{CornerRatio=Clamp(a.CornerRatio??legacy,0,1,legacy)};
    }
    public double ResolveCornerRadius(double width,double height)
    {
        if(!double.IsFinite(width) || !double.IsFinite(height) || width<=0 || height<=0)return 0;
        double ratio=CornerRatio is { } value && double.IsFinite(value)?Math.Clamp(value,0,1):Normalize().CornerRatio!.Value;
        return Math.Min(width,height)*.5*ratio;
    }
    public double BackgroundEffectOpacity()
    {
        if(Transparent || !double.IsFinite(Opacity))return 0;
        double t=Math.Clamp(Opacity/.1,0,1);return t*t*(3-2*t);
    }
    public static OverlayAppearance From(AppSettings s) => new OverlayAppearance {
        Preset=s.OverlayPreset=="极简透明"?"纯净透明":s.OverlayPreset,Material=s.OverlayMaterial,Font=s.OverlayFont,Weight=s.OverlayFontWeight,
        Color=s.OverlayColor,Background=s.OverlayBackgroundColor,Accent=s.OverlayAccentColor,Alignment=s.OverlayAlignment,
        FontSize=s.OverlayFontSize,Opacity=s.OverlayBackgroundOpacity,Width=s.OverlayWidth,Radius=s.OverlayCornerRadius,CornerRatio=s.OverlayCornerRatio,Padding=s.OverlayPadding,
        LineGap=s.OverlayLineGap,NextScale=s.OverlayNextScale,NextOpacity=s.OverlayNextOpacity,ShadowStrength=s.OverlayShadowStrength,
        GlassIntensity=s.OverlayGlassIntensity,TwoLines=s.OverlayTwoLines,Shadow=s.OverlayShadow,
        Transparent=s.OverlayTransparent || s.OverlayPreset=="极简透明",SystemBlur=s.OverlaySystemBlur,Border=s.OverlayBorder,Animations=s.OverlayAnimations,TransitionMs=s.OverlayTransitionMs,AdaptiveContrast=s.OverlayAdaptiveContrast
    }.Normalize();
    public void Apply(AppSettings s)
    {
        var a=Normalize();s.OverlayPreset=a.Preset;s.OverlayMaterial=a.Material;s.OverlayFont=a.Font;s.OverlayFontWeight=a.Weight;
        s.OverlayColor=a.Color;s.OverlayBackgroundColor=a.Background;s.OverlayAccentColor=a.Accent;s.OverlayAlignment=a.Alignment;
        s.OverlayFontSize=a.FontSize;s.OverlayBackgroundOpacity=a.Opacity;s.OverlayWidth=a.Width;s.OverlayCornerRadius=a.Radius;s.OverlayCornerRatio=a.CornerRatio;s.OverlayPadding=a.Padding;
        s.OverlayLineGap=a.LineGap;s.OverlayNextScale=a.NextScale;s.OverlayNextOpacity=a.NextOpacity;s.OverlayShadowStrength=a.ShadowStrength;
        s.OverlayGlassIntensity=a.GlassIntensity;s.OverlayTwoLines=a.TwoLines;s.OverlayShadow=a.Shadow;s.OverlayTransparent=a.Transparent;
        s.OverlaySystemBlur=a.SystemBlur;s.OverlayBorder=a.Border;s.OverlayAnimations=a.Animations;s.OverlayTransitionMs=a.TransitionMs;
        s.OverlayAdaptiveContrast=a.AdaptiveContrast;
    }
}

public sealed record OverlayTheme(string Name,string Caption,string Tag,OverlayAppearance Appearance);
public static class OverlayThemes
{
    public static IReadOnlyList<OverlayTheme> All { get; } = new OverlayTheme[] {
        new("液态玻璃","透亮厚边 · 随光轻盈","GLASS",new(){Preset="液态玻璃",Material="liquid",Background="#13202D",Accent="#A9C9DD",Color="#F7F9FC",Opacity=.16,Radius=30,GlassIntensity=.76,Padding=28,ShadowStrength=.9,AdaptiveContrast=true}),
        new("雾面毛玻璃","细腻柔雾 · 温润留白","FROST",new(){Preset="雾面毛玻璃",Material="frost",Background="#F0F2F5",Accent="#869BAE",Color="#28313B",Opacity=.64,Radius=28,GlassIntensity=.28,Shadow=false}),
        new("Material 云白","Google 风格 · 柔和粉彩","LIGHT",new(){Preset="Material 云白",Material="material",Color="#25332C",Background="#EDF3E9",Accent="#557A55",Opacity=.96,Radius=38,Shadow=false,Padding=28,GlassIntensity=.45}),
        new("Material 夜色","Google 风格 · 莓紫夜间","DARK",new(){Preset="Material 夜色",Material="material",Color="#F0E7FA",Background="#28232E",Accent="#D9B8FA",Opacity=.95,Radius=38,Shadow=false,Padding=28,GlassIntensity=.45}),
        new("极光渐层","紫蓝流光 · 深邃轻盈","AURORA",new(){Preset="极光渐层",Material="aurora",Background="#171C32",Accent="#A8A1FF",Opacity=.85,Radius=28,GlassIntensity=.65}),
        new("暖纸留白","奶油纸色 · 阅读时刻","PAPER",new(){Preset="暖纸留白",Material="paper",Color="#493D34",Background="#F6EFE2",Accent="#B07957",Opacity=.98,Radius=18,Shadow=false,GlassIntensity=.2}),
        new("柔和卡片","石墨灰底 · 经典耐看","CLASSIC",new()),
        new("纯净透明","只留下文字 · 融入桌面","CLEAR",new(){Preset="纯净透明",Material="none",Transparent=true,Border=false,Opacity=0,Radius=0,Padding=16})
    }.Select(t=>t with{Appearance=t.Appearance.Normalize()}).ToArray();
}
