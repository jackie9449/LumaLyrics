using System.IO.Compression;
using System.Text.Json;
using LumaLyrics.Core;
using Microsoft.Data.Sqlite;
namespace LumaLyrics.Infrastructure.Tests;

public sealed class OfflineTranslationTests:IDisposable
{
    readonly string root=Path.Combine(Path.GetTempPath(),"Luma-offline-"+Guid.NewGuid().ToString("N"));
    static readonly Track Song=new("Own offline song","Own artist","Own album",10000);
    const string Raw="[00:01]We sail home\n[00:03]\n[00:04]Across the quiet bay\n[00:08]We sail home";
    OfflineTranslationWorkflow Flow=>new(root);
    public OfflineTranslationTests(){Directory.CreateDirectory(root);using var lyrics=new LyricsRepository(Path.Combine(root,"lyrics.db"));lyrics.SyncLibrary(new("own",1,[new("1",Song)],"own"));lyrics.Save(Song,Raw,"Own source",true);using var translations=new TranslationRepository(root);}
    sealed class Engine(Func<IReadOnlyList<string>,CancellationToken,Task<IReadOnlyList<string>>>? run=null):IOfflineTranslationEngine
    {
        public int Calls;public IReadOnlyList<string>? Input;public string? Language;
        public Task<IReadOnlyList<string>> TranslateAsync(IReadOnlyList<string> texts,string sourceLanguage,CancellationToken token){Calls++;Input=texts;Language=sourceLanguage;return run?.Invoke(texts,token)??Task.FromResult<IReadOnlyList<string>>(texts.Select(_=>"我们驶向家乡").ToArray());}
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
    ChineseTranslation? Read(){using var repo=new TranslationRepository(root,true);return repo.Find(Song,new LrcParser().Parse(Raw));}
    void SaveOther(bool manual=false){using var repo=new TranslationRepository(root);repo.Save(Song,new(TranslationAlignment.Hash(Raw),[new(0,"已有中文")],"Own fixture","own","Own review",DateTimeOffset.UtcNow),manual);}
    [Fact] public async Task PreservesOriginalHashTimingBlankAndRepeatedGroups()
    {
        var item=Flow.Capture(Song);await using var engine=new Engine();Assert.True(await Flow.TranslateAsync(item,engine,default));
        Assert.Equal(new[]{"We sail home","Across the quiet bay","We sail home"},engine.Input);
        var result=Read()!;Assert.True(OfflineTranslationWorkflow.IsMachine(result));Assert.True(result.Draft);Assert.Equal(item.SourceHash,result.SourceHash);
        Assert.Equal(new[]{0,2,3},result.Lines.Select(l=>l.GroupIndex));Assert.Equal("",result.TextAt(1));
        Assert.False(LibraryWorkspaceProjection.Assess(new(new LrcParser().Parse(Raw),true,true,result,false,false,null,LibraryCoverState.Missing)).AiDraft);
        using var lyrics=new LyricsRepository(Path.Combine(root,"lyrics.db"),true);Assert.Equal(Raw,lyrics.FindReadOnly(Song)!.Raw);
    }
    [Theory][InlineData(false)][InlineData(true)] public async Task ExistingTranslationsDoNotInvokeEngine(bool manual)
    {
        SaveOther(manual);await using var engine=new Engine();Assert.False(await Flow.TranslateAsync(Flow.Capture(Song),engine,default));Assert.Equal(0,engine.Calls);Assert.Equal("已有中文",Read()!.TextAt(0));
    }
    [Fact] public async Task ConcurrentManualResultIsNeverOverwritten()
    {
        await using var engine=new Engine((texts,_)=>{SaveOther(true);return Task.FromResult<IReadOnlyList<string>>(texts.Select(_=>"迟到机翻").ToArray());});
        Assert.False(await Flow.TranslateAsync(Flow.Capture(Song),engine,default));Assert.Equal("已有中文",Read()!.TextAt(0));
    }
    [Fact] public async Task ChangedOriginalDuringInferenceCannotBeSaved()
    {
        await using var engine=new Engine((texts,_)=>{using var lyrics=new LyricsRepository(Path.Combine(root,"lyrics.db"));lyrics.Save(Song,Raw+"\n[00:09]Changed original","Own",true);return Task.FromResult<IReadOnlyList<string>>(texts.Select(_=>"迟到机翻").ToArray());});
        await Assert.ThrowsAsync<InvalidDataException>(()=>Flow.TranslateAsync(Flow.Capture(Song),engine,default));Assert.Null(Read());
    }
    [Fact] public async Task CancellationIgnoringEngineCannotCommitLateResults()
    {
        using var cancel=new CancellationTokenSource();await using var engine=new Engine((texts,_)=>{cancel.Cancel();return Task.FromResult<IReadOnlyList<string>>(texts.Select(_=>"迟到机翻").ToArray());});
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>Flow.TranslateAsync(Flow.Capture(Song),engine,cancel.Token));Assert.Null(Read());
    }
    [Fact] public async Task SelectionChangeDuringInferenceLeavesQueuePending()
    {
        bool current=true;var flow=new OfflineTranslationWorkflow(root,null,()=>current);flow.Enqueue([flow.Capture(Song)]);
        await using var engine=new Engine((texts,_)=>{current=false;return Task.FromResult<IReadOnlyList<string>>(texts.Select(_=>"迟到机翻").ToArray());});
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>flow.RunAsync(engine,null,default));Assert.Equal("Pending",Assert.Single(flow.ReadQueue()).State);Assert.Null(Read());
    }
    [Theory][InlineData("it")][InlineData("ru")][InlineData("ar")][InlineData("pt")]
    public async Task DetectedUnsupportedLanguageIsRejectedBeforeInference(string language)
    {
        var flow=new OfflineTranslationWorkflow(root,_=>language);await using var engine=new Engine();await Assert.ThrowsAsync<InvalidDataException>(()=>flow.TranslateAsync(flow.Capture(Song),engine,default));Assert.Equal(0,engine.Calls);
    }
    [Theory][InlineData("Ветер дует")][InlineData("الرياح تهب")][InlineData("Une étoile")]
    public async Task UnknownLanguageIsRejectedWithoutDetector(string text)
    {
        using(var lyrics=new LyricsRepository(Path.Combine(root,"lyrics.db")))lyrics.Save(Song,"[00:01]"+text,"Own",true);
        await using var engine=new Engine();await Assert.ThrowsAsync<InvalidDataException>(()=>Flow.TranslateAsync(Flow.Capture(Song),engine,default));Assert.Equal(0,engine.Calls);
    }
    [Theory]
    [InlineData("fr","Le vent traverse la vallée")][InlineData("de","Der Wind zieht durch das Tal")][InlineData("es","El viento cruza el valle")]
    [InlineData("ja","風が谷を渡る")][InlineData("ko","바람이 계곡을 건넌다")]
    public async Task NewSourcesPreserveGroupsAndCanUpgradeToAgent(string language,string text)
    {
        string raw="[00:01]"+text+"\n[00:03]\n[00:04]"+text;
        using(var lyrics=new LyricsRepository(Path.Combine(root,"lyrics.db")))lyrics.Save(Song,raw,"Own multilingual fixture",true);
        var flow=new OfflineTranslationWorkflow(root,_=>language);var item=flow.Capture(Song);await using var engine=new Engine();
        Assert.True(await flow.TranslateAsync(item,engine,default));Assert.Equal(language,engine.Language);
        using var repo=new TranslationRepository(root);var doc=new LrcParser().Parse(raw);var translated=repo.Find(Song,doc)!;
        Assert.Equal(new[]{0,2},translated.Lines.Select(l=>l.GroupIndex));Assert.Equal(ArgosLanguages.SourceUrl(language),translated.SourceUrl);Assert.Contains("经英语中转",translated.Review);
        Assert.True(repo.SaveMissing(Song,translated with{Source="Own provider",SourceUrl="own-provider",Draft=false}));Assert.Single(repo.ReadHistory(Song,item.SourceHash));
        // A separate exact original allows testing the agent upgrade independently.
        using(var lyrics=new LyricsRepository(Path.Combine(root,"lyrics.db")))lyrics.Save(Song,raw+"\n[00:08]"+text,"Own",true);
        var second=flow.Capture(Song);Assert.True(await flow.TranslateAsync(second,engine,default));
        var job=TranslationJobExchange.Create(root,Song,TranslationJobMode.FillMissing);Assert.Equal(TranslationJobMode.ReplaceExpected,job.Request.Mode);
        Assert.Equal(TranslationJobState.Applied,(await TranslationJobExchange.ImportAsync(root,Complete(job))).State);
    }
    [Fact]public async Task JapaneseKanjiGroupsUseTheJapaneseRouteAndBlankIsPreserved()
    {
        const string raw="[00:01]風が谷を渡る\n[00:03]\n[00:04]光の道\n[00:08]夢";
        using(var lyrics=new LyricsRepository(Path.Combine(root,"lyrics.db")))lyrics.Save(Song,raw,"Own",true);
        await using var engine=new Engine();await Flow.TranslateAsync(Flow.Capture(Song),engine,default);Assert.Equal("ja",engine.Language);Assert.Equal(new[]{"風が谷を渡る","光の道","夢"},engine.Input);
    }
    [Fact]public async Task JapaneseIterationMarksDoNotDropTheOtherKanjiGroups()
    {
        const string raw="[00:01]日々\n[00:03]\n[00:04]希望";
        using(var lyrics=new LyricsRepository(Path.Combine(root,"lyrics.db")))lyrics.Save(Song,raw,"Own",true);
        await using var engine=new Engine();await Flow.TranslateAsync(Flow.Capture(Song),engine,default);Assert.Equal("ja",engine.Language);Assert.Equal(new[]{"日々","希望"},engine.Input);
    }
    [Fact]public void QueueLanguageSurvivesLegacyJsonBackupAndExplicitReselection()
    {
        var item=Flow.Capture(Song);Flow.Enqueue([item]);string file=Path.Combine(root,"offline-translation-queue.json");
        var json=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!;json[0]!.AsObject().Remove("SourceLanguage");File.WriteAllText(file,json.ToJsonString());Assert.Equal("auto",Assert.Single(Flow.ReadQueue()).SourceLanguage);
        Flow.Enqueue([item with{SourceLanguage="fr"}]);Assert.Equal("fr",Assert.Single(Flow.ReadQueue()).SourceLanguage);
        Flow.Enqueue([item with{SourceLanguage="de"}]);Assert.Equal("de",Assert.Single(Flow.ReadQueue()).SourceLanguage);
        Assert.Throws<InvalidDataException>(()=>Flow.Enqueue([item with{SourceLanguage="xx"}]));
        string collection=root+"-collection",restored=root+"-restored";
        try{PersonalLibraryArchive.Export(root,collection);PersonalLibraryArchive.Restore(collection,restored);Assert.Equal("de",Assert.Single(new OfflineTranslationWorkflow(restored).ReadQueue()).SourceLanguage);}
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(collection))Directory.Delete(collection,true);if(Directory.Exists(restored))Directory.Delete(restored,true);}
    }
    [Theory][InlineData("auto")][InlineData("en")][InlineData("fr")][InlineData("de")][InlineData("es")][InlineData("ja")][InlineData("ko")]
    public void SourceChoicePersistsIndependentlyOfInterfaceLanguage(string language)
    {
        var store=new SettingsStore(root);var settings=store.Load();settings.UiLanguage="ja";settings.OfflineTranslationLanguage=language;store.Save(settings);Assert.Equal(language,store.Load().OfflineTranslationLanguage);Assert.Equal("ja",store.Load().UiLanguage);
        settings.OfflineTranslationLanguage="unknown";store.Save(settings);Assert.Equal("auto",store.Load().OfflineTranslationLanguage);
    }
    [Theory][InlineData(0)][InlineData(1)][InlineData(4)]public async Task InvalidGroupCountsCannotBeSaved(int count)
    {
        await using var engine=new Engine((_,_)=>Task.FromResult<IReadOnlyList<string>>(Enumerable.Repeat("中文",count).ToArray()));await Assert.ThrowsAsync<InvalidDataException>(()=>Flow.TranslateAsync(Flow.Capture(Song),engine,default));Assert.Null(Read());
    }
    [Fact] public async Task QueueCancellationAndNewInstanceResumeAreDurable()
    {
        var item=Flow.Capture(Song);Flow.Enqueue([item,item]);Assert.Single(Flow.ReadQueue());using var cancel=new CancellationTokenSource();
        await using(var engine=new Engine((_,_)=>{cancel.Cancel();throw new OperationCanceledException(cancel.Token);}))await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>Flow.RunAsync(engine,null,cancel.Token));
        Assert.Equal("Pending",Assert.Single(Flow.ReadQueue()).State);Flow.Enqueue([]);await using(var engine=new Engine())await Flow.RunAsync(engine,null,default);
        Assert.Equal("Completed",Assert.Single(Flow.ReadQueue()).State);Assert.NotNull(Read());
    }
    [Fact] public async Task FailedSongDoesNotBlockFollowingSong()
    {
        var other=Song with{Title="Own second song"};using(var lyrics=new LyricsRepository(Path.Combine(root,"lyrics.db")))lyrics.Save(other,Raw,"Own",true);
        var item=Flow.Capture(Song);Flow.Enqueue([item with{SourceHash=new string('0',64)},Flow.Capture(other)]);
        await using var engine=new Engine();await Flow.RunAsync(engine,null,default);Assert.Equal(new[]{"Failed","Completed"},Flow.ReadQueue().Select(i=>i.State));Assert.Equal(1,engine.Calls);
    }
    [Fact] public void InvalidQueueDoesNotReplaceExistingQueue()
    {
        var item=Flow.Capture(Song);Flow.Enqueue([item]);Assert.Throws<InvalidDataException>(()=>Flow.Enqueue([item with{BindingId=""}]));Assert.Equal(item,Assert.Single(Flow.ReadQueue()));
    }
    [Fact] public async Task SourceUpgradeRetainsMachineHistoryAndDoesNotReplaceManual()
    {
        await using var engine=new Engine();await Flow.TranslateAsync(Flow.Capture(Song),engine,default);using var repo=new TranslationRepository(root);
        Assert.True(repo.SaveMissing(Song,Read()! with{Source="Own provider",SourceUrl="own",Draft=false}));Assert.Single(repo.ReadHistory(Song,TranslationAlignment.Hash(Raw)));
        Assert.False(repo.SaveMissing(Song,Read()! with{Source="Another provider"}));SaveOther(true);Assert.False(repo.SaveMissing(Song,Read()! with{SourceUrl="own-new"}));
    }
    string Complete(TranslationJobTicket job,bool partial=false)
    {
        var result=new TranslationJobResult("LumaLyrics.TranslationJobResult",1,job.Request.JobId,job.RequestSha256,job.Request.Target.SourceHash,"Own AI",job.Request.Target.Groups.Select(g=>new TranslationJobResultLine(g.GroupIndex,g.Original,g.NeedsTranslation&&!(partial&&g.GroupIndex==2)?"更好的译文":"",partial&&g.GroupIndex==2?"Own incomplete":null)).ToArray());File.WriteAllText(job.ResultPath,JsonSerializer.Serialize(result));return job.ResultPath;
    }
    [Theory][InlineData(false,"en")][InlineData(true,"en")][InlineData(false,"fr")][InlineData(true,"fr")][InlineData(false,"de")][InlineData(true,"de")][InlineData(false,"es")][InlineData(true,"es")][InlineData(false,"ja")][InlineData(true,"ja")][InlineData(false,"ko")][InlineData(true,"ko")]public async Task AgentCanUpgradeMachineRegardlessOfCreationOrder(bool createBeforeMachine,string language)
    {
        TranslationJobTicket? job=createBeforeMachine?TranslationJobExchange.Create(root,Song,TranslationJobMode.FillMissing):null;await using var engine=new Engine();await Flow.TranslateAsync(Flow.Capture(Song) with{SourceLanguage=language},engine,default);
        job??=TranslationJobExchange.Create(root,Song,TranslationJobMode.FillMissing);Assert.Equal(createBeforeMachine?TranslationJobMode.FillMissing:TranslationJobMode.ReplaceExpected,job.Request.Mode);
        Assert.Equal(TranslationJobState.Applied,(await TranslationJobExchange.ImportAsync(root,Complete(job))).State);Assert.Equal("更好的译文",Read()!.TextAt(0));using var repo=new TranslationRepository(root,true);Assert.Single(repo.ReadHistory(Song,TranslationAlignment.Hash(Raw)));
    }
    [Theory][InlineData(false,"en")][InlineData(true,"en")][InlineData(false,"fr")][InlineData(true,"fr")][InlineData(false,"de")][InlineData(true,"de")][InlineData(false,"es")][InlineData(true,"es")][InlineData(false,"ja")][InlineData(true,"ja")][InlineData(false,"ko")][InlineData(true,"ko")]public async Task PartialAgentUpgradeRequiresExplicitAdoption(bool createBeforeMachine,string language)
    {
        TranslationJobTicket? job=createBeforeMachine?TranslationJobExchange.Create(root,Song,TranslationJobMode.FillMissing):null;await using var engine=new Engine();await Flow.TranslateAsync(Flow.Capture(Song) with{SourceLanguage=language},engine,default);job??=TranslationJobExchange.Create(root,Song,TranslationJobMode.FillMissing);
        Assert.Equal(TranslationJobState.PartialReady,(await TranslationJobExchange.ImportAsync(root,Complete(job,true))).State);Assert.True(OfflineTranslationWorkflow.IsMachine(Read()!));
    }
    [Fact]public async Task PersonalBackupRoundTripIncludesReadableQueueAndTranslations()
    {
        await using var engine=new Engine();Flow.Enqueue([Flow.Capture(Song)]);await Flow.RunAsync(engine,null,default);
        var collection=root+"-collection";var restored=root+"-restored";
        try{PersonalLibraryArchive.Export(root,collection);PersonalLibraryArchive.Restore(collection,restored);Assert.Equal("Completed",Assert.Single(new OfflineTranslationWorkflow(restored).ReadQueue()).State);using var tr=new TranslationRepository(restored,true);Assert.True(OfflineTranslationWorkflow.IsMachine(tr.Find(Song,new LrcParser().Parse(Raw))!));Assert.False(Directory.Exists(Path.Combine(restored,"argos")));}
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(collection))Directory.Delete(collection,true);if(Directory.Exists(restored))Directory.Delete(restored,true);}
    }
    [Theory][InlineData("../outside")][InlineData("C:/outside")][InlineData("folder/../../outside")][InlineData("folder\\outside")][InlineData("a:stream")]
    public void InstallerRejectsEscapingArchivePaths(string entry)
    {
        string archive=Path.Combine(root,"fixture.zip");using(var zip=ZipFile.Open(archive,ZipArchiveMode.Create))using(var w=new StreamWriter(zip.CreateEntry(entry).Open()))w.Write("own fixture");
        Assert.Throws<InvalidDataException>(()=>ArgosInstallation.Extract(archive,Path.Combine(root,"unpacked")));
    }
    [Fact]public void ManifestPinsEveryDownloadAndMissingRuntimeIsNotReady()
    {
        var installation=new ArgosInstallation(Path.Combine(root,"tools"));Assert.False(installation.Ready);Assert.InRange(installation.DownloadBytes,110_000_000,130_000_000);
        Assert.All(installation.Manifest.Components,c=>{Assert.Equal("https",new Uri(c.Url).Scheme);Assert.Matches("^[a-f0-9]{64}$",c.Sha256);Assert.True(c.Size>0);Assert.Equal(c.Name,Path.GetFileName(c.Name));});
        using var lease=installation.Lease();Assert.Throws<IOException>(()=>installation.Lease());
    }
    [Fact]public async Task InstallerChecksLengthAndHashBeforeUsingComponents()
    {
        var path=Path.Combine(root,"own-component");File.WriteAllBytes(path,[1,2,3]);
        var component=new ArgosComponent("own-component","https://example.org/fixture",Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(new byte[]{1,2,3})),3,"site");
        Assert.True(await ArgosInstallation.VerifyComponentAsync(path,component,default));File.WriteAllBytes(path,[3,2,1]);Assert.False(await ArgosInstallation.VerifyComponentAsync(path,component,default));File.WriteAllBytes(path,[1]);Assert.False(await ArgosInstallation.VerifyComponentAsync(path,component,default));
        File.WriteAllBytes(path,[1,2,3]);using var cancelled=new CancellationTokenSource();cancelled.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>ArgosInstallation.VerifyComponentAsync(path,component,cancelled.Token));
    }
    public void Dispose(){SqliteConnection.ClearAllPools();if(Directory.Exists(root))Directory.Delete(root,true);}
}
