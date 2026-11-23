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
        var tracks=Enumerable.Range(0,9).Select(i=>new Track("Own acquisition "+i,"Own author","Own album",479000)).ToArray();
        tracks[1]=new("Kaze no Oto","Sora Hikari & Own guest","Kotoba no Umi - Single",479000);
        tracks[6]=tracks[6] with{Artist="Own singer A & Own singer B"};
        tracks[7]=tracks[7] with{Artist=tracks[6].Artist};
        tracks[8]=new("Own cast interlude","Own conductor & Own orchestra","Own stage work (Original 1986 London Cast)",479000);
        string[] lines=["We follow the river beyond the morning","We carry the light across the valley","The path remembers every quiet footstep","The distant hills are waiting for the sunrise","We leave the harbor with a song of our own","We reach the shore and rest beneath the stars"];
        string raw=string.Join("\n",lines.Select((line,i)=>$"[{i*40/60:00}:{i*40%60:00}]{line}")),plain=string.Join("\n",lines);
        string provenance=JsonSerializer.Serialize(new{id=991001,trackName=tracks[0].Title,artistName=tracks[0].Artist,albumName=tracks[0].Album,duration=479,syncedLyrics=raw,plainLyrics=plain,instrumental=false});
        var good=new LyricsCandidate(991001,tracks[0],raw,plain,false,provenance);
        var bad=good with{Id=991002,Track=new("カゼノオト","ソラヒカリ & Own guest","コトバノウミ",479000),PlainLyrics=null};
        var staticCandidate=new LyricsCandidate(991004,tracks[4],null,plain,false);
        var credited=tracks[6] with{Artist="Own singer A",DurationMs=479100};
        string creditJson=JsonSerializer.Serialize(new{id=991006,trackName=credited.Title,artistName=credited.Artist,albumName=credited.Album,duration=479.1,syncedLyrics=raw,plainLyrics=plain,instrumental=false});
        var partial=good with{Id=991006,Track=credited,RawJson=creditJson};
        var otherEdition=partial with{Id=991007,Track=credited with{Title=tracks[7].Title,Album="Own other edition"},RawJson=""};
        var cast=tracks[8] with{Artist="Original London Cast",DurationMs=480300};
        string castJson=JsonSerializer.Serialize(new{id=991008,trackName=cast.Title,artistName=cast.Artist,albumName=cast.Album,duration=480.3,syncedLyrics=raw,plainLyrics=plain,instrumental=false});
        var collective=good with{Id=991008,Track=cast,RawJson=castJson};
        try
        {
            settings.LibraryPath="own-ui";settings.OnlineLookup=true;
            using(var repo=new LyricsRepository(Path.Combine(coordinator.DataRoot,"lyrics.db")))
            {
                repo.SyncLibrary(new("own-ui",tracks.Length,tracks.Select((t,i)=>new LibraryTrack(i.ToString(),t)).ToArray(),"own"));
                repo.RecordLookupFailure(tracks[3],"时间戳超出歌曲时长",false,false);
                repo.Save(tracks[5],plain,"Own manual text",true,plain:true);
                repo.RecordLookupFailure(tracks[8],"歌手信息不匹配",false,false);
            }
            coordinator.WorkspaceOriginalProvider=new WorkspaceOriginalFixture((track,_)=>Task.FromResult<IReadOnlyList<LyricsCandidate>>(
                track==tracks[0]?[good]:track==tracks[1]?[bad]:track==tracks[4]?new LyricsLookupResult([staticCandidate],["Own unavailable source"])
                :track==tracks[6]?[partial]:track==tracks[7]?[otherEdition]:track==tracks[8]?[collective]:[]));
            coordinator.WorkspaceChineseProvider=new WorkspaceChineseFixture((_,_)=>Task.FromResult<IReadOnlyList<TranslationCandidate>>([]));
            preferences=new(coordinator,id=>SelectUiTheme(id,false)){Owner=this,Width=1220,Height=900};preferences.Show();preferences.SelectLibrarySongs();
            var panel=preferences.Library.Songs;await panel.Reload(true);await Task.Delay(180);
            foreach(var item in panel.SongList.Items)panel.SongList.SelectedItems.Add(item);
            panel.PreferExisting.IsChecked=false;
            Descendants<Button>(panel).Single(b=>b.Content?.ToString()==UiText.T("准备所选原词与译文")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var until=DateTime.UtcNow.AddSeconds(15);while(coordinator.WorkspaceBusy&&DateTime.UtcNow<until)await Task.Delay(40);
            await panel.Reload(true);await Task.Delay(150);
            var rows=panel.SongList.Items.Cast<LibraryManagedSong>().ToArray();
            checks["acquisition.actualPrepareCompletes"]=!coordinator.WorkspaceBusy&&rows.Length==tracks.Length;
            checks["acquisition.tailAccepted"]=rows.Single(r=>r.Track==tracks[0]).Status.Reason is null;
            checks["acquisition.missingReferenceReason"]=rows.Single(r=>r.Track==tracks[1]).Status.Reason=="尾段较长且来源缺少全文对照，需手动确认";
            checks["acquisition.noCandidateReason"]=rows.Single(r=>r.Track==tracks[2]).Status.Reason=="来源未返回歌词候选";
            using(var repo=new LyricsRepository(Path.Combine(coordinator.DataRoot,"lyrics.db"),true))
            {
                checks["acquisition.originalUnchanged"]=repo.FindReadOnly(tracks[0])?.Raw==raw&&repo.FindReadOnly(tracks[1]) is null;
                checks["acquisition.collaborationPreservesProviderCredits"]=rows.Single(r=>r.Track==tracks[6]).Status.Original==LibraryOriginalState.Ready
                    && repo.FindReadOnly(tracks[6]) is {} bound && bound.ProviderJson==creditJson && LibraryPrefetcher.QualityOf(bound,tracks[6]);
                var castRow=rows.Single(r=>r.Track==tracks[8]);
                checks["acquisition.collectiveCastRecoversPreviousFailure"]=castRow.Status is {Original:LibraryOriginalState.Ready,Reason:null}
                    && repo.FindReadOnly(tracks[8]) is {} castBinding && castBinding.ProviderJson==castJson && LibraryPrefetcher.QualityOf(castBinding,tracks[8]);
            }
            string castReason=MatchRules.Score(tracks[8],collective).Reason;
            checks["acquisition.collectiveCastReasonLocalized"]=castReason.StartsWith("原版卡司署名与发行版相符",StringComparison.Ordinal)
                && (UiText.Language=="zh-Hans" || UiText.Status(castReason)!=castReason);
            string otherEditionReason=MatchRules.Score(tracks[7],otherEdition).Reason;
            checks["acquisition.collaborationNeedsSameEdition"]=otherEditionReason=="合作署名有重合，但专辑或时长不足以确认录音"
                && rows.Single(r=>r.Track==tracks[7]).Status.Reason==otherEditionReason
                && (UiText.Language=="zh-Hans" || UiText.Status(otherEditionReason)!=otherEditionReason);
            var staticRow=rows.Single(r=>r.Track==tracks[4]);
            checks["acquisition.partialSourceCanStillSaveStatic"]=staticRow.Status.Original==LibraryOriginalState.Static
                && staticRow.Status.Chinese==LibraryChineseState.BlockedOriginal && staticRow.Status.Reason?.Contains("Own unavailable source")==true;
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
            var filters=Descendants<ComboBox>(panel).Single();
            panel.SelectKeys([]);
            filters.SelectedItem=UiText.T("仅有静态歌词");
            var onlyStatic=panel.SongList.Items.Cast<LibraryManagedSong>().ToArray();
            checks["acquisition.staticOnlyFilter"]=onlyStatic.Select(r=>r.Track).ToHashSet().SetEquals([tracks[4],tracks[5]])
                && (UiText.Language=="zh-Hans" || filters.SelectedItem?.ToString()!="仅有静态歌词");
            VisualVerification.Save(preferences,Path.Combine(directory,"static-only-filter.png"));
            panel.SelectKeys(onlyStatic.Select(r=>r.Key));
            coordinator.WorkspaceOriginalProvider=new WorkspaceOriginalFixture((track,_)=>Task.FromResult<IReadOnlyList<LyricsCandidate>>(
                [good with{Id=991004,Track=track,RawJson=JsonSerializer.Serialize(new{id=991004,trackName=track.Title,artistName=track.Artist,albumName=track.Album,duration=479,syncedLyrics=raw,plainLyrics=plain,instrumental=false})}]));
            Descendants<Button>(panel).Single(b=>b.Content?.ToString()==UiText.T("准备所选原词与译文")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            until=DateTime.UtcNow.AddSeconds(15);while(coordinator.WorkspaceBusy&&DateTime.UtcNow<until)await Task.Delay(40);
            await panel.Reload(true);
            checks["acquisition.staticUpgradeLeavesOnlyManualStaticInFilter"]=panel.SongList.Items.Cast<LibraryManagedSong>().SingleOrDefault()?.Track==tracks[5];
            filters.SelectedIndex=0;
            checks["acquisition.upgradedStaticIsTimed"]=panel.SongList.Items.Cast<LibraryManagedSong>().Single(r=>r.Track==tracks[4]).Status.Original==LibraryOriginalState.Ready;
            // The candidate dialog must distinguish metadata score from the automatic quality gate.
            var fixtureStore=new SettingsStore(Path.Combine(coordinator.DataRoot,"candidate-fixture"));
            var previewPartial=partial with{Track=tracks[1] with{Artist="Sora Hikari",DurationMs=479100},RawJson=""};
            var provider=new ManualLyricsFixtureProvider(bad){Handler=(_,_)=>Task.FromResult<IReadOnlyList<LyricsCandidate>>([bad,previewPartial])};
            await using var subject=new PlaybackCoordinator(Dispatcher,fixtureStore,new AppSettings{OnlineLookup=true,AutoPrefetchLibrary=false,AppleMusicOriginalLyrics=false},provider);
            subject.SetTranslationVerificationSnapshot(tracks[4],1000);
            subject.ApplyAutomaticLookupResult(tracks[4],[staticCandidate]);
            checks["acquisition.playbackDisplaysUntimedFallback"]=subject.Binding?.StaticFallback==true && subject.Document is {IsSynced:false}
                && subject.Document.Raw==plain && subject.Translation is null;
            await using(var offline=new PlaybackCoordinator(Dispatcher,fixtureStore,new AppSettings{OnlineLookup=false,AutoPrefetchLibrary=false,AppleMusicOriginalLyrics=false}))
            {
                offline.SetTranslationVerificationSnapshot(tracks[4],1000);
                await offline.LookupAsync(false);
                checks["acquisition.playbackLoadsFallbackOffline"]=offline.Document?.Raw==plain && offline.LyricsStatus.StartsWith("静态歌词",StringComparison.Ordinal);
            }
            subject.ApplyAutomaticLookupResult(tracks[4],[good with{Track=tracks[4],RawJson=""}]);
            checks["acquisition.playbackUpgradesToTimed"]=subject.Binding?.StaticFallback==false && subject.Document?.IsSynced==true;
            subject.SetTranslationVerificationSnapshot(tracks[8],1000);
            subject.ApplyAutomaticLookupResult(tracks[8],[collective]);
            checks["acquisition.playbackUsesCollectiveCastOriginal"]=subject.Document is {IsSynced:true,Instrumental:false} document
                && document.Raw==raw && subject.Binding?.ProviderJson==castJson;
            subject.SetTranslationVerificationSnapshot(tracks[1],1000);await subject.LookupManualAsync(tracks[1],default);
            var dialog=new CandidateWindow(subject,_=>tracks[1],(_,_)=>{}){Owner=preferences};
            var finished=new TaskCompletionSource();
            _=Dispatcher.BeginInvoke(async()=>
            {
                try
                {
                    var candidates=Descendants<ListBox>(dialog).Single();
                    candidates.SelectedItem=bad;await Task.Delay(120);
                    var preview=Descendants<TextBox>(dialog).Single(t=>t.IsReadOnly);
                    string text=preview.Text;
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
                    candidates.SelectedItem=previewPartial;
                    string collaborationReason=MatchRules.Score(tracks[1],previewPartial).Reason;
                    checks["acquisition.dialog.collaborationMetadataLocalized"]=preview.Text.StartsWith(UiText.F($"歌曲信息匹配度 {.92:P0} · {UiText.Status(collaborationReason)}\n"))
                        && collaborationReason.StartsWith("合作歌手署名部分相符",StringComparison.Ordinal)
                        && (UiText.Language=="zh-Hans" || UiText.Status(collaborationReason)!=collaborationReason);
                    checks["acquisition.dialog.collaborationContentChecked"]=preview.Text.Contains(UiText.F($"自动检查通过：{UiText.Status(AutomaticLyricsMatcher.Assess(previewPartial).Reason)}\n\n"));
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
