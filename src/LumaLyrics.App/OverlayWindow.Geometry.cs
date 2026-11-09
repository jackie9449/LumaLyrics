using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using LumaLyrics.Infrastructure;
using Screen=System.Windows.Forms.Screen;
namespace LumaLyrics.App;
public partial class OverlayWindow
{
    bool constraining;
    DispatcherOperation? geometryRefresh;
    internal string FunctionPlacement {get;private set;}="";
    internal string PlaybackPlacement {get;private set;}="";
    [StructLayout(LayoutKind.Sequential)]struct WindowPosition{public nint Window,After;public int X,Y,Width,Height;public uint Flags;}
    static OverlayBounds Box(System.Drawing.Rectangle r)=>new(r.Left,r.Top,r.Width,r.Height);
    void ConstrainPosition(nint address)
    {
        if(address==0)return;var position=Marshal.PtrToStructure<WindowPosition>(address);
        if((position.Flags&3)==3)return;
        var current=NativeWindow.Bounds(this);bool noMove=(position.Flags&2)!=0,noSize=(position.Flags&1)!=0;
        var proposed=new System.Drawing.Rectangle(noMove?current.Left:position.X,noMove?current.Top:position.Y,Math.Max(1,noSize?current.Width:position.Width),Math.Max(1,noSize?current.Height:position.Height));
        var limited=OverlayScreenLayout.Contain(Box(proposed),Box(Screen.FromRectangle(proposed).WorkingArea));
        if(limited.Left!=proposed.Left||limited.Top!=proposed.Top){position.Flags&=~2u;position.X=(int)Math.Round(limited.Left);position.Y=(int)Math.Round(limited.Top);}
        if(limited.Width!=proposed.Width||limited.Height!=proposed.Height){position.Flags&=~1u;position.Width=(int)Math.Floor(limited.Width);position.Height=(int)Math.Floor(limited.Height);}
        Marshal.StructureToPtr(position,address,false);
    }
    void QueueGeometryRefresh()
    {
        if(closed||geometryRefresh is {Status:DispatcherOperationStatus.Pending})return;
        geometryRefresh=Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(()=>{geometryRefresh=null;if(!closed)EnsureVisibleBounds();}));
    }
    internal void EnsureVisibleBounds()
    {
        if(constraining||closed||nativeSource is null)return;constraining=true;
        try
        {
            var area=Screen.FromHandle(NativeWindow.Handle(this)).WorkingArea;var dpi=VisualTreeHelper.GetDpi(this);
            double maxWidth=area.Width/dpi.DpiScaleX,maxHeight=area.Height/dpi.DpiScaleY;
            MinWidth=Math.Min(360,maxWidth);MinHeight=Math.Min(90,maxHeight);MaxWidth=maxWidth;MaxHeight=maxHeight;
            NativeWindow.ClampVisible(this);UpdateToolbarLimits();RefreshPopupPlacement();
        }
        finally{constraining=false;}
    }
    internal void CenterHorizontally()
    {
        EnsureVisibleBounds();var bounds=NativeWindow.Bounds(this);var area=Screen.FromHandle(NativeWindow.Handle(this)).WorkingArea;
        NativeWindow.Place(this,OverlayScreenLayout.CenteredLeft(bounds.Width,Box(area)),bounds.Top);
        RememberPosition();RefreshPopupPlacement();
    }
    void CenterClicked(object sender,RoutedEventArgs e){e.Handled=true;CenterHorizontally();}
    void UpdateToolbarLimits()
    {
        var area=Screen.FromHandle(NativeWindow.Handle(this)).WorkingArea;var dpi=VisualTreeHelper.GetDpi(this);
        foreach(var bar in new[]{Toolbar,PlaybackToolbar}){bar.MaxWidth=area.Width/dpi.DpiScaleX;bar.MaxHeight=area.Height/dpi.DpiScaleY;bar.Measure(new Size(bar.MaxWidth,bar.MaxHeight));}
    }
    CustomPopupPlacement[] PlaceHoverPopup(bool playback,Size popup,Size target)
    {
        UpdateToolbarLimits();var area=Screen.FromHandle(NativeWindow.Handle(this)).WorkingArea;var dpi=VisualTreeHelper.GetDpi(LyricSurface);var origin=LyricSurface.PointToScreen(new());
        // WPF supplies device-pixel sizes to custom popup placement. DesiredSize is
        // still in DIPs; keep both popups and the monitor in the same physical space.
        var work=new OverlayBounds(area.Left-origin.X,area.Top-origin.Y,area.Width,area.Height);
        var functions=playback?new Size(Toolbar.DesiredSize.Width*dpi.DpiScaleX,Toolbar.DesiredSize.Height*dpi.DpiScaleY):popup;
        var transport=playback?popup:new Size(PlaybackToolbar.DesiredSize.Width*dpi.DpiScaleX,PlaybackToolbar.DesiredSize.Height*dpi.DpiScaleY);
        var placedControls=OverlayScreenLayout.Controls(new(0,0,target.Width,target.Height),new(0,0,functions.Width,functions.Height),new(0,0,transport.Width,transport.Height),work,6*dpi.DpiScaleY);
        var placement=playback?placedControls.Playback:placedControls.Functions;
        string diagnostic=$"target={target};popup={popup};function={functions};playback={transport};origin={origin};work={work};placed={placement}";
        if(playback)PlaybackPlacement=diagnostic;else FunctionPlacement=diagnostic;
        return[new(new Point(placement.Left,placement.Top),PopupPrimaryAxis.None)];
    }
    void RefreshPopupPlacement()
    {
        foreach(var popup in new[]{ToolbarPopup,PlaybackPopup})if(popup.IsOpen)
        {
            // Re-evaluate custom placement after a native position change without changing its final offset.
            double offset=popup.HorizontalOffset;popup.SetCurrentValue(Popup.HorizontalOffsetProperty,offset+.01);popup.SetCurrentValue(Popup.HorizontalOffsetProperty,offset);
        }
    }
}
