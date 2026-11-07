using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;
public partial class MainWindow
{
    async Task VerifyLibraryTabsAsync(string directory,Dictionary<string,object?> checks)
    {
        string originalTheme=settings.UiTheme;bool reduced=settings.ReduceMotion;SettingsWindow? window=null;
        try
        {
            window=new(coordinator,id=>SelectUiTheme(id,false)){Owner=this};window.Show();window.SelectLibraryTab();var tabs=window.Library.Navigation;
            foreach(var theme in AppThemes.All)
            {
                SelectUiTheme(theme.Id,false);
                foreach(double width in new[]{1220d,980d,820d,640d})
                {
                    window.Width=width;window.Height=width==640?520:820;
                    for(int index=0;index<4;index++)
                    {
                        tabs.SelectedIndex=index;await Task.Delay(45);var tab=(TabItem)tabs.Items[index];tab.Focus();await Task.Delay(150);window.UpdateLayout();
                        var strip=(FrameworkElement)VisualTreeHelper.GetParent(tab);var ink=VisualTreeHelper.GetDescendantBounds(tab);var clip=VisualTreeHelper.GetClip(tab)?.Bounds;
                        var visible=clip??new Rect(0,0,tab.ActualWidth,tab.ActualHeight);
                        bool contained=ink.IsEmpty||ink.Left>=visible.Left-0.5&&ink.Top>=visible.Top-0.5&&ink.Right<=visible.Right+0.5&&ink.Bottom<=visible.Bottom+0.5;
                        checks[$"tabs.{theme.Id}.{width}.{index}.fullFeedbackBounds"]=contained;
                        checks[$"tabs.{theme.Id}.{width}.{index}.selectionAndKeyboard"]=tab.IsSelected&&tab.IsKeyboardFocused;
                        if(index==1){VisualVerification.Save(strip,Path.Combine(directory,$"tabs-{theme.Id}-{width}.png"));checks[$"tabs.{theme.Id}.{width}.geometry"]=new{tab.ActualWidth,tab.ActualHeight,Ink=ink.ToString(),Clip=clip?.ToString()};}
                    }
                    tabs.SelectedIndex=0;window.UpdateLayout();((TabItem)tabs.Items[0]).Focus();await Task.Delay(60);
                    foreach(var (key,expected) in new[]{(Key.Right,1),(Key.Left,0),(Key.End,3),(Key.Home,0)})
                    {
                        var focused=(TabItem)tabs.Items[tabs.SelectedIndex];
                        // Directional traversal normally runs in InputManager, outside routed KeyDown delivery.
                        if(key is Key.Right or Key.Left)focused.MoveFocus(new TraversalRequest(key==Key.Right?FocusNavigationDirection.Right:FocusNavigationDirection.Left));
                        else focused.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(focused),Environment.TickCount,key){RoutedEvent=Keyboard.KeyDownEvent});
                        await Task.Delay(45);
                        checks[$"tabs.{theme.Id}.{width}.keyboard.{key}"]=tabs.SelectedIndex==expected&&((TabItem)tabs.Items[expected]).IsKeyboardFocused;
                        checks[$"tabs.{theme.Id}.{width}.keyboardState.{key}"]=new{tabs.SelectedIndex,Focused=(Keyboard.FocusedElement as FrameworkElement)?.GetType().Name};
                    }
                }
            }
            AppMotion.Configure(true);tabs.SelectedIndex=0;await Task.Delay(40);((TabItem)tabs.Items[0]).Focus();checks["tabs.reducedMotion.selection"]=((TabItem)tabs.Items[0]).IsSelected;
        }
        finally{window?.Close();AppMotion.Configure(reduced);SelectUiTheme(originalTheme,false);}
    }
}
