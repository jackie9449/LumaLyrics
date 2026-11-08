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
        string? oldSource=settings.LibraryPath;settings.LibraryPath="own-ui";string theme=settings.UiTheme;bool main=settings.ShowChineseTranslation,floating=settings.OverlayShowChineseTranslation;SettingsWindow? preferences=null;
        const string original="[00:01]We sail home\n[00:03]\n[00:04]Across the quiet bay\n[00:08]The stars will guide us";
        var tracks=Enumerable.Range(0,6).Select(i=>new Track($"Own batch song {i}","Own artist","Own album",10000)).ToArray();
        using(var lyrics=new LyricsRepository(Path.Combine(coordinator.DataRoot,"lyrics.db"))){lyrics.SyncLibrary(new("own-ui",6,tracks.Select((t,i)=>new LibraryTrack(i.ToString(),t)).ToArray(),"own"));foreach(var track in tracks)lyrics.Save(track,original,"Own UI",true);}
        coordinator.Import(tracks[0],original,"Own UI",false);coordinator.SetTranslationVerificationSnapshot(tracks[0],1000);Tick();
        try
        {
            preferences=new(coordinator,id=>SelectUiTheme(id,false)){Owner=this,Width=1100,Height=920};settingsWindow=preferences;preferences.Show();preferences.SelectLibrarySongs();await preferences.Library.Songs.Reload();await Task.Delay(120);
            var panel=Descendants<TranslationJobListPanel>(preferences).Single();
            var toggle=panel.PreferExisting;toggle.IsChecked=false;
            var button=Descendants<Button>(panel).Single(b=>b.Content?.ToString()=="直接生成本地任务");
            foreach(var item in panel.SongList.Items)panel.SongList.SelectedItems.Add(item);
            var peer=new ButtonAutomationPeer(button);((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
            var until=DateTime.UtcNow.AddSeconds(15);while(DateTime.UtcNow<until&&(coordinator.WorkspaceBusy||TranslationJobExchange.ReadJobs(coordinator.DataRoot).Count<6))await Task.Delay(50);
            var jobs=TranslationJobExchange.ReadJobs(coordinator.DataRoot);checks["jobs.actualButtonGeneratedSix"]=jobs.Count==6;
            var entries=Directory.GetFiles(Path.Combine(coordinator.DataRoot,"translation-batches"),"batch-task.md",SearchOption.AllDirectories);checks["jobs.oneBatchEntry"]=entries.Length==1;
            checks["jobs.twoChunks"]=Directory.GetFiles(Path.Combine(coordinator.DataRoot,"translation-batches"),"*.json",SearchOption.AllDirectories).Length==3;
            preferences.SelectLibraryTasks();await preferences.Library.Tasks.Reload();var taskPanel=preferences.Library.Tasks;
            // Loaded may already own the coalesced read; Reload then requests another pass without awaiting that owner.
            until=DateTime.UtcNow.AddSeconds(5);while(DateTime.UtcNow<until&&!taskPanel.BatchList.Items.Cast<LibraryTaskBatch>().Any(t=>t.EntryPath is not null))await Task.Delay(30);
            var selectedBatch=taskPanel.BatchList.Items.Cast<LibraryTaskBatch>().First(t=>t.EntryPath is not null);taskPanel.BatchList.SelectedItem=selectedBatch;
            string? copied=null;var realCopy=taskPanel.CopyText;taskPanel.CopyText=value=>copied=value;
            try{((IInvokeProvider)new ButtonAutomationPeer(taskPanel.CopyPromptButton).GetPattern(PatternInterface.Invoke)).Invoke();until=DateTime.UtcNow.AddSeconds(5);while(copied is null&&DateTime.UtcNow<until)await Task.Delay(30);checks["workspace.prompt.exactSelectedEntry"]=copied?.Contains(selectedBatch.EntryPath!)==true&&copied.Contains("translation-result.json");taskPanel.BatchList.SelectedItem=null;checks["workspace.prompt.requiresSelection"]=!taskPanel.CopyPromptButton.IsEnabled;taskPanel.BatchList.SelectedItem=selectedBatch;}
            finally{taskPanel.CopyText=realCopy;}
            preferences.SelectLibrarySongs();

            foreach(var appTheme in AppThemes.All)
            {
                SelectUiTheme(appTheme.Id,false);await Task.Delay(80);
                checks["jobs."+appTheme.Id+".controls"]=panel.IsVisible&&button.IsEnabled;
                checks["jobs."+appTheme.Id+".noBadge"]=!Descendants<TextBlock>(panel).Any(t=>t.Text.Contains("待校对"));
                panel.BringIntoView(new Rect(0,0,panel.ActualWidth,Math.Min(500,panel.ActualHeight)));await Task.Delay(60);
                VisualVerification.Save(preferences,Path.Combine(directory,"jobs-"+appTheme.Id+".png"));
                foreach(double width in new[]{1220d,980d,820d,640d})
                {
                    preferences.Width=width;preferences.Height=width==640?520:920;await Task.Delay(80);
                    var viewport=panel.SongList.TransformToAncestor(panel).TransformBounds(new Rect(0,0,panel.SongList.ActualWidth,panel.SongList.ActualHeight));
                    checks["workspace."+appTheme.Id+".width"+width+".viewport"]=viewport.Right<=panel.ActualWidth+1&&viewport.Bottom<=panel.ActualHeight+1&&viewport.Top>=0&&viewport.Height>=48;
                    if(width==640)VisualVerification.Save(preferences,Path.Combine(directory,"workspace-640-"+appTheme.Id+".png"));
                }
                preferences.Width=1100;preferences.Height=920;await Task.Delay(120);preferences.Activate();
                var more=Descendants<Button>(panel).Single(b=>b.Content?.ToString()=="更多操作…");
                more.Focus();((IInvokeProvider)new ButtonAutomationPeer(more).GetPattern(PatternInterface.Invoke)).Invoke();await Task.Delay(80);
                var commands=more.ContextMenu!;checks["workspace."+appTheme.Id+".commandMenu"]=commands.IsOpen&&commands.Style==FindResource("LibraryActionMenu")&&commands.ActualHeight<=SystemParameters.WorkArea.Height;
                var first=commands.Items.OfType<MenuItem>().Skip(1).First();first.Focus();checks["workspace."+appTheme.Id+".commandKeyboardFocus"]=first.IsKeyboardFocused;
                VisualVerification.Save(commands,Path.Combine(directory,"workspace-menu-"+appTheme.Id+".png"));commands.IsOpen=false;
                foreach(int page in new[]{0,2,3})
                {
                    preferences.Library.Select(page);preferences.Width=1100;preferences.Height=900;await Task.Delay(220);if(page==2)await preferences.Library.Tasks.Reload();
                    string view=page==0?"overview":page==2?"tasks":"display";VisualVerification.Save(preferences,Path.Combine(directory,"workspace-"+view+"-"+appTheme.Id+".png"));
                    checks["workspace."+appTheme.Id+"."+view+".visible"]=preferences.Library.IsVisible;
                    preferences.Width=640;preferences.Height=520;await Task.Delay(120);VisualVerification.Save(preferences,Path.Combine(directory,"workspace-"+view+"-640-"+appTheme.Id+".png"));
                    if(page==2){var batchBounds=taskPanel.BatchList.TransformToAncestor(taskPanel).TransformBounds(new Rect(0,0,taskPanel.BatchList.ActualWidth,taskPanel.BatchList.ActualHeight));var songBounds=taskPanel.TaskSongs.TransformToAncestor(taskPanel).TransformBounds(new Rect(0,0,taskPanel.TaskSongs.ActualWidth,taskPanel.TaskSongs.ActualHeight));checks["workspace."+appTheme.Id+".tasks.narrowBounded"]=batchBounds.Bottom<=taskPanel.ActualHeight+1&&songBounds.Bottom<=taskPanel.ActualHeight+1&&batchBounds.Height>=32&&songBounds.Height>=32;}
                }
                preferences.Width=1100;preferences.Height=920;preferences.SelectLibrarySongs();await Task.Delay(80);

            }
            foreach(double width in new[]{1220d,980d,820d,640d}){preferences.Width=width;preferences.Height=width==640?600:920;await Task.Delay(100);var bounds=panel.SongList.TransformToAncestor(panel).TransformBounds(new Rect(0,0,panel.SongList.ActualWidth,panel.SongList.ActualHeight));checks["jobs.width"+width+".bounded"]=panel.ActualWidth<=preferences.ActualWidth&&panel.ActualWidth>0&&bounds.Bottom<=panel.ActualHeight+1&&bounds.Height>=48;VisualVerification.Save(preferences,Path.Combine(directory,"workspace-width-"+width+".png"));}preferences.Width=1100;preferences.Height=920;
            checks["workspace.unifiedNavigation"]=Descendants<TabItem>(preferences).Count(t=>t.Header?.ToString()=="资料库与歌词")==1&&!Descendants<TabItem>(preferences).Any(t=>t.Header?.ToString()=="中文译文");
            await OpenSongManagementAsync([new MusicLibrarySong("own-ui","1",tracks[1])]);
            checks["workspace.managementTargetsB"]=panel.SelectedSongs.Count==1&&panel.SelectedSongs[0].Key.PersistentId=="1";
            coordinator.SetTranslationVerificationSnapshot(tracks[2],1000);await panel.Reload();
            checks["workspace.switchingPlaybackKeepsSelection"]=panel.SelectedSongs.Count==1&&panel.SelectedSongs[0].Key.PersistentId=="1";
            checks["workspace.settingsInstanceReused"]=ReferenceEquals(settingsWindow,preferences)&&Application.Current.Windows.OfType<SettingsWindow>().Count()==1;
            preferences.SelectGeneralTab();await Task.Delay(50);checks["workspace.hiddenDetaches"]=!panel.IsLoaded;
            preferences.SelectLibraryTasks();await Task.Delay(80);checks["workspace.tasksViewAvailable"]=Descendants<LibraryTasksPanel>(preferences).Any(p=>p.IsVisible);
            preferences.SelectLibrarySongs();
            var ticket=jobs[0].Ticket;var result=new TranslationJobResult("LumaLyrics.TranslationJobResult",1,ticket.Request.JobId,ticket.RequestSha256,ticket.Request.Target.SourceHash,"Own UI fixture",ticket.Request.Target.Groups.Select(g=>new TranslationJobResultLine(g.GroupIndex,g.Original,g.NeedsTranslation?"我们循着星光归家":"",null)).ToArray());
            File.WriteAllText(ticket.ResultPath,JsonSerializer.Serialize(result));coordinator.RefreshTranslationJobs();
            until=DateTime.UtcNow.AddSeconds(10);while(DateTime.UtcNow<until&&TranslationJobExchange.ReadJobs(coordinator.DataRoot).First(j=>j.Ticket.Request.JobId==ticket.Request.JobId).Receipt.State==TranslationJobState.AwaitingResult)await Task.Delay(50);
            checks["jobs.autoReceived"]=TranslationJobExchange.ReadJobs(coordinator.DataRoot).First(j=>j.Ticket.Request.JobId==ticket.Request.JobId).Receipt.State==TranslationJobState.Applied;
            checks["jobs.originalPreserved"]=coordinator.Document?.Raw==original;
            checks["jobs.visibilityPreserved"]=settings.ShowChineseTranslation==main&&settings.OverlayShowChineseTranslation==floating;
            await VerifyWorkspacePreparationAsync(checks,tracks,original);
            await VerifyLargeWorkspaceAsync(preferences,checks);

        }
        finally{preferences?.Close();settingsWindow=null;settings.LibraryPath=oldSource;SelectUiTheme(theme,false);}
    }
    async Task VerifyLargeWorkspaceAsync(SettingsWindow preferences,Dictionary<string,object?> checks)
    {
        var tracks=Enumerable.Range(0,3003).Select(i=>new Track("Own large "+i.ToString("D4"),"Own artist","Own album "+(i%30),10000)).ToArray();
        await Task.Run(()=>{using var lyrics=new LyricsRepository(Path.Combine(coordinator.DataRoot,"lyrics.db"));lyrics.SyncLibrary(new("own-ui",tracks.Length,tracks.Select((t,i)=>new LibraryTrack(i.ToString(),t)).ToArray(),"own"));});
        coordinator.RefreshTranslationJobs();preferences.SelectLibrarySongs();var panel=preferences.Library.Songs;
        var read=coordinator.ReadWorkspaceAsync(default,true);var same=coordinator.ReadWorkspaceAsync(default,true);checks["workspace.large.readsCoalesced"]=ReferenceEquals(read,same);await read;await panel.Reload();await Task.Delay(80);
        checks["workspace.large.virtualized"]=panel.SongList.Items.Count==3003&&Descendants<ListBoxItem>(panel.SongList).Count()<100;
        var clock=System.Diagnostics.Stopwatch.StartNew();Descendants<Button>(panel).Single(b=>b.Content?.ToString()=="全选当前筛选").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));clock.Stop();
        checks["workspace.large.selectAllMs"]=clock.ElapsedMilliseconds;checks["workspace.large.selectAllResponsive"]=panel.SelectedSongs.Count==3003&&clock.ElapsedMilliseconds<1500;
        var search=Descendants<TextBox>(panel).Single();search.Text="Own large 0001";await Task.Delay(300);
        checks["workspace.large.hiddenSelectionRetained"]=panel.SongList.Items.Count==1&&panel.SelectedSongs.Count==3003;
        search.Text="";await Task.Delay(300);clock.Restart();Descendants<Button>(panel).Single(b=>b.Content?.ToString()=="清空选择").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));clock.Stop();
        checks["workspace.large.clearMs"]=clock.ElapsedMilliseconds;checks["workspace.large.clearResponsive"]=panel.SelectedSongs.Count==0&&clock.ElapsedMilliseconds<1500;
        panel.SelectKeys([new("own-ui","100")]);var source=panel.SongList.ItemsSource;await panel.Reload();checks["workspace.large.unchangedRowsRetained"]=ReferenceEquals(source,panel.SongList.ItemsSource)&&panel.SelectedSongs.Single().Key.PersistentId=="100";
    }
    sealed class WorkspaceOriginalFixture(Func<Track,CancellationToken,Task<IReadOnlyList<LyricsCandidate>>> lookup):ILyricsProvider
    {public Task<IReadOnlyList<LyricsCandidate>> LookupAsync(Track track,CancellationToken token)=>lookup(track,token);}
    sealed class WorkspaceChineseFixture(Func<Track,CancellationToken,Task<IReadOnlyList<TranslationCandidate>>> lookup):ITranslationProvider
    {public Task<IReadOnlyList<TranslationCandidate>> LookupAsync(Track track,CancellationToken token)=>lookup(track,token);}
    async Task VerifyWorkspacePreparationAsync(Dictionary<string,object?> checks,Track[] previous,string raw)
    {
        bool online=settings.OnlineLookup;
        var mixed=Enumerable.Range(0,4).Select(i=>new Track("Own preparation "+i,"Own artist","Own album",10000)).ToArray();
        const string chinese="[00:01]我们驶向家乡\n[00:03]\n[00:04]穿过宁静海湾\n[00:08]星光为我们引路";
        using(var lyrics=new LyricsRepository(Path.Combine(coordinator.DataRoot,"lyrics.db")))
        {
            lyrics.SyncLibrary(new("own-ui",previous.Length+mixed.Length,previous.Concat(mixed).Select((t,i)=>new LibraryTrack(i.ToString(),t)).ToArray(),"own"));
            lyrics.Save(mixed[1],raw,"Own manual",true);lyrics.Save(mixed[2],raw,"Own manual",true);
        }
        var originalCalls=new List<string>();var chineseCalls=new List<string>();
        coordinator.WorkspaceOriginalProvider=new WorkspaceOriginalFixture((track,token)=>{originalCalls.Add(track.Title);return Task.FromResult<IReadOnlyList<LyricsCandidate>>([new TranslationCandidate(99,track,raw,chinese,"https://example.org/own").OriginalCandidate]);});
        coordinator.WorkspaceChineseProvider=new WorkspaceChineseFixture((track,token)=>{chineseCalls.Add(track.Title);return Task.FromResult<IReadOnlyList<TranslationCandidate>>(track==mixed[0]?[new(99,track,raw,chinese,"https://example.org/own")]:[]);});
        try
        {
            settings.OnlineLookup=true;
            using(var tr=new TranslationRepository(coordinator.DataRoot))tr.Save(mixed[2],new(TranslationAlignment.Hash(raw),[new(0,"保留个人版本")],"Own manual","manual","Own",DateTimeOffset.UtcNow),true);
            coordinator.RefreshTranslationJobs();var rows=(await coordinator.ReadWorkspaceAsync(default)).Songs.Where(s=>mixed.Take(3).Contains(s.Track)).ToArray();
            int before=TranslationJobExchange.ReadJobs(coordinator.DataRoot).Count;
            await coordinator.PrepareWorkspaceAsync(rows,false,true);
            using var lyrics=new LyricsRepository(Path.Combine(coordinator.DataRoot,"lyrics.db"),true);using var translations=new TranslationRepository(coordinator.DataRoot,true);
            checks["workspace.preparation.originalThenChinese"]=lyrics.FindReadOnly(mixed[0]) is {} binding&&LibraryPrefetcher.QualityOf(binding,mixed[0])&&translations.Find(mixed[0],new LrcParser().Parse(raw)) is not null;
            checks["workspace.preparation.onlyRemainingGetsTask"]=TranslationJobExchange.ReadJobs(coordinator.DataRoot).Count==before+1&&TranslationJobExchange.ReadJobs(coordinator.DataRoot).Any(j=>j.Ticket.Request.Target.Track==mixed[1]);
            checks["workspace.preparation.existingPreserved"]=translations.Find(mixed[2],new LrcParser().Parse(raw))?.TextAt(0)=="保留个人版本"&&!chineseCalls.Contains(mixed[2].Title)&&originalCalls.Count==1;
            var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource<IReadOnlyList<LyricsCandidate>>(TaskCreationOptions.RunContinuationsAsynchronously);
            coordinator.WorkspaceOriginalProvider=new WorkspaceOriginalFixture((track,token)=>{started.TrySetResult();return release.Task;});
            coordinator.RefreshTranslationJobs();var cancelled=(await coordinator.ReadWorkspaceAsync(default)).Songs.Single(s=>s.Track==mixed[3]);
            var pending=coordinator.PrepareWorkspaceAsync([cancelled],false,true);await started.Task.WaitAsync(TimeSpan.FromSeconds(5));coordinator.PauseWorkspace();release.TrySetResult([new TranslationCandidate(99,mixed[3],raw,chinese,"https://example.org/own").OriginalCandidate]);await pending;
            checks["workspace.preparation.cancelRejectsLateOriginal"]=lyrics.FindReadOnly(mixed[3]) is null&&!TranslationJobExchange.ReadJobs(coordinator.DataRoot).Any(j=>j.Ticket.Request.Target.Track==mixed[3]);
        }
        finally{settings.OnlineLookup=online;coordinator.WorkspaceOriginalProvider=null;coordinator.WorkspaceChineseProvider=null;}
    }

}
