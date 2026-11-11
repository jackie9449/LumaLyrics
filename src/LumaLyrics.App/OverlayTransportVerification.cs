using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

public partial class MainWindow
{
    async Task VerifyOverlayTransportAsync(string directory,Dictionary<string,object?> checks)
    {
        var appearance=new AppSettings{OverlayLeft=140,OverlayTop=220,ReduceMotion=settings.ReduceMotion};
        var text=new PlayerViewModel{Current="把此刻，轻轻唱成一束光。",Next="让旋律停留在你身旁",CanPlay=true,CanNext=true,CanPrevious=true,IsPlaying=true};
        var requests=new List<string>();var pending=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task Send(string action){requests.Add(action);return requests.Count==1?pending.Task:Task.CompletedTask;}
        var floating=new OverlayWindow(this,text,appearance,Send){Owner=this};
        async Task Settle(){floating.UpdateLayout();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(65);}
        async Task WaitUntil(Func<bool> ready)
        {var deadline=DateTime.UtcNow.AddSeconds(2);while(!ready() && DateTime.UtcNow<deadline){await Task.Delay(40);await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);}}
        void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        void CaptureControls(string file)
        {
            FrameworkElement[] views=[floating.Toolbar,floating.LyricSurface];var scale=VisualTreeHelper.GetDpi(floating);
            var points=views.Select(v=>v.PointToScreen(new())).ToArray();double left=points.Min(p=>p.X),top=points.Min(p=>p.Y);
            var canvas=new Canvas{Background=new SolidColorBrush(Color.FromRgb(24,37,45))};double width=0,height=0;
            for(int i=0;i<views.Length;i++)
            {
                var view=views[i];double x=20+(points[i].X-left)/scale.DpiScaleX,y=20+(points[i].Y-top)/scale.DpiScaleY;
                var image=new Image{Source=VisualVerification.Render(view),Width=view.ActualWidth,Height=view.ActualHeight};Canvas.SetLeft(image,x);Canvas.SetTop(image,y);canvas.Children.Add(image);
                width=Math.Max(width,x+view.ActualWidth+20);height=Math.Max(height,y+view.ActualHeight+20);
            }
            canvas.Width=width;canvas.Height=height;canvas.Measure(new Size(width,height));canvas.Arrange(new Rect(0,0,width,height));VisualVerification.Save(canvas,Path.Combine(directory,file));
        }
        string theme=settings.UiTheme;
        try
        {
            floating.Show();floating.ApplyStyle();floating.SetLocked(false,false);await Settle();floating.ShowHoverControls();await Settle();
            checks["overlayTransport.unlockedHoverShowsBothBars"]=floating.ToolbarPopup.IsOpen;
            var bottom=floating.LyricSurface.PointToScreen(new(0,floating.LyricSurface.ActualHeight));var bar=floating.PlaybackToolbar.PointToScreen(new());
            checks["overlayTransport.mergedToolbarAboveLyrics"]=floating.Toolbar.PointToScreen(new(0,floating.Toolbar.ActualHeight)).Y<=floating.LyricSurface.PointToScreen(new()).Y;
            var toolbarCenter=floating.PlaybackToolbar.PointToScreen(new(floating.PlaybackToolbar.ActualWidth/2,0));var lyricCenter=floating.LyricSurface.PointToScreen(new(floating.LyricSurface.ActualWidth/2,0));
            checks["overlayTransport.centeredUnderLyrics"]=Math.Abs(toolbarCenter.X-lyricCenter.X)<2;
            floating.PlaybackToolbar.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(floating),0,Key.Escape){RoutedEvent=Keyboard.PreviewKeyDownEvent});
            checks["overlayTransport.escapeClosesControls"]=!floating.ToolbarPopup.IsOpen;
            var monitor=System.Windows.Forms.Screen.FromHandle(NativeWindow.Handle(floating)).WorkingArea;
            var dpi=VisualTreeHelper.GetDpi(floating);floating.HideHoverControls();NativeWindow.Place(floating,monitor.Left+140,monitor.Bottom-floating.ActualHeight*dpi.DpiScaleY-2);
            floating.ShowHoverControls();await Settle();var edge=floating.PlaybackToolbar.PointToScreen(new());
            checks["overlayTransport.screenEdgeKeepsControlsVisible"]=edge.Y>=monitor.Top && edge.Y+floating.PlaybackToolbar.ActualHeight*dpi.DpiScaleY<=monitor.Bottom && edge.X>=monitor.Left && edge.X+floating.PlaybackToolbar.ActualWidth*dpi.DpiScaleX<=monitor.Right;
            var lyricTop=floating.LyricSurface.PointToScreen(new());var functionTop=floating.Toolbar.PointToScreen(new());
            checks["overlayTransport.bottomGeometry"]=$"dpi={dpi.DpiScaleX}/{dpi.DpiScaleY};screen={monitor};lyric={lyricTop}/{floating.LyricSurface.RenderSize};function={functionTop}/{floating.Toolbar.RenderSize};playback={edge}/{floating.PlaybackToolbar.RenderSize}";
            checks["overlayTransport.bottomFunctionPlacement"]=floating.ToolbarPlacement;checks["overlayTransport.bottomPlaybackPlacement"]=floating.ToolbarPlacement;
            checks["overlayTransport.bottomToolbarAdjacentToLyrics"]=Math.Abs(lyricTop.Y-(functionTop.Y+floating.Toolbar.ActualHeight*dpi.DpiScaleY)-6*dpi.DpiScaleY)<2;
            checks["overlayTransport.transportInsideMergedToolbar"]=floating.Toolbar.IsAncestorOf(floating.PlaybackToolbar);
            floating.CenterButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));await Settle();
            var centered=NativeWindow.Bounds(floating);
            checks["overlayTransport.horizontalCenterPreservesTop"]=Math.Abs(centered.Left-(monitor.Left+(monitor.Width-centered.Width)/2d))<=1 && Math.Abs(centered.Top-lyricTop.Y)<2;
            checks["overlayTransport.centerRemembersPosition"]=appearance.OverlayLeft==centered.Left && appearance.OverlayTop==centered.Top && !floating.IsLocked && floating.IsVisible;
            floating.HideHoverControls();floating.Left=monitor.Right+500;floating.Top=monitor.Bottom+500;await Settle();
            var constrained=NativeWindow.Bounds(floating);
            checks["overlayTransport.programmaticMoveCannotLeaveScreen"]=monitor.Contains(constrained);
            floating.Width=monitor.Width/dpi.DpiScaleX+500;await Settle();
            checks["overlayTransport.oversizedWidthContained"]=monitor.Contains(NativeWindow.Bounds(floating));
            appearance.OverlayWidth=360;floating.ApplyStyle();NativeWindow.Place(floating,monitor.Left,monitor.Top);await Settle();floating.ShowHoverControls();await Settle();
            var functionBounds=new Rect(floating.Toolbar.PointToScreen(new()),new Size(floating.Toolbar.ActualWidth*dpi.DpiScaleX,floating.Toolbar.ActualHeight*dpi.DpiScaleY));
            var playbackBounds=new Rect(floating.PlaybackToolbar.PointToScreen(new()),new Size(floating.PlaybackToolbar.ActualWidth*dpi.DpiScaleX,floating.PlaybackToolbar.ActualHeight*dpi.DpiScaleY));
            var workBounds=new Rect(monitor.Left,monitor.Top,monitor.Width,monitor.Height);
            checks["overlayTransport.topNarrowControlsContained"]=workBounds.Contains(functionBounds) && workBounds.Contains(playbackBounds);
            checks["overlayTransport.topNarrowToolbarContainsPlayback"]=functionBounds.Contains(playbackBounds);
            var narrowLyrics=new Rect(floating.LyricSurface.PointToScreen(new()),new Size(floating.LyricSurface.ActualWidth*dpi.DpiScaleX,floating.LyricSurface.ActualHeight*dpi.DpiScaleY));
            checks["overlayTransport.topControlsLeaveLyricsReadable"]=!functionBounds.IntersectsWith(narrowLyrics) && !playbackBounds.IntersectsWith(narrowLyrics);
            CaptureControls("floating-unified-narrow.png");
            foreach(double width in new[]{360d,820d})
            {
                appearance.OverlayWidth=width;floating.ApplyStyle();await Settle();
                var size=NativeWindow.Bounds(floating);
                foreach(var (name,x,y) in new[]{("topLeft",monitor.Left,monitor.Top),("topRight",monitor.Right-size.Width,monitor.Top),("bottomLeft",monitor.Left,monitor.Bottom-size.Height),("bottomRight",monitor.Right-size.Width,monitor.Bottom-size.Height)})
                {
                    NativeWindow.Place(floating,x,y);await Settle();floating.ShowHoverControls();await Settle();
                    var bounds=new Rect(floating.Toolbar.PointToScreen(new()),new Size(floating.Toolbar.ActualWidth*dpi.DpiScaleX,floating.Toolbar.ActualHeight*dpi.DpiScaleY));
                    var lyrics=new Rect(floating.LyricSurface.PointToScreen(new()),new Size(floating.LyricSurface.ActualWidth*dpi.DpiScaleX,floating.LyricSurface.ActualHeight*dpi.DpiScaleY));
                    var playCenter=floating.OverlayPlayPause.PointToScreen(new(floating.OverlayPlayPause.ActualWidth/2,0));
                    checks[$"overlayTransport.{width}.{name}.contained"]=workBounds.Contains(bounds);
                    checks[$"overlayTransport.{width}.{name}.centered"]=Math.Abs(playCenter.X-(lyrics.Left+lyrics.Width/2))<2;
                    checks[$"overlayTransport.{width}.{name}.attached"]=Math.Min(Math.Abs(lyrics.Top-bounds.Bottom-6*dpi.DpiScaleY),Math.Abs(bounds.Top-lyrics.Bottom-6*dpi.DpiScaleY))<2;
                }
            }
            appearance.OverlayWidth=820;floating.ApplyStyle();
            floating.HideHoverControls();NativeWindow.Place(floating,140,220);floating.ShowHoverControls();await Settle();
            floating.Toolbar.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=Mouse.MouseEnterEvent});
            checks["overlayTransport.threeAccessibleTargets"]=new[]{floating.OverlayPrevious,floating.OverlayPlayPause,floating.OverlayNext}.All(button=>button.ActualWidth>=44 && button.ActualHeight>=44 && !string.IsNullOrWhiteSpace(System.Windows.Automation.AutomationProperties.GetName(button)));
            checks["overlayTransport.playingShowsPause"]=Equals(floating.OverlayPlayPause.Content,"\uE769") && Equals(floating.OverlayPlayPause.ToolTip,"暂停");
            text.IsPlaying=false;await Settle();
            checks["overlayTransport.pausedShowsPlay"]=Equals(floating.OverlayPlayPause.Content,"\uE768") && Equals(floating.OverlayPlayPause.ToolTip,"播放");
            text.CanPlay=text.CanNext=text.CanPrevious=false;await Settle();
            checks["overlayTransport.unavailableActionsDisabled"]=!floating.OverlayPrevious.IsEnabled && !floating.OverlayPlayPause.IsEnabled && !floating.OverlayNext.IsEnabled;
            text.CanPlay=text.CanNext=text.CanPrevious=true;await Settle();
            var feedback=(Border)floating.OverlayNext.Template.FindName("Feedback",floating.OverlayNext);
            floating.ShowHoverControls();await WaitUntil(()=>floating.OverlayNext.IsLoaded);
            floating.Toolbar.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=Mouse.MouseEnterEvent});
            AppMotion.PreviewFeedback(floating.OverlayNext,true);await WaitUntil(()=>feedback.Opacity>.9);
            checks["overlayTransport.hoverFeedback"]=feedback.Opacity>.9;
            AppMotion.PreviewFeedback(floating.OverlayNext,false);await WaitUntil(()=>feedback.Opacity<.01 && !floating.OverlayNext.HasAnimatedProperties);
            checks["overlayTransport.hoverResetOpacity"]=feedback.Opacity;
            checks["overlayTransport.hoverResetMotion"]=AppMotion.GetHover(floating.OverlayNext);
            checks["overlayTransport.nativePointerOverNext"]=floating.OverlayNext.IsMouseOver.ToString();
            checks["overlayTransport.hoverReleasesClocks"]=feedback.Opacity<.01 && !floating.OverlayNext.HasAnimatedProperties;
            ((IInvokeProvider)new ButtonAutomationPeer(floating.OverlayPrevious).GetPattern(PatternInterface.Invoke)!).Invoke();await Settle();
            Click(floating.OverlayNext);
            checks["overlayTransport.serializesPendingAction"]=requests.SequenceEqual(["previous"]) && !floating.PlaybackToolbar.IsEnabled;
            pending.TrySetResult();await floating.ActiveTransport!;await Settle();Click(floating.OverlayPlayPause);await floating.ActiveTransport!;Click(floating.OverlayNext);await floating.ActiveTransport!;
            checks["overlayTransport.exactSingleCommands"]=requests.SequenceEqual(["previous","toggle","next"]) && floating.PlaybackToolbar.IsEnabled;
            floating.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=Mouse.MouseLeaveEvent});
            floating.Toolbar.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=Mouse.MouseEnterEvent});await Task.Delay(230);
            checks["overlayTransport.crossingGapRetainsControls"]=floating.ToolbarPopup.IsOpen;
            floating.Toolbar.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=Mouse.MouseLeaveEvent});await WaitUntil(()=>!floating.ToolbarPopup.IsOpen);
            checks["overlayTransport.pointerOverBarsOnExit"]=$"{floating.IsMouseOver}/{floating.Toolbar.IsMouseOver}/{floating.PlaybackToolbar.IsMouseOver}";
            checks["overlayTransport.leavingClosesBothBars"]=!floating.ToolbarPopup.IsOpen;
            floating.ShowHoverControls();floating.SetLocked(true,false);floating.ShowHoverControls();
            checks["overlayTransport.lockedHasNoControls"]=floating.IsLocked && !floating.ToolbarPopup.IsOpen;
            floating.SetLocked(false,false);floating.SetInstrumental(true);floating.ShowHoverControls();
            checks["overlayTransport.instrumentalHasNoControls"]=floating.InstrumentalSuppressed;
            floating.SetInstrumental(false);floating.ShowHoverControls();floating.BeginAppearanceEditing();floating.ShowHoverControls();
            checks["overlayTransport.appearanceEditingHasNoControls"]=!floating.ToolbarPopup.IsOpen;
            floating.EndAppearanceEditing();floating.ShowHoverControls();floating.Hide();
            checks["overlayTransport.hideClosesBothBars"]=!floating.ToolbarPopup.IsOpen;
            floating.Show();appearance.ReduceMotion=true;floating.ApplyStyle();await Settle();floating.ShowHoverControls();await WaitUntil(()=>floating.OverlayNext.IsLoaded);
            floating.Toolbar.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=Mouse.MouseEnterEvent});
            checks["overlayTransport.reducedMotionNoPopupAnimation"]=floating.ToolbarPopup.PopupAnimation==System.Windows.Controls.Primitives.PopupAnimation.None;
            AppMotion.PreviewFeedback(floating.OverlayNext,false);AppMotion.PreviewFeedback(floating.OverlayNext,true,true);
            checks["overlayTransport.reducedMotionState"]=$"enabled={AppMotion.GetAnimationsEnabled(floating.OverlayNext)},hover={AppMotion.GetHover(floating.OverlayNext)},pressed={AppMotion.GetPressed(floating.OverlayNext)},clocks={floating.OverlayNext.HasAnimatedProperties},scope={AppMotion.GetEnabled(floating.OverlayNext)},loaded={floating.OverlayNext.IsLoaded},visible={floating.OverlayNext.IsVisible},usable={floating.OverlayNext.IsEnabled},open={floating.ToolbarPopup.IsOpen}";
            checks["overlayTransport.reducedMotionFeedbackIsImmediate"]=AppMotion.GetHover(floating.OverlayNext)==1 && AppMotion.GetPressed(floating.OverlayNext)==1 && !floating.OverlayNext.HasAnimatedProperties;
            AppMotion.PreviewFeedback(floating.OverlayNext,false);
            string paint=floating.Toolbar.Background.ToString();
            foreach(var app in AppThemes.All){SelectUiTheme(app.Id,false);checks[$"overlayTransport.{app.Id}.independentAppearance"]=floating.Toolbar.Background.ToString()==paint;}
            foreach(var overlayTheme in OverlayThemes.All)
            {
                (overlayTheme.Appearance with{Width=820,Background="#342545",Color="#F1EEE8",Accent="#F8A84A"}).Apply(appearance);floating.ApplyStyle();await Settle();floating.ShowHoverControls();await Settle();
                checks[$"overlayTransport.{overlayTheme.Tag}.customToolbarPalette"]=floating.Toolbar.Background.ToString()=="#FF342545" && floating.PlaybackToolbar.Background.ToString()=="#00FFFFFF" && floating.CenterButton.Foreground.ToString()=="#FFF1EEE8" && floating.FindResource("AccentBrush").ToString()=="#FFF8A84A";
                if(overlayTheme.Tag is "PAPER" or "GLASS")
                {
                    (overlayTheme.Appearance with{Width=820,AdaptiveContrast=false}).Apply(appearance);floating.ApplyStyle();await Settle();floating.HideHoverControls();NativeWindow.Place(floating,monitor.Left+140,monitor.Bottom-floating.ActualHeight*dpi.DpiScaleY-2);floating.ShowHoverControls();await Settle();
                    CaptureControls($"floating-bottom-{overlayTheme.Tag.ToLowerInvariant()}.png");
                }
            }
            text.IsPlaying=true;floating.ShowHoverControls();await Settle();
            var preview=new StackPanel{Margin=new(26)};
            preview.Children.Add(new TextBlock{Text="未锁定 · 悬停播放控制",Foreground=Brushes.WhiteSmoke,FontSize=15,Margin=new(0,0,0,16)});
            preview.Children.Add(new Image{Source=VisualVerification.Render(floating.LyricSurface),Width=820});
            preview.Children.Add(new Image{Source=VisualVerification.Render(floating.Toolbar),Width=floating.Toolbar.ActualWidth,HorizontalAlignment=HorizontalAlignment.Center,Margin=new(0,6,0,0)});
            var screenshot=new Window{Owner=this,Content=preview,Width=900,SizeToContent=SizeToContent.Height,ShowInTaskbar=false,Background=new SolidColorBrush(Color.FromRgb(24,37,45))};
            screenshot.Show();await Settle();VisualVerification.Save(screenshot,Path.Combine(directory,"floating-playback-controls.png"));screenshot.Close();
            floating.HideHoverControls();floating.Close();
            checks["overlayTransport.closeCleansPopups"]=!floating.ToolbarPopup.IsOpen;
        }
        finally{pending.TrySetResult();if(floating.IsVisible)floating.Close();SelectUiTheme(theme,false);}
    }
}
