using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
namespace LumaLyrics.App;

public partial class MainWindow
{
    async Task VerifyLocalPlaybackShortcutAsync(string directory,Dictionary<string,object?> checks)
    {
        int toggles=0,buttonClicks=0;
        var finished=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var status=new TextBlock{FontSize=20,Text="Space: play / pause"};
        var neutral=new Border{Focusable=true,Background=Brushes.Transparent,Padding=new(12),Child=status};
        neutral.MouseLeftButtonDown+=(_,_)=>neutral.Focus();
        var input=new TextBox{Text="Search",Margin=new(0,12,0,12)};
        var button=new Button{Content="Button keeps Space",MinHeight=38};button.Click+=(_,_)=>{buttonClicks++;status.Text=$"Playback: {toggles}; button: {buttonClicks}";};
        var panel=new StackPanel{Margin=new(24)};panel.Children.Add(neutral);panel.Children.Add(input);panel.Children.Add(button);
        var window=new Window{Title="LumaLyrics local Space verification",Width=640,Height=320,Content=panel,WindowStartupLocation=WindowStartupLocation.CenterScreen};
        window.SetResourceReference(BackgroundProperty,"WindowBrush");window.SetResourceReference(ForegroundProperty,"TextBrush");
        bool interactive=false;
        LocalPlaybackShortcut.Attach(window,()=>{toggles++;status.Text=$"Playback: {toggles}; button: {buttonClicks}";if(interactive && toggles==2)finished.TrySetResult();});
        KeyEventArgs Send(UIElement target,Key key=Key.Space,bool handled=false)
        {
            var e=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window),Environment.TickCount,key){RoutedEvent=Keyboard.PreviewKeyDownEvent,Handled=handled};target.RaiseEvent(e);return e;
        }
        try
        {
            WindowState=WindowState.Minimized;window.Show();window.Activate();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            neutral.Focus();checks["space.fixtureActive"]=window.IsActive && neutral.IsKeyboardFocused;
            checks["space.playOnce"]=Send(neutral).Handled && toggles==1;
            checks["space.pauseAgain"]=Send(neutral).Handled && toggles==2;
            checks["space.otherKeysUntouched"]=!Send(neutral,Key.Enter).Handled && toggles==2;
            Send(neutral,handled:true);checks["space.handledEventUntouched"]=toggles==2;
            var controls=new (string Name,Control Control)[]{("text",input),("readOnlyText",new TextBox{IsReadOnly=true}),("richText",new RichTextBox()),("password",new PasswordBox()),("button",button),("checkBox",new CheckBox()),("comboBox",new ComboBox()),("slider",new Slider()),("listBox",new ListBox()),("treeItem",new TreeViewItem()),("menuItem",new MenuItem())};
            foreach(var (name,control) in controls)
            {
                bool added=!panel.Children.Contains(control);if(added)panel.Children.Add(control);window.UpdateLayout();control.Focus();
                checks[$"space.{name}Preserved"]=!Send(control).Handled && toggles==2;
                if(added)panel.Children.Remove(control);
            }
            var child=new Border{Focusable=true};var list=new ListBox();var item=new ListBoxItem{Content=child};list.Items.Add(item);panel.Children.Add(list);window.UpdateLayout();child.Focus();
            Send(child);checks["space.nestedSelectorPreserved"]=toggles==2;panel.Children.Remove(list);
            var link=new Hyperlink(new Run("Link"));var text=new TextBlock(link);panel.Children.Add(text);window.UpdateLayout();link.Focus();Send(text);checks["space.hyperlinkPreserved"]=toggles==2;panel.Children.Remove(text);
            window.Hide();Send(neutral);checks["space.inactiveWindowIgnored"]=toggles==2;
            if(Environment.GetCommandLineArgs().Contains("--verify-local-space-input"))
            {
                toggles=buttonClicks=0;input.Clear();status.Text="Click here, then Space. Check text and button, then Space here again.";status.TextWrapping=TextWrapping.Wrap;
                interactive=true;window.Show();window.Activate();neutral.Focus();
                File.WriteAllText(Path.Combine(directory,"local-space-ready.txt"),window.Title);
                try{await finished.Task.WaitAsync(TimeSpan.FromMinutes(3));}catch(TimeoutException){}
                checks["space.realInputTogglesTwice"]=toggles==2;
                checks["space.realInputButtonKeptAction"]=buttonClicks==1;
                checks["space.realInputTextKeepsSpace"]=input.Text==" ";
            }
        }
        finally{window.Close();}
    }
}
