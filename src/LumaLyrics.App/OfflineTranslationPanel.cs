using System.Windows;
using System.Windows.Controls;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

public sealed class OfflineTranslationPanel:StackPanel
{
    readonly PlaybackCoordinator coordinator;
    readonly TextBlock state=LibraryWorkspaceUi.Text("",11);
    readonly TextBlock failures=LibraryWorkspaceUi.Text("",11);
    readonly TextBlock hint=LibraryWorkspaceUi.Text("",11);
    internal ComboBox LanguageSelector {get;}=new(){SelectedValuePath="Tag",MinWidth=180,HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,4,0,8)};
    readonly Button prepare,current,resume,pause;
    bool listening;
    public OfflineTranslationPanel(PlaybackCoordinator coordinator)
    {
        this.coordinator=coordinator;
        Children.Add(LibraryWorkspaceUi.Text(UiText.T("离线基础机翻"),15,false));
        Children.Add(LibraryWorkspaceUi.Text(UiText.T("原词语言"),11));
        foreach(string code in new[]{"auto"}.Concat(ArgosLanguages.Supported))LanguageSelector.Items.Add(new ComboBoxItem{Tag=code,Content=UiText.T(ArgosLanguages.Name(code))});
        LanguageSelector.SelectedValue=coordinator.OfflineSourceLanguage;System.Windows.Automation.AutomationProperties.SetName(LanguageSelector,UiText.T("离线翻译原词语言"));Children.Add(LanguageSelector);
        LanguageSelector.SelectionChanged+=(_,_)=>{if(LanguageSelector.SelectedValue is string code)coordinator.SetOfflineSourceLanguage(code);};
        hint.Margin=new(0,2,0,10);Children.Add(hint);
        var actions=new WrapPanel();Children.Add(actions);
        prepare=LibraryWorkspaceUi.Button(actions,UiText.T("准备离线组件"),coordinator.PrepareOfflineEngine,true);
        current=LibraryWorkspaceUi.Button(actions,UiText.T("翻译当前歌曲"),()=>Act(coordinator.TranslateCurrentOffline));
        resume=LibraryWorkspaceUi.Button(actions,UiText.T("继续离线翻译"),()=>Act(coordinator.ContinueOfflineTranslation));
        pause=LibraryWorkspaceUi.Button(actions,UiText.T("暂停"),coordinator.PauseOfflineTranslation);
        state.Margin=new(0,8,0,0);Children.Add(state);Children.Add(failures);
        Loaded+=async(_,_)=>{if(listening)return;listening=true;coordinator.OfflineChanged+=Refresh;await coordinator.CheckOfflineReadyAsync();Refresh();};
        Unloaded+=(_,_)=>{listening=false;coordinator.OfflineChanged-=Refresh;};Refresh();
    }
    void Act(Action action){try{action();}catch(Exception e){state.Text=UiText.Status(e.Message);}}
    void Refresh()
    {
        prepare.IsEnabled=!coordinator.OfflineBusy;current.IsEnabled=resume.IsEnabled=coordinator.OfflineReady&&!coordinator.OfflineBusy;pause.IsEnabled=coordinator.OfflineBusy;
        LanguageSelector.IsEnabled=!coordinator.OfflineBusy;
        hint.Text=UiText.F($"支持英、法、德、西、日、韩语译中；法德西日韩经英语中转。所选语言组件共约 {coordinator.OfflineDownloadBytes/1000000:N0} MB，已有下载会复用。准备后本机翻译，不上传歌词。")+"\n"+UiText.T("混合语种批量请选择自动识别；短句识别不准时可指定原词语言。继续任务沿用入队时的语言。");
        state.Text=UiText.Status(coordinator.OfflineMessage)+(coordinator.OfflineReady&&coordinator.OfflineMessage!="所选离线语言已就绪。"?"\n"+UiText.T("所选离线语言已就绪。") : "");
        if(coordinator.OfflineSummary.Length>0 && coordinator.OfflineSummary!=coordinator.OfflineMessage)state.Text+="\n"+coordinator.OfflineSummary;
        failures.Text=coordinator.OfflineFailures;
    }
}
