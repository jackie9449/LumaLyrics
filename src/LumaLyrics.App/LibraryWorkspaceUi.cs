using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
namespace LumaLyrics.App;

internal static class LibraryWorkspaceUi
{
    internal static TextBlock Text(string value,double size=12,bool muted=true)
    {
        var text=new TextBlock{Text=value,FontSize=size,FontWeight=FontWeights.Normal,TextWrapping=TextWrapping.Wrap,LineHeight=size+7};
        ThemeManager.Bind(text,TextBlock.ForegroundProperty,muted?"MutedBrush":"TextBrush");return text;
    }
    internal static StackPanel Heading(string title,string description)
    {
        var result=new StackPanel{Margin=new(0,0,0,14)};var heading=Text(title,20,false);heading.FontWeight=FontWeights.SemiBold;ThemeManager.BindDisplay(heading);result.Children.Add(heading);
        var caption=Text(description);caption.Margin=new(0,4,0,0);result.Children.Add(caption);return result;
    }
    internal static Border Card(UIElement content,double padding=16)
    {
        var card=new Border{Child=content,Padding=new(padding),BorderThickness=new(1),Margin=new(0,0,0,12)};
        ThemeManager.Bind(card,Border.BackgroundProperty,"CardBrush");ThemeManager.Bind(card,Border.BorderBrushProperty,"BorderBrush");ThemeManager.Bind(card,Border.CornerRadiusProperty,"CardRadius");return card;
    }
    internal static Button Button(Panel panel,string label,Action action,bool primary=false)
    {
        var button=DialogUi.Button(panel,label,action);button.Margin=new(0,0,8,8);button.MinHeight=36;button.Padding=new(13,8,13,8);if(primary)button.SetResourceReference(FrameworkElement.StyleProperty,"PrimaryButton");return button;
    }
    internal static Border Metric(string label,out TextBlock value)
    {
        var content=new StackPanel();content.Children.Add(Text(label,11));value=Text("—",28,false);value.FontWeight=FontWeights.SemiBold;value.Margin=new(0,6,0,0);content.Children.Add(value);var card=Card(content,14);card.Margin=new(0,0,10,14);return card;
    }
    static FrameworkElementFactory Line(double size,bool muted,string[] paths,string? format=null,bool localize=false)
    {
        var text=new FrameworkElementFactory(typeof(TextBlock));text.SetValue(TextBlock.FontSizeProperty,size);text.SetValue(TextBlock.FontWeightProperty,FontWeights.Normal);text.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);text.SetValue(FrameworkElement.MarginProperty,new Thickness(0,0,0,4));text.SetResourceReference(TextBlock.ForegroundProperty,muted?"MutedBrush":"TextBrush");
        Binding Bind(string path)=>new(path){Converter=localize?LibraryStatusConverter.Instance:null};
        if(paths.Length==1)text.SetBinding(TextBlock.TextProperty,Bind(paths[0]));else{var binding=new MultiBinding{StringFormat=format};foreach(var path in paths)binding.Bindings.Add(Bind(path));text.SetBinding(TextBlock.TextProperty,binding);}return text;
    }
    internal static DataTemplate SongTemplate()
    {
        var row=new FrameworkElementFactory(typeof(StackPanel));var title=Line(13,false,["Track.Title"]);title.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);row.AppendChild(title);row.AppendChild(Line(11,true,["Track.Artist","Track.Album"],"{0} · {1}"));row.AppendChild(Line(10.5,true,["Status.OriginalLabel","Status.ChineseLabel","Status.CoverLabel"],"{0}   ·   {1}   ·   {2}",true));return new(){VisualTree=row};
    }
    internal static DataTemplate BatchTemplate(bool batch=false)
    {
        var row=new FrameworkElementFactory(typeof(StackPanel));var title=Line(13,false,["Title"]);if(batch)title.SetBinding(TextBlock.TextProperty,new Binding{Converter=LibraryBatchTitleConverter.Instance});title.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);row.AppendChild(title);row.AppendChild(Line(11,true,["Summary"],localize:batch));row.AppendChild(Line(10.5,true,["Availability"],localize:batch));return new(){VisualTree=row};
    }
}

internal sealed class LibraryStatusConverter : IValueConverter
{
    internal static readonly LibraryStatusConverter Instance=new();
    public object Convert(object value,Type targetType,object parameter,System.Globalization.CultureInfo culture)=>value is string text?UiText.Status(text):value;
    public object ConvertBack(object value,Type targetType,object parameter,System.Globalization.CultureInfo culture)=>Binding.DoNothing;
}
internal sealed class LibraryBatchTitleConverter : IValueConverter
{
    internal static readonly LibraryBatchTitleConverter Instance=new();
    internal static string Title(LumaLyrics.Infrastructure.LibraryTaskBatch task)=>task.Id.StartsWith("job:",StringComparison.Ordinal)?task.Jobs.FirstOrDefault()?.Ticket.Request.Target.Track.Title??UiText.T("历史任务"):UiText.Status(task.Title);
    public object Convert(object value,Type targetType,object parameter,System.Globalization.CultureInfo culture)=>value is LumaLyrics.Infrastructure.LibraryTaskBatch task?Title(task):"";
    public object ConvertBack(object value,Type targetType,object parameter,System.Globalization.CultureInfo culture)=>Binding.DoNothing;
}
