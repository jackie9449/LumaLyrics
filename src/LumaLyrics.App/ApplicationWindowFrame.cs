using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;

namespace LumaLyrics.App;

internal static class ApplicationWindowFrame
{
    internal static void SetContent(Window window,FrameworkElement content)
    {
        AppMotion.SetEnabled(window,true);
        window.WindowStyle=WindowStyle.None;
        WindowChrome.SetWindowChrome(window,new WindowChrome{CaptionHeight=44,ResizeBorderThickness=new Thickness(6),GlassFrameThickness=new Thickness(0),UseAeroCaptionButtons=false});
        var shell=new Border{BorderThickness=new Thickness(1)};
        ThemeManager.Bind(shell,Border.BackgroundProperty,"WindowBrush");ThemeManager.Bind(shell,Border.BorderBrushProperty,"BorderBrush");
        var layout=new Grid();layout.RowDefinitions.Add(new RowDefinition{Height=new GridLength(44)});layout.RowDefinitions.Add(new RowDefinition());
        var scene=new Grid();scene.Children.Add(new ThemeBackdrop());scene.Children.Add(layout);shell.Child=scene;
        var header=new Grid{Margin=new Thickness(20,0,10,0)};header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});layout.Children.Add(header);
        var rule=new Border{Height=1,VerticalAlignment=VerticalAlignment.Bottom,IsHitTestVisible=false};ThemeManager.Bind(rule,Border.BackgroundProperty,"FrameRuleBrush");Grid.SetColumnSpan(rule,2);header.Children.Add(rule);
        var identity=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};header.Children.Add(identity);
        var mark=new TextBlock{Text="◒",FontSize=18,Margin=new Thickness(0,0,10,0)};ThemeManager.Bind(mark,TextBlock.ForegroundProperty,"AccentBrush");identity.Children.Add(mark);
        var title=new TextBlock{Text=window.Title.Replace("浮光歌词 · ",""),FontSize=11,VerticalAlignment=VerticalAlignment.Center};ThemeManager.Bind(title,TextBlock.ForegroundProperty,"MutedBrush");identity.Children.Add(title);
        ThemeManager.Bind(title,TextBlock.FontFamilyProperty,"LabelFont");
        var close=new Button{Content="\uE8BB",FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=10,Width=34,Height=30,ToolTip="关闭",Margin=new Thickness(0),VerticalAlignment=VerticalAlignment.Center};
        close.SetResourceReference(FrameworkElement.StyleProperty,"IconButton");WindowChrome.SetIsHitTestVisibleInChrome(close,true);System.Windows.Automation.AutomationProperties.SetName(close,"关闭"+title.Text);
        close.Click+=(_,_)=>window.Close();Grid.SetColumn(close,1);header.Children.Add(close);
        content.SetValue(Grid.RowProperty,1);layout.Children.Add(content);AppMotion.SetRevealOnChange(content,true);window.Content=shell;
        window.KeyDown+=(_,e)=>{if(e.Key==Key.Escape && !e.Handled && window is not MainWindow){window.Close();e.Handled=true;}};
    }
}
