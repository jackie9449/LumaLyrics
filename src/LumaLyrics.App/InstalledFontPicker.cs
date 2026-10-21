using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
namespace LumaLyrics.App;

// SelectedItem remains the installed family name. Pointer previews never select an item.
internal sealed class InstalledFontPicker : ComboBox
{
    string? hovered;
    readonly DataTemplate specimen;
    internal event Action<string?>? PreviewFontChanged;
    internal string? HoveredFont=>hovered;
    public InstalledFontPicker()
    {
        SetResourceReference(StyleProperty,typeof(ComboBox));
        IsTextSearchEnabled=true;MaxDropDownHeight=320;
        VirtualizingPanel.SetIsVirtualizing(this,true);VirtualizingPanel.SetVirtualizationMode(this,VirtualizationMode.Recycling);ScrollViewer.SetCanContentScroll(this,true);
        ItemsPanel=new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
        var row=new FrameworkElementFactory(typeof(StackPanel));row.SetValue(MarginProperty,new Thickness(2,2,2,3));
        var name=new FrameworkElementFactory(typeof(TextBlock));name.SetBinding(TextBlock.TextProperty,new Binding());name.SetValue(TextBlock.FontSizeProperty,11d);name.SetValue(TextBlock.OpacityProperty,.75);row.AppendChild(name);
        var sample=new FrameworkElementFactory(typeof(TextBlock));sample.Name="FontSpecimen";sample.SetValue(TextBlock.TextProperty,"浮光歌词 · Aa Bb 123");sample.SetValue(TextBlock.FontSizeProperty,18d);sample.SetValue(MarginProperty,new Thickness(0,3,0,0));sample.SetBinding(TextBlock.FontFamilyProperty,new Binding(){Converter=new FamilyConverter()});row.AppendChild(sample);
        specimen=new DataTemplate{VisualTree=row};
        var items=new Style(typeof(ComboBoxItem),TryFindResource(typeof(ComboBoxItem)) as Style);
        items.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(10,6,10,6)));
        items.Setters.Add(new EventSetter(MouseEnterEvent,new MouseEventHandler(PreviewRow)){HandledEventsToo=true});
        items.Setters.Add(new EventSetter(MouseMoveEvent,new MouseEventHandler(PreviewRow)){HandledEventsToo=true});
        items.Setters.Add(new EventSetter(MouseLeaveEvent,new MouseEventHandler(LeaveRow)){HandledEventsToo=true});
        ItemContainerStyle=items;
    }
    protected override void PrepareContainerForItemOverride(DependencyObject element,object item)
    {base.PrepareContainerForItemOverride(element,item);if(element is ComboBoxItem row){row.ContentTemplate=specimen;if(IsDropDownOpen && row.IsMouseOver)PreviewRow(row,new MouseEventArgs(Mouse.PrimaryDevice,0));}}
    protected override void ClearContainerForItemOverride(DependencyObject element,object item)
    {if(Equals(item,hovered))EndPreview();base.ClearContainerForItemOverride(element,item);}
    void PreviewRow(object sender,MouseEventArgs e)
    {
        if(IsDropDownOpen && sender is ComboBoxItem{Content:string family} && hovered!=family)
        {hovered=family;PreviewFontChanged?.Invoke(family);}
    }
    void LeaveRow(object sender,MouseEventArgs e){if(sender is ComboBoxItem{Content:string family} && hovered==family)EndPreview();}
    internal void EndPreview(){if(hovered is null)return;hovered=null;PreviewFontChanged?.Invoke(null);}
    protected override void OnDropDownClosed(EventArgs e){EndPreview();base.OnDropDownClosed(e);}
    protected override void OnSelectionChanged(SelectionChangedEventArgs e){EndPreview();base.OnSelectionChanged(e);}
    sealed class FamilyConverter : IValueConverter
    {
        public object Convert(object value,Type target,object parameter,CultureInfo culture)=>new FontFamily(value+", Microsoft YaHei UI, Segoe UI");
        public object ConvertBack(object value,Type target,object parameter,CultureInfo culture)=>Binding.DoNothing;
    }
}
