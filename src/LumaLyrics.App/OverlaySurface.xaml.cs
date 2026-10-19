using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Animation;
using System.Windows.Data;
using System.Windows.Threading;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

public partial class OverlaySurface : UserControl
{
    double radius;bool reduceMotion;
    OverlayAppearance appearance=new();
    DispatcherOperation? pendingText;
    public static readonly DependencyProperty CurrentTextProperty=DependencyProperty.Register(nameof(CurrentText),typeof(string),typeof(OverlaySurface),new PropertyMetadata("",TextChanged));
    public static readonly DependencyProperty NextTextProperty=DependencyProperty.Register(nameof(NextText),typeof(string),typeof(OverlaySurface),new PropertyMetadata("",TextChanged));
    public string CurrentText {get=>(string)GetValue(CurrentTextProperty);set=>SetValue(CurrentTextProperty,value);}
    public string NextText {get=>(string)GetValue(NextTextProperty);set=>SetValue(NextTextProperty,value);}
    public OverlaySurface()
    {
        InitializeComponent();SetBinding(CurrentTextProperty,new Binding("Current"));SetBinding(NextTextProperty,new Binding("Next"));
        Loaded+=(_,_)=>FlushText(false);Unloaded+=(_,_)=>{pendingText?.Abort();pendingText=null;StopTransitions();};
    }
    static void TextChanged(DependencyObject d,DependencyPropertyChangedEventArgs e)=>((OverlaySurface)d).QueueText();
    void QueueText()
    {
        // Current and Next arrive as separate bindings. Commit them together on the next render pass.
        if(pendingText?.Status==DispatcherOperationStatus.Pending)return;
        pendingText=Dispatcher.BeginInvoke(DispatcherPriority.Render,()=>{pendingText=null;FlushText(IsLoaded);});
    }
    void FlushText(bool animate)
    {
        pendingText?.Abort();pendingText=null;
        bool motion=animate && appearance.Animations && !reduceMotion && SystemParameters.ClientAreaAnimation;
        ReplaceLine(CurrentLine,OutgoingCurrent,CurrentText??"",1,motion);
        ReplaceLine(NextLine,OutgoingNext,NextText??"",appearance.NextOpacity,motion && appearance.TwoLines);
    }
    void ReplaceLine(TextBlock incoming,TextBlock outgoing,string text,double opacity,bool animate)
    {
        if(incoming.Text==text)return;
        string previous=incoming.Text;double oldOpacity=incoming.Opacity;
        StopLine(incoming,outgoing,opacity);incoming.Text=text;
        if(!animate || previous.Length==0 || text.Length==0)return;
        outgoing.Text=previous;outgoing.Opacity=oldOpacity;
        var enter=new TranslateTransform(0,7);var exit=new TranslateTransform();incoming.RenderTransform=enter;outgoing.RenderTransform=exit;
        var duration=TimeSpan.FromMilliseconds(appearance.TransitionMs);var easing=new CubicEase{EasingMode=EasingMode.EaseOut};
        var fade=new DoubleAnimation(0,opacity,duration){EasingFunction=easing,FillBehavior=FillBehavior.Stop};
        object generation=new();incoming.Tag=generation;
        fade.Completed+=(_,_)=>{if(ReferenceEquals(incoming.Tag,generation))StopLine(incoming,outgoing,opacity);};
        incoming.BeginAnimation(OpacityProperty,fade,HandoffBehavior.SnapshotAndReplace);
        enter.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(7,0,duration){EasingFunction=easing,FillBehavior=FillBehavior.Stop});
        // Return the base transform to its final position once the animation clock finishes.
        enter.Y=0;
        var exitDuration=TimeSpan.FromMilliseconds(appearance.TransitionMs*.7);
        outgoing.BeginAnimation(OpacityProperty,new DoubleAnimation(oldOpacity,0,exitDuration){EasingFunction=easing,FillBehavior=FillBehavior.HoldEnd});
        exit.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(0,-5,exitDuration){EasingFunction=easing});
    }
    static void StopLine(TextBlock incoming,TextBlock outgoing,double opacity)
    {
        incoming.Tag=null;incoming.BeginAnimation(OpacityProperty,null);incoming.Opacity=opacity;incoming.RenderTransform=Transform.Identity;
        outgoing.BeginAnimation(OpacityProperty,null);outgoing.Opacity=0;outgoing.Text="";outgoing.RenderTransform=Transform.Identity;
    }
    void StopTransitions(){StopLine(CurrentLine,OutgoingCurrent,1);StopLine(NextLine,OutgoingNext,appearance.NextOpacity);}
    internal (double Opacity,double OutgoingOpacity,string Text,string OutgoingText) TransitionState=>(CurrentLine.Opacity,OutgoingCurrent.Opacity,CurrentLine.Text,OutgoingCurrent.Text);
    internal double CurrentCenterY=>CurrentHost.TranslatePoint(new Point(0,CurrentHost.ActualHeight/2),this).Y;
    internal double CurrentHeight=>CurrentHost.ActualHeight;
    internal static Color ColorOf(string value) => (Color)ColorConverter.ConvertFromString(value);
    static SolidColorBrush Solid(string value,double opacity=1) {var b=new SolidColorBrush(ColorOf(value)){Opacity=opacity};b.Freeze();return b;}
    static LinearGradientBrush Gradient(params (string Color,double Position)[] stops)
    {
        var b=new LinearGradientBrush{StartPoint=new(0,0),EndPoint=new(1,1)};
        foreach(var s in stops)b.GradientStops.Add(new(ColorOf(s.Color),s.Position));b.Freeze();return b;
    }
    public void Apply(OverlayAppearance options,bool reduceMotion=false)
    {
        var a=options.Normalize();appearance=a;this.reduceMotion=reduceMotion;radius=a.Radius;StopTransitions();
        foreach(var border in new[]{Base,Glow,Sheen,Rim})border.CornerRadius=new(radius);
        InnerRim.CornerRadius=new(Math.Max(0,radius-2));
        Base.Background=Solid(a.Background,a.Opacity);Glow.Background=null;Sheen.Background=null;
        Rim.BorderBrush=Solid("#FFFFFF",.16);InnerRim.BorderBrush=Brushes.Transparent;
        bool clear=a.Transparent || a.Opacity==0;
        Base.Visibility=Glow.Visibility=Sheen.Visibility=Rim.Visibility=InnerRim.Visibility=clear?Visibility.Hidden:Visibility.Visible;
        if(!clear)
        {
            if(a.Material=="liquid")
            {
                Glow.Background=new RadialGradientBrush(ColorOf(a.Accent),Colors.Transparent){Center=new(.1,0),GradientOrigin=new(.1,0),RadiusX=1.1,RadiusY=1.6,Opacity=a.GlassIntensity*.26};
                Sheen.Background=Gradient(("#50FFFFFF",0),("#08FFFFFF",.26),("#00FFFFFF",.55),("#18BBDEFF",1));Sheen.Opacity=a.GlassIntensity;
                Rim.BorderBrush=Gradient(("#D5FFFFFF",0),("#28FFFFFF",.34),("#14FFFFFF",.7),("#90D2EEFF",1));Rim.Opacity=.35+.6*a.GlassIntensity;
                InnerRim.BorderBrush=Solid("#FFFFFF",a.GlassIntensity*.12);
            }
            else if(a.Material=="frost")
            {
                Sheen.Background=Gradient(("#20FFFFFF",0),("#02FFFFFF",1));Sheen.Opacity=1;
                Glow.Background=Solid(a.Accent,a.GlassIntensity*.1);Rim.BorderBrush=Solid("#FFFFFF",.22);
            }
            else if(a.Material=="aurora")
            {
                Glow.Background=Gradient((a.Accent,0),("#778CE1",.42),("#469AAB",1));Glow.Opacity=a.GlassIntensity*.43;
                Sheen.Background=Gradient(("#10FFFFFF",0),("#00FFFFFF",.7));Sheen.Opacity=1;Rim.BorderBrush=Solid(a.Accent,.28);
            }
            else if(a.Material is "material" or "paper")
            {
                Glow.Background=new RadialGradientBrush(ColorOf(a.Accent),Colors.Transparent){Center=new(.98,1),GradientOrigin=new(.98,1),RadiusX=.8,RadiusY=1.5,Opacity=a.GlassIntensity*.16};
                Rim.BorderBrush=Solid(a.Accent,.16);
            }
        }
        // Reset shared layer opacity when switching presets; colors must not inherit the previous material.
        if(a.Material!="aurora")Glow.Opacity=1;
        if(a.Material!="liquid")Rim.Opacity=1;
        if(!a.Border)Rim.Visibility=InnerRim.Visibility=Visibility.Hidden;
        Lines.Margin=new(a.Padding,Math.Max(12,a.Padding*.65),a.Padding,Math.Max(12,a.Padding*.65));
        FontFamily font;
        try {font=new FontFamily(a.Font+", Microsoft YaHei UI, Segoe UI");}catch{font=new("Microsoft YaHei UI");}
        var weight=a.Weight switch{"Normal"=>FontWeights.Normal,"Medium"=>FontWeights.Medium,"Bold"=>FontWeights.Bold,_=>FontWeights.SemiBold};
        foreach(var line in new[]{CurrentLine,NextLine,OutgoingCurrent,OutgoingNext})
        {
            line.FontFamily=font;line.Foreground=Solid(a.Color);line.FontWeight=weight;
            line.TextAlignment=Enum.Parse<TextAlignment>(a.Alignment);
            line.Effect=a.Shadow?new DropShadowEffect{Color=Colors.Black,ShadowDepth=1.5,BlurRadius=6,Opacity=a.ShadowStrength}:null;
        }
        CurrentLine.FontSize=OutgoingCurrent.FontSize=a.FontSize;CurrentLine.LineHeight=OutgoingCurrent.LineHeight=a.FontSize*1.4;
        NextLine.FontSize=OutgoingNext.FontSize=a.FontSize*a.NextScale;NextLine.LineHeight=OutgoingNext.LineHeight=NextLine.FontSize*1.45;
        NextLine.Opacity=a.NextOpacity;NextLine.FontWeight=OutgoingNext.FontWeight=FontWeights.Normal;NextHost.Margin=new(0,a.LineGap,0,0);
        NextHost.Visibility=a.TwoLines?Visibility.Visible:Visibility.Collapsed;
        FlushText(false);UpdateClip();
    }
    void SurfaceSizeChanged(object sender,SizeChangedEventArgs e)=>UpdateClip();
    void UpdateClip()
    {
        if(Surface.ActualWidth<=0 || Surface.ActualHeight<=0)return;
        // Preserve fractional edge coverage instead of stacking geometric clips on the visible silhouette.
        Surface.Clip=null;
        Surface.OpacityMask=RoundedMask(Surface.RenderSize,radius);
    }
    internal static DrawingBrush RoundedMask(Size size,double radius)
    {
        var shape=new RectangleGeometry(new Rect(size),radius,radius);
        var mask=new DrawingBrush(new GeometryDrawing(Brushes.White,null,shape)){Stretch=Stretch.Fill};mask.Freeze();return mask;
    }
}
