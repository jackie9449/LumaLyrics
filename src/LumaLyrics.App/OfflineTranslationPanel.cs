using System.Windows;
using System.Windows.Controls;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

public sealed class OfflineTranslationPanel:StackPanel
{
    readonly PlaybackCoordinator coordinator;
    readonly TextBlock state=LibraryWorkspaceUi.Text("",11);
    readonly TextBlock failures=LibraryWorkspaceUi.Text("",11);
    readonly Button prepare,current,resume,pause;
    bool listening;
    public OfflineTranslationPanel(PlaybackCoordinator coordinator)
    {
        this.coordinator=coordinator;
        Children.Add(LibraryWorkspaceUi.Text(UiText.T("离线基础机翻"),15,false));
        var hint=LibraryWorkspaceUi.Text(UiText.F($"英译中兜底，无需 agent。首次下载约 {coordinator.OfflineDownloadBytes/1000000:N0} MB；之后本机翻译，不上传歌词。"),11);hint.Margin=new(0,6,0,10);Children.Add(hint);
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
        state.Text=UiText.Status(coordinator.OfflineMessage)+(coordinator.OfflineReady?"\n"+UiText.T("离线英译中已就绪。") : "");
        if(coordinator.OfflineSummary.Length>0 && coordinator.OfflineSummary!=coordinator.OfflineMessage)state.Text+="\n"+coordinator.OfflineSummary;
        failures.Text=coordinator.OfflineFailures;
    }
}
