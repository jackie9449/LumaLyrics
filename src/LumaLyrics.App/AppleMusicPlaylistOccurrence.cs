using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

internal static partial class AppleMusicLibraryHandoff
{
    sealed record PlaylistOccurrence(AutomationElement? Target,string? Failure);
    static PlaylistOccurrence CapturePlaylistOccurrence(UIA3Automation automation,AutomationElement list,MusicPlaylistSelection request,CancellationToken token,Action<string,object?>? trace)
    {
        if(request.Index is not { } selected)return new(null,"没有指定歌单位置。");
        double? scroll=list.Patterns.Scroll.IsSupported?list.Patterns.Scroll.Pattern.VerticalScrollPercent.Value:null;
        var watch=Stopwatch.StartNew();var rows=new List<AutomationElement>();bool complete=false;
        bool Add(AutomationElement item)
        {
            if(item.ControlType!=ControlType.ListItem)return true;
            int index=rows.Count;if(index>=request.Playlist.Songs.Count)return false;
            var expected=request.Playlist.Songs[index].Track;var names=RawNames(automation,item,token);
            if(!MusicLibraryPlayback.NamesMatch(expected,names) && item.Patterns.VirtualizedItem.IsSupported)
            {token.ThrowIfCancellationRequested();item.Patterns.VirtualizedItem.Pattern.Realize();
                if(WaitForNative(()=>MusicLibraryPlayback.NamesMatch(expected,RawNames(automation,item,token))?item:null,TimeSpan.FromSeconds(2),token) is null)return false;
                names=RawNames(automation,item,token);}
            if(!MusicLibraryPlayback.NamesMatch(expected,names))return false;
            rows.Add(item);return true;
        }
        try
        {
            if(request.Playlist.Songs.Count>5000)return new(null,"歌单过长，暂时无法完整核对原生位置，请在 Apple Music 选曲。");
            if(list.Patterns.ItemContainer.IsSupported)
            {
                var container=list.Patterns.ItemContainer.Pattern;AutomationElement? previous=null;
                for(int inspected=0;inspected<request.Playlist.Songs.Count+64 && watch.Elapsed<TimeSpan.FromSeconds(50);inspected++)
                {
                    token.ThrowIfCancellationRequested();var item=container.FindItemByProperty(previous!,null!,null!);
                    if(item is null){complete=true;break;}
                    if(item.Equals(previous) || !Add(item))return new(null,"原生歌单的顺序或录音已变化，请刷新后重试。");
                    previous=item;
                }
            }
            else
            {
                // A nonvirtual complete list may expose every occurrence directly.
                // Never infer a hidden position from only the currently visible rows.
                if(!list.Patterns.Scroll.IsSupported || list.Patterns.Scroll.Pattern.VerticallyScrollable.Value)return new(null,"Apple Music 未提供完整的歌单位置，未切换歌曲。");
                var walker=automation.TreeWalkerFactory.GetControlViewWalker();int inspected=0;
                for(var item=walker.GetFirstChild(list);item is not null;item=walker.GetNextSibling(item))
                {
                    token.ThrowIfCancellationRequested();if(inspected++>request.Playlist.Songs.Count+64 || !Add(item))return new(null,"原生歌单位置无法完整核对，请刷新后重试。");
                }
                complete=true;
            }
            trace?.Invoke("playlistOccurrences",new{count=rows.Count,complete,request.Index,elapsed=watch.Elapsed.TotalSeconds});
            return complete && rows.Count==request.Playlist.Songs.Count?new(rows[selected],null):new(null,"尚未完整读取原生歌单顺序，未切换歌曲。请稍后重试。");
        }
        finally
        {
            if(scroll>=0)try{list.Patterns.Scroll.Pattern.SetScrollPercent(-1,scroll.Value);}
            catch(Exception ex) when(ex is InvalidOperationException or COMException or FlaUI.Core.Exceptions.FlaUIException){}
        }
    }
    static string? InvokePlaylistOccurrence(UIA3Automation automation,AutomationElement root,AutomationElement item,MusicPlaylistSelection request,CancellationToken token,HandoffContext context,Action<string,object?>? trace)
    {
        if(request.Index is not { } index)return "没有指定歌单位置。";
        var track=request.Playlist.Songs[index].Track;
        token.ThrowIfCancellationRequested();
        if(item.Patterns.VirtualizedItem.IsSupported)item.Patterns.VirtualizedItem.Pattern.Realize();
        if(item.Patterns.ScrollItem.IsSupported)item.Patterns.ScrollItem.Pattern.ScrollIntoView();
        bool Valid()=>OwnsPlaylistWindow(context,token) && item.IsEnabled && MusicLibraryPlayback.NamesMatch(track,RawNames(automation,item,token));
        if(!Valid())return "原生歌单位置或焦点已变化，未发送播放命令。";
        if(FindReadySongAction(item) is not null && context.Window is { } window)
        {
            trace?.Invoke("playlistActivation",new{kind="exact-row-menu",request.Index});
            if(item.Patterns.SelectionItem.IsSupported)item.Patterns.SelectionItem.Pattern.Select();
            bool MenuStillOwned()
            {
                token.ThrowIfCancellationRequested();GetWindowThreadProcessId(QueueForegroundWindow(),out uint owner);
                return windowOwners.IsCurrent(context.Version) && owner==window.ProcessId && MusicLibraryPlayback.NamesMatch(track,RawNames(automation,item,token))
                    && (!item.Patterns.SelectionItem.IsSupported || item.Patterns.SelectionItem.Pattern.IsSelected.Value);
            }
            return InvokePlay(automation,root,item,track.Title,window.ProcessId,token,trace,null,false,context,MenuStillOwned);
        }
        // Do not treat an advertised row Invoke as proof of playback activation.
        // Without an exact-row menu, send one guarded Enter to the selected occurrence.
        if(!item.Patterns.SelectionItem.IsSupported)
        {
            if(!item.Patterns.Invoke.IsSupported)return "Apple Music 暂不接受这条歌单曲目的播放操作。";
            token.ThrowIfCancellationRequested();context.CommandAttempted=true;item.Patterns.Invoke.Pattern.Invoke();return null;
        }
        item.Patterns.SelectionItem.Pattern.Select();item.Focus();
        if(!Valid() || !item.Equals(automation.FocusedElement()) || !item.Patterns.SelectionItem.Pattern.IsSelected.Value || QueueModifiersDown())return "请确认 Apple Music 获得焦点并松开快捷键后重试。";
        token.ThrowIfCancellationRequested();context.CommandAttempted=true;FlaUI.Core.Input.Keyboard.Type(FlaUI.Core.WindowsAPI.VirtualKeyShort.RETURN);return null;
    }
}
