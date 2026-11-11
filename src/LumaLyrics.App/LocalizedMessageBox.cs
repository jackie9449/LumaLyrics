using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace LumaLyrics.App;

/// <summary>Application-owned messages use the application language, including their action buttons.</summary>
internal static class LocalizedMessageBox
{
    internal static MessageBoxResult Show(string text)=>Show(null,text,UiText.T("浮光歌词"));
    internal static MessageBoxResult Show(string text,string caption)=>Show(null,text,caption);
    internal static MessageBoxResult Show(Window? owner,string text)=>Show(owner,text,UiText.T("浮光歌词"));
    internal static MessageBoxResult Show(Window? owner,string text,string caption,MessageBoxButton buttons=MessageBoxButton.OK,MessageBoxImage image=MessageBoxImage.None)
    {
        var window=Create(owner,text,caption,buttons,image,out var result);
        window.ShowDialog();return result();
    }

    internal static Window Create(Window? owner,string text,string caption,MessageBoxButton buttons,MessageBoxImage image,out Func<MessageBoxResult> result)
    {
        if(System.Windows.Application.Current.TryFindResource("WindowBrush") is null)ThemeManager.Apply(ThemeManager.Current.Id);
        owner=owner?.IsVisible==true?owner:owner?.Owner?.IsVisible==true?owner.Owner:System.Windows.Application.Current.MainWindow is {IsVisible:true} main?main:null;
        var window=new Window{Title=caption,Owner=owner,Width=560,MinWidth=360,SizeToContent=SizeToContent.Height,MaxHeight=Math.Max(300,SystemParameters.WorkArea.Height-40),ResizeMode=ResizeMode.NoResize,ShowInTaskbar=owner is null,WindowStartupLocation=owner is null?WindowStartupLocation.CenterScreen:WindowStartupLocation.CenterOwner,FontFamily=new(UiTypography.Sans),Icon=ApplicationIcon.CreateWindowIcon()};
        var root=new Grid{Margin=new(26,20,26,24)};root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var message=new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,FontSize=14,LineHeight=23};ThemeManager.Bind(message,TextBlock.ForegroundProperty,"TextBrush");
        var scroll=new ScrollViewer{Content=message,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,MaxHeight=Math.Max(160,SystemParameters.WorkArea.Height-200)};root.Children.Add(scroll);
        var actions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,Margin=new(0,22,0,0)};Grid.SetRow(actions,1);root.Children.Add(actions);
        MessageBoxResult selected=buttons is MessageBoxButton.OK?MessageBoxResult.OK:buttons==MessageBoxButton.YesNo?MessageBoxResult.No:MessageBoxResult.Cancel;
        result=()=>selected;
        var choices=buttons switch
        {
            MessageBoxButton.OKCancel=>new[]{(UiText.T("确定"),MessageBoxResult.OK),(UiText.T("取消"),MessageBoxResult.Cancel)},
            MessageBoxButton.YesNo=>new[]{(UiText.T("是"),MessageBoxResult.Yes),(UiText.T("否"),MessageBoxResult.No)},
            MessageBoxButton.YesNoCancel=>new[]{(UiText.T("是"),MessageBoxResult.Yes),(UiText.T("否"),MessageBoxResult.No),(UiText.T("取消"),MessageBoxResult.Cancel)},
            _=>new[]{(UiText.T("确定"),MessageBoxResult.OK)}
        };
        foreach(var (label,value) in choices)
        {
            var button=new Button{Content=label,MinWidth=86,Margin=new(8,0,0,0),Padding=new(16,9,16,9),IsDefault=value==choices[0].Item2,IsCancel=value==MessageBoxResult.Cancel};
            if(button.IsDefault)button.SetResourceReference(FrameworkElement.StyleProperty,"PrimaryButton");
            button.Click+=(_,_)=>{selected=value;window.DialogResult=value is MessageBoxResult.OK or MessageBoxResult.Yes;};actions.Children.Add(button);
        }
        ApplicationWindowFrame.SetContent(window,root);return window;
    }
}
