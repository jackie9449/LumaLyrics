using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
namespace LumaLyrics.App;

internal sealed class InterfaceLanguagePanel : StackPanel
{
    internal ComboBox Selector {get;}=new(){MinWidth=210,HorizontalAlignment=HorizontalAlignment.Left,SelectedValuePath="Tag",Margin=new(0,8,0,8)};
    public InterfaceLanguagePanel(PlaybackCoordinator coordinator)
    {
        Margin=new(0,0,0,24);
        var heading=LibraryWorkspaceUi.Text(UiText.T("界面语言"),16,false);Children.Add(heading);
        foreach(var (code,name) in new[]{("zh-Hans","简体中文"),("zh-Hant","繁體中文"),("en","English"),("ja","日本語")})
            Selector.Items.Add(new ComboBoxItem{Tag=code,Content=name});
        AutomationProperties.SetName(Selector,UiText.T("界面语言"));Selector.SelectedValue=UiText.Normalize(coordinator.Settings.UiLanguage);Children.Add(Selector);
        Children.Add(LibraryWorkspaceUi.Text(UiText.T("选择后自动保存，退出并重新启动浮光歌词后生效。歌曲与歌词语言不受影响。"),11));
        Selector.SelectionChanged+=(_,_)=>{if(Selector.SelectedValue is string language){coordinator.Settings.UiLanguage=UiText.Normalize(language);coordinator.SaveSettings();}};
    }
}
