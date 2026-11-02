using LumaLyrics.Windows;
if(args.ElementAtOrDefault(0)=="--probe-native-memory-safety")
{
    await LumaLyrics.Probe.NativeMemorySafetyProbe.RunAsync(args.ElementAtOrDefault(1)??throw new ArgumentException("Specify an external private report directory."));return;
}
if(LumaLyrics.Probe.PersonalLibraryCommands.TryRun(args))return;
if(args.ElementAtOrDefault(0)=="--reuse-translation-drafts")
{
    var root=LumaLyrics.Infrastructure.DataDirectoryConfiguration.Resolve(args.ElementAtOrDefault(1));
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(LumaLyrics.Infrastructure.TranslationDraftReuse.Run(root)));return;
}
if(args.ElementAtOrDefault(0)=="--reconcile-translations")
{
    var root=LumaLyrics.Infrastructure.DataDirectoryConfiguration.Resolve(args.ElementAtOrDefault(1));
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(LumaLyrics.Infrastructure.TranslationCacheReconciliation.Run(root)));return;
}
if(args.ElementAtOrDefault(0)=="--audit-lyrics")
{
    var root=LumaLyrics.Infrastructure.DataDirectoryConfiguration.Resolve(args.ElementAtOrDefault(1));string output=args.ElementAtOrDefault(2)??throw new ArgumentException("Specify a private coverage report file");
    var snapshot=new LumaLyrics.Infrastructure.AppleMusicLibraryReader().Read(LumaLyrics.Infrastructure.AppleMusicLibraryReader.Discover()??throw new FileNotFoundException("No native library"));
    var report=LumaLyrics.Infrastructure.LibraryCoverageAudit.Create(root,snapshot);LumaLyrics.Infrastructure.LibraryCoverageAudit.Write(report,output);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{report.Total,report.Timed,report.Instrumental,report.MissingOrUnconfirmed,report.ForeignTimed,report.CompleteTranslations,report.AiDrafts,report.MissingOrPartialTranslations}));return;
}
if(args.ElementAtOrDefault(0) is "--export-translation-drafts" or "--import-translation-drafts")
{
    var root=LumaLyrics.Infrastructure.DataDirectoryConfiguration.Resolve(args.ElementAtOrDefault(1));
    var file=args.ElementAtOrDefault(2)??throw new ArgumentException("Specify a personal draft JSON file");
    int count=args[0]=="--export-translation-drafts"?LumaLyrics.Infrastructure.TranslationDraftExchange.Export(root,file):LumaLyrics.Infrastructure.TranslationDraftExchange.Import(root,file);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{tracks=count,action=args[0]}));return;
}
if(args.ElementAtOrDefault(0)=="--translations")
{
    var root=LumaLyrics.Infrastructure.DataDirectoryConfiguration.Resolve(args.ElementAtOrDefault(1));Directory.CreateDirectory(root);
    using var provider=new LumaLyrics.Infrastructure.NetEaseTranslationProvider();var worker=new LumaLyrics.Infrastructure.TranslationPreparation(root,provider);
    using var cancel=new CancellationTokenSource();Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;cancel.Cancel();};
    worker.Progress+=p=>Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(p));
    int? limit=int.TryParse(args.ElementAtOrDefault(2),out var maximum)?maximum:null;
    File.Delete(LumaLyrics.Infrastructure.TranslationPreparation.PausePath(root));
    await worker.RunAsync(LumaLyrics.Infrastructure.AppleMusicLibraryReader.Discover()??throw new FileNotFoundException("No Apple Music library"),cancel.Token,limit,args.Contains("--retry"));return;
}
if(args.ElementAtOrDefault(0)=="--asset-status")
{
    var root=LumaLyrics.Infrastructure.DataDirectoryConfiguration.Resolve(args.ElementAtOrDefault(1));
    using var lyrics=new LumaLyrics.Infrastructure.LyricsRepository(Path.Combine(root,"lyrics.db"));using var artwork=new LumaLyrics.Infrastructure.ArtworkCache(root);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{lyrics=lyrics.GetLibraryProgress(running:LumaLyrics.Infrastructure.LibraryPrefetcher.IsRunning(Path.Combine(root,"lyrics.db"))),artwork=artwork.GetProgress(running:LumaLyrics.Infrastructure.LibraryArtworkImporter.IsRunning(root))}));return;
}
if(args.ElementAtOrDefault(0)=="--artwork")
{
    var root=LumaLyrics.Infrastructure.DataDirectoryConfiguration.Resolve(args.ElementAtOrDefault(1));Directory.CreateDirectory(root);
    using var worker=new LumaLyrics.Infrastructure.LibraryArtworkImporter(root);using var cancel=new CancellationTokenSource();Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;cancel.Cancel();};
    worker.Progress+=p=>Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(p));
    if(args.Contains("--retry")){using var cache=new LumaLyrics.Infrastructure.ArtworkCache(root);cache.ResetRetries();}
    File.Delete(LumaLyrics.Infrastructure.LibraryPrefetcher.PausePath(Path.Combine(root,"lyrics.db")));
    int? limit=int.TryParse(args.ElementAtOrDefault(2),out var max)?max:null;
    await worker.RunAsync(LumaLyrics.Infrastructure.AppleMusicLibraryReader.Discover()??throw new FileNotFoundException("No library"),cancel.Token,limit);return;
}
if(args.ElementAtOrDefault(0)=="--library-status")
{
    var root=LumaLyrics.Infrastructure.DataDirectoryConfiguration.Resolve(args.ElementAtOrDefault(1));
    using var db=new LumaLyrics.Infrastructure.LyricsRepository(Path.Combine(root,"lyrics.db"));Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(db.GetLibraryProgress(running:LumaLyrics.Infrastructure.LibraryPrefetcher.IsRunning(Path.Combine(root,"lyrics.db")))));return;
}
if(args.ElementAtOrDefault(0)=="--prefetch")
{
    var root=LumaLyrics.Infrastructure.DataDirectoryConfiguration.Resolve(args.ElementAtOrDefault(1));Directory.CreateDirectory(root);
    using var provider=new LumaLyrics.Infrastructure.ResilientLyricsProvider();
    LumaLyrics.Core.ILyricsProvider lookupProvider=provider;
    var prefetcher=new LumaLyrics.Infrastructure.LibraryPrefetcher(Path.Combine(root,"lyrics.db"),lookupProvider);
    prefetcher.Progress+=p=>Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(p));
    using var cancel=new CancellationTokenSource();Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;cancel.Cancel();};
    int? limit=int.TryParse(args.ElementAtOrDefault(2),out var max)?max:null;
    var db=Path.Combine(root,"lyrics.db");File.Delete(LumaLyrics.Infrastructure.LibraryPrefetcher.PausePath(db));
    if(args.Contains("--retry")){using var repository=new LumaLyrics.Infrastructure.LyricsRepository(db);repository.ResetLibraryRetries();}
    do
    {
        await prefetcher.RunAsync(LumaLyrics.Infrastructure.AppleMusicLibraryReader.Discover()??throw new FileNotFoundException("No library"),cancel.Token,limit);
        if(cancel.IsCancellationRequested||limit.HasValue||args.Contains("--once")||File.Exists(LumaLyrics.Infrastructure.LibraryPrefetcher.PausePath(db)))break;
        if(args.Contains("--drain"))
        {
            using var repository=new LumaLyrics.Infrastructure.LyricsRepository(db);if(repository.GetLibraryProgress().Pending==0)break;
            try{await Task.Delay(30000,cancel.Token);}catch(OperationCanceledException){break;}continue;
        }
        try{for(int i=0;i<300&&!File.Exists(LumaLyrics.Infrastructure.LibraryPrefetcher.PausePath(db));i++)await Task.Delay(1000,cancel.Token);}catch(OperationCanceledException){break;}
    }while(!cancel.IsCancellationRequested&&!File.Exists(LumaLyrics.Infrastructure.LibraryPrefetcher.PausePath(db)));return;
}
if(args.ElementAtOrDefault(0)=="--library")
{
    var reader=new LumaLyrics.Infrastructure.AppleMusicLibraryReader();
    var library=reader.Read(args.ElementAtOrDefault(1)??LumaLyrics.Infrastructure.AppleMusicLibraryReader.Discover()??throw new FileNotFoundException("No library found"));
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{declared=library.DeclaredCount,read=library.Tracks.Count,stored=library.StoredTrackCount,timed=library.Tracks.Count(x=>x.Track.DurationMs.HasValue),blankTitles=library.Tracks.Count(x=>x.Track.Title.Length==0)}));return;
}
if(args.ElementAtOrDefault(0)=="--lookup")
{
    using var provider=new LumaLyrics.Infrastructure.ResilientLyricsProvider();
    var track=new LumaLyrics.Core.Track(args.ElementAtOrDefault(1)??"",args.ElementAtOrDefault(2)??"",args.ElementAtOrDefault(3)??"",double.TryParse(args.ElementAtOrDefault(4),out var duration)?duration*1000:null);
    var found=await provider.LookupAsync(track,default);
    var selection=LumaLyrics.Core.AutomaticLyricsMatcher.Select(track,found);
    int save=Array.IndexOf(args,"--save-root");
    if(save>=0)
    {
        if(save+1>=args.Length)throw new ArgumentException("Specify a private profile directory");
        var root=LumaLyrics.Infrastructure.DataDirectoryConfiguration.Resolve(args[save+1]);LumaLyrics.Infrastructure.PersonalLibraryArchive.EnsureOutsideSoftware(root);
        if(selection is not null){using var repository=new LumaLyrics.Infrastructure.LyricsRepository(Path.Combine(root,"lyrics.db"));repository.Save(track,selection.Candidate.Content,selection.Candidate.SourceLabel+" · 自动检查",false,selection.Candidate.Instrumental,providerJson:selection.Candidate.RawJson);}
    }
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{count=found.Count,synced=found.Count(c=>c.SyncedLyrics is not null),automatic=selection?.Candidate.Id,assessment=selection?.Assessment,rejected=found.Select(c=>LumaLyrics.Core.AutomaticLyricsMatcher.Assess(c).Reason).Distinct()}));return;
}
var path = args.ElementAtOrDefault(0) is null or "--probe"?Path.Combine(LumaLyrics.Infrastructure.DataDirectoryConfiguration.Resolve(),"logs","probe-redacted.jsonl"):args[0];
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
var seconds = int.TryParse(args.ElementAtOrDefault(1), out var n) ? n : 60;
Console.WriteLine($"Capturing GSMTC for {seconds}s to {Path.GetFullPath(path)}. Metadata redacted unless --metadata is supplied.");
await MediaProbe.RunAsync(path, seconds, args.Contains("--metadata"));
Console.WriteLine("Capture complete.");
