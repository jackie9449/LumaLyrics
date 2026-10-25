using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

internal static partial class AppleMusicLibraryHandoff
{
    [DllImport("user32.dll",EntryPoint="GetForegroundWindow")] static extern nint QueueForegroundWindow();
    [DllImport("user32.dll",EntryPoint="SetForegroundWindow")] static extern bool QueueSetForegroundWindow(nint handle);
    [DllImport("user32.dll",EntryPoint="GetAsyncKeyState")] static extern short QueueKeyState(int key);
    static bool QueueModifiersDown()=>new[]{0x10,0x11,0x12,0x5B,0x5C}.Any(key=>QueueKeyState(key)<0);
    internal static async Task<string?> SelectQueueAsync(MusicQueueSelection selection,CancellationToken token,HandoffContext context,Action<string,object?>? trace=null)
    {
        if(!MusicPlaybackQueue.CanSelect(selection))return "这首歌的队列信息不完整，请刷新后重试。";
        var bounded=CancellationTokenSource.CreateLinkedTokenSource(token);bounded.CancelAfter(TimeSpan.FromSeconds(60));
        var worker=Task.Run(()=>
        {
            while(true)
            {
                bounded.Token.ThrowIfCancellationRequested();
                try{return SelectQueue(selection,bounded.Token,context,trace);}
                catch(Exception ex) when(!context.CommandAttempted && ex is (InvalidOperationException or COMException or FlaUI.Core.Exceptions.FlaUIException))
                {trace?.Invoke("nativeQueueReadinessRetry",new{error=ex.GetType().Name});bounded.Token.WaitHandle.WaitOne(200);}
            }
        },bounded.Token);
        try{return await worker.WaitAsync(TimeSpan.FromSeconds(61),token);}
        catch(OperationCanceledException) when(!token.IsCancellationRequested){return context.CommandAttempted?null:"Apple Music 的播放列表尚未响应，请稍后重试。";}
        catch(TimeoutException){bounded.Cancel();return context.CommandAttempted?null:"Apple Music 的播放列表尚未响应，请稍后重试。";}
        catch(Exception ex) when(ex is InvalidOperationException or COMException or FlaUI.Core.Exceptions.FlaUIException)
        {return context.CommandAttempted?null:"Apple Music 的播放列表暂不可操作，请刷新后重试。";}
        finally
        {
            bounded.Cancel();if(worker.IsCompleted)bounded.Dispose();
            else _=worker.ContinueWith(t=>{_=t.Exception;bounded.Dispose();},TaskScheduler.Default);
        }
    }
    static string? SelectQueue(MusicQueueSelection selection,CancellationToken token,HandoffContext context,Action<string,object?>? trace)
    {
        gate.Wait(token);
        try
        {
            using var process=Process.GetProcessesByName("AppleMusic").FirstOrDefault(p=>p.MainWindowHandle!=0);
            if(process is null)return "请先打开并登录 Apple Music，再选择播放列表中的歌曲。";
            token.ThrowIfCancellationRequested();context.Capture(process,token);
            if(WaitForNative(()=>!IsIconic(process.MainWindowHandle)?context:null,TimeSpan.FromSeconds(15),token) is null)return "Apple Music 的窗口尚未响应，请稍后重试。";
            using var automation=new UIA3Automation{ConnectionTimeout=TimeSpan.FromSeconds(1),TransactionTimeout=TimeSpan.FromSeconds(3)};
            var root=automation.FromHandle(process.MainWindowHandle);var toggle=root.FindFirstDescendant(cf=>cf.ByAutomationId("PlayQueueToggleButton"));
            if(toggle?.Patterns.Toggle.IsSupported!=true)return "当前 Apple Music 版本暂未提供可操作的播放列表。";
            bool opened=toggle.Patterns.Toggle.Pattern.ToggleState.Value==ToggleState.On;AutomationElement? history=null;bool restoreHistory=false;
            var lyrics=root.FindFirstDescendant(cf=>cf.ByAutomationId("LyricsToggleButton"));bool lyricsOn=lyrics?.Patterns.Toggle.IsSupported==true && lyrics.Patterns.Toggle.Pattern.ToggleState.Value==ToggleState.On;
            try
            {
                token.ThrowIfCancellationRequested();if(!opened)toggle.Patterns.Toggle.Pattern.Toggle();
                var lists=WaitForNative(()=>
                {
                    history=root.FindAllDescendants(cf=>cf.ByControlType(ControlType.Button)).FirstOrDefault(b=>Name(b) is "历史记录" or "History");
                    if(history?.Patterns.Toggle.IsSupported==true && history.Patterns.Toggle.Pattern.ToggleState.Value==ToggleState.On)
                    {
                        var upcoming=root.FindAllDescendants(cf=>cf.ByControlType(ControlType.Button)).FirstOrDefault(b=>Name(b) is "待播清单" or "Playing Next");
                        if(upcoming?.Patterns.Toggle.IsSupported==true){token.ThrowIfCancellationRequested();upcoming.Patterns.Toggle.Pattern.Toggle();restoreHistory=true;return null;}
                    }
                    var found=root.FindAllDescendants(cf=>cf.ByControlType(ControlType.List)).Where(l=>(l.Properties.AutomationId.ValueOrDefault??"").StartsWith("PlayQueueListView",StringComparison.Ordinal)).ToArray();
                    return found.Length>0?found:CaptureExplicitEmptyQueue(root) is not null?[]:null;
                },TimeSpan.FromSeconds(15),token);
                return lists is null?"Apple Music 尚未显示待播清单，请刷新后重试。":lists.Length==0?"播放列表已经变化，请刷新后重新选择这首歌。":InvokeQueueOccurrence(automation,root,lists,selection,token,context,trace);
            }
            finally
            {
                try
                {
                    if(restoreHistory && history?.Patterns.Toggle.IsSupported==true && history.Patterns.Toggle.Pattern.ToggleState.Value!=ToggleState.On)history.Patterns.Toggle.Pattern.Toggle();
                    if(!opened && toggle.Patterns.Toggle.Pattern.ToggleState.Value==ToggleState.On)toggle.Patterns.Toggle.Pattern.Toggle();
                    if(lyricsOn && lyrics?.Patterns.Toggle.IsSupported==true && lyrics.Patterns.Toggle.Pattern.ToggleState.Value!=ToggleState.On)lyrics.Patterns.Toggle.Pattern.Toggle();
                }
                catch(Exception ex) when(ex is InvalidOperationException or COMException or FlaUI.Core.Exceptions.FlaUIException){}
            }
        }
        finally{gate.Release();}
    }
    internal static string? InvokeQueueOccurrence(UIA3Automation automation,AutomationElement root,IReadOnlyList<AutomationElement> lists,MusicQueueSelection selection,CancellationToken token,HandoffContext context,Action<string,object?>? trace=null)
    {
        var scroll=lists.Select(l=>(List:l,Percent:l.Patterns.Scroll.IsSupported?l.Patterns.Scroll.Pattern.VerticalScrollPercent.Value:(double?)null)).ToArray();
        try
        {
            var items=new List<AutomationElement>();var actual=CaptureQueue(automation,root,lists,token,trace,items);
            if(!MusicPlaybackQueue.MatchesSelection(selection,actual) || items.Count!=actual.Upcoming.Count)return "播放列表已经变化，请刷新后重新选择这首歌。";
            var item=items[selection.Index];token.ThrowIfCancellationRequested();
            if(item.Patterns.VirtualizedItem.IsSupported)item.Patterns.VirtualizedItem.Pattern.Realize();
            if(item.Patterns.ScrollItem.IsSupported)item.Patterns.ScrollItem.Pattern.ScrollIntoView();
            var requested=actual.Upcoming[selection.Index];var row=MusicPlaybackQueue.ParseNativeRow(RawNames(automation,item,token));
            if(row is null || row.Track!=requested.Track || QueueCurrent(root)!=actual.Current)return "播放列表刚刚发生变化，请刷新后重试。";
            if(!item.IsEnabled || !item.Patterns.Invoke.IsSupported && !item.Patterns.SelectionItem.IsSupported)return "当前 Apple Music 版本暂不支持直接跳到这条队列曲目。";
            // Invoke this captured occurrence, never a title search, library Play or a
            // sequence of Next commands. An ambiguous RPC timeout must not retry it.
            if(!item.Patterns.Invoke.IsSupported)
            {
                // WinUI's queue rows expose SelectionItem but no Invoke. Selection
                // alone does not play: focus this verified row, then use its Enter
                // action only while this exact native window and row own focus.
                if(context.Window is not { } window)return "尚未确认 Apple Music 窗口，未发送播放命令。";
                token.ThrowIfCancellationRequested();QueueSetForegroundWindow(window.Handle);
                if(WaitForNative(()=>QueueForegroundWindow()==window.Handle?item:null,TimeSpan.FromSeconds(2),token) is null)return "Apple Music 尚未获得焦点，请重新选择播放列表中的歌曲。";
                item.Patterns.SelectionItem.Pattern.Select();item.Focus();
                if(QueueForegroundWindow()!=window.Handle || !item.Equals(automation.FocusedElement()) || !item.Patterns.SelectionItem.Pattern.IsSelected.Value)
                    return "Apple Music 的队列焦点已经变化，未发送播放命令，请重试。";
                row=MusicPlaybackQueue.ParseNativeRow(RawNames(automation,item,token));
                if(row?.Track!=requested.Track || QueueCurrent(root)!=actual.Current)return "播放列表刚刚发生变化，请刷新后重试。";
                token.ThrowIfCancellationRequested();
                if(QueueForegroundWindow()!=window.Handle || !item.Equals(automation.FocusedElement()))return "Apple Music 的队列焦点已经变化，请重试。";
                if(QueueModifiersDown())return "请松开快捷键后，重新选择播放列表中的歌曲。";
                token.ThrowIfCancellationRequested();context.CommandAttempted=true;FlaUI.Core.Input.Keyboard.Type(FlaUI.Core.WindowsAPI.VirtualKeyShort.RETURN);
            }
            else{token.ThrowIfCancellationRequested();context.CommandAttempted=true;item.Patterns.Invoke.Pattern.Invoke();}
            trace?.Invoke("queueOccurrenceInvoked",new{selection.Index,requested.Inserted,invoked=item.Patterns.Invoke.IsSupported});return null;
        }
        finally
        {
            foreach(var (list,percent) in scroll)if(percent>=0)try{list.Patterns.Scroll.Pattern.SetScrollPercent(-1,percent.Value);}
            catch(Exception ex) when(ex is InvalidOperationException or COMException or FlaUI.Core.Exceptions.FlaUIException){}
        }
    }
}
