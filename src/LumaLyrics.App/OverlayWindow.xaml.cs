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
        MouseEnter+=(_,_)=>{toolbarDelay.Stop();if(!IsLocked)ToolbarPopup.IsOpen=true;};
        MouseLeave+=(_,_)=>toolbarDelay.Start();
        toolbarDelay.Tick+=(_,_)=>{toolbarDelay.Stop();if(!IsMouseOver && !Toolbar.IsMouseOver)ToolbarPopup.IsOpen=false;};
        IsVisibleChanged+=(_,_)=>{if(!IsVisible){ToolbarPopup.IsOpen=false;toolbarDelay.Stop();}};
        LocationChanged+=(_,_)=> {if(placed)RememberPosition();};
        SizeChanged+=(_,_)=> {if(placed)settings.OverlayWidth=Width;UpdateWindowShape();};
        DpiChanged+=(_,_)=>Dispatcher.BeginInvoke(DispatcherPriority.Loaded,UpdateWindowShape);
        Closed+=(_,_)=>{toolbarDelay.Stop();ToolbarPopup.IsOpen=false;backdrop.Dispose();};
    }
    public void ApplyStyle()
    {
        var a=OverlayAppearance.From(settings);
        double oldCenter=placed?LyricSurface.CurrentCenterY:0;var oldLocation=placed?NativeWindow.Location(this):default;
        Width=a.Width;SizeToContent=SizeToContent.Height;LyricSurface.Apply(a,settings.ReduceMotion);
        if(IsLoaded) {UpdateLayout();UpdateWindowShape();if(placed){var dpi=VisualTreeHelper.GetDpi(this);NativeWindow.Place(this,oldLocation.Left,oldLocation.Top+(oldCenter-LyricSurface.CurrentCenterY)*dpi.DpiScaleY);}}
    }
    void UpdateWindowShape()
    {
        if(ActualWidth<=0 || ActualHeight<=0)return;
        var a=OverlayAppearance.From(settings);
        // Include the resize handle and every other window visual in the same silhouette.
        WindowSurface.OpacityMask=OverlaySurface.RoundedMask(WindowSurface.RenderSize,a.Radius);
        backdrop.Apply(a);
    }
    public void SetLocked(bool locked,bool showTip=true)
    {
        if(locked && !owner.CanRecoverOverlay) { MessageBox.Show("托盘暂不可用，无法安全锁定。请先重新连接托盘。","浮光歌词"); return; }
        if(locked && firstLock && showTip) { MessageBox.Show($"锁定后可点击穿透。\n从托盘选择“解锁歌词”，或按 {settings.UnlockHotkey} 解锁。","桌面歌词"); firstLock=false; }
        IsLocked=locked; NativeWindow.ClickThrough(this,locked);ToolbarPopup.IsOpen=false;ResizeHandle.Visibility=locked?Visibility.Collapsed:Visibility.Visible; ResizeMode=locked?ResizeMode.NoResize:ResizeMode.CanResizeWithGrip;ApplyStyle();
    }
    void DragOverlay(object sender,MouseButtonEventArgs e) { if(!IsLocked && !ResizeHandle.IsMouseOver && e.ButtonState==MouseButtonState.Pressed) { try {ToolbarPopup.IsOpen=false;DragMove();NativeWindow.ClampVisible(this); } catch(InvalidOperationException) { } } }
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
