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
    internal sealed record PlaylistSelection(string? Failure,Track? FirstTrack,MusicPlaybackModeState? Modes=null);
    internal static async Task<PlaylistSelection> SelectPlaylistAsync(MusicPlaylistSelection request,CancellationToken token,HandoffContext context,Action<string,object?>? trace=null,bool modesOnly=false)
    {
        PlaylistSelection? prepared=null;
        int seconds=request.Index is null?60:90;
        var bounded=CancellationTokenSource.CreateLinkedTokenSource(token);bounded.CancelAfter(TimeSpan.FromSeconds(seconds));
        var worker=Task.Run(()=>
        {
            while(true)
            {
                bounded.Token.ThrowIfCancellationRequested();
                try{return SelectPlaylist(request,bounded.Token,context,trace,modesOnly,value=>Volatile.Write(ref prepared,value));}
                catch(Exception ex) when(!context.CommandAttempted && !context.PlaylistModeAttempted && ex is (InvalidOperationException or COMException or FlaUI.Core.Exceptions.FlaUIException))
                {trace?.Invoke("playlistReadinessRetry",new{error=ex.ToString()});bounded.Token.WaitHandle.WaitOne(200);}
            }
        },bounded.Token);
        try{return await worker.WaitAsync(TimeSpan.FromSeconds(seconds+1),token);}
        catch(OperationCanceledException) when(!token.IsCancellationRequested){return context.CommandAttempted && Volatile.Read(ref prepared) is { } sent?sent:new("Apple Music 的歌单界面等待超时，请稍后重试。",null);}
        catch(TimeoutException){bounded.Cancel();return context.CommandAttempted && Volatile.Read(ref prepared) is { } sent?sent:new("Apple Music 的歌单界面等待超时，请稍后重试。",null);}
        catch(Exception ex) when(ex is InvalidOperationException or System.ComponentModel.Win32Exception or COMException or FlaUI.Core.Exceptions.FlaUIException)
        {return context.CommandAttempted && Volatile.Read(ref prepared) is { } sent?sent:new("Apple Music 的歌单界面暂不可用，请打开客户端后重试。",null);}
        finally
        {
            bounded.Cancel();
            if(worker.IsCompleted)bounded.Dispose();
            else _=worker.ContinueWith(t=>{_=t.Exception;bounded.Dispose();},TaskScheduler.Default);
        }
    }
    static PlaylistSelection SelectPlaylist(MusicPlaylistSelection request,CancellationToken token,HandoffContext context,Action<string,object?>? trace,bool modesOnly,Action<PlaylistSelection> prepare)
    {
        var playlist=request.Playlist;
        if(!MusicPlaylistContinuation.CanSelect(request))return new("歌单尚未同步完整或名称不唯一，请刷新后重试。",null);
        gate.Wait(token);
        try
        {
            // Check the selected source and ordered occurrences again, even when the UI
            // still displays an older catalog snapshot. Never act on a renamed/stale list.
            bool Current()
            {
                var snapshot=new AppleMusicLibraryReader().Read(playlist.Source);
                return MusicPlaylistPlayback.IsCurrent(playlist,snapshot.Playlists);
            }
            if(!Current())return new("歌单已发生变化，请刷新音乐库后重试。",null);
            using var process=Process.GetProcessesByName("AppleMusic").FirstOrDefault(p=>p.MainWindowHandle!=0);
            if(process is null)return new("请先打开并登录 Apple Music。",null);
            context.Capture(process,token);nint handle=process.MainWindowHandle;
            if(WaitForNative(()=>!IsIconic(handle)?context:null,TimeSpan.FromSeconds(15),token) is null)return new("Apple Music 的窗口尚未响应，请稍后重试。",null);
            // Explicit selection verifies the visible native surface before Invoke.
            // Passive library and queue reads never acquire foreground ownership.
            lock(windowStateGate)
            {
                token.ThrowIfCancellationRequested();
                if(!windowOwners.IsCurrent(context.Version))throw new OperationCanceledException(token);
                QueueSetForegroundWindow(handle);
            }
            if(WaitForNative(()=>QueueForegroundWindow()==handle?context:null,TimeSpan.FromSeconds(3),token) is null)return new("Apple Music 尚未获得焦点，请重试歌单播放。",null);
            using var automation=new UIA3Automation{ConnectionTimeout=TimeSpan.FromSeconds(1),TransactionTimeout=TimeSpan.FromSeconds(3)};
            var root=automation.FromHandle(handle);
            AutomationElement[] Candidates()=>NativeSurfaceTree.Read(root,token).Where(e=>e.ControlType==ControlType.ListItem && Name(e)==playlist.Name)
                .Where(e=>(e.Properties.AutomationId.ValueOrDefault??"").EndsWith("-IKIND:ePlaylist",StringComparison.Ordinal)).ToArray();
            var candidates=Candidates();
            if(candidates.Length==0)
            {
                var hamburger=NativeSurfaceTree.Read(root,token).FirstOrDefault(e=>e.Properties.AutomationId.ValueOrDefault=="Hamburger_Button");
                if(hamburger?.Patterns.Invoke.IsSupported==true){token.ThrowIfCancellationRequested();hamburger.Patterns.Invoke.Pattern.Invoke();}
                candidates=WaitForNative(()=>Candidates() is {Length:>0} values?values:null,TimeSpan.FromSeconds(8),token)??[];
            }
            if(candidates.Length!=1 || !candidates[0].Patterns.SelectionItem.IsSupported)return new("没有找到唯一的 Apple Music 原生歌单，请在客户端展开歌单边栏后重试。",null);
            token.ThrowIfCancellationRequested();candidates[0].Patterns.SelectionItem.Pattern.Select();
            var watch=Stopwatch.StartNew();
            while(watch.Elapsed<TimeSpan.FromSeconds(30))
            {
                token.ThrowIfCancellationRequested();
                var button=FindPlaylistPlayButton(automation,root,token);
                if(button?.Patterns.Invoke.IsSupported==true && button.Properties.IsEnabled.ValueOrDefault)
                {
                    var header=automation.TreeWalkerFactory.GetControlViewWalker().GetParent(button);
                    if(header is not null && header.Properties.ControlType.ValueOrDefault==ControlType.Group)
                    {
                        var names=RawNames(automation,header,token);
                        var list=automation.TreeWalkerFactory.GetControlViewWalker().GetParent(header);
                        for(int depth=0;list is not null && list.ControlType!=ControlType.List && depth<6;depth++)list=automation.TreeWalkerFactory.GetControlViewWalker().GetParent(list);
                        if(list?.ControlType!=ControlType.List)list=null;
                        var rows=list is null?[]:ReadPlaylistPrefix(automation,list,Math.Min(3,playlist.Songs.Count),token);
                        var first=rows.FirstOrDefault();
                        bool rowsMatch=rows.Length==Math.Min(3,playlist.Songs.Count) && rows.Select((r,index)=>MusicLibraryPlayback.NamesMatch(playlist.Songs[index].Track,RawNames(automation,r,token))).All(v=>v);
                        trace?.Invoke("playlistContext",new{playlist.Name,header=names,visibleRows=rows.Length,rowsMatch,rows=rows.Select(r=>RawNames(automation,r,token)).ToArray()});
                        if(names.FirstOrDefault()==playlist.Name && rowsMatch)
                        {
                            if(!Current())return new("歌单已发生变化，请刷新音乐库后重试。",null);
                            AutomationElement? target=null;
                            if(request.Index is { } index)
                            {
                                var captured=CapturePlaylistOccurrence(automation,list!,request,token,trace);
                                if(captured.Failure is not null)return new(captured.Failure,null);
                                target=captured.Target;
                                if(target is null)return new("没有找到对应的歌单位置，未发送播放指令。",null);
                            }
                            if(!Current())return new("歌单已发生变化，请刷新音乐库后重试。",null);
                            string? modeFailure=ApplyNativePlaybackModes(root,request.Options,token,context,trace);
                            if(modeFailure is not null)return new(modeFailure+(context.PlaylistModeAttempted?" 部分设置可能已生效，请在 Apple Music 确认。":""),null);
                            var modes=ReadNativePlaybackModes(root,token);
                            if(!OwnsPlaylistWindow(context,token) || !candidates[0].Patterns.SelectionItem.Pattern.IsSelected.Value || RawNames(automation,header,token).FirstOrDefault()!=playlist.Name)
                                return new("Apple Music 的歌单或焦点已变化，未发送播放命令，请重试。",null);
                            if(modesOnly)return new(null,null,modes);
                            var pending=new PlaylistSelection(null,request.Index is { } chosen?playlist.Songs[chosen].Track:modes.Shuffle==true?null:playlist.Songs[0].Track,modes);
                            prepare(pending);
                            if(target is not null)
                            {
                                string? failure=InvokePlaylistOccurrence(automation,root,target,request,token,context,trace);
                                if(failure is not null)return new(failure,null);
                            }
                            else
                            {
                                if(!MusicLibraryPlayback.NamesMatch(playlist.Songs[0].Track,RawNames(automation,first!,token)))return new("原生歌单首曲已变化，未发送播放命令。",null);
                                token.ThrowIfCancellationRequested();context.CommandAttempted=true;button.Patterns.Invoke.Pattern.Invoke();
                            }
                            trace?.Invoke("playlistPlayInvoked",new{playlist.Name,playlist.DeclaredCount,request.Index,modes});
                            return pending;
                        }
                    }
                }
                token.WaitHandle.WaitOne(200);
            }
            return new("未能确认原生歌单的名称、顺序和首曲，未发送播放指令。请刷新或在 Apple Music 恢复歌单原始顺序后重试。",null);
        }
        finally{gate.Release();}
    }
    static AutomationElement[] ReadPlaylistPrefix(UIA3Automation automation,AutomationElement list,int count,CancellationToken token)
    {
        var rows=new List<AutomationElement>();
        // The playlist page includes a header and column labels inside its list.
        // Start at the actual top, then use direct child order; Track N is an album
        // track number and must never be interpreted as a playlist position.
        if(list.Patterns.Scroll.IsSupported && list.Patterns.Scroll.Pattern.VerticallyScrollable.Value)
        {token.ThrowIfCancellationRequested();list.Patterns.Scroll.Pattern.SetScrollPercent(-1,0);}
        var walker=automation.TreeWalkerFactory.GetControlViewWalker();int inspected=0;
        for(var child=walker.GetFirstChild(list);child is not null && rows.Count<count && inspected++<48;child=walker.GetNextSibling(child))
        {
            token.ThrowIfCancellationRequested();
            if(child.ControlType==ControlType.ListItem)rows.Add(child);
        }
        return rows.ToArray();
    }
}
