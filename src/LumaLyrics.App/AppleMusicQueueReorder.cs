using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.UIA3;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

internal static partial class AppleMusicLibraryHandoff
{
    [DllImport("user32.dll")] static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint handle,out uint processId);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll",SetLastError=true)] static extern uint SendInput(uint count,QueueMouseInput[] inputs,int size);
    [StructLayout(LayoutKind.Sequential)] struct QueueMouseData { public int X,Y;public uint Data,Flags,Time;public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] struct QueueMouseInput { public uint Type;public QueueMouseData Mouse; }
    static void QueueMovePointer(Point point)
    {
        int left=GetSystemMetrics(76),top=GetSystemMetrics(77),width=GetSystemMetrics(78),height=GetSystemMetrics(79);
        var input=new QueueMouseInput{Mouse=new(){X=(int)(((long)point.X-left)*65536/width+32768/width),Y=(int)(((long)point.Y-top)*65536/height+32768/height),Flags=0xC001}};
        if(SendInput(1,[input],Marshal.SizeOf<QueueMouseInput>())!=1)throw new InvalidOperationException("Native queue pointer input was not accepted.");
    }
    internal static async Task<string?> ReorderQueueAsync(MusicQueueMove move,CancellationToken token,HandoffContext context,Action<string,object?>? trace=null)
    {
        if(!MusicPlaybackQueue.CanMove(move))return "播放列表信息不完整或位置没有变化，请刷新后重试。";
        var bounded=CancellationTokenSource.CreateLinkedTokenSource(token);bounded.CancelAfter(TimeSpan.FromSeconds(50));
        var worker=Task.Run(()=>ReorderQueue(move,bounded.Token,context,trace),bounded.Token);
        try{return await worker.WaitAsync(TimeSpan.FromSeconds(51),token);}
        catch(OperationCanceledException) when(!token.IsCancellationRequested){return context.CommandAttempted?null:"Apple Music 的队列尚未就绪，请稍后重试。";}
        catch(TimeoutException){return context.CommandAttempted?null:"Apple Music 的队列尚未就绪，请稍后重试。";}
        catch(Exception ex) when(ex is InvalidOperationException or COMException or FlaUI.Core.Exceptions.FlaUIException)
        {return context.CommandAttempted?null:"Apple Music 暂不接受队列排序，请刷新后重试。";}
        finally{bounded.Cancel();if(worker.IsCompleted)bounded.Dispose();else _=worker.ContinueWith(t=>{_=t.Exception;bounded.Dispose();},TaskScheduler.Default);}
    }
    static string? ReorderQueue(MusicQueueMove move,CancellationToken token,HandoffContext context,Action<string,object?>? trace)
    {
        gate.Wait(token);
        nint previousDpi=SetThreadDpiAwarenessContext(-4);
        try
        {
            using var process=Process.GetProcessesByName("AppleMusic").FirstOrDefault(p=>p.MainWindowHandle!=0);if(process is null)return "请先打开 Apple Music，再调整播放顺序。";
            token.ThrowIfCancellationRequested();context.Capture(process);
            if(WaitForNative(()=>!IsIconic(process.MainWindowHandle)?context:null,TimeSpan.FromSeconds(10),token) is null)return "Apple Music 的窗口尚未就绪。";
            using var automation=new UIA3Automation{ConnectionTimeout=TimeSpan.FromSeconds(1),TransactionTimeout=TimeSpan.FromSeconds(3)};var root=automation.FromHandle(process.MainWindowHandle);
            var toggle=root.FindFirstDescendant(cf=>cf.ByAutomationId("PlayQueueToggleButton"));if(toggle?.Patterns.Toggle.IsSupported!=true)return "当前客户端没有可操作的队列。";
            var lyrics=root.FindFirstDescendant(cf=>cf.ByAutomationId("LyricsToggleButton"));bool lyricsOn=lyrics?.Patterns.Toggle.IsSupported==true && lyrics.Patterns.Toggle.Pattern.ToggleState.Value==ToggleState.On;
            bool opened=toggle.Patterns.Toggle.Pattern.ToggleState.Value==ToggleState.On;AutomationElement? history=null;bool historyOn=false;
            try
            {
                if(!opened)toggle.Patterns.Toggle.Pattern.Toggle();
                var lists=WaitForNative(()=>
                {
                    history=root.FindAllDescendants(cf=>cf.ByControlType(ControlType.Button)).FirstOrDefault(b=>Name(b) is "历史记录" or "History");
                    if(history?.Patterns.Toggle.IsSupported==true && history.Patterns.Toggle.Pattern.ToggleState.Value==ToggleState.On)
                    {
                        historyOn=true;var next=root.FindAllDescendants(cf=>cf.ByControlType(ControlType.Button)).FirstOrDefault(b=>Name(b) is "待播清单" or "Playing Next");
                        if(next?.Patterns.Toggle.IsSupported==true){next.Patterns.Toggle.Pattern.Toggle();return null;}
                    }
                    var all=root.FindAllDescendants(cf=>cf.ByControlType(ControlType.List)).Where(l=>(l.Properties.AutomationId.ValueOrDefault??"").StartsWith("PlayQueueListView",StringComparison.Ordinal)).ToArray();return all.Length>0?all:null;
                },TimeSpan.FromSeconds(12),token);
                if(lists is null)return "待播清单尚未就绪，请刷新后重试。";
                var scroll=lists.Select(l=>(List:l,Percent:l.Patterns.Scroll.IsSupported?l.Patterns.Scroll.Pattern.VerticalScrollPercent.Value:(double?)null)).ToArray();
                try
                {
                    var items=new List<AutomationElement>();var actual=CaptureQueue(automation,root,lists,token,trace,items);
                    if(!MusicPlaybackQueue.MatchesMove(move,actual) || items.Count!=actual.Upcoming.Count)return "播放列表已经变化，请刷新后重新拖动。";
                    int destination=move.InsertionIndex==items.Count?items.Count-1:move.InsertionIndex;var source=items[move.SourceIndex];var target=items[destination];
                    return DragQueueOccurrence(automation,root,process,source,target,move,context,token,trace);
                }
                finally{foreach(var (list,percent) in scroll)if(percent>=0)try{list.Patterns.Scroll.Pattern.SetScrollPercent(-1,percent.Value);}catch(Exception ex) when(ex is InvalidOperationException or COMException or FlaUI.Core.Exceptions.FlaUIException){}}
            }
            finally
            {
                try
                {
                    if(historyOn && history?.Patterns.Toggle.IsSupported==true && history.Patterns.Toggle.Pattern.ToggleState.Value!=ToggleState.On)history.Patterns.Toggle.Pattern.Toggle();
                    if(!opened && toggle.Patterns.Toggle.Pattern.ToggleState.Value==ToggleState.On)toggle.Patterns.Toggle.Pattern.Toggle();
                    if(lyricsOn && lyrics?.Patterns.Toggle.IsSupported==true && lyrics.Patterns.Toggle.Pattern.ToggleState.Value!=ToggleState.On)lyrics.Patterns.Toggle.Pattern.Toggle();
                }
                catch(Exception ex) when(ex is InvalidOperationException or COMException or FlaUI.Core.Exceptions.FlaUIException){}
            }
        }
        finally{SetThreadDpiAwarenessContext(previousDpi);gate.Release();}
    }
    static string? DragQueueOccurrence(UIA3Automation automation,AutomationElement root,Process process,AutomationElement source,AutomationElement target,MusicQueueMove move,HandoffContext context,CancellationToken token,Action<string,object?>? trace)
    {
        if(!source.Patterns.Drag.IsSupported || !source.Patterns.ScrollItem.IsSupported || !target.Patterns.ScrollItem.IsSupported)return "当前 Apple Music 队列暂不支持拖动排序。";
        if(QueueModifiersDown() || QueueKeyState(1)<0)return "请松开鼠标和快捷键后重试。";
        source.Patterns.ScrollItem.Pattern.ScrollIntoView();token.WaitHandle.WaitOne(120);token.ThrowIfCancellationRequested();
        QueueSetForegroundWindow(process.MainWindowHandle);
        if(WaitForNative(()=>QueueForegroundWindow()==process.MainWindowHandle?source:null,TimeSpan.FromSeconds(2),token) is null)return "Apple Music 未获得操作焦点，未更改顺序。";
        var sourceRow=MusicPlaybackQueue.ParseNativeRow(RawNames(automation,source,token));
        if(sourceRow?.Track!=move.Snapshot.Upcoming[move.SourceIndex].Track || QueueCurrent(root)!=move.Snapshot.Current)return "播放列表已经变化，请刷新后重新拖动。";
        var bounds=source.BoundingRectangle;if(source.IsOffscreen || bounds.Width<20 || bounds.Height<12)return "目标曲目当前不可见，请刷新后重试。";
        var original=Mouse.Position;var last=original;bool pressed=false,completed=false;
        bool Owned(Point point){GetWindowThreadProcessId(WindowFromPoint(point),out uint pid);return pid==process.Id && QueueForegroundWindow()==process.MainWindowHandle;}
        void Move(Point point)
        {
            token.ThrowIfCancellationRequested();var actual=Mouse.Position;
            if(Math.Abs(actual.X-last.X)>8 || Math.Abs(actual.Y-last.Y)>8 || !Owned(point))throw new OperationCanceledException("Native drag lost its verified input target.");
            QueueMovePointer(point);last=point;
        }
        try
        {
            var start=new Point(bounds.Left+bounds.Width/2,bounds.Top+bounds.Height/2);
            if(!Owned(start))return "队列曲目被其他窗口遮挡，未更改顺序。";
            token.ThrowIfCancellationRequested();QueueMovePointer(start);last=start;token.WaitHandle.WaitOne(250);trace?.Invoke("queueDragBefore",new{bounds,start});
            token.ThrowIfCancellationRequested();var pointer=Mouse.Position;
            if(Math.Abs(pointer.X-start.X)>8 || Math.Abs(pointer.Y-start.Y)>8 || !Owned(start) || QueueModifiersDown() || QueueKeyState(1)<0)return "输入焦点或鼠标位置已变化，未更改队列。";
            context.CommandAttempted=true;Mouse.Down(MouseButton.Left);pressed=true;token.WaitHandle.WaitOne(350);
            for(int i=1;i<=8;i++){Move(new(start.X+i*3,start.Y+i));token.WaitHandle.WaitOne(40);}
            token.WaitHandle.WaitOne(350);bool grabbed=source.Patterns.Drag.Pattern.IsGrabbed.Value;trace?.Invoke("queueDragThreshold",new{grabbed,position=Mouse.Position});token.ThrowIfCancellationRequested();
            if(!grabbed)return "Apple Music 尚未接受拖动，未调整队列。";
            if(target.IsOffscreen){target.Patterns.ScrollItem.Pattern.ScrollIntoView();token.WaitHandle.WaitOne(150);}token.ThrowIfCancellationRequested();
            var targetRow=MusicPlaybackQueue.ParseNativeRow(RawNames(automation,target,token));int targetIndex=move.InsertionIndex==move.Snapshot.Upcoming.Count?move.InsertionIndex-1:move.InsertionIndex;
            if(targetRow?.Track!=move.Snapshot.Upcoming[targetIndex].Track || QueueCurrent(root)!=move.Snapshot.Current)throw new OperationCanceledException("The native queue changed during drag.");
            var end=target.BoundingRectangle;if(target.IsOffscreen || end.Width<20 || end.Height<12)throw new InvalidOperationException("Native drop row is not visible.");
            var drop=new Point(end.Left+end.Width/2,move.InsertionIndex==move.Snapshot.Upcoming.Count?end.Bottom-3:end.Top+end.Height/3);var from=last;
            for(int i=1;i<=10;i++){Move(new(from.X+(drop.X-from.X)*i/10,from.Y+(drop.Y-from.Y)*i/10));token.WaitHandle.WaitOne(20);}
            token.ThrowIfCancellationRequested();if(!Owned(drop))throw new OperationCanceledException("Native drop lost focus.");token.WaitHandle.WaitOne(500);trace?.Invoke("queueDragTarget",new{end,drop,grabbed=source.Patterns.Drag.Pattern.IsGrabbed.Value});
            Mouse.Up(MouseButton.Left);pressed=false;completed=true;trace?.Invoke("queueMoveDropped",new{move.SourceIndex,move.InsertionIndex});return null;
        }
        finally
        {
            if(pressed){try{if(QueueForegroundWindow()==process.MainWindowHandle && !QueueModifiersDown())Keyboard.Type(FlaUI.Core.WindowsAPI.VirtualKeyShort.ESCAPE);}finally{Mouse.Up(MouseButton.Left);}}
            var point=Mouse.Position;if(Math.Abs(point.X-last.X)<=8 && Math.Abs(point.Y-last.Y)<=8 && QueueKeyState(1)>=0)QueueMovePointer(original);
            trace?.Invoke("queueDragCleanup",new{completed,buttonReleased=QueueKeyState(1)>=0});
        }
    }
}
