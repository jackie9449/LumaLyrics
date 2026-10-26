using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using LumaLyrics.Core;

namespace LumaLyrics.App;

internal static partial class AppleMusicLibraryHandoff
{
    // Observed native LCDScrubber exposes seconds, independently of GSMTC capability.
    internal static bool CanUseSeekControl(AutomationElement control,PlaybackSnapshot expected)
    {
        if(control.Properties.AutomationId.ValueOrDefault!="LCDScrubber" || control.ControlType!=ControlType.Slider || !control.IsEnabled || !control.Patterns.RangeValue.IsSupported || !PlaybackSeeking.Available(expected,true))return false;
        var range=control.Patterns.RangeValue.Pattern;
        return !range.IsReadOnly.Value && range.Minimum.Value==0 && double.IsFinite(range.Maximum.Value) && Math.Abs(range.Maximum.Value*1000-expected.Track.DurationMs!.Value)<=2000;
    }
    internal static async Task<bool> SeekControlAsync(PlaybackSnapshot expected,Func<PlaybackSnapshot?> latest,double? target,CancellationToken token,Action<string,object?>? trace=null)
    {
        var bounded=CancellationTokenSource.CreateLinkedTokenSource(token);bounded.CancelAfter(TimeSpan.FromSeconds(6));
        var worker=Task.Run(()=>
        {
            gate.Wait(bounded.Token);
            try
            {
                if(!PlaybackSeeking.SameRecording(expected,latest()) || !expected.SessionId.Contains("AppleMusic",StringComparison.OrdinalIgnoreCase))return false;
                using var process=Process.GetProcessesByName("AppleMusic").FirstOrDefault(p=>p.MainWindowHandle!=0);if(process is null)return false;
                using var automation=new UIA3Automation{ConnectionTimeout=TimeSpan.FromSeconds(1),TransactionTimeout=TimeSpan.FromSeconds(2)};
                var root=automation.FromHandle(process.MainWindowHandle);
                var controls=root.FindAllDescendants(cf=>cf.ByAutomationId("LCDScrubber"));
                if(controls.Length!=1 || !CanUseSeekControl(controls[0],expected)){trace?.Invoke("seekControlUnavailable",new{count=controls.Length,duration=expected.Track.DurationMs,enabled=controls.FirstOrDefault()?.Properties.IsEnabled.ValueOrDefault});return false;}
                if(!target.HasValue)return PlaybackSeeking.SameRecording(expected,latest());
                var current=QueueCurrent(root);
                trace?.Invoke("seekNativeRecording",new{current,expected=expected.Track});
                if(current is null || !PlaybackSeeking.MatchesNativeLabels(expected.Track,current.Title,current.Artist))return false;
                bounded.Token.ThrowIfCancellationRequested();
                if(!PlaybackSeeking.SameRecording(expected,latest()) || !CanUseSeekControl(controls[0],expected))return false;
                var seconds=Math.Clamp(target.Value/1000,0,controls[0].Patterns.RangeValue.Pattern.Maximum.Value);
                bounded.Token.ThrowIfCancellationRequested();
                trace?.Invoke("seekCommand",new{seconds});controls[0].Patterns.RangeValue.Pattern.SetValue(seconds);return true;
            }
            finally{gate.Release();}
        },bounded.Token);
        try{return await worker.WaitAsync(bounded.Token);}
        catch(OperationCanceledException) when(!token.IsCancellationRequested){return false;}
        catch(Exception ex) when(ex is not OperationCanceledException){trace?.Invoke("seekNativeError",new{type=ex.GetType().Name,ex.Message});return false;}
        finally
        {
            bounded.Cancel();
            // Keep the CTS alive if a native RPC outlives the bounded caller.
            if(worker.IsCompleted)bounded.Dispose();
            else _=worker.ContinueWith(t=>{_=t.Exception;bounded.Dispose();},TaskScheduler.Default);
        }
    }
}
