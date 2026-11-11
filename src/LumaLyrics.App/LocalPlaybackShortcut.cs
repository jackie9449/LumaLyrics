using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
namespace LumaLyrics.App;

internal static class LocalPlaybackShortcut
{
    internal static void Attach(Window window,Action toggle)
    {
        window.PreviewKeyDown+=(_,e)=>
        {
            if(e.Handled || e.Key!=Key.Space || Keyboard.Modifiers!=ModifierKeys.None || !window.IsActive || HotkeySettingsPanel.IsRecording)return;
            if(OwnsSpace(Keyboard.FocusedElement as DependencyObject) || OwnsSpace(e.OriginalSource as DependencyObject))return;
            // Consume held-key repeats without repeatedly toggling playback.
            e.Handled=true;
            if(!e.IsRepeat)toggle();
        };
    }

    static bool OwnsSpace(DependencyObject? node)
    {
        for(;node is not null;node=Parent(node))
            if(node is TextBoxBase or PasswordBox or ButtonBase or Selector or RangeBase or MenuItem or TreeViewItem or Hyperlink)return true;
        return false;
    }

    static DependencyObject? Parent(DependencyObject node)=>node switch
    {
        Visual=>VisualTreeHelper.GetParent(node),
        FrameworkContentElement content=>content.Parent,
        _=>LogicalTreeHelper.GetParent(node)
    };
}
