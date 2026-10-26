using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LumaLyrics.Infrastructure;

namespace LumaLyrics.App;

public partial class MainWindow
{
    async Task VerifySeekSlidersAsync(string directory,Dictionary<string,object?> checks)
    {
        var original=settings.UiTheme;var reduce=settings.ReduceMotion;var floating=OverlayAppearance.From(settings);overlay.ApplyStyle();var toolbar=overlay.Toolbar.Background.ToString();
        try
        {
            UpdateLayout();await Task.Delay(400);
            vm.Duration=180000;vm.CanSeek=true;vm.Position=0;
            SeekSlider.SetCurrentValue(System.Windows.Controls.Primitives.RangeBase.ValueProperty,90000d);Tick();
            checks["seek.mainRestoresUnchangedBindingAfterRejectedPreview"]=SeekSlider.Value==0;
            SeekSlider.BeginEdit();SeekSlider.SetCurrentValue(System.Windows.Controls.Primitives.RangeBase.ValueProperty,60000d);Tick();
            checks["seek.mainTickPreservesDragPreview"]=SeekSlider.IsEditing && SeekSlider.Value==60000 && vm.Time.StartsWith("01:00");SeekSlider.CancelEdit();
            checks["seek.mainCancelRestoresActualTimeline"]=SeekSlider.Value==0 && !SeekSlider.IsEditing;Update();
            foreach(var theme in AppThemes.All)
            {
                SelectUiTheme(theme.Id);AppMotion.Configure(false);
                int active=AppMotion.ActiveFeedbackCount,ranges=RangeMotion.ActiveCount;
                var seek=new SeekSlider{Minimum=0,Maximum=180000,Value=30000,SmallChange=5000,LargeChange=10000};seek.SetResourceReference(StyleProperty,typeof(Slider));
                var regular=new Slider{Minimum=0,Maximum=100,Value=45,Margin=new(0,12,0,12)};
                var progress=new ProgressBar{Minimum=0,Maximum=100,Value=25,Height=6,Margin=new(0,16,0,16)};
                var busy=new ProgressBar{IsIndeterminate=true,Height=6};
                var panel=new StackPanel{Margin=new(28)};panel.SetResourceReference(Panel.BackgroundProperty,"WindowBrush");panel.Children.Add(new TextBlock{Text=theme.Name+" · 播放进度与滑条",FontSize=22,Margin=new(0,0,0,18)});panel.Children.Add(new TextBlock{Text="拖动预览 · 松开跳转 · Esc 取消"});panel.Children.Add(seek);panel.Children.Add(regular);panel.Children.Add(progress);panel.Children.Add(busy);
                var gallery=new Window{Owner=this,Width=580,Height=320,Content=panel,ShowInTaskbar=false};AppMotion.SetEnabled(gallery,true);gallery.SetResourceReference(BackgroundProperty,"WindowBrush");gallery.SetResourceReference(ForegroundProperty,"TextBrush");ThemeManager.Apply(theme.Id,applicationScope:gallery.Resources);gallery.Show();await Task.Delay(250);gallery.UpdateLayout();
                try
                {
                    string key="ranges."+theme.Id+".";int commits=0,cancels=0;double committed=0;seek.EditCommitted+=(_,_)=>{commits++;committed=seek.Value;};seek.EditCancelled+=(_,_)=>cancels++;
                    checks[key+"sharedTemplate"]=seek.Template.FindName("PART_Track",seek) is Track && regular.Style.BasedOn==FindResource("LumaSliderStyle");
                    seek.BeginEdit();seek.MoveTo(new Point(seek.ActualWidth*.7,seek.ActualHeight/2));double first=seek.Value;seek.MoveTo(new Point(seek.ActualWidth*.8,seek.ActualHeight/2));
                    checks[key+"dragPreviewsWithoutCommit"]=seek.IsEditing && first>100000 && seek.Value>first && commits==0;seek.CommitEdit();seek.CommitEdit();checks[key+"oneCommitOnRelease"]=commits==1 && committed>140000 && !seek.IsEditing;
                    seek.BeginEdit();seek.MoveTo(new Point(-100,14));checks[key+"zeroEndpoint"]=seek.Value==0;seek.MoveTo(new Point(seek.ActualWidth+100,14));checks[key+"endEndpoint"]=seek.Value==seek.Maximum;seek.CancelEdit();checks[key+"cancelRestoresValue"]=cancels==1 && commits==1 && seek.Value==committed;
                    seek.BeginEdit();seek.Value=70000;seek.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(seek),0,Key.Escape){RoutedEvent=Keyboard.PreviewKeyDownEvent});checks[key+"escapeCancels"]=!seek.IsEditing && cancels==2 && commits==1;
                    seek.BeginEdit();seek.IsEnabled=false;checks[key+"disabledCancels"]=!seek.IsEditing && cancels==3;seek.IsEnabled=true;
                    seek.BeginEdit();seek.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=Mouse.LostMouseCaptureEvent});checks[key+"captureLossCancels"]=!seek.IsEditing && cancels==4;
                    var source=PresentationSource.FromVisual(seek);seek.Value=60000;
                    seek.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,source,0,Key.Right){RoutedEvent=Keyboard.PreviewKeyDownEvent});seek.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,source,0,Key.Right){RoutedEvent=Keyboard.KeyDownEvent});seek.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,source,0,Key.Right){RoutedEvent=Keyboard.PreviewKeyUpEvent});
                    checks[key+"keyboardCommits"]=commits==2 && !seek.IsEditing && seek.Value==65000;
                    AppMotion.PreviewFeedback(regular,true);await Task.Delay(70);double mid=AppMotion.GetHover(regular);checks[key+"hoverHasIntermediateFrames"]=!AppMotion.Allowed || mid>0 && mid<1;AppMotion.PreviewFeedback(regular,false);double interrupted=AppMotion.GetHover(regular);checks[key+"hoverInterruptionIsContinuous"]=Math.Abs(mid-interrupted)<.15;AppMotion.PreviewFeedback(regular,true,true);await Task.Delay(260);
                    var handle=(Thumb)regular.Template.FindName("Handle",regular);handle.ApplyTemplate();var visual=(Grid)handle.Template.FindName("InteractionVisual",handle);checks[key+"dragFeedbackFitsThumb"]=AppMotion.GetPressed(regular)==1 && visual.RenderTransform is ScaleTransform{ScaleX:>1.3} && visual.ActualWidth*((ScaleTransform)visual.RenderTransform).ScaleX<=handle.ActualWidth;
                    VisualVerification.Save(panel,Path.Combine(directory,"ranges-"+theme.Id+".png"));
                    progress.Value=75;gallery.UpdateLayout();var indicator=(FrameworkElement)progress.Template.FindName("PART_Indicator",progress);var scale=(ScaleTransform)indicator.RenderTransform;checks[key+"determinateFillAnimates"]=!AppMotion.Allowed || scale.HasAnimatedProperties;await Task.Delay(240);checks[key+"completedFillReleasesClock"]=!scale.HasAnimatedProperties && scale.ScaleX==1;
                    var pulse=(FrameworkElement)busy.Template.FindName("Pulse",busy);var travel=(TranslateTransform)pulse.RenderTransform;double x=travel.X;await Task.Delay(90);checks[key+"busyMovesWithinOwnWidth"]=!AppMotion.Allowed || travel.HasAnimatedProperties && travel.X!=x && travel.X<=busy.ActualWidth;
                    busy.Visibility=Visibility.Collapsed;checks[key+"hiddenBusyStops"]=!travel.HasAnimatedProperties;busy.Visibility=Visibility.Visible;AppMotion.Configure(true);AppMotion.PreviewFeedback(regular,true,true);progress.Value=90;gallery.UpdateLayout();
                    checks[key+"reducedMotionImmediate"]=AppMotion.GetPressed(regular)==1 && !regular.HasAnimatedProperties && !scale.HasAnimatedProperties && !travel.HasAnimatedProperties && pulse.IsVisible;
                    AppMotion.Configure(false);seek.BeginEdit();gallery.Hide();checks[key+"hiddenSeekCancels"]=!seek.IsEditing;
                }
                finally{gallery.Close();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);}
                checks["ranges."+theme.Id+".unloadReleasesSubscriptions"]=AppMotion.ActiveFeedbackCount==active && RangeMotion.ActiveCount==ranges;
                if(AppMotion.ActiveFeedbackCount!=active || RangeMotion.ActiveCount!=ranges)checks["ranges."+theme.Id+".subscriptionCounts"]=new{before=active,after=AppMotion.ActiveFeedbackCount,rangesBefore=ranges,rangesAfter=RangeMotion.ActiveCount};
            }
            checks["ranges.overlayRemainsIndependent"]=floating==OverlayAppearance.From(settings) && toolbar==overlay.Toolbar.Background.ToString() && !AppMotion.GetEnabled(overlay);
        }
        finally{SelectUiTheme(original);AppMotion.Configure(reduce);}
    }
}
