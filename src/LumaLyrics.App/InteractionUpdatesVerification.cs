using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

public partial class MainWindow
{
    async Task VerifySeekInputAsync(string directory,Dictionary<string,object?> checks)
    {
        var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);int commits=0;double dragged=0;
        var slider=new SeekSlider{Minimum=0,Maximum=180000,Value=30000,SmallChange=5000,LargeChange=10000,Margin=new(0,20,0,0)};
        slider.SetResourceReference(StyleProperty,typeof(Slider));
        var panel=new StackPanel{Margin=new(30)};panel.Children.Add(new TextBlock{Text="Drag the timeline, then press Right",FontSize=20});panel.Children.Add(slider);
        var window=new Window{Title="LumaLyrics seek input verification",Width=640,Height=210,Content=panel,WindowStartupLocation=WindowStartupLocation.CenterScreen};
        window.SetResourceReference(BackgroundProperty,"WindowBrush");window.SetResourceReference(ForegroundProperty,"TextBrush");
        slider.EditCommitted+=(_,_)=>{commits++;if(commits==1)dragged=slider.Value;if(commits==2)ready.TrySetResult();};
        try
        {
            WindowState=WindowState.Minimized;window.Show();window.Activate();
            File.WriteAllText(Path.Combine(directory,"seek-input-ready.txt"),window.Title);
            try{await ready.Task.WaitAsync(TimeSpan.FromMinutes(3));}catch(TimeoutException){}
            checks["seek.pointerAndKeyboardEachCommitOnce"]=commits==2;
            checks["seek.realPointerMovedPosition"]=dragged>60000 && dragged<175000;
            checks["seek.keyboardAddsFiveSeconds"]=Math.Abs(slider.Value-dragged-5000)<1;
            checks["seek.inputReleased"]=!slider.IsEditing && !slider.IsMouseCaptured;
        }
        finally{window.Close();}
    }

    async Task VerifyInteractionUpdatesAsync(string directory,Dictionary<string,object?> checks)
    {
        const string chinese="[00:00]风中的音乐照亮远方\n[00:12]让微光陪我们走过长夜";
        const string english="[00:00]A quiet night\n[00:12]Our voices follow the wind";
        var track=new Track("Original interaction fixture","Fixture artist","Fixture album",60000);
        void Load(string text){coordinator.Import(track,text,"Original interaction fixture",false);coordinator.SetTranslationVerificationSnapshot(track,1000);}
        async Task Settle(){await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(60);UpdateLayout();}
        Rect Bounds(FrameworkElement element)=>element.TransformToAncestor(PlaybackFooter).TransformBounds(new Rect(element.RenderSize));
        coordinator.SetTranslationVisibility(false,false);Load(english);await Settle();checks["script.foreignHidden"]=ChineseScriptHost.Visibility==Visibility.Collapsed;
        coordinator.SetTranslationVisibility(true,false);await Settle();checks["script.mainTranslationShowsSelector"]=ChineseScriptHost.IsVisible;
        coordinator.SetTranslationVisibility(false,true);await Settle();checks["script.floatingTranslationShowsSelector"]=ChineseScriptHost.IsVisible;
        coordinator.SetTranslationVisibility(false,false);Load("[00:00]風の歌を聞く\n[00:12]音樂");await Settle();checks["script.japaneseHidden"]=ChineseScriptHost.Visibility==Visibility.Collapsed;
        Load(chinese);await Settle();checks["script.chineseVisible"]=ChineseScriptHost.IsVisible;
        foreach(var theme in AppThemes.All)
        {
            SelectUiTheme(theme.Id,false);
            foreach(double width in new[]{1220d,980d,820d})
            {
                Width=width;await Settle();var selector=Bounds(ChineseScriptHost);var offsets=Bounds(OffsetActions);var actions=Bounds(PlaybackActions);
                string key=$"footer.{theme.Id}.{width:0}";
                checks[key+".selectorFits"]=selector.Left>=0 && selector.Right<=PlaybackFooter.ActualWidth+.5 && selector.Bottom<=PlaybackFooter.ActualHeight;
                checks[key+".controlsDoNotOverlap"]=!selector.IntersectsWith(offsets) && !selector.IntersectsWith(actions) && !offsets.IntersectsWith(actions);
                checks[key+".actionsFit"]=actions.Left>=0 && actions.Right<=PlaybackFooter.ActualWidth+.5;
                if(width==820)VisualVerification.Save((FrameworkElement)Content,Path.Combine(directory,$"footer-{theme.Id}-820.png"),true);
            }
        }
        var blockerWindow=new Window{Title="LumaLyrics isolated shortcut registration",Width=300,Height=140,ShowInTaskbar=false,Owner=this};
        using var blocker=new Hotkeys(blockerWindow,_=>{});using var own=new Hotkeys(this,_=>{});
        try
        {
            var plan=KeyboardShortcuts.All.ToDictionary(d=>d.Action,_=>"");plan[ShortcutAction.PlayPause]="Ctrl+Alt+Shift+F9";
            plan=blocker.Registry.Suggest(plan);var blocked=blocker.Registry.Apply(plan);
            checks["shortcuts.nativeFixtureRegistered"]=blocked.All(r=>r.Issue is ShortcutIssue.None or ShortcutIssue.Disabled);
            checks["shortcuts.nativeConflictDetected"]=own.Registry.Inspect(plan).Single(r=>r.Action==ShortcutAction.PlayPause).Issue==ShortcutIssue.Occupied;
            var suggestion=own.Registry.Suggest(plan);checks["shortcuts.nativeAlternativeDifferent"]=suggestion[ShortcutAction.PlayPause]!=plan[ShortcutAction.PlayPause];
            checks["shortcuts.nativeAlternativeRegistered"]=own.Registry.Apply(suggestion).All(r=>r.Issue is ShortcutIssue.None or ShortcutIssue.Disabled);
            own.Registry.Dispose();checks["shortcuts.disposalReleasesCombination"]=blocker.Registry.Inspect(suggestion).All(r=>r.Issue is ShortcutIssue.None or ShortcutIssue.Disabled);
        }
        finally{blockerWindow.Close();}
        Width=820;SettingsClicked(this,new());settingsWindow!.SelectShortcutsTab();await Settle();
        checks["shortcuts.settingsHaveEightBindings"]=Descendants<HotkeySettingsPanel>(embeddedPageHost).SelectMany(p=>Descendants<TextBox>(p)).Count()==8;
        VisualVerification.Save((FrameworkElement)Content,Path.Combine(directory,"shortcuts-settings-820.png"),true);
        foreach(var scroll in Descendants<ScrollViewer>(embeddedPageHost))scroll.ScrollToEnd();await Settle();
        checks["shortcuts.conflictActionVisible"]=Descendants<Button>(embeddedPageHost).Any(b=>b.IsVisible && b.Content?.ToString()==UiText.T("自动解决冲突"));
        VisualVerification.Save((FrameworkElement)Content,Path.Combine(directory,"shortcuts-settings-actions-820.png"),true);
        CloseEmbeddedPage();coordinator.SetTranslationVerificationSnapshot(null,0);
    }

    async Task VerifyGlobalShortcutInputAsync(string directory,Dictionary<string,object?> checks)
    {
        var received=new HashSet<ShortcutAction>();var complete=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var capture=new Hotkeys(this,action=>{received.Add(action);if(received.Count==KeyboardShortcuts.All.Count)complete.TrySetResult();});
        // An isolated receiver proves actual Windows delivery from another process,
        // without changing the user's playback, queue, lyrics or persistent keys.
        var bindings=capture.Registry.Suggest(KeyboardShortcuts.All.ToDictionary(d=>d.Action,d=>d.Default));
        var registered=capture.Registry.Apply(bindings);checks["shortcuts.allNativeRegistrationsAvailable"]=registered.All(r=>r.Issue==ShortcutIssue.None);
        WindowState=WindowState.Minimized;await Task.Delay(200);
        File.WriteAllText(Path.Combine(directory,"shortcut-input-ready.json"),JsonSerializer.Serialize(bindings.ToDictionary(p=>p.Key.ToString(),p=>p.Value)));
        try{await complete.Task.WaitAsync(TimeSpan.FromMinutes(3));}catch(TimeoutException){}
        foreach(var definition in KeyboardShortcuts.All)checks["shortcuts.externalInput."+definition.Action]=received.Contains(definition.Action);
        checks["shortcuts.receivedWhileMinimized"]=WindowState==WindowState.Minimized;
    }
}
