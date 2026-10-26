using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
namespace LumaLyrics.App;

internal static partial class AppleMusicLibraryHandoff
{
    static AutomationElement? FindPlaylistPlayButton(UIA3Automation automation,AutomationElement root,CancellationToken token)
    {
        var walker=automation.TreeWalkerFactory.GetControlViewWalker();
        foreach(var list in NativeSurfaceTree.Read(root,token).Where(e=>e.ControlType==ControlType.List))
        {
            int inspected=0;
            for(var child=walker.GetFirstChild(list);child is not null && inspected++<32;child=walker.GetNextSibling(child))
            {
                token.ThrowIfCancellationRequested();if(child.ControlType==ControlType.ListItem)break;
                if(child.ControlType!=ControlType.Group)continue;
                var button=child.FindFirstDescendant(cf=>cf.ByAutomationId("PlayButton").And(cf.ByControlType(ControlType.Button)));
                if(button is not null)return button;
            }
        }
        return null;
    }
    internal static object InspectPlaylistSurface()
    {
        using var process=Process.GetProcessesByName("AppleMusic").FirstOrDefault(p=>p.MainWindowHandle!=0);
        if(process is null)return new{available=false};
        using var automation=new UIA3Automation{ConnectionTimeout=TimeSpan.FromSeconds(1),TransactionTimeout=TimeSpan.FromSeconds(3)};
        var root=automation.FromHandle(process.MainWindowHandle);
        return new{available=true,process.Id,minimized=IsIconic(process.MainWindowHandle),modes=ReadNativePlaybackModes(root,default),
            elements=NativeSurfaceTree.Read(root).Select(e=>new{name=Name(e),id=e.Properties.AutomationId.ValueOrDefault,type=e.ControlType.ToString()}).ToArray()};
    }
}
