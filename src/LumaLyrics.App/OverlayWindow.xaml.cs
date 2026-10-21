using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;
public partial class OverlayWindow : Window
{
    readonly MainWindow owner; readonly AppSettings settings;
    public bool IsLocked { get; private set; }
    bool firstLock=true,placed;
    bool instrumental;
    bool darkAdaptiveText;
    readonly DispatcherTimer contrastTimer=new(){Interval=TimeSpan.FromMilliseconds(400)};
    bool appearanceEditing,visibleBeforeEditing,visibilityPreferenceBeforeEditing;
    double opacityBeforeEditing;
    OverlayAppearance? previewAppearance;
    OverlayAppearance CurrentAppearance=>previewAppearance??OverlayAppearance.From(settings);
    internal bool CanDrag=>!instrumental && (!IsLocked || appearanceEditing);
    internal bool AppearanceEditing=>appearanceEditing;
    internal bool GlassHostVisible=>backdrop.HostVisible;
    internal bool GlassHostInputTransparent=>backdrop.HostInputTransparent;
    internal nint GlassHostHandle=>backdrop.HostHandle;
    readonly DispatcherTimer toolbarDelay=new(){Interval=TimeSpan.FromMilliseconds(160)};
    readonly NativeBackdrop backdrop;
    public string MaterialStatus=>backdrop.Status;
    public bool BlurAccepted=>backdrop.Accepted;
    public OverlayWindow(MainWindow owner,PlayerViewModel vm,AppSettings settings)
    {
        InitializeComponent(); this.owner=owner; this.settings=settings; DataContext=vm;backdrop=new(this);
        ToolbarPopup.CustomPopupPlacementCallback=(popup,target,offset)=>[new(new Point(Math.Max(0,target.Width-popup.Width),-popup.Height-4),System.Windows.Controls.Primitives.PopupPrimaryAxis.Vertical),new(new Point(Math.Max(0,target.Width-popup.Width),target.Height+4),System.Windows.Controls.Primitives.PopupPrimaryAxis.Vertical)];
        SourceInitialized+=(_,_)=>NativeWindow.NoActivate(this);
        Loaded+=(_,_)=> {var left=settings.OverlayLeft;var top=settings.OverlayTop;ApplyStyle();UpdateLayout();NativeWindow.Place(this,left,top,true,DefaultBottomInset());placed=true;RememberPosition();};
        MouseEnter+=(_,_)=>{toolbarDelay.Stop();if(!IsLocked && !instrumental && !appearanceEditing)ToolbarPopup.IsOpen=true;LyricSurface.SetInteraction(new(.35,0),true);};
        MouseMove+=(_,e)=>{var p=e.GetPosition(LyricSurface);LyricSurface.SetInteraction(new(p.X/Math.Max(1,LyricSurface.ActualWidth),p.Y/Math.Max(1,LyricSurface.ActualHeight)),true);};
        MouseLeave+=(_,_)=>{toolbarDelay.Start();LyricSurface.SetInteraction(new(.25,0),false);};
        toolbarDelay.Tick+=(_,_)=>{toolbarDelay.Stop();if(!IsMouseOver && !Toolbar.IsMouseOver)ToolbarPopup.IsOpen=false;};
        IsVisibleChanged+=(_,_)=>{if(!IsVisible){ToolbarPopup.IsOpen=false;toolbarDelay.Stop();}UpdateAdaptiveText();backdrop.Refresh();};
        LocationChanged+=(_,_)=> {if(placed)RememberPosition();backdrop.Refresh();UpdateAdaptiveText();};
        SizeChanged+=(_,_)=> { if(placed) {settings.OverlayWidth=Width;backdrop.RoundRegion(CurrentAppearance.Radius);}backdrop.Refresh();};
        DpiChanged+=(_,_)=>Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(()=>{backdrop.RoundRegion(CurrentAppearance.Radius);backdrop.Refresh();}));
        contrastTimer.Tick+=(_,_)=>UpdateAdaptiveText();
        Closed+=(_,_)=>{toolbarDelay.Stop();contrastTimer.Stop();ToolbarPopup.IsOpen=false;backdrop.Dispose();};
    }
    public void ApplyStyle()
    {
        var a=CurrentAppearance;
        ThemeManager.ApplyOverlayResources(this,a);
        ThemeManager.ApplyOverlayResources(Toolbar,a);
        double oldCenter=placed?LyricSurface.CurrentCenterY:0;var oldLocation=placed?NativeWindow.Location(this):default;
        Width=a.Width;SizeToContent=SizeToContent.Height;LyricSurface.Apply(OverlayContrast.Paint(a,darkAdaptiveText),settings.ReduceMotion);backdrop.Apply(a);
        if(IsLoaded) {UpdateLayout();backdrop.RoundRegion(a.Radius);if(placed){var dpi=VisualTreeHelper.GetDpi(this);NativeWindow.Place(this,oldLocation.Left,oldLocation.Top+(oldCenter-LyricSurface.CurrentCenterY)*dpi.DpiScaleY);}}
        ApplyInstrumentalState();
    }
    void UpdateAdaptiveText()
    {
        var a=CurrentAppearance;
        if(!IsVisible || instrumental || !a.AdaptiveContrast || a.Material!="liquid" || a.Transparent){contrastTimer.Stop();return;}
        contrastTimer.Start();var brightness=OverlayAmbientContrast.Brightness(this,a);if(!brightness.HasValue)return;
        bool next=OverlayContrast.UseDarkText(brightness.Value,darkAdaptiveText);if(next==darkAdaptiveText)return;
        darkAdaptiveText=next;LyricSurface.Apply(OverlayContrast.Paint(a,next),settings.ReduceMotion);
    }
    internal void PreviewAppearance(OverlayAppearance? appearance){previewAppearance=appearance;ApplyStyle();}
    internal void BeginAppearanceEditing()
    {
        if(appearanceEditing)return;
        visibleBeforeEditing=IsVisible;visibilityPreferenceBeforeEditing=settings.OverlayVisible;opacityBeforeEditing=Opacity;appearanceEditing=true;Opacity=1;
        if(!IsVisible)Show();ToolbarPopup.IsOpen=false;ResizeMode=ResizeMode.CanResizeWithGrip;ApplyInstrumentalState();
    }
    internal void EndAppearanceEditing()
    {
        if(!appearanceEditing)return;appearanceEditing=false;previewAppearance=null;Opacity=opacityBeforeEditing;
        ResizeMode=IsLocked?ResizeMode.NoResize:ResizeMode.CanResizeWithGrip;ApplyStyle();
        bool visible=settings.OverlayVisible==visibilityPreferenceBeforeEditing?visibleBeforeEditing:settings.OverlayVisible;
        if(!visible)Hide();
    }
    public void SetInstrumental(bool value)
    {
        if(instrumental==value)return;instrumental=value;ApplyInstrumentalState();
    }
    void ApplyInstrumentalState()
    {
        LyricSurface.Visibility=instrumental?Visibility.Hidden:Visibility.Visible;
        if(instrumental){ToolbarPopup.IsOpen=false;backdrop.Apply(new(){SystemBlur=false});}
        else backdrop.Apply(CurrentAppearance);
        if(NativeWindow.Handle(this)!=0)NativeWindow.ClickThrough(this,IsLocked && !appearanceEditing || instrumental);
        UpdateAdaptiveText();
    }
    internal bool InstrumentalSuppressed=>instrumental && LyricSurface.Visibility==Visibility.Hidden && !ToolbarPopup.IsOpen;
    public void SetLocked(bool locked,bool showTip=true)
    {
        if(locked && !owner.CanRecoverOverlay) { MessageBox.Show("托盘暂不可用，无法安全锁定。请先重新连接托盘。","浮光歌词"); return; }
        if(locked && firstLock && showTip) { MessageBox.Show($"锁定后可点击穿透。\n从托盘选择“解锁歌词”，或按 {settings.UnlockHotkey} 解锁。","桌面歌词"); firstLock=false; }
        IsLocked=locked; NativeWindow.ClickThrough(this,locked && !appearanceEditing);ToolbarPopup.IsOpen=false; ResizeMode=locked && !appearanceEditing?ResizeMode.NoResize:ResizeMode.CanResizeWithGrip;ApplyStyle();
    }
    void DragOverlay(object sender,MouseButtonEventArgs e) { if(CanDrag && e.ButtonState==MouseButtonState.Pressed) { try {ToolbarPopup.IsOpen=false;DragMove();NativeWindow.ClampVisible(this); } catch(InvalidOperationException) { } } }
    void LockClicked(object s,RoutedEventArgs e) { e.Handled=true; SetLocked(true); }
    void MainClicked(object s,RoutedEventArgs e) { e.Handled=true; owner.Restore(); }
    void AppearanceClicked(object s,RoutedEventArgs e) {e.Handled=true;owner.ShowAppearance();}
    void HideClicked(object s,RoutedEventArgs e) { e.Handled=true; owner.ToggleOverlay(); }
    void ToolbarEntered(object s,System.Windows.Input.MouseEventArgs e)=>toolbarDelay.Stop();
    void ToolbarLeft(object s,System.Windows.Input.MouseEventArgs e)=>toolbarDelay.Start();
    void RememberPosition(){var p=NativeWindow.Location(this);settings.OverlayLeft=p.Left;settings.OverlayTop=p.Top;}
    double DefaultBottomInset()
    {
        var a=OverlayAppearance.From(settings);if(a.TwoLines)return 60;
        // Anchor the current lyric where it would be in two-line mode, instead of sinking toward the taskbar.
        double next=a.LineGap+a.FontSize*a.NextScale*1.45;
        double twoHeight=Math.Max(MinHeight,2*Math.Max(12,a.Padding*.65)+LyricSurface.CurrentHeight+next);
        return 60+twoHeight-ActualHeight+LyricSurface.CurrentCenterY-(twoHeight-next)/2;
    }
    internal double CurrentScreenY=>NativeWindow.Location(this).Top+LyricSurface.CurrentCenterY*VisualTreeHelper.GetDpi(this).DpiScaleY;
    internal double CurrentCenterOffset=>LyricSurface.CurrentCenterY-ActualHeight/2;
    internal void RestorePlacement(double? left,double? top)=>NativeWindow.Place(this,left,top,true,DefaultBottomInset());
    public void ResetPosition() { settings.OverlayLeft=null; settings.OverlayTop=null;UpdateLayout();NativeWindow.Place(this,null,null,true,DefaultBottomInset()); }
}
