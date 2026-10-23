using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

internal sealed record NativeLyricsCapture(DateTimeOffset CapturedUtc,LyricsCaptureBounds Bounds,byte[]? Image,IReadOnlyList<string> NativeLines);
internal static partial class AppleMusicLibraryHandoff
{
    [DllImport("user32.dll")] static extern nint SetThreadDpiAwarenessContext(nint context);
    internal static async Task<NativeLyricsCapture> CaptureLyricsAsync(Track track,bool image,CancellationToken token,Action<string,object?>? trace=null)
    {
        using var bounded=CancellationTokenSource.CreateLinkedTokenSource(token);bounded.CancelAfter(TimeSpan.FromSeconds(25));
        return await Task.Run(()=>CaptureLyrics(track,image,bounded.Token,trace),bounded.Token);
    }
    static NativeLyricsCapture CaptureLyrics(Track track,bool image,CancellationToken token,Action<string,object?>? trace)
    {
        gate.Wait(token);var context=new HandoffContext();nint previousDpi=SetThreadDpiAwarenessContext(-4),foreground=QueueForegroundWindow();nint handle=0;
        try
        {
            using var process=Process.GetProcessesByName("AppleMusic").FirstOrDefault(p=>p.MainWindowHandle!=0)??throw new InvalidOperationException("请先打开 Apple Music，并选择需要校对的歌曲。");
            context.Capture(process);handle=process.MainWindowHandle;
            if(WaitForNative(()=>!IsIconic(handle)?process:null,TimeSpan.FromSeconds(8),token) is null)throw new InvalidOperationException("Apple Music 的窗口尚未就绪。");
            using var automation=new UIA3Automation{ConnectionTimeout=TimeSpan.FromSeconds(1),TransactionTimeout=TimeSpan.FromSeconds(3)};var root=automation.FromHandle(handle);
            bool SameRecording()=>QueueCurrent(root) is { } actual && MusicLibraryPlayback.IsRequestedRecording(track,actual);
            if(!SameRecording())throw new InvalidOperationException("Apple Music 的当前歌曲已变化，请重新开始校对。");
            var lyrics=root.FindFirstDescendant(cf=>cf.ByAutomationId("LyricsToggleButton"));var queue=root.FindFirstDescendant(cf=>cf.ByAutomationId("PlayQueueToggleButton"));
            if(lyrics?.Patterns.Toggle.IsSupported!=true)throw new InvalidOperationException("当前 Apple Music 没有可操作的歌词面板。");
            bool lyricsOn=lyrics.Patterns.Toggle.Pattern.ToggleState.Value==ToggleState.On,queueOn=queue?.Patterns.Toggle.IsSupported==true && queue.Patterns.Toggle.Pattern.ToggleState.Value==ToggleState.On;
            try
            {
                token.ThrowIfCancellationRequested();if(!lyricsOn)lyrics.Patterns.Toggle.Pattern.Toggle();
                var pane=WaitForNative(()=>
                {
                    var panes=root.FindAllDescendants(cf=>cf.ByAutomationId("PaneRoot")).Where(p=>p.FindFirstDescendant(cf=>cf.ByAutomationId("WritersTextBlock")) is not null).ToArray();
                    return panes.Length==1?panes[0].FindFirstDescendant(cf=>cf.ByAutomationId("TimeBasedLyrics")):null;
                },TimeSpan.FromSeconds(8),token)??throw new InvalidOperationException("此歌曲的 Apple Music 动态歌词尚不可读；不会把缺失内容判断为纯音乐。");
                var lines=pane.FindAllChildren(cf=>cf.ByControlType(ControlType.Group)).Select(Name).Where(s=>s.Length>0).Take(500).ToArray();
                var area=pane.BoundingRectangle;byte[]? png=null;
                if(image)
                {
                    QueueSetForegroundWindow(handle);
                    if(WaitForNative(()=>QueueForegroundWindow()==handle?pane:null,TimeSpan.FromSeconds(2),token) is null)throw new InvalidOperationException("歌词窗口未获得焦点，未截取屏幕。");
                    token.WaitHandle.WaitOne(850);token.ThrowIfCancellationRequested();area=Rectangle.Intersect(pane.BoundingRectangle,System.Windows.Forms.Screen.FromRectangle(pane.BoundingRectangle).WorkingArea);area.Inflate(-2,-2);
                    if(pane.IsOffscreen || area.Width<50 || area.Height<50 || area.Width>10000 || area.Height>10000 || !System.Windows.Forms.SystemInformation.VirtualScreen.Contains(area))throw new InvalidOperationException("歌词区域不可见或超出屏幕，请调整 Apple Music 窗口后重试。");
                    foreach(var point in new[]{new Point(area.Left+8,area.Top+8),new Point(area.Right-8,area.Top+8),new Point(area.Left+8,area.Bottom-8),new Point(area.Right-8,area.Bottom-8),new Point(area.Left+area.Width/2,area.Top+area.Height/2)})
                    {GetWindowThreadProcessId(WindowFromPoint(point),out uint pid);trace?.Invoke("lyricCaptureProbe",new{area,point,pid,expected=process.Id});if(pid!=process.Id)throw new InvalidOperationException("Apple Music 歌词区域被其他窗口遮挡，未截取屏幕。");}
                    if(!SameRecording() || QueueForegroundWindow()!=handle)throw new InvalidOperationException("当前歌曲或窗口焦点发生变化，未保存截图。");
                    using var bitmap=new Bitmap(area.Width,area.Height);using var graphics=Graphics.FromImage(bitmap);graphics.CopyFromScreen(area.Location,Point.Empty,area.Size);
                    if(!SameRecording() || QueueForegroundWindow()!=handle)throw new InvalidOperationException("截图期间歌曲或窗口发生变化，本次图像已丢弃。");
                    using var output=new MemoryStream();bitmap.Save(output,ImageFormat.Png);png=output.ToArray();trace?.Invoke("lyricCaptureImage",png);
                }
                if(!SameRecording())throw new InvalidOperationException("读取期间歌曲发生变化，本次文字已丢弃。");
                return new(DateTimeOffset.UtcNow,new(area.Left,area.Top,area.Width,area.Height),png,lines);
            }
            finally
            {
                try
                {
                    if((lyrics.Patterns.Toggle.Pattern.ToggleState.Value==ToggleState.On)!=lyricsOn)lyrics.Patterns.Toggle.Pattern.Toggle();
                    if(queue?.Patterns.Toggle.IsSupported==true && (queue.Patterns.Toggle.Pattern.ToggleState.Value==ToggleState.On)!=queueOn)queue.Patterns.Toggle.Pattern.Toggle();
                }
                catch(Exception ex) when(ex is InvalidOperationException or COMException or FlaUI.Core.Exceptions.FlaUIException){}
            }
        }
        finally
        {
            try{CompleteHandoffWindowAsync(context,false).GetAwaiter().GetResult();if(handle!=0 && QueueForegroundWindow()==handle && foreground!=0 && foreground!=handle)QueueSetForegroundWindow(foreground);}
            finally{SetThreadDpiAwarenessContext(previousDpi);gate.Release();}
        }
    }
}
