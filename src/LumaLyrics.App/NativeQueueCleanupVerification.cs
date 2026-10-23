using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

internal static partial class AppleMusicLibraryHandoff
{
    // Explicit integration-test cleanup only. Clear the manual section only when it
    // consists entirely of our known test insertions and the original queue is intact.
    internal static async Task<bool> RestoreQueueVerificationAsync(MusicQueueSnapshot baseline,MusicQueueSnapshot expected,CancellationToken token)
    {
        int manual=expected.Upcoming.TakeWhile(e=>e.Inserted).Count();
        if(!baseline.Available || !baseline.IsComplete || baseline.Current is null || baseline.Upcoming.Any(e=>e.Inserted) || !expected.IsComplete || manual==0 || expected.Upcoming.Count(e=>e.Inserted)!=manual || !expected.Upcoming.Skip(manual).SequenceEqual(baseline.Upcoming) || expected.Current?.Fingerprint!=baseline.Current.Fingerprint)return false;
        bool invoked=await Task.Run(()=>
        {
            gate.Wait(token);
            try
            {
                using var process=Process.GetProcessesByName("AppleMusic").FirstOrDefault(p=>p.MainWindowHandle!=0);if(process is null)return false;
                using var automation=new UIA3Automation{ConnectionTimeout=TimeSpan.FromSeconds(1),TransactionTimeout=TimeSpan.FromSeconds(3)};var root=automation.FromHandle(process.MainWindowHandle);
                var toggle=root.FindFirstDescendant(cf=>cf.ByAutomationId("PlayQueueToggleButton"));if(toggle?.Patterns.Toggle.IsSupported!=true)return false;
                bool opened=toggle.Patterns.Toggle.Pattern.ToggleState.Value==ToggleState.On;
                try
                {
                    if(!opened){toggle.Patterns.Toggle.Pattern.Toggle();token.WaitHandle.WaitOne(300);}
                    var lists=root.FindAllDescendants(cf=>cf.ByControlType(ControlType.List)).Where(l=>(l.Properties.AutomationId.ValueOrDefault??"").StartsWith("PlayQueueListView",StringComparison.Ordinal)).ToArray();
                    var actual=CaptureQueue(automation,root,lists,token,null);
                    if(!actual.IsComplete || actual.Current?.Fingerprint!=expected.Current?.Fingerprint || !actual.Upcoming.SequenceEqual(expected.Upcoming))return false;
                    var list=lists.SingleOrDefault(l=>l.Properties.AutomationId.ValueOrDefault=="PlayQueueListViewManual");if(list is null)return false;
                    var walker=automation.TreeWalkerFactory.GetControlViewWalker();var sibling=walker.GetPreviousSibling(list);AutomationElement? clear=null;bool header=false;
                    for(int i=0;i<5 && sibling is not null;i++,sibling=walker.GetPreviousSibling(sibling))
                    {
                        if(sibling.Properties.ControlType.ValueOrDefault==ControlType.Text){header=Name(sibling) is "手动添加" or "Manually Added";break;}
                        if(Name(sibling) is "清除" or "Clear" && sibling.Patterns.Invoke.IsSupported)clear=sibling;
                    }
                    if(!header || clear is null)return false;
                    token.ThrowIfCancellationRequested();clear.Patterns.Invoke.Pattern.Invoke();token.WaitHandle.WaitOne(300);return true;
                }
                finally{if(!opened && toggle.Patterns.Toggle.Pattern.ToggleState.Value==ToggleState.On)toggle.Patterns.Toggle.Pattern.Toggle();}
            }
            finally{gate.Release();}
        },token);
        if(!invoked)return false;
        var restored=await ReadQueueAsync(token);return restored.Available && restored.IsComplete && restored.Current?.Fingerprint==baseline.Current.Fingerprint && restored.Upcoming.SequenceEqual(baseline.Upcoming);
    }
}
