using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

internal static partial class AppleMusicLibraryHandoff
{
    internal static async Task<MusicQueueSnapshot> ReadQueueAsync(CancellationToken token,Action<string,object?>? trace=null,HandoffContext? command=null)
    {
        if(command is null)return await ReadPassiveAsync((process,automation,nativeToken)=>ReadQueueSurface(automation,automation.FromHandle(process.MainWindowHandle),nativeToken,trace),
            MusicQueueSnapshot.Unavailable("Apple Music 暂不可读；超时后暂停自动读取，重新启动客户端可恢复。"),TimeSpan.FromSeconds(18),token);
        var bounded=CancellationTokenSource.CreateLinkedTokenSource(token);bounded.CancelAfter(TimeSpan.FromSeconds(18));
        var worker=Task.Run(()=>ReadQueue(bounded.Token,trace,command),bounded.Token);
        try{return await worker.WaitAsync(TimeSpan.FromSeconds(19),token);}
        catch(OperationCanceledException) when(!token.IsCancellationRequested){return MusicQueueSnapshot.Unavailable("播放列表读取超时，请稍后刷新。");}
        catch(TimeoutException){bounded.Cancel();return MusicQueueSnapshot.Unavailable("Apple Music 的播放列表暂未响应，请稍后刷新。");}
        catch(Exception ex) when(ex is InvalidOperationException or COMException or FlaUI.Core.Exceptions.FlaUIException){return MusicQueueSnapshot.Unavailable("Apple Music 的播放列表暂不可读，请打开客户端后刷新。");}
        finally
        {
            bounded.Cancel();if(worker.IsCompleted)bounded.Dispose();
            else _=worker.ContinueWith(t=>{_=t.Exception;bounded.Dispose();},TaskScheduler.Default);
        }
    }
    static MusicQueueSnapshot ReadQueue(CancellationToken token,Action<string,object?>? trace,HandoffContext? command)
    {
        gate.Wait(token);
        try
        {
            using var process=Process.GetProcessesByName("AppleMusic").FirstOrDefault(p=>p.MainWindowHandle!=0);
            if(process is null)return MusicQueueSnapshot.Unavailable("请先打开并登录 Apple Music，再刷新播放列表。");
            token.ThrowIfCancellationRequested();
            if(command is null && IsIconic(process.MainWindowHandle))return MusicQueueSnapshot.Unavailable("Apple Music 已最小化；暂停读取待播清单，避免弹出窗口。");
            if(command is not null){command.Capture(process,token);if(WaitForNative(()=>!IsIconic(process.MainWindowHandle)?process:null,TimeSpan.FromSeconds(5),token) is null)return MusicQueueSnapshot.Unavailable("Apple Music 的窗口尚未就绪。");}
            using var automation=new UIA3Automation{ConnectionTimeout=TimeSpan.FromSeconds(1),TransactionTimeout=TimeSpan.FromSeconds(3)};
            return ReadQueueSurface(automation,automation.FromHandle(process.MainWindowHandle),token,trace,command is not null);
        }
        finally{gate.Release();}
    }
    internal static MusicQueueSnapshot ReadQueueSurface(UIA3Automation automation,AutomationElement root,CancellationToken token,Action<string,object?>? trace=null,bool allowPanelChanges=false)
    {
            var surface=NativeSurfaceTree.Read(root,token).ToArray();
            var toggle=surface.FirstOrDefault(e=>e.Properties.AutomationId.ValueOrDefault=="PlayQueueToggleButton");
            if(toggle?.Patterns.Toggle.IsSupported!=true)return MusicQueueSnapshot.Unavailable("当前 Apple Music 版本未提供可读取的播放列表。");
            bool opened=toggle.Patterns.Toggle.Pattern.ToggleState.Value==ToggleState.On;AutomationElement? history=null;bool restoreHistory=false;
            if(!allowPanelChanges && !opened)return MusicQueueSnapshot.Unavailable("Apple Music 的待播清单未展开；自动刷新不会打开原生面板。");
            var lyrics=surface.FirstOrDefault(e=>e.Properties.AutomationId.ValueOrDefault=="LyricsToggleButton");bool lyricsOn=lyrics?.Patterns.Toggle.IsSupported==true && lyrics.Patterns.Toggle.Pattern.ToggleState.Value==ToggleState.On;
            // Reads do not restore or activate a minimized native window. Unsupported
            // providers return an unavailable snapshot instead of repeatedly flashing it.
            try
            {
                token.ThrowIfCancellationRequested();if(!opened){toggle.Patterns.Toggle.Pattern.Toggle();token.WaitHandle.WaitOne(250);}
                MusicQueueSnapshot? emptyCandidate=null;
                for(int attempt=0;attempt<10;attempt++)
                {
                    token.ThrowIfCancellationRequested();
                    surface=NativeSurfaceTree.Read(root,token).ToArray();
                    history=surface.FirstOrDefault(b=>b.ControlType==ControlType.Button && Name(b) is "历史记录" or "History");
                    if(history?.Patterns.Toggle.IsSupported==true && history.Patterns.Toggle.Pattern.ToggleState.Value==ToggleState.On)
                    {
                        if(!allowPanelChanges)return MusicQueueSnapshot.Unavailable("Apple Music 正在显示历史记录；保留原生面板选择。");
                        var upcoming=surface.FirstOrDefault(b=>b.ControlType==ControlType.Button && Name(b) is "待播清单" or "Playing Next");
                        if(upcoming?.Patterns.Toggle.IsSupported==true){token.ThrowIfCancellationRequested();upcoming.Patterns.Toggle.Pattern.Toggle();restoreHistory=true;token.WaitHandle.WaitOne(150);}
                    }
                    if(restoreHistory)surface=NativeSurfaceTree.Read(root,token).ToArray();
                    var lists=surface.Where(l=>l.ControlType==ControlType.List && (l.Properties.AutomationId.ValueOrDefault??"").StartsWith("PlayQueueListView",StringComparison.Ordinal)).ToArray();
                    if(lists.Length>0)return CaptureQueue(automation,root,lists,token,trace,allowRealize:allowPanelChanges);
                    // The installed client removes all queue lists after the final
                    // upcoming song starts. Its explicit empty state, observed twice,
                    // distinguishes an empty queue from an unavailable provider.
                    var empty=CaptureExplicitEmptyQueue(root);
                    if(empty is not null && emptyCandidate is not null && empty.Current==emptyCandidate.Current){trace?.Invoke("queueSnapshot",empty);return empty;}
                    emptyCandidate=empty;
                    token.WaitHandle.WaitOne(100);
                }
                return MusicQueueSnapshot.Unavailable("Apple Music 暂未提供待播清单，请打开客户端的队列后重试。");
            }
            finally
            {
                try
                {
                    if(allowPanelChanges)
                    {
                        if(restoreHistory && history?.Patterns.Toggle.IsSupported==true && history.Patterns.Toggle.Pattern.ToggleState.Value!=ToggleState.On)history.Patterns.Toggle.Pattern.Toggle();
                        if(!opened && toggle.Patterns.Toggle.Pattern.ToggleState.Value==ToggleState.On)toggle.Patterns.Toggle.Pattern.Toggle();
                        if(lyricsOn && lyrics?.Patterns.Toggle.IsSupported==true && lyrics.Patterns.Toggle.Pattern.ToggleState.Value!=ToggleState.On)lyrics.Patterns.Toggle.Pattern.Toggle();
                    }
                }
                catch(Exception ex) when(ex is InvalidOperationException or COMException or FlaUI.Core.Exceptions.FlaUIException){}
            }
    }
    static Track? QueueCurrent(AutomationElement root)
        =>CurrentFromSurface(NativeSurfaceTree.Read(root));
    static Track? CurrentFromSurface(IEnumerable<AutomationElement> surface)
    {
        var names=surface.Where(e=>e.Properties.AutomationId.ValueOrDefault=="ScrollingText").Select(Name).Where(n=>n.Length>0).Take(3).ToArray();
        return names.Length==2?MusicPlaybackQueue.ParseNativeRow(names)?.Track:null;
    }
    internal static MusicQueueSnapshot? CaptureExplicitEmptyQueue(AutomationElement root)
    {
        var current=QueueCurrent(root);
        bool empty=NativeSurfaceTree.Read(root).Where(e=>e.Properties.AutomationId.ValueOrDefault=="PaneRoot").Any(pane=>
            NativeSurfaceTree.Read(pane).Any(e=>e.ControlType==ControlType.Text && Name(e)=="无待播歌曲。")
            && NativeSurfaceTree.Read(pane).FirstOrDefault(e=>e.ControlType==ControlType.Button && Name(e) is "待播清单" or "Playing Next") is { } button
            && button.Patterns.Toggle.IsSupported && button.Patterns.Toggle.Pattern.ToggleState.Value==ToggleState.On);
        return empty && current==QueueCurrent(root)?new(current,[],true,true,DateTimeOffset.UtcNow,"接下来还没有歌曲"):null;
    }
    internal static MusicQueueSnapshot CaptureQueue(UIA3Automation automation,AutomationElement root,IReadOnlyList<AutomationElement> lists,CancellationToken token,Action<string,object?>? trace,List<AutomationElement>? nativeItems=null,bool allowRealize=true)
    {
        var before=QueueCurrent(root);var entries=new List<MusicQueueEntry>();bool complete=lists.Count>0,metadataComplete=true;
        var watch=Stopwatch.StartNew();
        void Add(AutomationElement item,bool inserted)
        {
            var names=RawNames(automation,item,token);
            if(names.Length==0 && allowRealize && item.Patterns.VirtualizedItem.IsSupported){token.ThrowIfCancellationRequested();item.Patterns.VirtualizedItem.Pattern.Realize();names=RawNames(automation,item,token);}
            var entry=MusicPlaybackQueue.ParseNativeRow(names);
            if(entry is null){metadataComplete=false;entry=new(new(Name(item),"","",null),false);}
            entries.Add(entry with{Inserted=inserted});nativeItems?.Add(item);
        }
        // Manual Play Next entries occupy a distinct native list before the album's
        // remaining queue. Read all exposed queue lists in native control-tree order.
        foreach(var list in lists)
        {
            double? vertical=list.Patterns.Scroll.IsSupported?list.Patterns.Scroll.Pattern.VerticalScrollPercent.Value:null;
            bool ended=false,inserted=list.Properties.AutomationId.ValueOrDefault=="PlayQueueListViewManual";
            try
            {
                if(list.Patterns.ItemContainer.IsSupported)
                {
                    var container=list.Patterns.ItemContainer.Pattern;AutomationElement? previous=null;
                    // ItemContainer traverses virtualized rows, unlike visible descendants.
                    while(entries.Count<2000 && watch.Elapsed<TimeSpan.FromSeconds(11))
                    {
                        token.ThrowIfCancellationRequested();var item=container.FindItemByProperty(previous!,null!,null!);
                        if(item is null){ended=true;break;}
                        if(previous is not null && item.Equals(previous))break;
                        Add(item,inserted);previous=item;
                    }
                }
                else
                {
                    foreach(var item in list.FindAllChildren(cf=>cf.ByControlType(ControlType.ListItem))){token.ThrowIfCancellationRequested();if(entries.Count>=2000 || watch.Elapsed>=TimeSpan.FromSeconds(11))break;Add(item,inserted);}
                    ended=list.Patterns.Scroll.IsSupported && !list.Patterns.Scroll.Pattern.VerticallyScrollable.Value;
                }
            }
            finally
            {
                if(allowRealize && vertical>=0)try{list.Patterns.Scroll.Pattern.SetScrollPercent(-1,vertical.Value);}
                catch(Exception ex) when(ex is InvalidOperationException or COMException or FlaUI.Core.Exceptions.FlaUIException){}
            }
            complete&=ended;if(!ended)break;
        }
        var current=QueueCurrent(root);
        if(before?.Fingerprint!=current?.Fingerprint)return MusicQueueSnapshot.Unavailable("播放刚刚发生变化，请刷新以读取最新列表。");
        complete&=metadataComplete;
        var snapshot=new MusicQueueSnapshot(current,entries,true,complete,DateTimeOffset.UtcNow,complete?"与 Apple Music 待播清单同步":$"已读取 {entries.Count:N0} 首；Apple Music 暂未完整提供其余信息。");
        trace?.Invoke("queueSnapshot",snapshot);return snapshot;
    }
}
