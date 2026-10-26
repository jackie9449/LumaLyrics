using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

internal static partial class AppleMusicLibraryHandoff
{
    static MusicPlaybackModeState ReadNativePlaybackModes(AutomationElement root,CancellationToken token)
    {
        var surface=NativeSurfaceTree.Read(root,token).ToArray();
        var shuffle=surface.Where(e=>e.Properties.AutomationId.ValueOrDefault=="ShuffleButton" && e.Patterns.Toggle.IsSupported).ToArray();
        bool? shuffled=shuffle.Length==1?shuffle[0].Patterns.Toggle.Pattern.ToggleState.Value switch{ToggleState.On=>true,ToggleState.Off=>false,_=>(bool?)null}:null;
        var repeat=surface.Where(e=>e.Properties.AutomationId.ValueOrDefault=="RepeatButton").ToArray();
        return new(shuffled,repeat.Length==1?MusicPlaybackModeRules.ParseRepeat(Name(repeat[0])):null);
    }
    static bool OwnsPlaylistWindow(HandoffContext context,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();return context.Window is { } window && windowOwners.IsCurrent(context.Version) && QueueForegroundWindow()==window.Handle;
    }
    static string? ApplyNativePlaybackModes(AutomationElement root,MusicPlaybackOptions? options,CancellationToken token,HandoffContext context,Action<string,object?>? trace,Func<bool>? ownership=null)
    {
        bool Owns(){token.ThrowIfCancellationRequested();return ownership?.Invoke()??OwnsPlaylistWindow(context,token);}
        var state=ReadNativePlaybackModes(root,token);trace?.Invoke("playlistModesBefore",state);
        if(options?.Shuffle is { } shuffle && state.Shuffle!=shuffle)
        {
            if(state.Shuffle is null)return "Apple Music 没有提供可确认的随机播放状态，未切换歌曲。";
            var controls=NativeSurfaceTree.Read(root,token).Where(e=>e.Properties.AutomationId.ValueOrDefault=="ShuffleButton" && e.Patterns.Toggle.IsSupported).ToArray();
            if(controls.Length!=1 || !controls[0].IsEnabled || !Owns())return "Apple Music 的播放顺序控制暂不可用。";
            context.PlaylistModeAttempted=true;controls[0].Patterns.Toggle.Pattern.Toggle();
            var changed=WaitForNative(()=>ReadNativePlaybackModes(root,token) is { } s && s.Shuffle==shuffle?s:null,TimeSpan.FromSeconds(3),token);
            if(changed is null)return "已发送随机播放设置，但尚未确认状态，未切换歌曲。";state=changed;
        }
        if(options?.Repeat is { } repeat)
        {
            var visited=new HashSet<MusicRepeatMode>();
            for(int step=0;state.Repeat!=repeat && step<2;step++)
            {
                if(state.Repeat is not { } previous || !visited.Add(previous))return "Apple Music 没有提供可确认的循环状态，未切换歌曲。";
                var button=NativeSurfaceTree.Read(root,token).SingleOrDefault(e=>e.Properties.AutomationId.ValueOrDefault=="RepeatButton");
                if(button?.Patterns.Invoke.IsSupported!=true || !button.IsEnabled || !Owns())return "Apple Music 的循环控制暂不可用。";
                context.PlaylistModeAttempted=true;button.Patterns.Invoke.Pattern.Invoke();
                var changed=WaitForNative(()=>ReadNativePlaybackModes(root,token) is {Repeat:not null} s && s.Repeat!=previous?s:null,TimeSpan.FromSeconds(3),token);
                if(changed is null){trace?.Invoke("playlistRepeatUnconfirmed",NativeSurfaceTree.Read(root,token).Where(e=>e.Properties.AutomationId.ValueOrDefault=="RepeatButton").Select(Name).ToArray());return "已发送循环设置，但尚未确认状态，未切换歌曲。";}
                state=changed;
            }
        }
        state=ReadNativePlaybackModes(root,token);trace?.Invoke("playlistModesApplied",state);
        return state.Shuffle is not null && MusicPlaybackModeRules.Matches(options,state)?null:"尚未确认 Apple Music 的播放顺序或循环设置，未切换歌曲。";
    }
    internal static async Task<MusicPlaybackModeState> ReadPlaylistModesAsync(CancellationToken token,HandoffContext? context=null)
    {
        if(context is null)return await ReadPassiveAsync((p,a,t)=>ReadNativePlaybackModes(a.FromHandle(p.MainWindowHandle),t),new MusicPlaybackModeState(null,null),TimeSpan.FromSeconds(7),token);
        var bounded=CancellationTokenSource.CreateLinkedTokenSource(token);bounded.CancelAfter(TimeSpan.FromSeconds(7));
        var worker=Task.Run(()=>
        {
            gate.Wait(bounded.Token);
            try
            {
                if(context.Window is not { } window || !windowOwners.IsCurrent(context.Version) || IsIconic(window.Handle))return new MusicPlaybackModeState(null,null);
                using var process=Process.GetProcessById(window.ProcessId);if(process.MainWindowHandle!=window.Handle)return new(null,null);
                using var automation=new UIA3Automation{ConnectionTimeout=TimeSpan.FromSeconds(1),TransactionTimeout=TimeSpan.FromSeconds(3)};
                return ReadNativePlaybackModes(automation.FromHandle(window.Handle),bounded.Token);
            }
            finally{gate.Release();}
        },bounded.Token);
        try{return await worker.WaitAsync(TimeSpan.FromSeconds(8),token);}
        catch(OperationCanceledException) when(!token.IsCancellationRequested){return new(null,null);}
        catch(Exception ex) when(ex is TimeoutException or System.ComponentModel.Win32Exception or ArgumentException or InvalidOperationException or COMException or FlaUI.Core.Exceptions.FlaUIException){return new(null,null);}
        finally{bounded.Cancel();if(worker.IsCompleted)bounded.Dispose();else _=worker.ContinueWith(t=>{_=t.Exception;bounded.Dispose();},TaskScheduler.Default);}
    }
}
