using System.IO;
using System.Text.Json;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

public partial class MainWindow
{
    [System.Runtime.InteropServices.DllImport("user32.dll",EntryPoint="GetForegroundWindow")] static extern nint QueueForegroundForVerification();
    sealed record QueueRestoreVerification(MusicQueueSnapshot Baseline,MusicQueueSnapshot Expected);
    sealed record QueueBaselineVerification(MusicQueueSnapshot Queue,LumaLyrics.Core.PlayState State);
    // Explicit integration mode only. Reports contain private library metadata and must
    // stay in the isolated directory selected by the caller, never in software docs.
    async Task VerifyNativeLibraryPlaybackAsync(string directory,string[] args)
    {
        PersonalLibraryArchive.EnsureOutsideSoftware(directory);Directory.CreateDirectory(directory);string traceFile=Path.Combine(directory,"native-playback.jsonl");var traceGate=new object();
        void Trace(string stage,object? value)
        {
            if(stage=="lyricCaptureImage" && value is byte[] png){File.WriteAllBytes(Path.Combine(directory,"native-capture.png"),png);return;}
            lock(traceGate)File.AppendAllText(traceFile,JsonSerializer.Serialize(new{utc=DateTimeOffset.UtcNow,stage,value})+Environment.NewLine);
            if(args.Contains("--native-drag-screenshots") && stage.StartsWith("queueDrag",StringComparison.Ordinal))
            {
                using var process=System.Diagnostics.Process.GetProcessesByName("AppleMusic").FirstOrDefault(p=>p.MainWindowHandle!=0);
                if(process is not null && QueueForegroundForVerification()==process.MainWindowHandle)
                {
                    using var automation=new FlaUI.UIA3.UIA3Automation();var bounds=automation.FromHandle(process.MainWindowHandle).BoundingRectangle;
                    using var bitmap=new System.Drawing.Bitmap(bounds.Width,bounds.Height);using var graphics=System.Drawing.Graphics.FromImage(bitmap);
                    graphics.CopyFromScreen(bounds.Location,System.Drawing.Point.Empty,bounds.Size);bitmap.Save(Path.Combine(directory,stage+".png"));
                }
            }
        }
        string? Argument(string name){int index=Array.IndexOf(args,name);return index>=0 && index+1<args.Length?args[index+1]:null;}
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(150));
        if(args.Contains("--native-probe-read-safety")){await VerifyNativeReadSafetyAsync(directory,Argument("--native-safety-mode")??"anchors",timeout.Token);return;}
        if(args.Contains("--native-verify-passive")){await VerifyPassiveNativeAsync(directory,args.Contains("--native-acquire-original"),timeout.Token);return;}
        if(args.Contains("--native-pause-before")){await coordinator.Source.StartAsync(timeout.Token);await coordinator.Source.ControlAsync("pause");await Task.Delay(300,timeout.Token);}
        if(args.Contains("--native-start-visible"))Trace("preparedVisible",new{visible=await AppleMusicLibraryHandoff.PrepareVisibleVerificationAsync(timeout.Token)});
        Trace("initial",await Task.Run(AppleMusicLibraryHandoff.Inspect,timeout.Token).WaitAsync(TimeSpan.FromSeconds(12),timeout.Token));
        if(args.Contains("--native-inspect-queue")){Trace("queue",await AppleMusicLibraryHandoff.InspectQueueAsync(timeout.Token));return;}
        if(args.Contains("--native-read-queue")){Trace("queueRead",await AppleMusicLibraryHandoff.ReadQueueAsync(timeout.Token,Trace));return;}
        if(args.Contains("--native-verify-queue-state")){Trace("queueState",await AppleMusicLibraryHandoff.VerifyQueueStateAsync(timeout.Token));return;}
        if(args.Contains("--native-read-current"))
        {
            await coordinator.Source.StartAsync(timeout.Token);await Task.Delay(200,timeout.Token);Trace("current",coordinator.Source.Latest);Trace("queue",await AppleMusicLibraryHandoff.ReadQueueAsync(timeout.Token,Trace));return;
        }
        if(args.Contains("--native-restore-queue-check"))
        {
            var request=JsonSerializer.Deserialize<QueueRestoreVerification>(File.ReadAllText(Path.Combine(directory,"queue-restore-input.json")))??throw new InvalidDataException("Missing exact native queue baseline.");
            Trace("queueRestored",new{restored=await AppleMusicLibraryHandoff.RestoreQueueVerificationAsync(request.Baseline,request.Expected,timeout.Token)});return;
        }
        if(args.Contains("--native-inspect-only"))return;
        if(Argument("--native-play-playlist") is { } playlistName)
        {
            var playlistCatalog=new MusicLibraryCatalog(store.Root,settings.LibraryPath).Load(timeout.Token);
            var playlist=playlistCatalog.Playlists.Single(p=>p.Name==playlistName);
            await coordinator.Source.StartAsync(timeout.Token);
            Trace("playlistSelected",new{playlist.Name,playlist.DeclaredCount,playlist.Complete});
            var playlistBridge=new AppleMusicPlaybackBridge(coordinator,Trace);
            var playlistResult=await playlistBridge.PlayPlaylistAsync(playlist,timeout.Token);Trace("playlistResult",new{playlistResult,actual=coordinator.Source.Latest});return;
        }

        if(args.Contains("--native-verify-lyrics"))
        {
            await coordinator.Source.StartAsync(timeout.Token);await coordinator.Source.RefreshAsync();var before=coordinator.Source.Latest??throw new InvalidOperationException("No current native recording");
            var track=LumaLyrics.Core.TrackMetadata.FromAppleSession(before.Track);string profile=Argument("--native-original-profile")??throw new ArgumentException("Specify an existing private original profile");
            using var personal=new LyricsRepository(Path.Combine(profile,"lyrics.db"));var binding=personal.Find(track)??throw new InvalidOperationException("No current original for comparison");
            if(!binding.Manual && !LibraryPrefetcher.QualityOf(binding,track))throw new InvalidDataException("Current original has not passed checks");
            coordinator.Import(track,binding.Raw,"Private native comparison fixture",binding.Plain);var document=coordinator.Document!;var reviewBridge=new AppleMusicPlaybackBridge(coordinator,Trace);
            foreach(bool image in new[]{true,false})
            {
                var reviewResult=await reviewBridge.CaptureLyricsReviewAsync(track,document,image,timeout.Token);var report=LumaLyrics.Core.LyricsOcrComparison.Compare(track,document,reviewResult.Observation);
                string record=new LyricsReviewStore(store.Root).Save(track,document,reviewResult.Observation,image?"windows-ocr":"native-text",image?reviewResult.Capture.Image:null);
                Trace("nativeLyricsReview",new{image,report.Accepted,report.ObservedGroups,report.TotalGroups,lines=reviewResult.Observation.Lines.Count,method=reviewResult.Observation.Language,recordingRetained=coordinator.Source.Latest is { } after && MusicLibraryPlayback.IsRequestedRecording(track,after.Track),playStateRetained=coordinator.Source.Latest?.Status==before.Status,stored=File.Exists(record)});
            }
            return;
        }
        if(args.Contains("--native-reorder-queue"))
        {
            await coordinator.Source.StartAsync(timeout.Token);var moveBridge=new AppleMusicPlaybackBridge(coordinator,Trace);var baseline=await moveBridge.ReadQueueAsync(timeout.Token);var before=coordinator.Source.Latest;
            int source=int.TryParse(Argument("--native-move-source"),out int from)?from:1;int insertion=int.TryParse(Argument("--native-move-insertion"),out int to)?to:0;
            var move=new MusicQueueMove(baseline,source,insertion);Trace("queueMoveBefore",new{baseline,before});var moved=await moveBridge.ReorderQueueAsync(move,timeout.Token);var after=await moveBridge.ReadQueueAsync(timeout.Token);
            Trace("queueMoveResult",new{moved,confirmed=MusicPlaybackQueue.ConfirmsMove(move,after),after,actual=coordinator.Source.Latest});
            if(moved.Confirmed && args.Contains("--native-restore-after"))
            {
                int destination=insertion>source?insertion-1:insertion;int reverseInsertion=destination<source?source+1:source;
                var restored=await moveBridge.ReorderQueueAsync(new(after,destination,reverseInsertion),timeout.Token);var final=await moveBridge.ReadQueueAsync(timeout.Token);
                Trace("queueMoveRestored",new{restored,originalOrderRestored=baseline.Upcoming.Select(e=>e.Track).SequenceEqual(final.Upcoming.Select(e=>e.Track)),currentRecordingRetained=baseline.Current==final.Current,playStateRetained=before?.Status==coordinator.Source.Latest?.Status});
            }
            return;
        }
        if(args.Contains("--native-play-queue"))
        {
            await coordinator.Source.StartAsync(timeout.Token);await Task.Delay(200,timeout.Token);
            var queueBridge=new AppleMusicPlaybackBridge(coordinator,Trace);
            var baseline=await queueBridge.ReadQueueAsync(timeout.Token);Trace("queuePlaybackBefore",new{queue=baseline,actual=coordinator.Source.Latest});
            int index=int.TryParse(Argument("--native-queue-index"),out int selectedIndex)?selectedIndex:1;
            var selection=new MusicQueueSelection(baseline,index);var queueResult=await queueBridge.PlayQueueAsync(selection,timeout.Token);
            var after=await queueBridge.ReadQueueAsync(timeout.Token);Trace("queuePlaybackResult",new{result=queueResult,occurrenceConfirmed=MusicPlaybackQueue.ConfirmsJump(selection,after),actual=coordinator.Source.Latest,after});
            if(queueResult.Confirmed && args.Contains("--native-stale-queue-check"))
            {
                var stale=await queueBridge.PlayQueueAsync(selection,timeout.Token);var retained=await queueBridge.ReadQueueAsync(timeout.Token);
                Trace("staleQueueResult",new{stale,queuePreserved=after.Current==retained.Current && after.Upcoming.SequenceEqual(retained.Upcoming)});
            }
            if(queueResult.Confirmed && args.Contains("--native-pause-after")){bool paused=await coordinator.Source.ControlAsync("pause");await Task.Delay(300,timeout.Token);Trace("paused",new{paused,actual=coordinator.Source.Latest});}
            return;
        }
        if(args.Contains("--native-probe-queue-selection"))
        {
            await coordinator.Source.StartAsync(timeout.Token);await Task.Delay(200,timeout.Token);
            var baseline=await AppleMusicLibraryHandoff.ReadQueueAsync(timeout.Token,Trace);
            int index=int.TryParse(Argument("--native-queue-index"),out int selectedIndex)?selectedIndex:1;
            var selection=new MusicQueueSelection(baseline,index);var context=new AppleMusicLibraryHandoff.HandoffContext();
            var failure=await AppleMusicLibraryHandoff.SelectQueueAsync(selection,timeout.Token,context,Trace);Trace("queueSelectionProbe",new{index,failure});
            await Task.Delay(2500,timeout.Token);await coordinator.Source.RefreshAsync();
            var after=await AppleMusicLibraryHandoff.ReadQueueAsync(timeout.Token,Trace);bool confirmed=MusicPlaybackQueue.ConfirmsJump(selection,after);
            Trace("queueSelectionProbeResult",new{confirmed,actual=coordinator.Source.Latest,after});
            await AppleMusicLibraryHandoff.CompleteHandoffWindowAsync(context,confirmed,Trace);return;
        }
        var catalog=await Task.Run(()=>new MusicLibraryCatalog(store.Root,settings.LibraryPath).Load(timeout.Token),timeout.Token);
        if(args.Contains("--native-restore-queue-baseline"))
        {
            var baseline=JsonSerializer.Deserialize<QueueBaselineVerification>(File.ReadAllText(Path.Combine(directory,"queue-baseline-input.json")))??throw new InvalidDataException("Missing private original queue baseline.");
            var original=baseline.Queue.Current??throw new InvalidDataException("Missing original current recording.");
            var albums=MusicLibraryAlbums.Group(catalog.Songs).Where(a=>a.Songs.Any(s=>MusicLibraryPlayback.IsRequestedRecording(original,s.Track))).ToArray();
            if(albums.Length!=1)throw new InvalidDataException("The original recording does not identify a unique native album.");
            await coordinator.Source.StartAsync(timeout.Token);var restoreBridge=new AppleMusicPlaybackBridge(coordinator,Trace);
            var played=await restoreBridge.PlayAlbumAsync(albums[0],timeout.Token);if(!played.Confirmed)throw new InvalidOperationException("The original album could not be confirmed.");
            var queue=await restoreBridge.ReadQueueAsync(timeout.Token);
            if(queue.Current is null || !MusicLibraryPlayback.IsRequestedRecording(original,queue.Current))
            {
                var indexes=queue.Upcoming.Select((entry,index)=>(entry,index)).Where(p=>MusicLibraryPlayback.IsRequestedRecording(original,p.entry.Track) && queue.Upcoming.Skip(p.index+1).SequenceEqual(baseline.Queue.Upcoming)).Select(p=>p.index).ToArray();
                if(indexes.Length!=1)throw new InvalidDataException("The original recording and remaining sequence could not be uniquely restored.");
                var restored=await restoreBridge.PlayQueueAsync(new(queue,indexes[0]),timeout.Token);if(!restored.Confirmed)throw new InvalidOperationException("The original queue position could not be confirmed.");
            }
            bool state=baseline.State==LumaLyrics.Core.PlayState.Playing || await coordinator.Source.ControlAsync("pause");await Task.Delay(300,timeout.Token);await coordinator.Source.RefreshAsync();
            var after=await restoreBridge.ReadQueueAsync(timeout.Token);Trace("queueBaselineRestored",new{recordingRestored=after.Current is not null && MusicLibraryPlayback.IsRequestedRecording(original,after.Current),remainingQueueRestored=baseline.Queue.Upcoming.SequenceEqual(after.Upcoming),playStateRestored=state && coordinator.Source.Latest?.Status==baseline.State});return;
        }
        string title=Argument("--native-song")??throw new ArgumentException("Specify --native-song and --native-artist for a real playback check.");
        string artist=Argument("--native-artist")??throw new ArgumentException("Specify --native-artist.");
        string? album=Argument("--native-album");
        var matches=catalog.Songs.Where(s=>s.Title==title && s.Artist==artist && (album is null || s.Album==album)).ToArray();
        if(matches.Length!=1)throw new InvalidDataException("Native playback verification requires one exact library recording.");
        Trace("requested",matches[0]);
        await coordinator.Source.StartAsync(timeout.Token);
        await Task.Delay(150,timeout.Token);
        TimeSpan? latency=int.TryParse(Argument("--native-confirmation-delay-ms"),out int delay) && delay is >0 and <=30000?TimeSpan.FromMilliseconds(delay):null;
        var bridge=new AppleMusicPlaybackBridge(coordinator,Trace,latency);
        if(args.Contains("--native-insert-next"))
        {
            var before=coordinator.Snapshot;var baseline=await bridge.ReadQueueAsync(timeout.Token);Trace("queueBefore",baseline);
            var inserted=await bridge.InsertNextAsync(matches[0],timeout.Token);
            await coordinator.Source.RefreshAsync();await Task.Delay(100,timeout.Token);
            Trace("insertResult",new{inserted,currentPreserved=before is not null && coordinator.Snapshot is { } actual && MusicLibraryPlayback.IsRequestedRecording(before.Track,actual.Track),before,actual=coordinator.Snapshot});
            var after=await bridge.ReadQueueAsync(timeout.Token);Trace("queueAfter",after);
            bool repeatedConfirmed=false;
            if(args.Contains("--native-repeat-insert"))
            {
                var repeated=await bridge.InsertNextAsync(matches[0],timeout.Token);after=await bridge.ReadQueueAsync(timeout.Token);
                repeatedConfirmed=inserted.Confirmed && repeated.Confirmed;
                Trace("repeatInsertResult",new{repeated,after});
            }
            if(args.Contains("--native-play-second-insert"))
            {
                if(!repeatedConfirmed || after.Upcoming.Count<2 || !after.Upcoming.Take(2).All(e=>e.Inserted && MusicLibraryPlayback.IsRequestedRecording(matches[0].Track,e.Track)))
                    throw new InvalidOperationException("Both exact repeated insertions must be confirmed before testing the second occurrence.");
                var selection=new MusicQueueSelection(after,1);var jumpResult=await bridge.PlayQueueAsync(selection,timeout.Token);var remaining=await bridge.ReadQueueAsync(timeout.Token);
                Trace("repeatedQueueJumpResult",new{result=jumpResult,secondOccurrenceConfirmed=MusicPlaybackQueue.ConfirmsJump(selection,remaining),originalRemainingQueueRetained=baseline.Upcoming.SequenceEqual(remaining.Upcoming),actual=coordinator.Source.Latest,remaining});
                if(jumpResult.Confirmed && args.Contains("--native-pause-after")){bool paused=await coordinator.Source.ControlAsync("pause");await Task.Delay(300,timeout.Token);Trace("paused",new{paused,actual=coordinator.Source.Latest});}
            }
            if(args.Contains("--native-restore-after"))Trace("queueRestored",new{restored=await AppleMusicLibraryHandoff.RestoreQueueVerificationAsync(baseline,after,timeout.Token)});
            return;
        }
        if(args.Contains("--native-play-album"))
        {
            var group=MusicLibraryAlbums.Group(catalog.Songs).Single(a=>a.Songs.Contains(matches[0]));
            Trace("albumRequest",new{group.Title,group.Artist,tracks=group.Songs.Count});
            var albumResult=await bridge.PlayAlbumAsync(group,timeout.Token);Trace("albumResult",new{albumResult,actual=coordinator.Snapshot});
            Trace("albumQueue",await bridge.ReadQueueAsync(timeout.Token));
            if(albumResult.Confirmed && args.Contains("--native-pause-after")){bool paused=await coordinator.Source.ControlAsync("pause");await Task.Delay(300,timeout.Token);Trace("paused",new{paused,actual=coordinator.Source.Latest});}
            if(albumResult.Confirmed && args.Contains("--native-check-next"))
            {
                bool next=await coordinator.Source.ControlAsync("next");await Task.Delay(1500,timeout.Token);await coordinator.Source.RefreshAsync();await Task.Delay(100,timeout.Token);
                Trace("albumNext",new{requested=next,member=coordinator.Snapshot is { } actual && MusicLibraryPlayback.IsRequestedAlbum(group,actual.Track),actual=coordinator.Snapshot});
            }
            return;
        }
        var result=await bridge.PlayAsync(matches[0],timeout.Token);
        Trace("result",new{result,actual=coordinator.Snapshot});
        if(result.Confirmed && args.Contains("--native-pause-after")){bool paused=await coordinator.Source.ControlAsync("pause");await Task.Delay(400,timeout.Token);Trace("paused",new{paused,actual=coordinator.Snapshot});}
        Trace("final",await Task.Run(AppleMusicLibraryHandoff.Inspect,timeout.Token).WaitAsync(TimeSpan.FromSeconds(12),timeout.Token));
    }
}
