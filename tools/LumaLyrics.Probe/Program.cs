using LumaLyrics.Windows;
if(args.ElementAtOrDefault(0)=="--library-status")
{
    var root=args.ElementAtOrDefault(1)??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LumaLyrics");
    using var db=new LumaLyrics.Infrastructure.LyricsRepository(Path.Combine(root,"lyrics.db"));Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(db.GetLibraryProgress()));return;
}
if(args.ElementAtOrDefault(0)=="--prefetch")
{
    var root=args.ElementAtOrDefault(1)??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LumaLyrics");Directory.CreateDirectory(root);
    using var provider=new LumaLyrics.Infrastructure.LrclibProvider();
    var prefetcher=new LumaLyrics.Infrastructure.LibraryPrefetcher(Path.Combine(root,"lyrics.db"),provider);
    prefetcher.Progress+=p=>Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(p));
    using var cancel=new CancellationTokenSource();Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;cancel.Cancel();};
    int? limit=int.TryParse(args.ElementAtOrDefault(2),out var max)?max:null;
    var db=Path.Combine(root,"lyrics.db");File.Delete(LumaLyrics.Infrastructure.LibraryPrefetcher.PausePath(db));
    do
    {
        await prefetcher.RunAsync(LumaLyrics.Infrastructure.AppleMusicLibraryReader.Discover()??throw new FileNotFoundException("No library"),cancel.Token,limit);
        if(limit.HasValue||File.Exists(LumaLyrics.Infrastructure.LibraryPrefetcher.PausePath(db)))break;
        try{for(int i=0;i<300&&!File.Exists(LumaLyrics.Infrastructure.LibraryPrefetcher.PausePath(db));i++)await Task.Delay(1000,cancel.Token);}catch(OperationCanceledException){break;}
    }while(!cancel.IsCancellationRequested&&!File.Exists(LumaLyrics.Infrastructure.LibraryPrefetcher.PausePath(db)));return;
}
if(args.ElementAtOrDefault(0)=="--library")
{
    var reader=new LumaLyrics.Infrastructure.AppleMusicLibraryReader();
    var library=reader.Read(args.ElementAtOrDefault(1)??LumaLyrics.Infrastructure.AppleMusicLibraryReader.Discover()??throw new FileNotFoundException("No library found"));
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{declared=library.DeclaredCount,read=library.Tracks.Count,timed=library.Tracks.Count(x=>x.Track.DurationMs.HasValue),blankTitles=library.Tracks.Count(x=>x.Track.Title.Length==0)}));return;
}
if(args.ElementAtOrDefault(0)=="--lookup")
{
    using var provider=new LumaLyrics.Infrastructure.LrclibProvider();
    var track=new LumaLyrics.Core.Track(args.ElementAtOrDefault(1)??"",args.ElementAtOrDefault(2)??"",args.ElementAtOrDefault(3)??"",double.TryParse(args.ElementAtOrDefault(4),out var duration)?duration*1000:null);
    var found=await provider.LookupAsync(track,default);
    var selection=LumaLyrics.Core.AutomaticLyricsMatcher.Select(track,found);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{count=found.Count,synced=found.Count(c=>c.SyncedLyrics is not null),automatic=selection?.Candidate.Id,assessment=selection?.Assessment,rejected=found.Select(c=>LumaLyrics.Core.AutomaticLyricsMatcher.Assess(c).Reason).Distinct()}));return;
}
var path = args.ElementAtOrDefault(0) ?? "probe-redacted.jsonl";
var seconds = int.TryParse(args.ElementAtOrDefault(1), out var n) ? n : 60;
Console.WriteLine($"Capturing GSMTC for {seconds}s to {Path.GetFullPath(path)}. Metadata redacted unless --metadata is supplied.");
await MediaProbe.RunAsync(path, seconds, args.Contains("--metadata"));
Console.WriteLine("Capture complete.");
