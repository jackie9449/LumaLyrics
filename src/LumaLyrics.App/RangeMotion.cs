using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LumaLyrics.App;

/// <summary>Owns only template visual clocks; the real ProgressBar value stays exact.</summary>
public static class RangeMotion
{
    public static readonly DependencyProperty EnabledProperty=DependencyProperty.RegisterAttached("Enabled",typeof(bool),typeof(RangeMotion),new PropertyMetadata(false,Changed));
    public static bool GetEnabled(DependencyObject d)=>(bool)d.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject d,bool value)=>d.SetValue(EnabledProperty,value);
    static readonly ConditionalWeakTable<ProgressBar,State> states=new();
    static readonly HashSet<State> active=[];
    internal static int ActiveCount=>active.Count;
    internal static void RefreshAll(){foreach(var state in active.ToArray())state.Refresh();}
    static void Changed(DependencyObject d,DependencyPropertyChangedEventArgs e)
    {if(d is ProgressBar bar){var state=states.GetValue(bar,b=>new(b));if((bool)e.NewValue)state.Enable();else state.Disable();}}
    sealed class State(ProgressBar bar)
    {
        FrameworkElement? indicator,pulse;
        readonly ScaleTransform fill=new(1,1);
        readonly TranslateTransform travel=new();
        bool connected;int version;
        readonly DependencyPropertyDescriptor indeterminate=DependencyPropertyDescriptor.FromProperty(ProgressBar.IsIndeterminateProperty,typeof(ProgressBar));
        readonly DependencyPropertyDescriptor template=DependencyPropertyDescriptor.FromProperty(Control.TemplateProperty,typeof(ProgressBar));
        public void Enable(){bar.Loaded-=Loaded;bar.Unloaded-=Unloaded;bar.Loaded+=Loaded;bar.Unloaded+=Unloaded;if(bar.IsLoaded)Connect();}
        public void Disable(){bar.Loaded-=Loaded;bar.Unloaded-=Unloaded;Disconnect();}
        void Loaded(object s,RoutedEventArgs e){if(ReferenceEquals(s,e.OriginalSource))Connect();}
        void Unloaded(object s,RoutedEventArgs e){if(ReferenceEquals(s,e.OriginalSource))Disconnect();}
        void Connect()
        {
            if(connected)return;connected=true;active.Add(this);bar.SizeChanged+=Size;bar.IsVisibleChanged+=Visible;indeterminate.AddValueChanged(bar,Mode);template.AddValueChanged(bar,TemplateChanged);Attach();Refresh();
        }
        void Detach(){if(indicator is not null)indicator.SizeChanged-=FillSize;fill.BeginAnimation(ScaleTransform.ScaleXProperty,null);travel.BeginAnimation(TranslateTransform.XProperty,null);fill.ScaleX=1;travel.X=0;indicator=null;pulse=null;++version;}
        void Disconnect(){if(!connected)return;connected=false;active.Remove(this);bar.SizeChanged-=Size;bar.IsVisibleChanged-=Visible;indeterminate.RemoveValueChanged(bar,Mode);template.RemoveValueChanged(bar,TemplateChanged);Detach();}
        void Attach()
        {
            Detach();bar.ApplyTemplate();indicator=bar.Template.FindName("PART_Indicator",bar) as FrameworkElement;pulse=bar.Template.FindName("Pulse",bar) as FrameworkElement;
            if(indicator is not null){indicator.RenderTransform=fill;indicator.SizeChanged+=FillSize;}if(pulse is not null)pulse.RenderTransform=travel;
        }
        void TemplateChanged(object? s,EventArgs e){Attach();Refresh();}
        void Mode(object? s,EventArgs e)=>Refresh();
        void Size(object s,SizeChangedEventArgs e)=>Refresh();
        void Visible(object s,DependencyPropertyChangedEventArgs e)=>Refresh();
        bool Allowed=>AppMotion.Allowed && AppMotion.GetAnimationsEnabled(bar) && bar.IsVisible && bar.Orientation==Orientation.Horizontal;
        void FillSize(object s,SizeChangedEventArgs e)
        {
            double from=e.PreviousSize.Width*fill.ScaleX;fill.BeginAnimation(ScaleTransform.ScaleXProperty,null);fill.ScaleX=1;int serial=++version;
            if(!Allowed || bar.IsIndeterminate || e.NewSize.Width<=0 || from<=0)return;
            var animation=new DoubleAnimation(from/e.NewSize.Width,1,TimeSpan.FromMilliseconds(180)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}};
            animation.Completed+=(_,_)=>{if(version==serial){fill.BeginAnimation(ScaleTransform.ScaleXProperty,null);fill.ScaleX=1;}};fill.BeginAnimation(ScaleTransform.ScaleXProperty,animation);
        }
        public void Refresh()
        {
            ++version;fill.BeginAnimation(ScaleTransform.ScaleXProperty,null);fill.ScaleX=1;travel.BeginAnimation(TranslateTransform.XProperty,null);travel.X=0;
            if(pulse is null)return;pulse.Width=Math.Min(90,Math.Max(0,bar.ActualWidth*.35));
            if(Allowed && bar.IsIndeterminate && bar.ActualWidth>0)travel.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(-pulse.Width,bar.ActualWidth,TimeSpan.FromMilliseconds(1500)){RepeatBehavior=RepeatBehavior.Forever});
        }
    }
}
