using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;
public partial class MainWindow
{
    async Task VerifyTranslationJobsAsync(string directory,Dictionary<string,object?> checks)
    {
        string theme=settings.UiTheme;bool main=settings.ShowChineseTranslation,floating=settings.OverlayShowChineseTranslation;SettingsWindow? preferences=null;
        const string original="[00:01]We sail home\n[00:03]\n[00:04]Across the quiet bay\n[00:08]The stars will guide us";
        var tracks=Enumerable.Range(0,6).Select(i=>new Track($"Own batch song {i}","Own artist","Own album",10000)).ToArray();
        using(var lyrics=new LyricsRepository(Path.Combine(coordinator.DataRoot,"lyrics.db"))){lyrics.SyncLibrary(new("own-ui",6,tracks.Select((t,i)=>new LibraryTrack(i.ToString(),t)).ToArray(),"own"));foreach(var track in tracks)lyrics.Save(track,original,"Own UI",true);}
        coordinator.Import(tracks[0],original,"Own UI",false);coordinator.SetTranslationVerificationSnapshot(tracks[0],1000);Tick();
        try
        {
            preferences=new(coordinator,id=>SelectUiTheme(id,false)){Owner=this,Width=1100,Height=920};preferences.Show();preferences.SelectTranslationTab();await Task.Delay(120);
            var panel=Descendants<TranslationJobListPanel>(preferences).Single();
            var toggle=Descendants<CheckBox>(panel).Single();toggle.IsChecked=false;
            var button=Descendants<Button>(panel).Single(b=>b.Content?.ToString()=="全部缺失：生成批量任务");
            var peer=new ButtonAutomationPeer(button);((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
            var until=DateTime.UtcNow.AddSeconds(15);while(DateTime.UtcNow<until&&TranslationJobExchange.ReadJobs(coordinator.DataRoot).Count<6)await Task.Delay(50);
            var jobs=TranslationJobExchange.ReadJobs(coordinator.DataRoot);checks["jobs.actualButtonGeneratedSix"]=jobs.Count==6;
            var entries=Directory.GetFiles(Path.Combine(coordinator.DataRoot,"translation-batches"),"batch-task.md",SearchOption.AllDirectories);checks["jobs.oneBatchEntry"]=entries.Length==1;
            checks["jobs.twoChunks"]=Directory.GetFiles(Path.Combine(coordinator.DataRoot,"translation-batches"),"*.json",SearchOption.AllDirectories).Length==3;
            foreach(var appTheme in AppThemes.All)
            {
                SelectUiTheme(appTheme.Id,false);await Task.Delay(80);
                checks["jobs."+appTheme.Id+".controls"]=panel.IsVisible&&button.IsEnabled;
                checks["jobs."+appTheme.Id+".noBadge"]=!Descendants<TextBlock>(panel).Any(t=>t.Text.Contains("待校对"));
                panel.BringIntoView(new Rect(0,0,panel.ActualWidth,Math.Min(500,panel.ActualHeight)));await Task.Delay(60);
                VisualVerification.Save(preferences,Path.Combine(directory,"jobs-"+appTheme.Id+".png"));
            }
            foreach(double width in new[]{1220d,980d,820d}){preferences.Width=width;await Task.Delay(80);checks["jobs.width"+width+".bounded"]=panel.ActualWidth<=preferences.ActualWidth&&panel.ActualWidth>0;}preferences.Width=1100;
            var ticket=jobs[0].Ticket;var result=new TranslationJobResult("LumaLyrics.TranslationJobResult",1,ticket.Request.JobId,ticket.RequestSha256,ticket.Request.Target.SourceHash,"Own UI fixture",ticket.Request.Target.Groups.Select(g=>new TranslationJobResultLine(g.GroupIndex,g.Original,g.NeedsTranslation?"我们循着星光归家":"",null)).ToArray());
            File.WriteAllText(ticket.ResultPath,JsonSerializer.Serialize(result));coordinator.RefreshTranslationJobs();
            until=DateTime.UtcNow.AddSeconds(10);while(DateTime.UtcNow<until&&TranslationJobExchange.ReadJobs(coordinator.DataRoot).First(j=>j.Ticket.Request.JobId==ticket.Request.JobId).Receipt.State==TranslationJobState.AwaitingResult)await Task.Delay(50);
            checks["jobs.autoReceived"]=TranslationJobExchange.ReadJobs(coordinator.DataRoot).First(j=>j.Ticket.Request.JobId==ticket.Request.JobId).Receipt.State==TranslationJobState.Applied;
            checks["jobs.originalPreserved"]=coordinator.Document?.Raw==original;
            checks["jobs.visibilityPreserved"]=settings.ShowChineseTranslation==main&&settings.OverlayShowChineseTranslation==floating;
        }
        finally{preferences?.Close();SelectUiTheme(theme,false);}
    }
}
