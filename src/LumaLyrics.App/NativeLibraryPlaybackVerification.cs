using System.IO;
using System.Text.Json;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

public partial class MainWindow
{
    // Explicit integration mode only. Reports contain private library metadata and must
    // stay in the isolated directory selected by the caller, never in software docs.
    async Task VerifyNativeLibraryPlaybackAsync(string directory,string[] args)
    {
        Directory.CreateDirectory(directory);string traceFile=Path.Combine(directory,"native-playback.jsonl");var traceGate=new object();
        void Trace(string stage,object? value){lock(traceGate)File.AppendAllText(traceFile,JsonSerializer.Serialize(new{utc=DateTimeOffset.UtcNow,stage,value})+Environment.NewLine);}
        string? Argument(string name){int index=Array.IndexOf(args,name);return index>=0 && index+1<args.Length?args[index+1]:null;}
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(50));
        Trace("initial",await Task.Run(AppleMusicLibraryHandoff.Inspect,timeout.Token).WaitAsync(TimeSpan.FromSeconds(12),timeout.Token));
        if(args.Contains("--native-inspect-only"))return;
        var catalog=await Task.Run(()=>new MusicLibraryCatalog(store.Root,settings.LibraryPath).Load(timeout.Token),timeout.Token);
        string title=Argument("--native-song")??throw new ArgumentException("Specify --native-song and --native-artist for a real playback check.");
        string artist=Argument("--native-artist")??throw new ArgumentException("Specify --native-artist.");
        string? album=Argument("--native-album");
        var matches=catalog.Songs.Where(s=>s.Title==title && s.Artist==artist && (album is null || s.Album==album)).ToArray();
        if(matches.Length!=1)throw new InvalidDataException("Native playback verification requires one exact library recording.");
        Trace("requested",matches[0]);
        await coordinator.Source.StartAsync(timeout.Token);
        var bridge=new AppleMusicPlaybackBridge(coordinator,Trace);
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
