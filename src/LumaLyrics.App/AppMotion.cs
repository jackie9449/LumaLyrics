using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace LumaLyrics.App;

/// <summary>One interruptible motion vocabulary for the application, opt-in by window scope.</summary>
public static class AppMotion
{
    public static readonly DependencyProperty EnabledProperty=DependencyProperty.RegisterAttached("Enabled",typeof(bool),typeof(AppMotion),new FrameworkPropertyMetadata(false,FrameworkPropertyMetadataOptions.Inherits,ScopeChanged));
    public static readonly DependencyProperty AnimationsEnabledProperty=DependencyProperty.RegisterAttached("AnimationsEnabled",typeof(bool),typeof(AppMotion),new FrameworkPropertyMetadata(true,FrameworkPropertyMetadataOptions.Inherits));
    public static readonly DependencyProperty HoverProperty=DependencyProperty.RegisterAttached("Hover",typeof(double),typeof(AppMotion),new PropertyMetadata(0d));
    public static readonly DependencyProperty PressedProperty=DependencyProperty.RegisterAttached("Pressed",typeof(double),typeof(AppMotion),new PropertyMetadata(0d));
    public static readonly DependencyProperty FocusProperty=DependencyProperty.RegisterAttached("Focus",typeof(double),typeof(AppMotion),new PropertyMetadata(0d));
    public static readonly DependencyProperty SelectedProperty=DependencyProperty.RegisterAttached("Selected",typeof(double),typeof(AppMotion),new PropertyMetadata(0d));
    public static readonly DependencyProperty AvailabilityProperty=DependencyProperty.RegisterAttached("Availability",typeof(double),typeof(AppMotion),new PropertyMetadata(1d));
    public static readonly DependencyProperty IsSelectedProperty=DependencyProperty.RegisterAttached("IsSelected",typeof(bool),typeof(AppMotion),new PropertyMetadata(false,SelectionChanged));
    public static readonly DependencyProperty CornerRadiusProperty=DependencyProperty.RegisterAttached("CornerRadius",typeof(CornerRadius),typeof(AppMotion),new PropertyMetadata(new CornerRadius(12)));
    public static readonly DependencyProperty FeedbackBrushProperty=DependencyProperty.RegisterAttached("FeedbackBrush",typeof(Brush),typeof(AppMotion),new PropertyMetadata(Brushes.Transparent));
    public static readonly DependencyProperty RevealOnChangeProperty=DependencyProperty.RegisterAttached("RevealOnChange",typeof(bool),typeof(AppMotion),new PropertyMetadata(false,RevealChanged));
    static readonly ConditionalWeakTable<Control,Feedback> feedback=new();
    static readonly ConditionalWeakTable<FrameworkElement,Entrance> entrances=new();
    static readonly HashSet<Feedback> active=[];
    static readonly HashSet<Entrance> moving=[];
    static bool reduced;

    static AppMotion()=>SystemParameters.StaticPropertyChanged+=(_,e)=>
    {
        if(e.PropertyName==nameof(SystemParameters.ClientAreaAnimation))System.Windows.Application.Current?.Dispatcher.BeginInvoke(()=>Configure(reduced));
    };
    public static bool GetEnabled(DependencyObject d)=>(bool)d.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject d,bool value)=>d.SetValue(EnabledProperty,value);
    public static bool GetAnimationsEnabled(DependencyObject d)=>(bool)d.GetValue(AnimationsEnabledProperty);
    public static void SetAnimationsEnabled(DependencyObject d,bool value)=>d.SetValue(AnimationsEnabledProperty,value);
    public static double GetHover(DependencyObject d)=>(double)d.GetValue(HoverProperty);
    public static void SetHover(DependencyObject d,double value)=>d.SetValue(HoverProperty,value);
    public static double GetPressed(DependencyObject d)=>(double)d.GetValue(PressedProperty);
    public static void SetPressed(DependencyObject d,double value)=>d.SetValue(PressedProperty,value);
    public static double GetFocus(DependencyObject d)=>(double)d.GetValue(FocusProperty);
    public static void SetFocus(DependencyObject d,double value)=>d.SetValue(FocusProperty,value);
    public static double GetSelected(DependencyObject d)=>(double)d.GetValue(SelectedProperty);
    public static void SetSelected(DependencyObject d,double value)=>d.SetValue(SelectedProperty,value);
    public static double GetAvailability(DependencyObject d)=>(double)d.GetValue(AvailabilityProperty);
    public static void SetAvailability(DependencyObject d,double value)=>d.SetValue(AvailabilityProperty,value);
    public static bool GetIsSelected(DependencyObject d)=>(bool)d.GetValue(IsSelectedProperty);
    public static void SetIsSelected(DependencyObject d,bool value)=>d.SetValue(IsSelectedProperty,value);
    public static CornerRadius GetCornerRadius(DependencyObject d)=>(CornerRadius)d.GetValue(CornerRadiusProperty);
    public static void SetCornerRadius(DependencyObject d,CornerRadius value)=>d.SetValue(CornerRadiusProperty,value);
    public static Brush GetFeedbackBrush(DependencyObject d)=>(Brush)d.GetValue(FeedbackBrushProperty);
    public static void SetFeedbackBrush(DependencyObject d,Brush value)=>d.SetValue(FeedbackBrushProperty,value);
    public static bool GetRevealOnChange(DependencyObject d)=>(bool)d.GetValue(RevealOnChangeProperty);
    public static void SetRevealOnChange(DependencyObject d,bool value)=>d.SetValue(RevealOnChangeProperty,value);
    internal static bool Allowed=>!reduced && SystemParameters.ClientAreaAnimation;
    internal static int ActiveFeedbackCount=>active.Count;
    internal static int ActiveEntranceCount=>moving.Count;

    internal static void Configure(bool reduceMotion)
    {
        reduced=reduceMotion;
        if(System.Windows.Application.Current is not { } app)return;
        app.Resources["AppPopupAnimation"]=Allowed?PopupAnimation.Fade:PopupAnimation.None;
        foreach(Window window in app.Windows)if(GetEnabled(window))SetAnimationsEnabled(window,Allowed);
        foreach(var item in active.ToArray())item.Update(!Allowed);
        if(!Allowed)foreach(var entrance in moving.ToArray())entrance.Finish();
    }
    static void ScopeChanged(DependencyObject d,DependencyPropertyChangedEventArgs e)
    {
        if(d is Window window && (bool)e.NewValue)SetAnimationsEnabled(window,Allowed);
        if(d is not Control control || control is not (ButtonBase or ListBoxItem or TabItem or TextBoxBase or ComboBox or Thumb or DataGridRow))return;
        var state=feedback.GetValue(control,c=>new(c));
        if((bool)e.NewValue)state.Enable();else state.Disable();
    }
    static void SelectionChanged(DependencyObject d,DependencyPropertyChangedEventArgs e){if(d is Control c && feedback.TryGetValue(c,out var state))state.Update();}
    static void RevealChanged(DependencyObject d,DependencyPropertyChangedEventArgs e)
    {
        if(d is not FrameworkElement element)return;
        element.Loaded-=RevealLoaded;element.Unloaded-=RevealUnloaded;element.IsVisibleChanged-=RevealVisible;
        if((bool)e.NewValue){element.Loaded+=RevealLoaded;element.Unloaded+=RevealUnloaded;element.IsVisibleChanged+=RevealVisible;}
        if(element is ContentPresenter presenter)
        {
            var descriptor=DependencyPropertyDescriptor.FromProperty(ContentPresenter.ContentProperty,typeof(ContentPresenter));
            descriptor.RemoveValueChanged(presenter,ContentChanged);
            if((bool)e.NewValue && element.IsLoaded)descriptor.AddValueChanged(presenter,ContentChanged);
        }
    }
    static void RevealLoaded(object sender,RoutedEventArgs e)
    {
        if(!ReferenceEquals(sender,e.OriginalSource))return;
        if(sender is ContentPresenter presenter){var descriptor=DependencyPropertyDescriptor.FromProperty(ContentPresenter.ContentProperty,typeof(ContentPresenter));descriptor.RemoveValueChanged(presenter,ContentChanged);descriptor.AddValueChanged(presenter,ContentChanged);}
        Reveal((FrameworkElement)sender);
    }
    static void RevealUnloaded(object sender,RoutedEventArgs e)
    {
        if(!ReferenceEquals(sender,e.OriginalSource))return;
        if(sender is ContentPresenter presenter)DependencyPropertyDescriptor.FromProperty(ContentPresenter.ContentProperty,typeof(ContentPresenter)).RemoveValueChanged(presenter,ContentChanged);
        if(entrances.TryGetValue((FrameworkElement)sender,out var entrance))entrance.Finish();
    }
    static void RevealVisible(object sender,DependencyPropertyChangedEventArgs e){if((bool)e.NewValue)Reveal((FrameworkElement)sender);else if(entrances.TryGetValue((FrameworkElement)sender,out var entrance))entrance.Finish();}
    static void ContentChanged(object? sender,EventArgs e){if(sender is FrameworkElement element)element.Dispatcher.BeginInvoke(DispatcherPriority.Loaded,()=>Reveal(element));}
    internal static void Reveal(FrameworkElement element)
    {
        if(!element.IsLoaded || !element.IsVisible || !GetEnabled(element))return;
        entrances.GetValue(element,e=>new(e)).Start();
    }
    internal static void PreviewFeedback(Control control,bool hover,bool press=false)
    {if(feedback.TryGetValue(control,out var state))state.Preview(hover,press);}

    sealed class Feedback(Control control)
    {
        readonly List<DependencyPropertyDescriptor> watches=[];
        readonly Dictionary<(DependencyObject,DependencyProperty),int> versions=[];
        readonly ScaleTransform scale=new(1,1);
        readonly TranslateTransform translate=new();
        Transform? originalTransform;
        FrameworkElement? transformSurface;
        Point originalOrigin;
        bool enabled,connected,hover,pressed,ownsTransform;
        int next;
        public void Enable()
        {
            if(enabled)return;enabled=true;control.Loaded+=Loaded;control.Unloaded+=Unloaded;
            if(control.IsLoaded)Connect();
        }
        public void Disable(){if(!enabled)return;enabled=false;control.Loaded-=Loaded;control.Unloaded-=Unloaded;Disconnect();}
        void Loaded(object s,RoutedEventArgs e){if(ReferenceEquals(s,e.OriginalSource))Connect();}
        void Unloaded(object s,RoutedEventArgs e){if(ReferenceEquals(s,e.OriginalSource))Disconnect();}
        void Connect()
        {
            if(connected)return;connected=true;active.Add(this);hover=control.IsMouseOver;
            control.MouseEnter+=Enter;control.MouseLeave+=Leave;control.PreviewMouseLeftButtonDown+=Down;control.PreviewMouseLeftButtonUp+=Up;control.LostMouseCapture+=Release;
            control.GotKeyboardFocus+=FocusChanged;control.LostKeyboardFocus+=FocusChanged;control.IsEnabledChanged+=EnabledChanged;control.IsVisibleChanged+=VisibilityChanged;
            control.PreviewKeyDown+=KeyDown;control.PreviewKeyUp+=KeyUp;
            void Watch(DependencyProperty property){var descriptor=DependencyPropertyDescriptor.FromProperty(property,control.GetType());descriptor.AddValueChanged(control,Changed);watches.Add(descriptor);}
            if(control is ButtonBase)Watch(ButtonBase.IsPressedProperty);
            if(control is ToggleButton)Watch(ToggleButton.IsCheckedProperty);
            if(control is ListBoxItem)Watch(ListBoxItem.IsSelectedProperty);
            if(control is ComboBoxItem)Watch(ComboBoxItem.IsHighlightedProperty);
            if(control is TabItem)Watch(TabItem.IsSelectedProperty);
            if(control is ComboBox)Watch(ComboBox.IsDropDownOpenProperty);
            if(control is Thumb)Watch(Thumb.IsDraggingProperty);
            if(control is DataGridRow)Watch(DataGridRow.IsSelectedProperty);
            control.ApplyTemplate();
            transformSurface=control is ButtonBase?control.Template?.FindName("InteractionVisual",control) as FrameworkElement:control is Thumb?control:null;
            if(transformSurface is { } surface && !BindingOperations.IsDataBound(surface,UIElement.RenderTransformProperty) && surface.RenderTransform.Value.IsIdentity && !surface.RenderTransform.HasAnimatedProperties)
            {
                originalTransform=surface.RenderTransform;originalOrigin=surface.RenderTransformOrigin;
                var group=new TransformGroup();if(originalTransform is not null && !originalTransform.Value.IsIdentity)group.Children.Add(originalTransform);
                group.Children.Add(scale);group.Children.Add(translate);surface.SetCurrentValue(UIElement.RenderTransformProperty,group);surface.SetCurrentValue(UIElement.RenderTransformOriginProperty,new Point(.5,.5));ownsTransform=true;
            }
            Update(true);
        }
        void Disconnect()
        {
            if(!connected)return;
            hover=pressed=false;Update(true);connected=false;active.Remove(this);
            control.MouseEnter-=Enter;control.MouseLeave-=Leave;control.PreviewMouseLeftButtonDown-=Down;control.PreviewMouseLeftButtonUp-=Up;control.LostMouseCapture-=Release;
            control.GotKeyboardFocus-=FocusChanged;control.LostKeyboardFocus-=FocusChanged;control.IsEnabledChanged-=EnabledChanged;control.IsVisibleChanged-=VisibilityChanged;control.PreviewKeyDown-=KeyDown;control.PreviewKeyUp-=KeyUp;
            foreach(var descriptor in watches)descriptor.RemoveValueChanged(control,Changed);watches.Clear();
            foreach(var ((target,property),_) in versions.ToArray())Clear(target,property);versions.Clear();
            if(ownsTransform && transformSurface is { } surface){surface.SetCurrentValue(UIElement.RenderTransformProperty,originalTransform??Transform.Identity);surface.SetCurrentValue(UIElement.RenderTransformOriginProperty,originalOrigin);ownsTransform=false;}transformSurface=null;
        }
        void Enter(object s,MouseEventArgs e){hover=true;Update();}
        void Leave(object s,MouseEventArgs e){hover=false;pressed=false;Update();}
        void Down(object s,MouseButtonEventArgs e){if(control is ButtonBase)pressed=true;Update();}
        void Up(object s,MouseButtonEventArgs e){pressed=false;Update();}
        void Release(object s,MouseEventArgs e){pressed=false;Update();}
        void KeyDown(object s,KeyEventArgs e){if(control.IsKeyboardFocused && e.Key is Key.Space or Key.Enter){pressed=true;Update();}}
        void KeyUp(object s,KeyEventArgs e){if(e.Key is Key.Space or Key.Enter){pressed=false;Update();}}
        void FocusChanged(object s,KeyboardFocusChangedEventArgs e)=>Update();
        void EnabledChanged(object s,DependencyPropertyChangedEventArgs e){pressed=false;hover=control.IsEnabled && control.IsMouseOver;Update();}
        void VisibilityChanged(object s,DependencyPropertyChangedEventArgs e){pressed=false;hover=control.IsVisible && control.IsMouseOver;Update(true);}
        void Changed(object? s,EventArgs e)=>Update();
        public void Preview(bool over,bool down){hover=over;pressed=down;Update();}
        public void Update(bool instant=false)
        {
            if(!connected)return;
            bool usable=control.IsEnabled;
            bool down=usable && (pressed || control is ButtonBase{IsPressed:true} || control is Thumb{IsDragging:true});
            bool selected=GetIsSelected(control) || control is ToggleButton{IsChecked:true} or ListBoxItem{IsSelected:true} or ComboBoxItem{IsHighlighted:true} or TabItem{IsSelected:true} or ComboBox{IsDropDownOpen:true} or DataGridRow{IsSelected:true};
            int duration=instant || !Allowed || !GetAnimationsEnabled(control) || !control.IsVisible?0:ThemeManager.Current.Id=="material"?150:190;
            Smooth(control,HoverProperty,usable && hover?1:0,duration);Smooth(control,PressedProperty,down?1:0,Math.Min(duration,85));
            Smooth(control,FocusProperty,usable && control.IsKeyboardFocusWithin?1:0,duration);Smooth(control,SelectedProperty,selected?1:0,duration);
            Smooth(control,AvailabilityProperty,usable?1:.38,duration);
            if(ownsTransform)
            {
                double size=down?.975:usable && hover?control is Thumb?1.18:control is RadioButton?1.006:1.018:1;
                Smooth(scale,ScaleTransform.ScaleXProperty,size,duration);Smooth(scale,ScaleTransform.ScaleYProperty,size,duration);
                Smooth(translate,TranslateTransform.YProperty,usable && hover && !down && control is Button?-1:0,duration);
            }
        }
        void Smooth(DependencyObject target,DependencyProperty property,double value,int milliseconds)
        {
            double from=(double)target.GetValue(property);int serial=++next;versions[(target,property)]=serial;
            target.SetValue(property,from);Clear(target,property);
            if(milliseconds==0 || Math.Abs(from-value)<.0001){target.SetValue(property,value);return;}
            var animation=new DoubleAnimation(from,value,TimeSpan.FromMilliseconds(milliseconds)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.HoldEnd};
            animation.Completed+=(_,_)=>{if(versions.GetValueOrDefault((target,property))==serial){target.SetValue(property,value);Clear(target,property);}};
            if(target is UIElement element)element.BeginAnimation(property,animation);else ((Animatable)target).BeginAnimation(property,animation);
        }
        static void Clear(DependencyObject target,DependencyProperty property){if(target is UIElement element)element.BeginAnimation(property,null);else ((Animatable)target).BeginAnimation(property,null);}
    }

    sealed class Entrance(FrameworkElement element)
    {
        readonly TranslateTransform offset=new();
        Transform? original;
        double opacity;
        int version;
        bool running;
        public void Start()
        {
            if(!Allowed){Finish();return;}
            double fromOpacity=running?element.Opacity:0,fromY=running?offset.Y:9;
            if(!running)
            {
                original=element.RenderTransform;opacity=element.Opacity;
                var group=new TransformGroup();if(original is not null && !original.Value.IsIdentity)group.Children.Add(original);group.Children.Add(offset);element.SetCurrentValue(UIElement.RenderTransformProperty,group);
                element.Unloaded+=Unloaded;running=true;moving.Add(this);
            }
            int serial=++version;var duration=TimeSpan.FromMilliseconds(ThemeManager.Current.Id=="material"?210:270);
            element.BeginAnimation(UIElement.OpacityProperty,null);element.SetCurrentValue(UIElement.OpacityProperty,fromOpacity);offset.BeginAnimation(TranslateTransform.YProperty,null);offset.Y=fromY;
            var fade=new DoubleAnimation(fromOpacity,opacity,duration){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.HoldEnd};
            fade.Completed+=(_,_)=>{if(serial==version)Finish();};element.BeginAnimation(UIElement.OpacityProperty,fade);
            offset.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(fromY,0,duration){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.HoldEnd});
        }
        void Unloaded(object sender,RoutedEventArgs e){if(ReferenceEquals(sender,e.OriginalSource))Finish();}
        public void Finish()
        {
            if(!running)return;++version;element.BeginAnimation(UIElement.OpacityProperty,null);element.SetCurrentValue(UIElement.OpacityProperty,opacity);offset.BeginAnimation(TranslateTransform.YProperty,null);offset.Y=0;
            element.SetCurrentValue(UIElement.RenderTransformProperty,original??Transform.Identity);element.Unloaded-=Unloaded;running=false;moving.Remove(this);
        }
    }
}

public sealed class MotionFactorConverter:IValueConverter
{
    public object Convert(object value,Type type,object parameter,CultureInfo culture)=>value is double number && double.TryParse(parameter?.ToString(),NumberStyles.Float,CultureInfo.InvariantCulture,out double factor)?number*factor:0d;
    public object ConvertBack(object value,Type type,object parameter,CultureInfo culture)=>Binding.DoNothing;
}
