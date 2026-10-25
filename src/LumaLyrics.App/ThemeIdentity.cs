using System.Windows;

namespace LumaLyrics.App;

public static class ThemeIdentity
{
    public static readonly DependencyProperty LabelProperty=DependencyProperty.RegisterAttached("Label",typeof(string),typeof(ThemeIdentity),new PropertyMetadata(""));
    public static string GetLabel(DependencyObject element)=>(string)element.GetValue(LabelProperty);
    public static void SetLabel(DependencyObject element,string value)=>element.SetValue(LabelProperty,value);
}
