using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

public partial class MainWindow
{
    sealed class OfflineFixtureEngine(int delay):IOfflineTranslationEngine
    {
        public async Task<IReadOnlyList<string>> TranslateAsync(IReadOnlyList<string> texts,CancellationToken token){await Task.Delay(delay,token);return texts.Select(_=>"我们沿河走向清晨").ToArray();}
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
    async Task VerifyOfflineTranslationAsync(string directory,Dictionary<string,object?> checks)
    {
        string? source=settings.LibraryPath;string theme=settings.UiTheme;bool visible=settings.ShowChineseTranslation;SettingsWindow? preferences=null;
        const string raw="[00:01]We follow the river into morning\n[00:03]\n[00:04]The stars will guide us home";
        var tracks=Enumerable.Range(0,4).Select(i=>new Track("Own offline fixture "+i,"Own artist","Own album",10000)).ToArray();
        async Task Wait(Func<bool> condition){var until=DateTime.UtcNow.AddSeconds(15);while(!condition()&&DateTime.UtcNow<until)await Task.Delay(30);if(!condition())throw new TimeoutException("Offline fixture wait");}
        void Invoke(Button button)=>((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
        try
        {
            settings.LibraryPath="own-offline-ui";using(var lyrics=new LyricsRepository(Path.Combine(store.Root,"lyrics.db"))){lyrics.SyncLibrary(new(settings.LibraryPath,4,tracks.Select((t,i)=>new LibraryTrack(i.ToString(),t)).ToArray(),"own"));foreach(var track in tracks)lyrics.Save(track,raw,"Own UI fixture",true);}
            coordinator.SetOfflineVerificationEngine(()=>new OfflineFixtureEngine(80));
            coordinator.Import(tracks[0],raw,"Own UI fixture",false);coordinator.SetTranslationVerificationSnapshot(tracks[0],1000);
            preferences=new(coordinator,id=>SelectUiTheme(id,false)){Owner=this,Width=1100,Height=860};settingsWindow=preferences;preferences.Show();preferences.SelectLibraryTab();await Task.Delay(150);
            var panel=Descendants<OfflineTranslationPanel>(preferences).Single();
            Button Find(string text)=>Descendants<Button>(panel).Single(b=>b.Content?.ToString()==UiText.T(text));
            checks["offline.insideSettings"]=panel.IsVisible;
            checks["offline.readyButtons"]=Find("翻译当前歌曲").IsEnabled && !Find("暂停").IsEnabled;
            Invoke(Find("翻译当前歌曲"));await Wait(()=>coordinator.Translation is not null&&!coordinator.OfflineBusy);
            checks["offline.actualCurrentButtonSavesMachine"]=coordinator.Translation is {} tr && OfflineTranslationWorkflow.IsMachine(tr) && tr.Lines.Select(l=>l.GroupIndex).SequenceEqual(new[]{0,2});
            checks["offline.noAiMislabel"]=!coordinator.TranslationStatus.Contains("AI 生成") && coordinator.TranslationStatus.Contains("基础机翻");
            coordinator.SetTranslationVisibility(true);Tick();await Task.Delay(60);
            checks["offline.sharedVisibilityWorks"]=settings.ShowChineseTranslation && vm.CurrentTranslation.Length>0;
            var snapshot=await coordinator.ReadWorkspaceAsync(CancellationToken.None,true);var picked=snapshot.Songs.Where(s=>s.Track!=tracks[0]).ToArray();
            coordinator.SetOfflineVerificationEngine(()=>new OfflineFixtureEngine(1500));coordinator.TranslateSelectedOffline(picked);await Wait(()=>coordinator.OfflineBusy);await Task.Delay(100);
            checks["offline.pauseAvailable"]=Find("暂停").IsEnabled && !Find("翻译当前歌曲").IsEnabled;
            Invoke(Find("暂停"));await Wait(()=>!coordinator.OfflineBusy);await Task.Delay(80);
            checks["offline.pauseRetainsPending"]=new OfflineTranslationWorkflow(store.Root).ReadQueue().Any(i=>i.State=="Pending");
            coordinator.SetOfflineVerificationEngine(()=>new OfflineFixtureEngine(30));Invoke(Find("继续离线翻译"));await Wait(()=>!coordinator.OfflineBusy && new OfflineTranslationWorkflow(store.Root).ReadQueue().All(i=>i.State=="Completed"));await Task.Delay(100);
            checks["offline.resumeFinishesBatch"]=new OfflineTranslationWorkflow(store.Root).ReadQueue().All(i=>i.State=="Completed");
            var metrics=Descendants<System.Windows.Controls.Primitives.UniformGrid>(preferences.Library.Overview).First(g=>g.Columns==3);
            await Wait(()=>Descendants<TextBlock>(metrics.Children[2]).Any(t=>t.Text=="4"));
            checks["offline.overviewCoverageRefreshes"]=true;
            foreach(var appTheme in AppThemes.All)
            {
                SelectUiTheme(appTheme.Id,false);foreach(double width in new[]{1220d,820d})
                {
                    preferences.Width=width;await Task.Delay(80);preferences.UpdateLayout();panel.BringIntoView();await Task.Delay(40);
                    var buttons=Descendants<Button>(panel).ToArray();
                    checks[$"offline.{appTheme.Id}.{width:0}.feedbackFits"]=buttons.All(FeedbackFits);
                    checks[$"offline.{appTheme.Id}.{width:0}.withinPanel"]=buttons.All(b=>{var r=new Rect(b.TranslatePoint(new(),panel),b.RenderSize);return r.Left>=-1 && r.Right<=panel.ActualWidth+1;});
                }
                VisualVerification.Save(preferences,Path.Combine(directory,"offline-"+appTheme.Id+".png"));
            }
            checks["offline.localizedLabels"]=UiText.MissingKeys.Count==0;
        }
        finally
        {
            coordinator.PauseOfflineTranslation();await Wait(()=>!coordinator.OfflineBusy);preferences?.Close();settingsWindow=null;coordinator.SetOfflineVerificationEngine(null);coordinator.SetTranslationVerificationSnapshot(null,0);settings.LibraryPath=source;coordinator.SetTranslationVisibility(visible);SelectUiTheme(theme,false);
        }
    }
}
public sealed partial class PlaybackCoordinator
{
    internal void SetOfflineVerificationEngine(Func<IOfflineTranslationEngine>? factory)
    {
        if(!Environment.GetCommandLineArgs().Contains("--verify-ui"))throw new InvalidOperationException("Offline fixtures require explicit isolated UI verification.");
        offlineEngineFactory=factory;OfflineReady=factory is not null;OfflineChanged?.Invoke();
    }
}
