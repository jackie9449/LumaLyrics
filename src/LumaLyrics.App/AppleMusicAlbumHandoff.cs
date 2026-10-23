using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

internal static partial class AppleMusicLibraryHandoff
{
    static string? InvokeAlbumPlay(UIA3Automation automation,AutomationElement root,AlbumRequest request,CancellationToken token,Action<string,object?>? trace)
    {
        var album=request.Album;
        for(int attempt=0;attempt<15;attempt++)
        {
            token.WaitHandle.WaitOne(150);token.ThrowIfCancellationRequested();
            var play=root.FindFirstDescendant(cf=>cf.ByAutomationId("PlayButton").And(cf.ByControlType(ControlType.Button)));
            if(play?.Patterns.Invoke.IsSupported!=true || !play.Properties.IsEnabled.ValueOrDefault)continue;
            // Album pages leave PageHeaderTitle empty. The Play button's own header
            // group contains the real title and artist and excludes track/sidebar rows.
            var header=automation.TreeWalkerFactory.GetControlViewWalker().GetParent(play);
            if(header is null || header.Properties.ControlType.ValueOrDefault!=ControlType.Group || header.FindAllDescendants(cf=>cf.ByControlType(ControlType.ListItem)).Length!=0)continue;
            var names=RawNames(automation,header,token);string title=names.FirstOrDefault()??"";
            var trackRows=root.FindAllDescendants(cf=>cf.ByControlType(ControlType.ListItem)).Where(r=>string.IsNullOrEmpty(r.Properties.AutomationId.ValueOrDefault) && (Name(r).StartsWith("音轨 ",StringComparison.Ordinal) || Name(r).StartsWith("Track ",StringComparison.OrdinalIgnoreCase))).Select(r=>new{Name=Name(r),Names=(IReadOnlyList<string>)RawNames(automation,r,token)}).ToArray();
            var rows=trackRows.Select(r=>r.Names).ToArray();
            trace?.Invoke("albumContext",new{title,header=names.Take(3).ToArray(),rows});
            if(!MusicLibraryPlayback.AlbumContextMatches(album,title,names,rows))return "Apple Music 的专辑与资料库录音信息未完整对应，未开始播放。";
            var first=trackRows.FirstOrDefault(r=>r.Name.StartsWith("音轨 1 ",StringComparison.Ordinal) || r.Name.StartsWith("Track 1 ",StringComparison.OrdinalIgnoreCase));
            var firstMatches=first is null?[]:album.Songs.Where(s=>MusicLibraryPlayback.NamesMatch(s.Track,first.Names.Concat(new[]{s.Artist,s.Album}).ToArray())).ToArray();
            if(firstMatches.Length!=1)return "Apple Music 的专辑首曲尚无法明确对应，未开始播放。";
            request.FirstTrack=firstMatches[0].Track;
            token.ThrowIfCancellationRequested();play.Patterns.Invoke.Pattern.Invoke();trace?.Invoke("albumPlayInvoked",new{album.Title,album.Artist});return null;
        }
        return "Apple Music 暂未提供这张专辑的播放按钮，请稍后重试。";
    }
    static void RestoreFilter(UIA3Automation automation,AutomationElement root,AutomationElement previous,string temporary,string original,bool albumNavigated,Action<string,object?>? trace)
    {
        try
        {
            if(previous.Patterns.Value.Pattern.Value.Value==temporary)previous.Patterns.Value.Pattern.SetValue(original);
            trace?.Invoke("filterRestored",new{navigated=false});return;
        }
        catch(Exception ex) when(ex is InvalidOperationException or COMException or FlaUI.Core.Exceptions.FlaUIException){}
        if(!albumNavigated)return;
        try
        {
            // Album navigation can destroy the old filter element. Restore only our
            // temporary value on a fresh Songs page; the native album queue keeps playing.
            var navigation=FindSongsNavigation(root);if(navigation?.Patterns.SelectionItem.IsSupported!=true)return;
            navigation.Patterns.SelectionItem.Pattern.Select();
            for(int i=0;i<8;i++)
            {
                Thread.Sleep(100);var filter=root.FindFirstDescendant(cf=>cf.ByAutomationId("FilterButton"));if(filter is null)continue;
                var edit=FindFilterText(automation,filter);
                if(edit is null && filter.Patterns.Toggle.IsSupported){filter.Patterns.Toggle.Pattern.Toggle();continue;}
                if(edit?.Patterns.Value.IsSupported!=true)continue;
                if(edit.Patterns.Value.Pattern.Value.Value==temporary)edit.Patterns.Value.Pattern.SetValue(original);
                trace?.Invoke("filterRestored",new{navigated=true});return;
            }
        }
        catch(Exception ex) when(ex is InvalidOperationException or COMException or FlaUI.Core.Exceptions.FlaUIException){}
    }
}
