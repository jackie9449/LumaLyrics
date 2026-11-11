using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

internal sealed class HotkeySettingsPanel : StackPanel
{
    readonly PlaybackCoordinator coordinator;
    readonly Func<MainWindow?> owner;
    readonly Dictionary<ShortcutAction,TextBox> fields=[];
    readonly Dictionary<ShortcutAction,TextBlock> statuses=[];
    readonly TextBlock feedback=LibraryWorkspaceUi.Text("",12);
    static TextBox? recordingInput;
    public static bool IsRecording=>recordingInput?.IsKeyboardFocusWithin==true && Window.GetWindow(recordingInput)?.IsActive==true;
    static Action<string>? capture;
    internal static void CaptureRegisteredGesture(string value)=>capture?.Invoke(value);
    internal HotkeySettingsPanel(PlaybackCoordinator coordinator,Func<MainWindow?> owner)
    {
        this.coordinator=coordinator;this.owner=owner;
        Children.Add(LibraryWorkspaceUi.Heading(UiText.T("全局快捷键"),UiText.T("在其他软件中也能控制播放。点击组合键后直接按下新组合；Backspace 清空，Esc 取消录入。")));
        Children.Add(LibraryWorkspaceUi.Text(UiText.T("软件内可按空格播放 / 暂停；输入框、按钮等控件保留原有空格操作。"),11));
        var bindings=ShortcutSettings.Read(coordinator.Settings);
        foreach(var definition in KeyboardShortcuts.All)
        {
            var row=new Grid();row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            var copy=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,12,0)};
            copy.Children.Add(LibraryWorkspaceUi.Text(UiText.T(definition.Label),13,false));
            var status=LibraryWorkspaceUi.Text("",10.5);statuses[definition.Action]=status;copy.Children.Add(status);row.Children.Add(copy);
            var input=new TextBox{Text=bindings[definition.Action],IsReadOnly=true,Width=204,MinHeight=38,VerticalContentAlignment=VerticalAlignment.Center,FontSize=12,ToolTip=UiText.T("点击并按下快捷键"),Margin=new(0)};
            AutomationProperties.SetName(input,UiText.T(definition.Label));fields[definition.Action]=input;Grid.SetColumn(input,1);row.Children.Add(input);
            string previous=input.Text;
            input.GotKeyboardFocus+=(_,_)=>{previous=input.Text;recordingInput=input;capture=value=>{input.Text=value;Check();};input.SelectAll();};
            input.LostKeyboardFocus+=(_,_)=>{recordingInput=null;capture=null;Check();};
            input.PreviewKeyDown+=(_,e)=>
            {
                var key=e.Key==Key.System?e.SystemKey:e.Key;
                if(key==Key.Tab)return;
                e.Handled=true;
                if(key==Key.Escape){input.Text=previous;Keyboard.ClearFocus();return;}
                if(key is Key.Back or Key.Delete && Keyboard.Modifiers==ModifierKeys.None){input.Text="";Check();return;}
                if(key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)return;
                var modifiers=Keyboard.Modifiers;
                string name=key switch{Key.Return=>"Enter",Key.Prior=>"PageUp",Key.Next=>"PageDown",Key.Back=>"Backspace",_=>key.ToString()};
                if(key is >=Key.D0 and <=Key.D9)name=((int)key-(int)Key.D0).ToString();
                string value=(modifiers.HasFlag(ModifierKeys.Windows)?"Win+":"")+(modifiers.HasFlag(ModifierKeys.Control)?"Ctrl+":"")+(modifiers.HasFlag(ModifierKeys.Alt)?"Alt+":"")+(modifiers.HasFlag(ModifierKeys.Shift)?"Shift+":"")+name;
                input.Text=ShortcutGesture.TryParse(value,out var gesture)?gesture.Text:value;Check();
            };
            Children.Add(LibraryWorkspaceUi.Card(row,12));
        }
        Children.Add(LibraryWorkspaceUi.Text(UiText.T("冲突时可自动分配未被占用的组合，也可自行改键或清空停用。Windows 无法可靠返回占用程序的名称；不会抢占其他软件的快捷键。"),11));
        var actions=new WrapPanel{Margin=new(0,18,0,0)};Children.Add(actions);
        LibraryWorkspaceUi.Button(actions,UiText.T("保存快捷键"),Save,true);
        LibraryWorkspaceUi.Button(actions,UiText.T("自动解决冲突"),()=>{
            if(owner()?.Shortcuts is not { } registry)return;
            foreach(var (action,value) in registry.Suggest(Read()))fields[action].Text=value;
            Check();feedback.Text=UiText.T("已分配可用组合，请查看后保存。");
        });
        LibraryWorkspaceUi.Button(actions,UiText.T("恢复默认"),()=>{foreach(var d in KeyboardShortcuts.All)fields[d.Action].Text=d.Default;Check();feedback.Text=UiText.T("已恢复默认组合，保存后生效。");});
        Children.Add(feedback);
        Loaded+=(_,_)=>Check();Unloaded+=(_,_)=>{recordingInput=null;capture=null;};
    }
    Dictionary<ShortcutAction,string> Read()=>fields.ToDictionary(p=>p.Key,p=>p.Value.Text);
    void ShowStatus(IReadOnlyList<ShortcutRegistration> result){foreach(var item in result)statuses[item.Action].Text=Describe(item.Issue);}
    void Check(){if(owner()?.Shortcuts is {} registry)ShowStatus(registry.Inspect(Read()));}
    void Save()
    {
        Keyboard.ClearFocus();if(owner() is not { } main)return;
        var registry=main.Shortcuts;var previous=ShortcutSettings.Read(coordinator.Settings);var result=registry.Apply(Read());ShowStatus(result);main.RefreshShortcutStatus(result);
        if(result.Any(r=>r.Issue is not (ShortcutIssue.None or ShortcutIssue.Disabled))){feedback.Text=result.Any(r=>r.Issue==ShortcutIssue.RegistrationFailed)?UiText.T("未保存，部分原快捷键也已不可用。请自动解决冲突后保存。"):UiText.T("未保存：请先解决标出的冲突；原有可用快捷键保持生效。");return;}
        try
        {
            ShortcutSettings.Write(coordinator.Settings,result.ToDictionary(r=>r.Action,r=>r.Binding));coordinator.SaveSettings();
            feedback.Text=UiText.T("快捷键已保存并立即生效。");
        }
        catch(Exception ex)
        {
            ShortcutSettings.Write(coordinator.Settings,previous);var restored=registry.Apply(previous,keepAvailableOnFailure:true);main.RefreshShortcutStatus(restored);ShowStatus(restored);
            feedback.Text=UiText.T("保存失败：")+UiText.Status(ex.Message);
        }
    }
    internal static string Describe(ShortcutIssue issue)=>issue switch
    {
        ShortcutIssue.Disabled=>UiText.T("已停用"),ShortcutIssue.Invalid=>UiText.T("组合无效或系统保留；请使用 Ctrl / Alt 加按键"),
        ShortcutIssue.Duplicate=>UiText.T("与本页其他操作重复"),ShortcutIssue.Occupied=>UiText.T("被其他程序占用或系统不可用"),
        ShortcutIssue.RegistrationFailed=>UiText.T("原组合已不可用，请重新分配"),_=>UiText.T("可用")
    };
}
