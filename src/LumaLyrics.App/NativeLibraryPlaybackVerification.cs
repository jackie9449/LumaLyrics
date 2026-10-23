using System.IO;
using System.Text.Json;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

public partial class MainWindow
{
    sealed record QueueRestoreVerification(MusicQueueSnapshot Baseline,MusicQueueSnapshot Expected);
    // Explicit integration mode only. Reports contain private library metadata and must
    // stay in the isolated directory selected by the caller, never in software docs.
    async Task VerifyNativeLibraryPlaybackAsync(string directory,string[] args)
    {
        Directory.CreateDirectory(directory);string traceFile=Path.Combine(directory,"native-playback.jsonl");var traceGate=new object();
        void Trace(string stage,object? value){lock(traceGate)File.AppendAllText(traceFile,JsonSerializer.Serialize(new{utc=DateTimeOffset.UtcNow,stage,value})+Environment.NewLine);}
        string? Argument(string name){int index=Array.IndexOf(args,name);return index>=0 && index+1<args.Length?args[index+1]:null;}
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(50));
        Trace("initial",await Task.Run(AppleMusicLibraryHandoff.Inspect,timeout.Token).WaitAsync(TimeSpan.FromSeconds(12),timeout.Token));
        if(args.Contains("--native-inspect-queue")){Trace("queue",await AppleMusicLibraryHandoff.InspectQueueAsync(timeout.Token));return;}
        if(args.Contains("--native-read-queue")){Trace("queueRead",await AppleMusicLibraryHandoff.ReadQueueAsync(timeout.Token,Trace));return;}
        if(args.Contains("--native-verify-queue-state")){Trace("queueState",await AppleMusicLibraryHandoff.VerifyQueueStateAsync(timeout.Token));return;}
        if(args.Contains("--native-restore-queue-check"))
        {
            var request=JsonSerializer.Deserialize<QueueRestoreVerification>(File.ReadAllText(Path.Combine(directory,"queue-restore-input.json")))??throw new InvalidDataException("Missing exact native queue baseline.");
            Trace("queueRestored",new{restored=await AppleMusicLibraryHandoff.RestoreQueueVerificationAsync(request.Baseline,request.Expected,timeout.Token)});return;
        }
        if(args.Contains("--native-inspect-only"))return;
        var catalog=await Task.Run(()=>new MusicLibraryCatalog(store.Root,settings.LibraryPath).Load(timeout.Token),timeout.Token);
        string title=Argument("--native-song")??throw new ArgumentException("Specify --native-song and --native-artist for a real playback check.");
        string artist=Argument("--native-artist")??throw new ArgumentException("Specify --native-artist.");
        string? album=Argument("--native-album");
        var matches=catalog.Songs.Where(s=>s.Title==title && s.Artist==artist && (album is null || s.Album==album)).ToArray();
        if(matches.Length!=1)throw new InvalidDataException("Native playback verification requires one exact library recording.");
        Trace("requested",matches[0]);
        await coordinator.Source.StartAsync(timeout.Token);
        await Task.Delay(150,timeout.Token);
        var bridge=new AppleMusicPlaybackBridge(coordinator,Trace);
        if(args.Contains("--native-insert-next"))
        {
            var before=coordinator.Snapshot;var baseline=await bridge.ReadQueueAsync(timeout.Token);Trace("queueBefore",baseline);
            var inserted=await bridge.InsertNextAsync(matches[0],timeout.Token);
            await coordinator.Source.RefreshAsync();await Task.Delay(100,timeout.Token);
            Trace("insertResult",new{inserted,currentPreserved=before is not null && coordinator.Snapshot is { } actual && MusicLibraryPlayback.IsRequestedRecording(before.Track,actual.Track),before,actual=coordinator.Snapshot});
            var after=await bridge.ReadQueueAsync(timeout.Token);Trace("queueAfter",after);
            if(args.Contains("--native-repeat-insert"))
            {
                var repeated=await bridge.InsertNextAsync(matches[0],timeout.Token);after=await bridge.ReadQueueAsync(timeout.Token);
                Trace("repeatInsertResult",new{repeated,after});
            }
            if(args.Contains("--native-restore-after"))Trace("queueRestored",new{restored=await AppleMusicLibraryHandoff.RestoreQueueVerificationAsync(baseline,after,timeout.Token)});
            return;
        }
        if(args.Contains("--native-play-album"))
        {
            var group=MusicLibraryAlbums.Group(catalog.Songs).Single(a=>a.Songs.Contains(matches[0]));
            Trace("albumRequest",new{group.Title,group.Artist,tracks=group.Songs.Count});
            var albumResult=await bridge.PlayAlbumAsync(group,timeout.Token);Trace("albumResult",new{albumResult,actual=coordinator.Snapshot});
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
