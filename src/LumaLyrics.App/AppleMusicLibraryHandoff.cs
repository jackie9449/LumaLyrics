using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

// Use the native local Songs view, avoiding catalog searches and synthetic input.
// A command is accepted only for a unique row with the requested recording metadata.
internal static class AppleMusicLibraryHandoff
{
    static readonly SemaphoreSlim gate=new(1,1);
    [DllImport("user32.dll")] static extern bool IsIconic(nint handle);
    [DllImport("user32.dll")] static extern bool ShowWindow(nint handle,int command);
    internal static async Task<string?> SelectAsync(MusicLibrarySong song,CancellationToken token)
    {
        var bounded=CancellationTokenSource.CreateLinkedTokenSource(token);bounded.CancelAfter(TimeSpan.FromSeconds(15));
        var worker=Task.Run(()=>Select(song,bounded.Token),bounded.Token);
        try{return await worker.WaitAsync(TimeSpan.FromSeconds(16),token);}
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
    static string? Select(MusicLibrarySong song,CancellationToken token)
    {
        gate.Wait(token);
        try
        {
            using var process=Process.GetProcessesByName("AppleMusic").FirstOrDefault(p=>p.MainWindowHandle!=0);
            if(process is null)return "请先打开并登录 Apple Music，浮光即可从音乐库选歌。";
            nint handle=process.MainWindowHandle;bool minimized=IsIconic(handle);if(minimized)ShowWindow(handle,4);
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
                if(navigation.Patterns.SelectionItem.IsSupported)navigation.Patterns.SelectionItem.Pattern.Select();
                else if(navigation.Patterns.Invoke.IsSupported)navigation.Patterns.Invoke.Pattern.Invoke();
                else return "Apple Music 暂不接受音乐库选歌。";
                AutomationElement? filter=null;
                for(int i=0;i<8 && filter is null;i++){filter=root.FindFirstDescendant(cf=>cf.ByAutomationId("FilterButton"));if(filter is null && token.WaitHandle.WaitOne(100))token.ThrowIfCancellationRequested();}
                if(filter is null)return "Apple Music 没有提供本地歌曲筛选，请打开歌曲列表后重试。";
                var filterText=FindFilterText(automation,filter);
                if(filterText is null)
                {
                    token.ThrowIfCancellationRequested();
                    if(filter.Patterns.Toggle.IsSupported)filter.Patterns.Toggle.Pattern.Toggle();
                    else if(filter.Patterns.Invoke.IsSupported)filter.Patterns.Invoke.Pattern.Invoke();
                    for(int i=0;i<8 && filterText is null;i++){token.WaitHandle.WaitOne(100);token.ThrowIfCancellationRequested();filterText=FindFilterText(automation,filter);}
                }
                if(filterText?.Patterns.Value.IsSupported!=true)return "Apple Music 的本地歌曲筛选暂不可用。";
                string previousFilter=filterText.Patterns.Value.Pattern.Value.Value;
                token.ThrowIfCancellationRequested();filterText.Patterns.Value.Pattern.SetValue(song.Title);
                try
                {
                for(int attempt=0;attempt<12;attempt++)
                {
                    token.ThrowIfCancellationRequested();var grid=root.FindFirstDescendant(cf=>cf.ByAutomationId("DataGridListView"));
                    if(grid is not null)
                    {
                        var rows=grid.FindAllDescendants(cf=>cf.ByControlType(ControlType.ListItem));
                        var candidates=new List<AutomationElement>();
                        var possible=rows.Where(r=>Name(r).StartsWith(song.Title+" ",StringComparison.OrdinalIgnoreCase) || Name(r).Equals(song.Title,StringComparison.OrdinalIgnoreCase)).ToArray();
                        if(possible.Length>100)return "同名歌曲过多，未自动切换；请在 Apple Music 确认版本。";
                        foreach(var row in possible)
                        {
                            token.ThrowIfCancellationRequested();var names=RawNames(automation,row,token);
                            if(MusicLibraryPlayback.NamesMatch(song.Track,names))candidates.Add(row);
                        }
                        if(candidates.Count==1)return InvokePlay(automation,root,candidates[0],song.Title,process.Id,token);
                        if(candidates.Count>1)return "找到多个相同录音信息的结果，未自动切换；请在 Apple Music 确认版本。";
                    }
                    if(token.WaitHandle.WaitOne(250))token.ThrowIfCancellationRequested();
                }
                return "Apple Music 未找到这首歌的对应录音，未切换到相似版本。";
                }
                finally
                {
                    try{if(filterText.Patterns.Value.Pattern.Value.Value==song.Title)filterText.Patterns.Value.Pattern.SetValue(previousFilter);}
                    catch(Exception ex) when(ex is InvalidOperationException or COMException or FlaUI.Core.Exceptions.FlaUIException){}
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
            if(element.ControlType==ControlType.Text && Name(element) is {Length:>0} value)names.Add(value);
            for(var child=walker.GetFirstChild(element);child is not null && remaining>0;child=walker.GetNextSibling(child))Visit(child,depth+1);
        }
        Visit(row,0);return names.ToArray();
    }
    static AutomationElement? FindFilterText(UIA3Automation automation,AutomationElement button)
    {
        var walker=automation.TreeWalkerFactory.GetControlViewWalker();var parent=walker.GetParent(button);
        for(int depth=0;parent is not null && depth<3 && parent.ControlType!=ControlType.Window;depth++,parent=walker.GetParent(parent))
        {
            var edits=parent.FindAllDescendants(cf=>cf.ByControlType(ControlType.Edit));
            if(edits.Length==1)return edits[0];
        }
        return null;
    }
    static string? InvokePlay(UIA3Automation automation,AutomationElement root,AutomationElement row,string title,int processId,CancellationToken token)
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
            foreach(var window in windows.Prepend(root))
            foreach(var command in window.FindAllDescendants(cf=>cf.ByControlType(ControlType.MenuItem)))
            {
                if(MusicLibraryPlayback.IsPlayCommand(Name(command),title) && command.Patterns.Invoke.IsSupported)
                {token.ThrowIfCancellationRequested();command.Patterns.Invoke.Pattern.Invoke();invoked=true;return null;}
            }
            if(token.WaitHandle.WaitOne(120))token.ThrowIfCancellationRequested();
        }
        return "Apple Music 的歌曲菜单没有可用的播放命令。";
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
}
