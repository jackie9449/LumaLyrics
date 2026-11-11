using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using LumaLyrics.Windows;
namespace LumaLyrics.App;

internal static class ChineseScriptDisplay
{
    internal static string Normalize(string? mode)=>mode is "simplified" or "traditional"?mode:"original";

    internal static ComboBox CreateSelector(PlaybackCoordinator coordinator)
    {
        var choice=new ComboBox{Width=UiText.Language is "en" or "ja"?170:126,MinHeight=30,FontSize=12,Margin=new(0),Padding=new(10,4,10,4),SelectedValuePath="Tag",ToolTip=UiText.T("中文原词与译文的简繁显示，同时用于主窗口和悬浮歌词")};
        AutomationProperties.SetName(choice,UiText.T("歌词简繁显示"));
        foreach(var (id,label) in new[]{("original",UiText.T("按原文")),("simplified",UiText.T("简体中文")),("traditional",UiText.T("繁体中文"))})
            choice.Items.Add(new ComboBoxItem{Tag=id,Content=label});
        bool syncing=false;
        void Refresh(){syncing=true;try{choice.SelectedValue=Normalize(coordinator.Settings.ChineseScript);}finally{syncing=false;}}
        Refresh();
        choice.SelectionChanged+=(_,_)=>{if(!syncing && choice.SelectedValue is string mode)coordinator.SetChineseScript(mode);};
        choice.Loaded+=(_,_)=>{coordinator.Updated-=Refresh;coordinator.Updated+=Refresh;Refresh();};
        choice.Unloaded+=(_,_)=>coordinator.Updated-=Refresh;
        return choice;
    }

    internal static string[] ConvertOriginals(string[] lines,string mode)
    {
        // Detect language over the whole document so Han-only Japanese lines are
        // preserved too. Chinese translations are converted independently.
        bool japaneseOrKorean=lines.Any(line=>line.Any(c=>char.IsLetter(c) && c is >= '\u3040' and <= '\u30ff' or >= '\u31f0' and <= '\u31ff' or >= '\uff66' and <= '\uff9f' or >= '\u1100' and <= '\u11ff' or >= '\u3130' and <= '\u318f' or >= '\uac00' and <= '\ud7af'));
        return japaneseOrKorean?lines:lines.Select(line=>ChineseScriptConverter.Convert(line,mode)).ToArray();
    }
}
