using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;
public partial class MainWindow
{
    async Task VerifyLyricAcquisitionAsync(string directory,Dictionary<string,object?> checks)
    {
        var oldSource=settings.LibraryPath;bool online=settings.OnlineLookup;string theme=settings.UiTheme;
        SettingsWindow? preferences=null;
        var tracks=Enumerable.Range(0,4).Select(i=>new Track("Own acquisition "+i,"Own author","Own album",479000)).ToArray();
        tracks[1]=new("Kaze no Oto","Sora Hikari","Kotoba no Umi - Single",479000);
        string[] lines=["We follow the river beyond the morning","We carry the light across the valley","The path remembers every quiet footstep","The distant hills are waiting for the sunrise","We leave the harbor with a song of our own","We reach the shore and rest beneath the stars"];
        string raw=string.Join("\n",lines.Select((line,i)=>$"[{i*40/60:00}:{i*40%60:00}]{line}")),plain=string.Join("\n",lines);
        string provenance=JsonSerializer.Serialize(new{id=991001,trackName=tracks[0].Title,artistName=tracks[0].Artist,albumName=tracks[0].Album,duration=479,syncedLyrics=raw,plainLyrics=plain,instrumental=false});
        var good=new LyricsCandidate(991001,tracks[0],raw,plain,false,provenance);
        var bad=good with{Id=991002,Track=new("カゼノオト","ソラヒカリ","コトバノウミ",479000),PlainLyrics=null};
        try
        {
            settings.LibraryPath="own-ui";settings.OnlineLookup=true;
            using(var repo=new LyricsRepository(Path.Combine(coordinator.DataRoot,"lyrics.db")))
            {
                repo.SyncLibrary(new("own-ui",4,tracks.Select((t,i)=>new LibraryTrack(i.ToString(),t)).ToArray(),"own"));
                repo.RecordLookupFailure(tracks[3],"时间戳超出歌曲时长",false,false);
            }
            coordinator.WorkspaceOriginalProvider=new WorkspaceOriginalFixture((track,_)=>Task.FromResult<IReadOnlyList<LyricsCandidate>>(track==tracks[0]?[good]:track==tracks[1]?[bad]:[]));
            coordinator.WorkspaceChineseProvider=new WorkspaceChineseFixture((_,_)=>Task.FromResult<IReadOnlyList<TranslationCandidate>>([]));
            preferences=new(coordinator,id=>SelectUiTheme(id,false)){Owner=this,Width=1220,Height=900};preferences.Show();preferences.SelectLibrarySongs();
            var panel=preferences.Library.Songs;await panel.Reload(true);await Task.Delay(180);
            foreach(var item in panel.SongList.Items)panel.SongList.SelectedItems.Add(item);
            panel.PreferExisting.IsChecked=false;
            Descendants<Button>(panel).Single(b=>b.Content?.ToString()==UiText.T("准备所选原词与译文")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var until=DateTime.UtcNow.AddSeconds(15);while(coordinator.WorkspaceBusy&&DateTime.UtcNow<until)await Task.Delay(40);
            await panel.Reload(true);await Task.Delay(150);
            var rows=panel.SongList.Items.Cast<LibraryManagedSong>().ToArray();
            checks["acquisition.actualPrepareCompletes"]=!coordinator.WorkspaceBusy&&rows.Length==4;
            checks["acquisition.tailAccepted"]=rows.Single(r=>r.Track==tracks[0]).Status.Reason is null;
            checks["acquisition.missingReferenceReason"]=rows.Single(r=>r.Track==tracks[1]).Status.Reason=="尾段较长且来源缺少全文对照，需手动确认";
            checks["acquisition.noCandidateReason"]=rows.Single(r=>r.Track==tracks[2]).Status.Reason=="来源未返回歌词候选";
            using(var repo=new LyricsRepository(Path.Combine(coordinator.DataRoot,"lyrics.db"),true))checks["acquisition.originalUnchanged"]=repo.FindReadOnly(tracks[0])?.Raw==raw&&repo.FindReadOnly(tracks[1]) is null;
            foreach(var appTheme in AppThemes.All)
            {
                SelectUiTheme(appTheme.Id,false);
                foreach(double width in new[]{1220d,820d})
                {
                    preferences.Width=width;await Task.Delay(100);
                    string key="acquisition."+appTheme.Id+"."+width;
                    var reason=Descendants<TextBlock>(panel.SongList).FirstOrDefault(t=>t.Text==UiText.Status("尾段较长且来源缺少全文对照，需手动确认"));
                    checks[key+".reasonVisible"]=reason?.IsVisible==true;
                    checks[key+".fullTooltip"]=reason?.ToolTip?.ToString()==UiText.Status("尾段较长且来源缺少全文对照，需手动确认");
                    var bounds=panel.SongList.TransformToAncestor(panel).TransformBounds(new Rect(0,0,panel.SongList.ActualWidth,panel.SongList.ActualHeight));
                    checks[key+".bounded"]=bounds.Right<=panel.ActualWidth+1&&bounds.Bottom<=panel.ActualHeight+1;
                    VisualVerification.Save(preferences,Path.Combine(directory,key+".png"));
                }
            }
            // A later successful preparation must remove the old persisted reason in the actual list.
            string retryJson=JsonSerializer.Serialize(new{id=991002,trackName=tracks[1].Title,artistName=tracks[1].Artist,albumName=tracks[1].Album,duration=479,syncedLyrics=raw,plainLyrics=plain,instrumental=false});
            coordinator.WorkspaceOriginalProvider=new WorkspaceOriginalFixture((track,_)=>Task.FromResult<IReadOnlyList<LyricsCandidate>>([good with{Id=991002,Track=track,RawJson=retryJson}]));
            panel.SongList.SelectedItems.Clear();panel.SongList.SelectedItems.Add(panel.SongList.Items.Cast<LibraryManagedSong>().Single(r=>r.Track==tracks[1]));
            Descendants<Button>(panel).Single(b=>b.Content?.ToString()==UiText.T("准备所选原词与译文")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            until=DateTime.UtcNow.AddSeconds(15);while(coordinator.WorkspaceBusy&&DateTime.UtcNow<until)await Task.Delay(40);await panel.Reload(true);await Task.Delay(100);
            checks["acquisition.successClearsVisibleReason"]=panel.SongList.Items.Cast<LibraryManagedSong>().Single(r=>r.Track==tracks[1]).Status.Reason is null;
            // The candidate dialog must distinguish metadata score from the automatic quality gate.
            var fixtureStore=new SettingsStore(Path.Combine(coordinator.DataRoot,"candidate-fixture"));
            var provider=new ManualLyricsFixtureProvider(bad);
            await using var subject=new PlaybackCoordinator(Dispatcher,fixtureStore,new AppSettings{OnlineLookup=true,AutoPrefetchLibrary=false,AppleMusicOriginalLyrics=false},provider);
            subject.SetTranslationVerificationSnapshot(tracks[1],1000);await subject.LookupManualAsync(tracks[1],default);
            var dialog=new CandidateWindow(subject,_=>tracks[1],(_,_)=>{}){Owner=preferences};
            var finished=new TaskCompletionSource();
            _=Dispatcher.BeginInvoke(async()=>
            {
                try
                {
                    Descendants<ListBox>(dialog).Single().SelectedIndex=0;await Task.Delay(120);
                    string text=Descendants<TextBox>(dialog).Single(t=>t.IsReadOnly).Text;
                    checks["acquisition.dialog.metadataScore"]=text.StartsWith(UiText.F($"歌曲信息匹配度 {1d:P0} · {UiText.Status(MatchRules.Score(tracks[1],bad).Reason)}\n"));
                    string readingReason=MatchRules.Score(tracks[1],bad).Reason;
                    checks["acquisition.dialog.japaneseReadingReasonLocalized"]=readingReason.StartsWith("日文读法相符",StringComparison.Ordinal)
                        && (UiText.Language=="zh-Hans" || UiText.Status(readingReason)!=readingReason);
                    checks["acquisition.dialog.independentRejection"]=text.Contains(UiText.F($"自动未采用：{UiText.Status(AutomaticLyricsMatcher.Decide(tracks[1],bad).Reason)}\n\n"));
                    checks["acquisition.dialog.manualStillAvailable"]=Descendants<Button>(dialog).Single(b=>b.Content?.ToString()==UiText.T("使用所选歌词")).IsEnabled;
                    foreach(var appTheme in AppThemes.All)
                    {
                        SelectUiTheme(appTheme.Id,false);await Task.Delay(80);
                        checks["acquisition.dialog."+appTheme.Id+".readable"]=Descendants<TextBox>(dialog).Single(t=>t.IsReadOnly).ActualWidth>300&&Descendants<Button>(dialog).Single(b=>b.Content?.ToString()==UiText.T("使用所选歌词")).IsEnabled;
                        VisualVerification.Save(dialog,Path.Combine(directory,"candidate-metadata-score-"+appTheme.Id+".png"));
                    }
                    finished.SetResult();
                }
                catch(Exception e){finished.SetException(e);}
                finally{dialog.Close();}
            });
            dialog.ShowDialog();await finished.Task;
        }
        finally{preferences?.Close();settings.LibraryPath=oldSource;settings.OnlineLookup=online;coordinator.WorkspaceOriginalProvider=null;coordinator.WorkspaceChineseProvider=null;SelectUiTheme(theme,false);}
    }
}
