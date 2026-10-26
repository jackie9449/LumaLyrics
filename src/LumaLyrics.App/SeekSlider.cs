using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace LumaLyrics.App;

/// <summary>Edits a timeline locally and commits once, independently of playback refreshes.</summary>
public sealed class SeekSlider:Slider
{
    static readonly DependencyPropertyKey editingKey=DependencyProperty.RegisterReadOnly(nameof(IsEditing),typeof(bool),typeof(SeekSlider),new PropertyMetadata(false));
    public static readonly DependencyProperty IsEditingProperty=editingKey.DependencyProperty;
    public bool IsEditing=>(bool)GetValue(IsEditingProperty);
    public event EventHandler? EditStarted;
    public event EventHandler? EditCommitted;
    public event EventHandler? EditCancelled;
    readonly ToolTip targetTip=new(){Placement=PlacementMode.Top,StaysOpen=true,Focusable=false};
    double original;
    bool releasing;
    public SeekSlider()
    {
        Unloaded+=(_,_)=>CancelEdit();IsEnabledChanged+=(_,_)=>{if(!IsEnabled)CancelEdit();};IsVisibleChanged+=(_,_)=>{if(!IsVisible)CancelEdit();};
        ValueChanged+=(_,_)=>{if(IsEditing)UpdateTip();};
    }
    internal void BeginEdit()
    {
        if(IsEditing || !IsEnabled)return;original=Value;SetValue(editingKey,true);EditStarted?.Invoke(this,EventArgs.Empty);UpdateTip();
    }
    void UpdateTip(){targetTip.Content=TimeSpan.FromMilliseconds(Math.Max(0,Value)).ToString(Value>=3600000?@"h\:mm\:ss":@"mm\:ss");targetTip.PlacementTarget=this;targetTip.IsOpen=IsLoaded && IsVisible;}
    internal void MoveTo(Point point)
    {
        if(!IsEditing || Template.FindName("PART_Track",this) is not Track track)return;
        var local=TranslatePoint(point,track);bool vertical=Orientation==Orientation.Vertical;
        double length=vertical?track.ActualHeight:track.ActualWidth,thumb=vertical?track.Thumb.ActualHeight:track.Thumb.ActualWidth;
        if(length<=thumb)return;
        double ratio=Math.Clamp(((vertical?local.Y:local.X)-thumb/2)/(length-thumb),0,1);
        if(vertical!=IsDirectionReversed)ratio=1-ratio;
        var value=Minimum+(Maximum-Minimum)*ratio;
        if(double.IsFinite(value))SetCurrentValue(ValueProperty,Math.Clamp(value,Minimum,Maximum));
    }
    internal void CommitEdit()=>Finish(false);
    public void CancelEdit()=>Finish(true);
    void Finish(bool cancel)
    {
        if(!IsEditing)return;SetValue(editingKey,false);targetTip.IsOpen=false;
        releasing=true;try{if(IsMouseCaptured)ReleaseMouseCapture();}finally{releasing=false;}
        if(cancel){SetCurrentValue(ValueProperty,original);EditCancelled?.Invoke(this,EventArgs.Empty);}else EditCommitted?.Invoke(this,EventArgs.Empty);
    }
    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if(!IsEnabled)return;Focus();BeginEdit();
        if(!CaptureMouse()){CancelEdit();return;}MoveTo(e.GetPosition(this));e.Handled=true;
    }
    protected override void OnPreviewMouseMove(MouseEventArgs e){base.OnPreviewMouseMove(e);if(IsEditing && IsMouseCaptured){if(e.LeftButton==MouseButtonState.Pressed)MoveTo(e.GetPosition(this));else CancelEdit();}}
    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e){if(IsEditing && IsMouseCaptured){MoveTo(e.GetPosition(this));CommitEdit();e.Handled=true;}else base.OnPreviewMouseLeftButtonUp(e);}
    protected override void OnLostMouseCapture(MouseEventArgs e){base.OnLostMouseCapture(e);if(!releasing)CancelEdit();}
    static bool SeekKey(Key key)=>key is Key.Left or Key.Right or Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End;
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if(e.Key==Key.Escape && IsEditing){CancelEdit();e.Handled=true;return;}
        if(SeekKey(e.Key) && Keyboard.Modifiers==ModifierKeys.None)BeginEdit();base.OnPreviewKeyDown(e);
    }
    protected override void OnPreviewKeyUp(KeyEventArgs e){base.OnPreviewKeyUp(e);if(SeekKey(e.Key) && IsEditing)CommitEdit();}
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e){base.OnLostKeyboardFocus(e);if(IsEditing && !IsMouseCaptured)CommitEdit();}
}
