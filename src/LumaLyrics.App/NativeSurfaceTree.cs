using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace LumaLyrics.App;

internal static class NativeSurfaceTree
{
    // Never ask the provider to recursively walk the full library/virtualized
    // lyric tree. Find surface containers only; enumerate a verified pane later.
    internal static IEnumerable<AutomationElement> Read(AutomationElement root,CancellationToken token=default)
    {
        var walker=root.Automation.TreeWalkerFactory.GetControlViewWalker();
        var watch=Stopwatch.StartNew();int remaining=512;
        IEnumerable<AutomationElement> Visit(AutomationElement element,int depth)
        {
            token.ThrowIfCancellationRequested();
            if(--remaining<0 || depth>24 || watch.Elapsed>TimeSpan.FromSeconds(4))throw new TimeoutException("Native surface traversal exceeded its budget.");
            yield return element;
            var type=element.Properties.ControlType.ValueOrDefault;
            string id=element.Properties.AutomationId.ValueOrDefault??"";
            if(type is ControlType.List or ControlType.ListItem or ControlType.DataGrid or ControlType.DataItem or ControlType.Tree || id=="TimeBasedLyrics")yield break;
            for(var child=walker.GetFirstChild(element);child is not null;child=walker.GetNextSibling(child))
                foreach(var value in Visit(child,depth+1))yield return value;
        }
        return Visit(root,0);
    }

    internal static AutomationElement[] Children(AutomationElement parent,CancellationToken token,int maximum)
    {
        var walker=parent.Automation.TreeWalkerFactory.GetControlViewWalker();var result=new List<AutomationElement>();
        for(var child=walker.GetFirstChild(parent);child is not null;child=walker.GetNextSibling(child))
        {
            token.ThrowIfCancellationRequested();if(result.Count>=maximum)throw new TimeoutException("Native pane exceeded its item budget.");result.Add(child);
        }
        return result.ToArray();
    }
}
