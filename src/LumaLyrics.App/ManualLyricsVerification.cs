using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;

namespace LumaLyrics.App;

public partial class MainWindow
{
    async Task VerifyManualLyricsAsync(string directory,Dictionary<string,object?> checks)
    {
        var root=Path.Combine(store.Root,"manual-lyrics-fixture");
        var fixtureStore=new SettingsStore(root);
        var fixtureSettings=new AppSettings{OnlineLookup=true,AutoPrefetchLibrary=false,AppleMusicOriginalLyrics=false};
        var playback=new Track("微光之间","浮光","原创验证专辑",96000);
        var candidate=new LyricsCandidate(910001,new("别名搜索结果","另一种歌手拼写","候选专辑",97000),
            "[00:00]这是手动选中的原创文字\n[00:12]让微光停留在你身旁",null,false,"{}");
        var provider=new ManualLyricsFixtureProvider(candidate);
        await using var subject=new PlaybackCoordinator(Dispatcher,fixtureStore,fixtureSettings,provider);
        var snapshot=new PlaybackSnapshot("manual-lyrics-fixture",1,1,playback,PlayState.Paused,1,12000,
            DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,Stopwatch.GetTimestamp(),PositionQuality.Reliable);
        subject.SetManualLyricsVerificationSnapshot(snapshot);

        static Button FindButton(CandidateWindow dialog,string label)=>Descendants<Button>(dialog).Single(b=>b.Content?.ToString()==label);
        static void Click(CandidateWindow dialog,string label)
        {
            var button=FindButton(dialog,label);
            if(!button.IsEnabled)throw new InvalidOperationException("Verification attempted a disabled button: "+label);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        async Task ExerciseAsync(CandidateWindow dialog,Func<Task> exercise)
        {
            var finished=new TaskCompletionSource();
            _=Dispatcher.BeginInvoke(async () =>
            {
                try{await exercise();dialog.Close();finished.SetResult();}
                catch(Exception ex){dialog.Close();finished.SetException(ex);}
            });
            dialog.ShowDialog();await finished.Task;
        }

        subject.Import(playback,"[00:00]旧的手动原创文字","Previous manual fixture",false);

        foreach(var (name,query) in new[]
        {
            ("emptyAlbum",playback with{Album="",DurationMs=null}),
            ("differentSearchMetadata",new Track("微光","浮光音乐","",null))
        })
        {
            var messages=new List<string>();
            var dialog=new CandidateWindow(subject,_=>query,(_,message)=>messages.Add(message)){Owner=this};
            var previousBinding=subject.Binding?.Id;
            await ExerciseAsync(dialog,async () =>
            {
                Click(dialog,"手动填写歌名 / 歌手搜索");
                var list=Descendants<ListBox>(dialog).Single();list.SelectedIndex=0;
                checks[$"manualLyrics.{name}.targetRemainsVisible"]=Descendants<TextBlock>(dialog).Any(t=>t.Text.StartsWith("绑定目标："+playback.Title) && t.Text.Contains(playback.Album));
                checks[$"manualLyrics.{name}.lowConfidenceStillSelectable"]=!MatchRules.Score(playback,candidate).Eligible && FindButton(dialog,"使用所选歌词").IsEnabled;
                FindButton(dialog,"使用所选歌词").BringIntoView();await Task.Delay(220);
                VisualVerification.Save(dialog,Path.Combine(directory,name+"-selection.png"));
                Click(dialog,"使用所选歌词");
                checks[$"manualLyrics.{name}.noFalseSongChangeWarning"]=messages.Count==0;
                checks[$"manualLyrics.{name}.appliesImmediately"]=subject.Binding is{Manual:true} && subject.Document?.Raw==candidate.Content;
                checks[$"manualLyrics.{name}.replacesPreviousManualBinding"]=subject.Binding?.Id!=previousBinding;
                using var saved=new LyricsRepository(Path.Combine(root,"lyrics.db"));
                checks[$"manualLyrics.{name}.savedForPlaybackTarget"]=saved.Find(playback) is{Manual:true} binding && binding.Raw==candidate.Content;
                checks[$"manualLyrics.{name}.notSavedForSearchQuery"]=saved.Find(query) is null;
                checks[$"manualLyrics.{name}.providerReceivesQuery"]=provider.LastQuery==query;
                checks[$"manualLyrics.{name}.warning"]=string.Join("\n",messages);
            });
        }

        string? manualBinding=subject.Binding?.Id;
        await subject.LookupAsync(true);
        checks["manualLyrics.forceAutomaticLookupKeepsManual"]=subject.Binding?.Id==manualBinding && subject.Binding?.Manual==true;
        using(var saved=new LyricsRepository(Path.Combine(root,"lyrics.db")))
        {
            var result=saved.Save(playback,"[00:00]后台原创缓存文字","Background automatic fixture",false);
            checks["manualLyrics.backgroundCacheKeepsManual"]=result.Id==manualBinding && saved.Find(playback)?.Raw==candidate.Content;
        }

        foreach(var (name,current) in new (string,PlaybackSnapshot?)[]
        {
            ("differentSong",snapshot with{Track=playback with{Title="另一首原创歌曲"},Revision=2}),
            ("sameMetadataNewRevision",snapshot with{Revision=3}),
            ("newSessionGeneration",snapshot with{Generation=2}),
            ("differentSession",snapshot with{SessionId="another-fixture"}),
            ("disconnected",null)
        })
        {
            subject.SetManualLyricsVerificationSnapshot(snapshot);var messages=new List<string>();
            var dialog=new CandidateWindow(subject,_=>playback,(_,message)=>messages.Add(message)){Owner=this};
            await ExerciseAsync(dialog,() =>
            {
                Descendants<ListBox>(dialog).Single().SelectedIndex=0;
                subject.SetManualLyricsVerificationSnapshot(current);
                Click(dialog,"使用所选歌词");
                checks[$"manualLyrics.{name}.rejectsStaleTarget"]=messages.Count==1 && subject.Binding?.Id==manualBinding && dialog.IsVisible;
                return Task.CompletedTask;
            });
        }

        subject.SetManualLyricsVerificationSnapshot(snapshot with{Track=playback with{DurationMs=null}});
        var durationMessages=new List<string>();
        var durationDialog=new CandidateWindow(subject,_=>playback,(_,message)=>durationMessages.Add(message)){Owner=this};
        await ExerciseAsync(durationDialog,() =>
        {
            Descendants<ListBox>(durationDialog).Single().SelectedIndex=0;
            subject.SetManualLyricsVerificationSnapshot(snapshot with{Status=PlayState.Playing,PositionMs=18000});
            Click(durationDialog,"使用所选歌词");
            checks["manualLyrics.durationAndPlaybackStateChangesAreAllowed"]=durationMessages.Count==0 && !durationDialog.IsVisible && subject.Binding?.Id!=manualBinding;
            return Task.CompletedTask;
        });

        subject.SetManualLyricsVerificationSnapshot(null);
        var offline=new Track("离线原创歌曲","浮光","离线验证专辑",96000);
        var offlineMessages=new List<string>();
        var offlineDialog=new CandidateWindow(subject,_=>offline,(_,message)=>offlineMessages.Add(message)){Owner=this};
        await ExerciseAsync(offlineDialog,() =>
        {
            Click(offlineDialog,"手动填写歌名 / 歌手搜索");
            Descendants<ListBox>(offlineDialog).Single().SelectedIndex=0;Click(offlineDialog,"使用所选歌词");
            using var saved=new LyricsRepository(Path.Combine(root,"lyrics.db"));
            checks["manualLyrics.offlineSelectionStillApplies"]=offlineMessages.Count==0 && subject.ActiveTrack==offline && subject.Document?.Raw==candidate.Content && saved.Find(offline)?.Manual==true;
            return Task.CompletedTask;
        });

        var appearedMessages=new List<string>();
        var appearedDialog=new CandidateWindow(subject,_=>offline,(_,message)=>appearedMessages.Add(message)){Owner=this};
        await ExerciseAsync(appearedDialog,() =>
        {
            Descendants<ListBox>(appearedDialog).Single().SelectedIndex=0;
            var previous=subject.Binding?.Id;subject.SetManualLyricsVerificationSnapshot(snapshot);
            Click(appearedDialog,"使用所选歌词");
            checks["manualLyrics.sessionAppearingRequiresReconfirmation"]=appearedMessages.Count==1 && subject.Binding?.Id==previous;
            return Task.CompletedTask;
        });

        var oldResponse=new TaskCompletionSource<IReadOnlyList<LyricsCandidate>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newResponse=new TaskCompletionSource<IReadOnlyList<LyricsCandidate>>(TaskCreationOptions.RunContinuationsAsynchronously);
        int request=0;provider.Handler=(_,_)=>++request==1?oldResponse.Task:newResponse.Task;
        var latest=candidate with{Id=910002,SyncedLyrics="[00:00]最后一次搜索的原创文字"};
        var raceMessages=new List<string>();
        var raceDialog=new CandidateWindow(subject,_=>playback with{Album=""},(_,message)=>raceMessages.Add(message)){Owner=this};
        await ExerciseAsync(raceDialog,async () =>
        {
            Descendants<ListBox>(raceDialog).Single().SelectedIndex=0;
            Click(raceDialog,"手动填写歌名 / 歌手搜索");
            checks["manualLyrics.inFlightSearchClearsPreviousSelection"]=!FindButton(raceDialog,"使用所选歌词").IsEnabled && Descendants<ListBox>(raceDialog).Single().Items.Count==0;
            Click(raceDialog,"手动填写歌名 / 歌手搜索");newResponse.SetResult([latest]);
            await Task.Delay(80);oldResponse.SetResult([candidate]);await Task.Delay(80);
            var list=Descendants<ListBox>(raceDialog).Single();
            checks["manualLyrics.lateOldResponseCannotReplaceLatest"]=list.Items.Count==1 && list.Items[0] is LyricsCandidate{Id:910002};
            list.SelectedIndex=0;Click(raceDialog,"使用所选歌词");
            checks["manualLyrics.latestSearchAppliesToPlaybackTarget"]=raceMessages.Count==0 && subject.Document?.Raw==latest.Content && subject.Binding?.Manual==true;
        });

        var closeResponse=new TaskCompletionSource<IReadOnlyList<LyricsCandidate>>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken closedToken=default;provider.Handler=(_,token)=>{closedToken=token;return closeResponse.Task;};
        var closeDialog=new CandidateWindow(subject,_=>playback,(_,_)=>{}){Owner=this};
        await ExerciseAsync(closeDialog,async () =>
        {
            Click(closeDialog,"手动填写歌名 / 歌手搜索");var previous=subject.Binding?.Id;
            closeDialog.Close();closeResponse.SetResult([candidate]);await Task.Delay(80);
            checks["manualLyrics.closeCancelsOwnedSearch"]=closedToken.IsCancellationRequested;
            checks["manualLyrics.closedWindowCannotApplyLateResults"]=subject.Binding?.Id==previous && subject.Candidates.Single().Id==latest.Id;
        });

        provider.Handler=(_,_)=>Task.FromException<IReadOnlyList<LyricsCandidate>>(new IOException("Original verification failure"));
        var failureDialog=new CandidateWindow(subject,_=>playback,(_,_)=>{}){Owner=this};
        await ExerciseAsync(failureDialog,() =>
        {
            Descendants<ListBox>(failureDialog).Single().SelectedIndex=0;
            Click(failureDialog,"手动填写歌名 / 歌手搜索");
            checks["manualLyrics.failedSearchCannotReusePreviousResults"]=!FindButton(failureDialog,"使用所选歌词").IsEnabled && Descendants<ListBox>(failureDialog).Single().Items.Count==0;
            return Task.CompletedTask;
        });
    }

    sealed class ManualLyricsFixtureProvider(LyricsCandidate candidate):ILyricsProvider
    {
        public Track? LastQuery {get;private set;}
        public Func<Track,CancellationToken,Task<IReadOnlyList<LyricsCandidate>>>? Handler {get;set;}
        public Task<IReadOnlyList<LyricsCandidate>> LookupAsync(Track track,CancellationToken token)
        {
            LastQuery=track;return Handler?.Invoke(track,token)??Task.FromResult<IReadOnlyList<LyricsCandidate>>([candidate]);
        }
    }
}

public sealed partial class PlaybackCoordinator
{
    internal void SetManualLyricsVerificationSnapshot(PlaybackSnapshot? snapshot)
    {
        if(!Environment.GetCommandLineArgs().Contains("--verify-ui"))throw new InvalidOperationException("Manual lyrics fixtures require explicit isolated UI verification.");
        Snapshot=snapshot;clock.Update(snapshot);
    }
}
