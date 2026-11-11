using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

public partial class MainWindow
{
    bool shortcutPlaybackPending;
    internal ShortcutRegistry Shortcuts=>(hotkeys??=new(this,HandleGlobalShortcut)).Registry;
    internal void RefreshShortcutStatus(IReadOnlyList<ShortcutRegistration> result)
    {
        hotkeyError=string.Join("\n",result.Where(r=>r.Issue is not (ShortcutIssue.None or ShortcutIssue.Disabled)).Select(r=>UiText.T(KeyboardShortcuts.All.Single(d=>d.Action==r.Action).Label)+" · "+r.Binding+" · "+HotkeySettingsPanel.Describe(r.Issue)));
        OverlayToggle.ToolTip=UiText.T("显示 / 隐藏歌词")+(hotkeys?.Registry.Active.TryGetValue(ShortcutAction.ToggleOverlay,out var gesture)==true?" · "+gesture.Text:"");
    }
    void RegisterHotkeys()
    {
        RefreshShortcutStatus(Shortcuts.Apply(ShortcutSettings.Read(settings),keepAvailableOnFailure:true));
        if(!string.IsNullOrEmpty(hotkeyError))
        {
            vm.Status=UiText.T("部分快捷键不可用，请在设置 → 快捷键中解决冲突。");
            tray?.ShowBalloonTip(6000,UiText.T("快捷键提示"),vm.Status,System.Windows.Forms.ToolTipIcon.Info);
        }
    }
    async void HandleGlobalShortcut(ShortcutAction action)
    {
        if(quitting)return;
        // Recording a replacement must never also trigger playback or alter offsets.
        if(HotkeySettingsPanel.IsRecording)return;
        switch(action)
        {
            case ShortcutAction.ToggleOverlay:ToggleOverlay();return;
            case ShortcutAction.UnlockOverlay:if(!overlay.IsVisible)ToggleOverlay();overlay.SetLocked(false);return;
            case ShortcutAction.LyricsEarlier:coordinator.SetOffset((coordinator.Binding?.OffsetMs??0)-100);return;
            case ShortcutAction.LyricsLater:coordinator.SetOffset((coordinator.Binding?.OffsetMs??0)+100);return;
            case ShortcutAction.ResetOffset:coordinator.SetOffset(0);return;
        }
        if(shortcutPlaybackPending)return;
        shortcutPlaybackPending=true;
        try
        {
            // Read live state instead of toggling from a stale UI snapshot while minimized.
            await coordinator.Source.RefreshAsync();var current=coordinator.Source.Latest;
            if(current is null)return;
            string command=action switch{ShortcutAction.PlayPause=>current.Status==PlayState.Playing?"pause":"play",ShortcutAction.Previous=>"previous",ShortcutAction.Next=>"next",_=>""};
            if(command.Length>0 && !await coordinator.Source.ControlAsync(command))vm.Status=UiText.T("播放器未接受此操作。");
        }
        catch(Exception ex){store.Log("Shortcut: "+ex.GetType().Name);vm.Status=UiText.T("播放器未接受此操作。");}
        finally{shortcutPlaybackPending=false;}
    }
}
