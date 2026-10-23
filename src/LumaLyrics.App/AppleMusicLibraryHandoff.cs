using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using LumaLyrics.Infrastructure;
using LumaLyrics.Core;
namespace LumaLyrics.App;

// Use the native local Songs view, avoiding catalog searches and synthetic input.
// A command is accepted only for a unique row with the requested recording metadata.
internal static partial class AppleMusicLibraryHandoff
{
    static readonly SemaphoreSlim gate=new(1,1);
    [DllImport("user32.dll")] static extern bool IsIconic(nint handle);
    [DllImport("user32.dll")] static extern bool ShowWindow(nint handle,int command);
    internal sealed record AlbumSelection(string? Failure,Track? FirstTrack);
    sealed class AlbumRequest(MusicAlbum album){internal MusicAlbum Album=>album;internal Track? FirstTrack {get;set;}}
    internal static Task<string?> SelectAsync(MusicLibrarySong song,CancellationToken token,Action<string,object?>? trace=null)=>RunAsync(song,null,token,trace);
    internal static Task<string?> InsertNextAsync(MusicLibrarySong song,CancellationToken token,Action<string,object?>? trace=null)=>RunAsync(song,null,token,trace,true);
    internal static async Task<AlbumSelection> SelectAlbumAsync(MusicAlbum album,CancellationToken token,Action<string,object?>? trace=null)
    {
        if(MusicLibraryPlayback.AlbumAnchor(album) is not { } anchor)return new("这张专辑的录音信息不完整，暂时无法准确播放。",null);
        var request=new AlbumRequest(album);string? failure=await RunAsync(anchor,request,token,trace);return new(failure,request.FirstTrack);
    }
    static async Task<string?> RunAsync(MusicLibrarySong song,AlbumRequest? album,CancellationToken token,Action<string,object?>? trace,bool insertNext=false)
    {
        int seconds=album is null?15:25;
        var bounded=CancellationTokenSource.CreateLinkedTokenSource(token);bounded.CancelAfter(TimeSpan.FromSeconds(seconds));
        var worker=Task.Run(()=>Select(song,album,bounded.Token,trace,insertNext),bounded.Token);
        try{return await worker.WaitAsync(TimeSpan.FromSeconds(seconds+1),token);}
        catch(OperationCanceledException) when(!token.IsCancellationRequested){return "Apple Music 的选歌界面暂未响应，请稍后重试。";}
        catch(TimeoutException){bounded.Cancel();return "Apple Music 的选歌界面暂未响应，请稍后重试。";}
        catch(Exception ex) when(ex is InvalidOperationException or COMException or FlaUI.Core.Exceptions.FlaUIException){return "Apple Music 的音乐库界面暂不可用，请打开客户端后重试。";}
        finally
        {
            bounded.Cancel();
            // A timed-out COM call can finish after the caller returns. Keep its token alive
            // until cleanup completes; the gate prevents a later request from interleaving.
            if(worker.IsCompleted)bounded.Dispose();
            else _=worker.ContinueWith(t=>{_=t.Exception;bounded.Dispose();},TaskScheduler.Default);
        }
    }
    static string Name(AutomationElement element)=>element.Properties.Name.ValueOrDefault??"";
    static string? Select(MusicLibrarySong song,AlbumRequest? album,CancellationToken token,Action<string,object?>? trace,bool insertNext)
    {
        gate.Wait(token);
        try
        {
            using var process=Process.GetProcessesByName("AppleMusic").FirstOrDefault(p=>p.MainWindowHandle!=0);
            if(process is null)return "请先打开并登录 Apple Music，浮光即可从音乐库选歌。";
            nint handle=process.MainWindowHandle;bool minimized=IsIconic(handle);if(minimized)ShowWindow(handle,4);
            trace?.Invoke("window",new{process.Id,minimized});
            try
            {
                using var automation=new UIA3Automation{ConnectionTimeout=TimeSpan.FromSeconds(1),TransactionTimeout=TimeSpan.FromSeconds(3)};var root=automation.FromHandle(handle);
                token.ThrowIfCancellationRequested();var navigation=FindSongsNavigation(root);
                if(navigation is null)
                {
                    var hamburger=root.FindFirstDescendant(cf=>cf.ByAutomationId("Hamburger_Button"));
                    if(hamburger?.Patterns.Invoke.IsSupported==true)hamburger.Patterns.Invoke.Pattern.Invoke();
                    token.WaitHandle.WaitOne(150);token.ThrowIfCancellationRequested();navigation=FindSongsNavigation(root);
                }
                if(navigation is null)return "当前 Apple Music 版本没有提供可用的歌曲列表，请打开资料库后重试。";
                trace?.Invoke("navigation",new{name=Name(navigation),type=navigation.ControlType.ToString()});
                if(navigation.Patterns.SelectionItem.IsSupported)navigation.Patterns.SelectionItem.Pattern.Select();
                else if(navigation.Patterns.Invoke.IsSupported)navigation.Patterns.Invoke.Pattern.Invoke();
                else return "Apple Music 暂不接受音乐库选歌。";
                AutomationElement? filter=null;
                for(int i=0;i<8 && filter is null;i++){filter=root.FindFirstDescendant(cf=>cf.ByAutomationId("FilterButton"));if(filter is null && token.WaitHandle.WaitOne(100))token.ThrowIfCancellationRequested();}
                if(filter is null)return "Apple Music 没有提供本地歌曲筛选，请打开歌曲列表后重试。";
                trace?.Invoke("filterButton",new{name=Name(filter),toggle=filter.Patterns.Toggle.IsSupported,invoke=filter.Patterns.Invoke.IsSupported});
                var filterText=FindFilterText(automation,filter);
                if(filterText is null)
                {
                    token.ThrowIfCancellationRequested();
                    if(filter.Patterns.Toggle.IsSupported)filter.Patterns.Toggle.Pattern.Toggle();
                    else if(filter.Patterns.Invoke.IsSupported)filter.Patterns.Invoke.Pattern.Invoke();
                    for(int i=0;i<8 && filterText is null;i++){token.WaitHandle.WaitOne(100);token.ThrowIfCancellationRequested();filterText=FindFilterText(automation,filter);}
                    if(trace is not null)trace("filterOpenedState",Inspect());
                }
                if(filterText?.Patterns.Value.IsSupported!=true)return "Apple Music 的本地歌曲筛选暂不可用。";
                trace?.Invoke("filterText",new{name=Name(filterText),id=filterText.Properties.AutomationId.ValueOrDefault});
                string previousFilter=filterText.Patterns.Value.Pattern.Value.Value;
                token.ThrowIfCancellationRequested();filterText.Patterns.Value.Pattern.SetValue(song.Title);
                try
                {
                token.WaitHandle.WaitOne(300);token.ThrowIfCancellationRequested();
                for(int attempt=0;attempt<12;attempt++)
                {
                    token.ThrowIfCancellationRequested();var grid=root.FindFirstDescendant(cf=>cf.ByAutomationId("DataGridListView"));
                    if(grid is not null)
                    {
                        var rows=grid.FindAllDescendants(cf=>cf.ByControlType(ControlType.ListItem));
                        var candidates=new List<AutomationElement>();
                        var possible=rows.Where(r=>Name(r).StartsWith(song.Title+" ",StringComparison.OrdinalIgnoreCase) || Name(r).Equals(song.Title,StringComparison.OrdinalIgnoreCase)).ToArray();
                        trace?.Invoke("rows",new{attempt,count=rows.Length,possible=possible.Select(Name).ToArray()});
                        if(possible.Length>100)return "同名歌曲过多，未自动切换；请在 Apple Music 确认版本。";
                        foreach(var row in possible)
                        {
                            token.ThrowIfCancellationRequested();var names=RawNames(automation,row,token);
                            trace?.Invoke("rowMetadata",names);
                            if(MusicLibraryPlayback.NamesMatch(song.Track,names))candidates.Add(row);
                        }
                        if(candidates.Count==1)return InvokePlay(automation,root,candidates[0],song.Title,process.Id,token,trace,album,insertNext);
                        if(candidates.Count>1)return "找到多个相同录音信息的结果，未自动切换；请在 Apple Music 确认版本。";
                    }
                    if(token.WaitHandle.WaitOne(250))token.ThrowIfCancellationRequested();
                }
                return "Apple Music 未找到这首歌的对应录音，未切换到相似版本。";
                }
                finally
                {
                    RestoreFilter(automation,root,filterText,song.Title,previousFilter,album is not null,trace);
                }
            }
            finally{if(minimized)ShowWindow(handle,6);}
        }
        finally{gate.Release();}
    }
    static AutomationElement? FindSongsNavigation(AutomationElement root)=>root.FindFirstDescendant(cf=>cf.ByControlType(ControlType.ListItem).And(cf.ByName("歌曲").Or(cf.ByName("Songs")).Or(cf.ByName("曲"))));
    static string[] RawNames(UIA3Automation automation,AutomationElement row,CancellationToken token)
    {
        var names=new List<string>();var walker=automation.TreeWalkerFactory.GetRawViewWalker();int remaining=256;
        void Visit(AutomationElement element,int depth)
        {
            token.ThrowIfCancellationRequested();if(depth>8 || --remaining<0)return;
            if(element.Properties.ControlType.ValueOrDefault==ControlType.Text && Name(element) is {Length:>0} value)names.Add(value);
            for(var child=walker.GetFirstChild(element);child is not null && remaining>0;child=walker.GetNextSibling(child))Visit(child,depth+1);
        }
        Visit(row,0);return names.ToArray();
    }
    internal static AutomationElement? FindFilterText(UIA3Automation automation,AutomationElement button)
    {
        var walker=automation.TreeWalkerFactory.GetControlViewWalker();var parent=walker.GetParent(button);
        for(int depth=0;parent is not null && depth<3 && parent.ControlType!=ControlType.Window;depth++,parent=walker.GetParent(parent))
        {
            // The sidebar's global TextBox can share a high-level ancestor with this
            // toolbar. Never fall back to it: setting it replaces the native library page.
            var edits=parent.FindAllDescendants(cf=>cf.ByAutomationId("FilterTextBox").And(cf.ByControlType(ControlType.Edit)));
            if(edits.Length==1)return edits[0];
        }
        return null;
    }
    static string? InvokePlay(UIA3Automation automation,AutomationElement root,AutomationElement row,string title,int processId,CancellationToken token,Action<string,object?>? trace,AlbumRequest? album,bool insertNext)
    {
        token.ThrowIfCancellationRequested();if(row.Patterns.ScrollItem.IsSupported)row.Patterns.ScrollItem.Pattern.ScrollIntoView();
        var more=row.FindFirstDescendant(cf=>cf.ByAutomationId("MoreButton"));
        if(more?.Patterns.Invoke.IsSupported!=true)return "Apple Music 没有提供这首歌的播放操作。";
        token.ThrowIfCancellationRequested();more.Patterns.Invoke.Pattern.Invoke();
        bool invoked=false;
        try
        {
        for(int i=0;i<12;i++)
        {
            token.ThrowIfCancellationRequested();
            var windows=automation.GetDesktop().FindAllChildren(cf=>cf.ByProcessId(processId));
            if(trace is not null)trace("menus",windows.Prepend(root).SelectMany(window=>window.FindAllDescendants(cf=>cf.ByControlType(ControlType.MenuItem))).Select(Name).Distinct().ToArray());
            foreach(var window in windows.Prepend(root))
            foreach(var command in window.FindAllDescendants(cf=>cf.ByControlType(ControlType.MenuItem)))
            {
                bool requested=insertNext?MusicPlaybackQueue.IsPlayNextCommand(Name(command)):album is null?MusicLibraryPlayback.IsPlayCommand(Name(command),title):Name(command) is "在资料库中显示专辑" or "Show Album in Library";
                if(requested && command.Patterns.Invoke.IsSupported)
                {token.ThrowIfCancellationRequested();command.Patterns.Invoke.Pattern.Invoke();invoked=true;return album is null?null:InvokeAlbumPlay(automation,root,album,token,trace);}
            }
            if(token.WaitHandle.WaitOne(120))token.ThrowIfCancellationRequested();
        }
        return insertNext?"Apple Music 的歌曲菜单没有可用的插播命令。":"Apple Music 的歌曲菜单没有可用的播放命令。";
        }
        finally
        {
            if(!invoked)try
            {
                if(root.FindAllDescendants(cf=>cf.ByControlType(ControlType.MenuItem)).Any(c=>MusicLibraryPlayback.IsPlayCommand(Name(c),title)))more.Patterns.Invoke.Pattern.Invoke();
            }
            catch(Exception ex) when(ex is InvalidOperationException or COMException or FlaUI.Core.Exceptions.FlaUIException){}
        }
    }
    internal static object Inspect()
    {
        using var process=Process.GetProcessesByName("AppleMusic").FirstOrDefault(p=>p.MainWindowHandle!=0);
        if(process is null)return new{available=false};
        using var automation=new UIA3Automation{ConnectionTimeout=TimeSpan.FromSeconds(1),TransactionTimeout=TimeSpan.FromSeconds(3)};
        var root=automation.FromHandle(process.MainWindowHandle);
        var elements=root.FindAllDescendants(cf=>cf.ByControlType(ControlType.Button).Or(cf.ByControlType(ControlType.ListItem)).Or(cf.ByControlType(ControlType.Edit)).Or(cf.ByControlType(ControlType.MenuItem)).Or(cf.ByControlType(ControlType.Text))).Take(500);
        return new{available=true,process.Id,minimized=IsIconic(process.MainWindowHandle),elements=elements.Select(e=>{string name=Name(e);return new{name=name[..Math.Min(240,name.Length)],id=e.Properties.AutomationId.ValueOrDefault,type=e.Properties.ControlType.ValueOrDefault.ToString(),invoke=e.Patterns.Invoke.IsSupported,select=e.Patterns.SelectionItem.IsSupported,value=e.Patterns.Value.IsSupported};}).ToArray()};
    }
}
