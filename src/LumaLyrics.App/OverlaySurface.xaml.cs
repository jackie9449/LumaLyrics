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
    bool reduceMotion;
    OverlayAppearance appearance=new();
    FrameworkElement? previewScene;
    VisualBrush? previewBrush;
    Rect previewViewbox;
    double? previewBrightness;
    DispatcherOperation? pendingText;
    public static readonly DependencyProperty CurrentTextProperty=DependencyProperty.Register(nameof(CurrentText),typeof(string),typeof(OverlaySurface),new PropertyMetadata("",TextChanged));
    public static readonly DependencyProperty NextTextProperty=DependencyProperty.Register(nameof(NextText),typeof(string),typeof(OverlaySurface),new PropertyMetadata("",TextChanged));
    public static readonly DependencyProperty CurrentTranslationProperty=DependencyProperty.Register(nameof(CurrentTranslation),typeof(string),typeof(OverlaySurface),new PropertyMetadata("",TextChanged));
    public static readonly DependencyProperty NextTranslationProperty=DependencyProperty.Register(nameof(NextTranslation),typeof(string),typeof(OverlaySurface),new PropertyMetadata("",TextChanged));
    public string CurrentText {get=>(string)GetValue(CurrentTextProperty);set=>SetValue(CurrentTextProperty,value);}
    public string NextText {get=>(string)GetValue(NextTextProperty);set=>SetValue(NextTextProperty,value);}
    public string CurrentTranslation {get=>(string)GetValue(CurrentTranslationProperty);set=>SetValue(CurrentTranslationProperty,value);}
    public string NextTranslation {get=>(string)GetValue(NextTranslationProperty);set=>SetValue(NextTranslationProperty,value);}
    public OverlaySurface()
    {
        InitializeComponent();SetBinding(CurrentTextProperty,new Binding("Current"));SetBinding(NextTextProperty,new Binding("Next"));
        SetBinding(CurrentTranslationProperty,new Binding("CurrentTranslation"));SetBinding(NextTranslationProperty,new Binding("NextTranslation"));
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
        CurrentChinese.Text=CurrentTranslation??"";NextChinese.Text=NextTranslation??"";
        CurrentChinese.Visibility=CurrentChinese.Text.Length>0 && !string.IsNullOrEmpty(CurrentText)?Visibility.Visible:Visibility.Collapsed;
        NextChinese.Visibility=appearance.TwoLines && NextChinese.Text.Length>0 && !string.IsNullOrEmpty(NextText)?Visibility.Visible:Visibility.Collapsed;
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
    public void Apply(OverlayAppearance options,bool reduceMotion=false)
    {
        var a=options.Normalize();appearance=a;this.reduceMotion=reduceMotion;StopTransitions();
        if(previewBrightness.HasValue)a=OverlayContrast.Paint(a,OverlayContrast.UseDarkText(previewBrightness.Value,false));
        Material.Apply(a,reduceMotion);
        PreviewBackdrop.Opacity=(a.Material=="frost"?.98:.78)*a.BackgroundEffectOpacity();
        PreviewBackdrop.Effect=new BlurEffect{Radius=a.Material=="frost"?24:12,RenderingBias=RenderingBias.Quality};
        UpdatePreviewBackdrop();
        Lines.Margin=new(a.Padding,Math.Max(12,a.Padding*.65),a.Padding,Math.Max(12,a.Padding*.65));
        FontFamily font;
        try {font=new FontFamily(a.Font+", Microsoft YaHei UI, Segoe UI");}catch{font=new("Microsoft YaHei UI");}
        var weight=a.Weight switch{"Normal"=>FontWeights.Normal,"Medium"=>FontWeights.Medium,"Bold"=>FontWeights.Bold,_=>FontWeights.SemiBold};
        foreach(var line in new[]{CurrentLine,NextLine,OutgoingCurrent,OutgoingNext,CurrentChinese,NextChinese})
        {
            line.FontFamily=font;line.Foreground=Solid(a.Color);line.FontWeight=weight;
            line.TextAlignment=Enum.Parse<TextAlignment>(a.Alignment);
            line.Effect=a.Shadow?new DropShadowEffect{Color=Colors.Black,ShadowDepth=1.5,BlurRadius=6,Opacity=a.ShadowStrength}:null;
        }
        CurrentLine.FontSize=OutgoingCurrent.FontSize=a.FontSize;CurrentLine.LineHeight=OutgoingCurrent.LineHeight=a.FontSize*1.4;
        NextLine.FontSize=OutgoingNext.FontSize=a.FontSize*a.NextScale;NextLine.LineHeight=OutgoingNext.LineHeight=NextLine.FontSize*1.45;
        NextLine.Opacity=a.NextOpacity;NextLine.FontWeight=OutgoingNext.FontWeight=FontWeights.Normal;NextHost.Margin=new(0,a.LineGap,0,0);
        CurrentChinese.FontSize=Math.Max(12,a.FontSize*.56);CurrentChinese.LineHeight=CurrentChinese.FontSize*1.5;CurrentChinese.Opacity=.83;CurrentChinese.FontWeight=FontWeights.Normal;
        NextChinese.FontSize=Math.Max(11,a.FontSize*a.NextScale*.68);NextChinese.LineHeight=NextChinese.FontSize*1.5;NextChinese.Opacity=a.NextOpacity*.85;NextChinese.FontWeight=FontWeights.Normal;
        NextHost.Visibility=a.TwoLines?Visibility.Visible:Visibility.Collapsed;
        FlushText(false);UpdateClip();
    }
    internal string RenderedFont=>CurrentLine.FontFamily.Source;
    internal void SetPreviewBrightness(double value){previewBrightness=value;Apply(appearance,reduceMotion);}
    internal void SetInteraction(Point point,bool active)=>Material.SetInteraction(point,active);
    internal void AttachPreviewBackdrop(FrameworkElement scene)
    {
        previewScene=scene;previewBrush=new VisualBrush(scene){ViewboxUnits=BrushMappingMode.Absolute,Stretch=Stretch.Fill};
        PreviewBackdrop.Background=previewBrush;LayoutUpdated+=(_,_)=>UpdatePreviewBackdrop();
    }
    void UpdatePreviewBackdrop()
    {
        bool visible=previewScene is not null && !appearance.Transparent && appearance.Opacity>0 && appearance.SystemBlur && appearance.Material is "liquid" or "frost";
        PreviewBackdrop.Visibility=visible?Visibility.Visible:Visibility.Hidden;
        if(!visible || !IsLoaded || ActualWidth<=0 || ActualHeight<=0)return;
        var origin=TranslatePoint(new Point(),previewScene!);var rect=new Rect(origin.X,origin.Y,ActualWidth,ActualHeight);
        if(rect!=previewViewbox){previewViewbox=rect;previewBrush!.Viewbox=rect;}
    }
    void SurfaceSizeChanged(object sender,SizeChangedEventArgs e){UpdateClip();UpdatePreviewBackdrop();}
    void UpdateClip()
    {
        if(Surface.ActualWidth<=0 || Surface.ActualHeight<=0)return;
        // Preserve fractional edge coverage instead of stacking geometric clips on the visible silhouette.
        Surface.Clip=null;
        double radius=appearance.ResolveCornerRadius(Surface.ActualWidth,Surface.ActualHeight);
        var shape=new RectangleGeometry(new Rect(0,0,Surface.ActualWidth,Surface.ActualHeight),radius,radius);
        var mask=new DrawingBrush(new GeometryDrawing(Brushes.White,null,shape)){Stretch=Stretch.Fill};mask.Freeze();Surface.OpacityMask=mask;
    }
}
