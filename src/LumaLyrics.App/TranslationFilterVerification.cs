using System.IO;
using System.Windows;
using System.Windows.Controls;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;
public partial class MainWindow
{
    async Task VerifyExistingChineseFilterAsync(SettingsWindow window,Track[] tracks,string raw,string directory,Dictionary<string,object?> checks)
    {
        window.SelectLibrarySongs();var panel=window.Library.Songs;var filter=Descendants<ComboBox>(panel).Single();
        checks["workspace.existingChinese.optionExists"]=filter.Items.Cast<string>().Contains("有中文译文");
        var more=Descendants<Button>(panel).Single(b=>b.Content?.ToString()=="更多操作…");
        var regenerate=more.ContextMenu!.Items.OfType<MenuItem>().FirstOrDefault(i=>i.Header?.ToString()=="重新生成翻译任务…");
        checks["workspace.existingChinese.regenerationEntry"]=regenerate is not null;
        if(!filter.Items.Cast<string>().Contains("有中文译文")||regenerate is null)return;
        var instrumental=new Track("Own instrumental filter fixture","Own artist","Own album",10000);
        using(var lyrics=new LyricsRepository(Path.Combine(coordinator.DataRoot,"lyrics.db")))
        {
            lyrics.SyncLibrary(new("own-ui",7,tracks.Append(instrumental).Select((t,i)=>new LibraryTrack(i.ToString(),t)).ToArray(),"own"));
            lyrics.Save(tracks[5],"Own static fixture","Own",true,plain:true);lyrics.Save(instrumental,"","Own",true,instrumental:true);
        }
        using(var translations=new TranslationRepository(coordinator.DataRoot))
        {
            ChineseTranslation Translation(string source,IReadOnlyList<TranslationLine> lines,string? hash=null)=>new(hash??TranslationAlignment.Hash(raw),lines,source,"manual","Own filter fixture",DateTimeOffset.UtcNow);
            translations.Save(tracks[0],Translation("Own AI",[new(0,"我们驶向家乡"),new(2,"越过安静的海湾"),new(3,"星光将指引我们")]) with{Draft=true});
            translations.Save(tracks[1],Translation("Own manual",[new(0,"我们驶向家乡"),new(2,"越过安静的海湾"),new(3,"星光将指引我们")]),true);
            translations.Save(tracks[2],Translation("Own community",[new(0,"我们驶向家乡")]));
            translations.Save(tracks[3],Translation("Own previous original",[new(0,"原有译文")],TranslationAlignment.Hash(raw+"\n")));
        }
        coordinator.RefreshTranslationJobs();await panel.Reload(true);await Task.Delay(80);
        void Click(string label)=>Descendants<Button>(panel).Single(b=>b.Content?.ToString()==label).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        void Choose(string label){filter.SelectedItem=label;window.UpdateLayout();}
        string[] VisibleIds()=>panel.SongList.Items.Cast<LibraryManagedSong>().Select(r=>r.Key.PersistentId).Order().ToArray();
        Click("清空选择");Choose("有中文译文");
        checks["workspace.existingChinese.completeManualAndPartial"]=VisibleIds().SequenceEqual(new[]{"0","1","2"})&&panel.SongList.Items.Cast<LibraryManagedSong>().Any(r=>r.Status.AiDraft)&&panel.SongList.Items.Cast<LibraryManagedSong>().Any(r=>r.Status.ManualChinese)&&panel.SongList.Items.Cast<LibraryManagedSong>().Any(r=>r.Status.Chinese==LibraryChineseState.Partial);
        checks["workspace.existingChinese.onlyCurrentTranslations"]=panel.SongList.Items.Cast<LibraryManagedSong>().All(r=>r.Status.Chinese is LibraryChineseState.Complete or LibraryChineseState.Partial);
        Click("全选当前筛选");checks["workspace.existingChinese.selectAll"]=panel.SelectedSongs.Count==3;
        var search=Descendants<TextBox>(panel).Single();search.Text="song 1";await Task.Delay(250);
        checks["workspace.existingChinese.searchAndRetainedSelection"]=VisibleIds().SequenceEqual(new[]{"1"})&&panel.SelectedSongs.Count==3;
        search.Text="";await Task.Delay(250);
        foreach(var (label,id) in new[]{("缺中文译文","4"),("部分译文","2"),("原词已更新","3")}){Choose(label);checks["workspace.existingChinese.legacyFilter."+label]=VisibleIds().SequenceEqual(new[]{id});}
        Choose("有中文译文");
        var existing=panel.SelectedSongs.ToDictionary(r=>r.Key.PersistentId,r=>r.Status.Revision);
        var before=TranslationJobExchange.ReadJobs(coordinator.DataRoot).Select(j=>j.Ticket.Request.JobId).ToHashSet();
        var confirm=panel.ConfirmRegeneration;int confirmedCount=0;
        try
        {
            panel.ConfirmRegeneration=count=>{confirmedCount=count;return false;};regenerate.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));await Task.Delay(40);
            checks["workspace.existingChinese.cancelCreatesNoTasks"]=confirmedCount==3&&TranslationJobExchange.ReadJobs(coordinator.DataRoot).Count==before.Count;
            panel.ConfirmRegeneration=count=>{confirmedCount=count;return true;};regenerate.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var deadline=DateTime.UtcNow.AddSeconds(15);while(DateTime.UtcNow<deadline&&(coordinator.WorkspaceBusy||TranslationJobExchange.ReadJobs(coordinator.DataRoot).Count<before.Count+3))await Task.Delay(40);
            var generated=TranslationJobExchange.ReadJobs(coordinator.DataRoot).Where(j=>!before.Contains(j.Ticket.Request.JobId)).ToArray();
            checks["workspace.existingChinese.exactRegenerationTargets"]=confirmedCount==3&&generated.Select(j=>j.Ticket.Request.Target.PersistentId).Order().SequenceEqual(new[]{"0","1","2"});
            checks["workspace.existingChinese.expectedRevisionAndOriginal"]=generated.Length==3&&generated.All(j=>j.Ticket.Request.Mode==TranslationJobMode.ReplaceExpected&&j.Ticket.Request.ExpectedRevision==existing[j.Ticket.Request.Target.PersistentId!]&&j.Ticket.Request.Target.SourceHash==TranslationAlignment.Hash(raw));
            using var tr=new TranslationRepository(coordinator.DataRoot,true);var doc=new LrcParser().Parse(raw);
            checks["workspace.existingChinese.currentTranslationsRetained"]=Enumerable.Range(0,3).All(i=>existing.TryGetValue(i.ToString(),out var revision)&&tr.FindActive(tracks[i],doc)?.Revision==revision);
            checks["workspace.existingChinese.manualTranslationRetained"]=tr.FindActive(tracks[1],doc) is {Record.Manual:true};
            window.Width=1100;window.Height=900;await Task.Delay(100);window.UpdateLayout();VisualVerification.Save(window,Path.Combine(directory,"workspace-existing-chinese.png"));
            window.Width=640;window.Height=520;await Task.Delay(100);window.UpdateLayout();
            var bounds=filter.TransformToAncestor(panel).TransformBounds(new Rect(filter.RenderSize));checks["workspace.existingChinese.narrowFilterFits"]=bounds.Right<=panel.ActualWidth+1&&panel.SongList.ActualHeight>=48;
            VisualVerification.Save(window,Path.Combine(directory,"workspace-existing-chinese-640.png"));
        }
        finally{panel.ConfirmRegeneration=confirm;Choose("全部");Click("清空选择");window.Width=1100;window.Height=920;}
    }
}
