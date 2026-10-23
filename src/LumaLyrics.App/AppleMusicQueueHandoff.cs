using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
namespace LumaLyrics.App;

internal static partial class AppleMusicLibraryHandoff
{
    internal static Task<bool> PrepareVisibleVerificationAsync(CancellationToken token)=>Task.Run(()=>
    {
        using var process=Process.GetProcessesByName("AppleMusic").FirstOrDefault(p=>p.MainWindowHandle!=0);if(process is null)return false;
        var context=new HandoffContext();context.Capture(process);
        return WaitForNative(()=>!IsIconic(process.MainWindowHandle)?context:null,TimeSpan.FromSeconds(10),token) is not null;
    },token);
    internal static Task<object> VerifyQueueStateAsync(CancellationToken token)=>Task.Run<object>(async()=>
    {
        using var process=Process.GetProcessesByName("AppleMusic").FirstOrDefault(p=>p.MainWindowHandle!=0)??throw new InvalidOperationException("Open Apple Music for native queue verification.");
        nint handle=process.MainWindowHandle;bool minimized=IsIconic(handle);if(minimized)ShowWindow(handle,4);
        using var automation=new UIA3Automation{ConnectionTimeout=TimeSpan.FromSeconds(1),TransactionTimeout=TimeSpan.FromSeconds(3)};var root=automation.FromHandle(handle);
        var toggle=root.FindFirstDescendant(cf=>cf.ByAutomationId("PlayQueueToggleButton"))??throw new InvalidOperationException("No native queue toggle.");
        bool opened=toggle.Patterns.Toggle.Pattern.ToggleState.Value==ToggleState.On;AutomationElement? history=null;bool historyWasOn=false;
        try
        {
            if(!opened){toggle.Patterns.Toggle.Pattern.Toggle();await Task.Delay(300,token);}
            history=root.FindAllDescendants(cf=>cf.ByControlType(ControlType.Button)).FirstOrDefault(b=>Name(b) is "历史记录" or "History");
            historyWasOn=history?.Patterns.Toggle.IsSupported==true && history.Patterns.Toggle.Pattern.ToggleState.Value==ToggleState.On;
            if(!historyWasOn && history?.Patterns.Toggle.IsSupported==true){history.Patterns.Toggle.Pattern.Toggle();await Task.Delay(200,token);}
            var historyRead=await ReadQueueAsync(token);bool historyRestored=history?.Patterns.Toggle.IsSupported==true && history.Patterns.Toggle.Pattern.ToggleState.Value==ToggleState.On;
            if(!historyWasOn && historyRestored)history!.Patterns.Toggle.Pattern.Toggle();
            if(!opened && toggle.Patterns.Toggle.Pattern.ToggleState.Value==ToggleState.On)toggle.Patterns.Toggle.Pattern.Toggle();
            ShowWindow(handle,6);var backgroundRead=await ReadQueueAsync(token);
            return new{historyReadAvailable=historyRead.Available,historyPreferenceRestored=historyRestored,minimizedReadAvailable=backgroundRead.Available,minimizedPreferenceRetained=IsIconic(handle),queuePanelPreferenceRetained=(toggle.Patterns.Toggle.Pattern.ToggleState.Value==ToggleState.On)==opened,currentRecordingRetained=historyRead.Current?.Fingerprint==backgroundRead.Current?.Fingerprint,historyRead.IsComplete,upcoming=backgroundRead.Upcoming.Count};
        }
        finally
        {
            try
            {
                if(history?.Patterns.Toggle.IsSupported==true && (history.Patterns.Toggle.Pattern.ToggleState.Value==ToggleState.On)!=historyWasOn)history.Patterns.Toggle.Pattern.Toggle();
                if((toggle.Patterns.Toggle.Pattern.ToggleState.Value==ToggleState.On)!=opened)toggle.Patterns.Toggle.Pattern.Toggle();
            }
            finally{ShowWindow(handle,minimized?6:4);}
        }
    },token);
    // Explicit private diagnostic used to identify the installed client's queue UI.
    internal static Task<object> InspectQueueAsync(CancellationToken token)=>Task.Run<object>(()=>
    {
        gate.Wait(token);
        try
        {
            using var process=Process.GetProcessesByName("AppleMusic").FirstOrDefault(p=>p.MainWindowHandle!=0);
            if(process is null)return new{available=false};
            nint handle=process.MainWindowHandle;bool minimized=IsIconic(handle);if(minimized)ShowWindow(handle,4);
            try
            {
                using var automation=new UIA3Automation{ConnectionTimeout=TimeSpan.FromSeconds(1),TransactionTimeout=TimeSpan.FromSeconds(3)};
                var root=automation.FromHandle(handle);var toggle=root.FindFirstDescendant(cf=>cf.ByAutomationId("PlayQueueToggleButton"));
                if(toggle?.Patterns.Toggle.IsSupported!=true)return new{available=false,reason="No native queue toggle"};
                bool opened=toggle.Patterns.Toggle.Pattern.ToggleState.Value==ToggleState.On;
                try
                {
                    if(!opened){toggle.Patterns.Toggle.Pattern.Toggle();token.WaitHandle.WaitOne(500);token.ThrowIfCancellationRequested();}
                    var walker=automation.TreeWalkerFactory.GetControlViewWalker();var rows=new List<object>();int remaining=650;
                    void Visit(AutomationElement element,int depth)
                    {
                        token.ThrowIfCancellationRequested();if(depth>20 || --remaining<0)return;
                        string name=Name(element);rows.Add(new{depth,type=element.Properties.ControlType.ValueOrDefault.ToString(),id=element.Properties.AutomationId.ValueOrDefault,name=name[..Math.Min(240,name.Length)],scroll=element.Patterns.Scroll.IsSupported,itemContainer=element.Patterns.ItemContainer.IsSupported});
                        for(var child=walker.GetFirstChild(element);child is not null && remaining>0;child=walker.GetNextSibling(child))Visit(child,depth+1);
                    }
                    Visit(root,0);
                    var list=root.FindFirstDescendant(cf=>cf.ByAutomationId("PlayQueueListView"));
                    var items=new List<object>();bool end=false;string? failure=null;double? vertical=null;
                    if(list is not null)
                    {
                        if(list.Patterns.Scroll.IsSupported)vertical=list.Patterns.Scroll.Pattern.VerticalScrollPercent.Value;
                        try
                        {
                            var container=list.Patterns.ItemContainer.Pattern;AutomationElement? previous=null;
                            for(int i=0;i<300;i++)
                            {
                                token.ThrowIfCancellationRequested();var item=container.FindItemByProperty(previous!,null!,null!);
                                if(item is null){end=true;break;}
                                var names=RawNames(automation,item,token);
                                if(names.Length==0 && item.Patterns.VirtualizedItem.IsSupported){item.Patterns.VirtualizedItem.Pattern.Realize();names=RawNames(automation,item,token);}
                                items.Add(new{name=Name(item),names});previous=item;
                            }
                        }
                        catch(Exception ex){failure=ex.GetType().Name+": "+ex.Message;}
                        finally{if(vertical>=0 && list.Patterns.Scroll.IsSupported)list.Patterns.Scroll.Pattern.SetScrollPercent(-1,vertical.Value);}
                    }
                    var tabs=root.FindAllDescendants(cf=>cf.ByControlType(ControlType.Button)).Where(b=>Name(b) is "待播清单" or "历史记录").Select(b=>new{name=Name(b),invoke=b.Patterns.Invoke.IsSupported,selection=b.Patterns.SelectionItem.IsSupported,toggle=b.Patterns.Toggle.IsSupported}).ToArray();
                    return new{available=true,opened,elements=rows,items,end,failure,vertical,tabs};
                }
                finally{if(!opened && toggle.Patterns.Toggle.Pattern.ToggleState.Value==ToggleState.On)toggle.Patterns.Toggle.Pattern.Toggle();}
            }
            finally{if(minimized)ShowWindow(handle,6);}
        }
        finally{gate.Release();}
    },token);
}
