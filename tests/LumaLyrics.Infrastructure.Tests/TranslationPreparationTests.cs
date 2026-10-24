using LumaLyrics.Core;
using Microsoft.Data.Sqlite;
namespace LumaLyrics.Infrastructure.Tests;
public class TranslationPreparationTests : IDisposable
{
    readonly string root=Path.Combine(Path.GetTempPath(),"Luma-translation-preparation-tests-"+Guid.NewGuid().ToString("N"));
    static readonly Track Track=new("Own song","Own artist","Own album",10000);
    const string Original="[00:01]We sail home\n[00:04]Across the quiet bay\n[00:08]The stars will guide us";
    const string Translated="[00:01]我们驶向家乡\n[00:04]穿过宁静海湾\n[00:08]星光为我们引路";
    static LibrarySnapshot Snapshot(params Track[] tracks)=>new("own-library",tracks.Length,tracks.Select((t,i)=>new LibraryTrack(i.ToString(),t)).ToArray(),"own");
    sealed class Provider(Func<Track,CancellationToken,Task<IReadOnlyList<TranslationCandidate>>> lookup):ITranslationProvider{public Task<IReadOnlyList<TranslationCandidate>> LookupAsync(Track t,CancellationToken c)=>lookup(t,c);}
    static ITranslationProvider Found()=>new Provider((t,c)=>Task.FromResult<IReadOnlyList<TranslationCandidate>>([new(42,Track,Original,Translated,"https://example.org/own")]));
    public TranslationPreparationTests()=>Directory.CreateDirectory(root);
    [Fact] public async Task ImportsOnlyCompleteCheckedPairAndResumesWithoutRepeatingRequests()
    {
        using var lyrics=new LyricsRepository(Path.Combine(root,"lyrics.db"));using var translations=new TranslationRepository(root);int count=0;
        var provider=new Provider((t,c)=>{count++;return Found().LookupAsync(t,c);});var worker=new TranslationPreparation(root,provider);
        var snapshot=Snapshot(Track);lyrics.SyncLibrary(snapshot);await worker.PrepareAsync(snapshot,lyrics,translations,default);
        var binding=lyrics.Find(Track)!;Assert.False(binding.Manual);Assert.True(LibraryPrefetcher.QualityOf(binding,Track));Assert.NotNull(translations.Find(Track,new LrcParser().Parse(binding.Raw)));Assert.Equal(1,translations.Progress().Prepared);
        await worker.PrepareAsync(snapshot,lyrics,translations,default);Assert.Equal(1,count);
    }
    [Fact] public async Task KeepsManualOriginalItsOffsetAndItsOwnTimeline()
    {
        using var lyrics=new LyricsRepository(Path.Combine(root,"lyrics.db"));using var translations=new TranslationRepository(root);
        string local="[00:02]We sail home\n[00:05]Across the quiet bay\n[00:09]The stars will guide us";
        var binding=lyrics.Save(Track,local,"own manual",true);lyrics.SetOffset(binding.Id,450);await new TranslationPreparation(root,Found()).PrepareAsync(Snapshot(Track),lyrics,translations,default);
        var current=lyrics.Find(Track)!;Assert.Equal(local,current.Raw);Assert.Equal(450,current.OffsetMs);Assert.True(current.Manual);Assert.NotNull(translations.Find(Track,new LrcParser().Parse(local)));
    }
    [Fact] public async Task RejectsWrongOriginalAndMissingTranslationWithoutLosingManualSource()
    {
        using var lyrics=new LyricsRepository(Path.Combine(root,"lyrics.db"));using var translations=new TranslationRepository(root);lyrics.Save(Track,Original.Replace("home","away"),"manual",true);
        await new TranslationPreparation(root,Found()).PrepareAsync(Snapshot(Track),lyrics,translations,default);Assert.Empty(translations.ReadAll());Assert.Equal(1,translations.Progress().Mismatch);Assert.Contains("away",lyrics.Find(Track)!.Raw);
    }
    [Fact] public async Task ConfirmedInstrumentalsAndChineseNeedNoNetwork()
    {
        using var lyrics=new LyricsRepository(Path.Combine(root,"lyrics.db"));using var translations=new TranslationRepository(root);var chinese=Track with{Title="中文原创"};var instrumental=Track with{Title="Own instrumental"};
        lyrics.Save(chinese,"[00:01]我们驶向家乡\n[00:04]穿过宁静海湾\n[00:08]星光为我们引路","own",true);lyrics.Save(instrumental,"","own",true,true);
        var provider=new Provider((t,c)=>throw new Exception("Should not request"));await new TranslationPreparation(root,provider).PrepareAsync(Snapshot(chinese,instrumental),lyrics,translations,default);
        Assert.Equal(1,translations.Progress().Chinese);Assert.Equal(1,translations.Progress().Instrumental);Assert.Empty(translations.ReadAll());
    }
    [Fact] public async Task AbsentLyricsNeverBecomeInstrumental()
    {
        using var lyrics=new LyricsRepository(Path.Combine(root,"lyrics.db"));using var translations=new TranslationRepository(root);
        var provider=new Provider((t,c)=>Task.FromResult<IReadOnlyList<TranslationCandidate>>([]));await new TranslationPreparation(root,provider).PrepareAsync(Snapshot(Track),lyrics,translations,default);
        Assert.Null(lyrics.Find(Track));Assert.Equal(1,translations.Progress().MissingOriginal);Assert.Equal(0,translations.Progress().Instrumental);
    }
    [Fact] public async Task ExistingLeasePreventsCompetingWorkerWithoutReadingLibrary()
    {
        using var lease=new FileStream(Path.Combine(root,"translation-preparation.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        await new TranslationPreparation(root,Found()).RunAsync("nonexistent-own-library",default);Assert.False(File.Exists(Path.Combine(root,"translations.db")));
    }
    [Fact] public async Task TemporaryProgressReaderDoesNotAbortPreparationOrReplaceIncompleteJson()
    {
        if(!OperatingSystem.IsWindows())return;
        using var lyrics=new LyricsRepository(Path.Combine(root,"lyrics.db"));using var translations=new TranslationRepository(root);
        string path=TranslationPreparation.ProgressPath(root),previous="{\"ownPreviousSnapshot\":true}";File.WriteAllText(path,previous);
        var created=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher=new FileSystemWatcher(root,"translation-preparation-progress.json*.tmp");
        watcher.Created+=(_,_)=>created.TrySetResult();watcher.EnableRaisingEvents=true;
        var reader=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        Task preparation=Task.CompletedTask;
        try
        {
            preparation=Task.Run(()=>new TranslationPreparation(root,Found()).PrepareAsync(Snapshot(Track),lyrics,translations,default));
            await created.Task.WaitAsync(TimeSpan.FromSeconds(3));await Task.Delay(40);
            Assert.False(preparation.IsCompleted);Assert.Equal(previous,File.ReadAllText(path));
        }
        finally{reader.Dispose();await preparation.WaitAsync(TimeSpan.FromSeconds(5));}
        var progress=System.Text.Json.JsonSerializer.Deserialize<TranslationProgress>(File.ReadAllText(path));
        Assert.NotNull(progress);Assert.False(progress.Running);Assert.Equal(1,progress.Prepared);
        Assert.NotNull(translations.Find(Track,new LrcParser().Parse(Original)));
        Assert.Empty(Directory.GetFiles(root,"translation-preparation-progress.json*.tmp"));
    }
    [Fact] public async Task StopsAfterFourProviderErrorsAndKeepsAResumableQueue()
    {
        using var lyrics=new LyricsRepository(Path.Combine(root,"lyrics.db"));using var translations=new TranslationRepository(root);int requests=0;
        var provider=new Provider((t,c)=>{requests++;throw new LyricsProviderException("Own service failure");});var snapshot=Snapshot(Enumerable.Range(0,6).Select(i=>Track with{Title="Own "+i}).ToArray());
        await new TranslationPreparation(root,provider).PrepareAsync(snapshot,lyrics,translations,default);Assert.Equal(4,requests);Assert.Equal(4,translations.Progress().Errors);Assert.Equal(2,translations.Progress().Pending);
    }
    public void Dispose(){SqliteConnection.ClearAllPools();if(Directory.Exists(root))Directory.Delete(root,true);}
}
