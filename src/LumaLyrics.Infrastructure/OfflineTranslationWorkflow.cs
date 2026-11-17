using System.Text.Json;
using LumaLyrics.Core;
using static LumaLyrics.Infrastructure.TranslationJobRepository;
namespace LumaLyrics.Infrastructure;

public sealed record OfflineTranslationItem(Track Track,string BindingId,string SourceHash,LibraryPreparationTarget? Member,string State="Pending",string? Detail=null,string SourceLanguage="auto");
public sealed record OfflineTranslationProgress(int Total,int Completed,int Skipped,int Failed,int Pending,bool Running)
{public IReadOnlyList<OfflineTranslationItem> Failures {get;init;}=[];}
public sealed class OfflineTranslationWorkflow(string root,Func<string,string?>? languageDetector=null,Func<bool>? selectionCurrent=null)
{
    static readonly JsonSerializerOptions json=new(){WriteIndented=true};
    string QueuePath=>Path.Combine(root,"offline-translation-queue.json");
    public static bool IsMachine(ChineseTranslation translation)=>ArgosLanguages.IsMachineUrl(translation.SourceUrl);
    public IReadOnlyList<OfflineTranslationItem> ReadQueue()
    {
        if(!File.Exists(QueuePath))return [];
        if(new FileInfo(QueuePath).Length>8*1024*1024)throw new InvalidDataException("Offline queue exceeds limit");
        return ReadQueueFile(QueuePath);
    }
    public static IReadOnlyList<OfflineTranslationItem> ReadQueueFile(string path)
    {
        if(new FileInfo(path).Length>8*1024*1024)throw new InvalidDataException("Offline queue exceeds limit");
        var items=JsonSerializer.Deserialize<OfflineTranslationItem[]>(File.ReadAllText(path),json)??throw new InvalidDataException("Empty queue");
        if(items.Length>5000 || items.Any(i=>i is null||i.Track is null||i.Track.Title is null||i.Track.Artist is null||i.Track.Album is null||!ArgosLanguages.IsSelection(i.SourceLanguage)||string.IsNullOrWhiteSpace(i.BindingId)||i.BindingId.Length>500||!PersonalLibraryArchive.IsHash(i.SourceHash)||i.Detail?.Length>1000||i.State is not ("Pending" or "Completed" or "Skipped" or "Failed")))throw new InvalidDataException("Invalid offline queue");return items;
    }
    void Save(IReadOnlyList<OfflineTranslationItem> items)
    {
        string text=JsonSerializer.Serialize(items,json);
        if(System.Text.Encoding.UTF8.GetByteCount(text)>8*1024*1024)throw new InvalidDataException("Offline queue exceeds limit");
        string temp=QueuePath+".tmp";File.WriteAllText(temp,text);File.Move(temp,QueuePath,true);
    }
    public OfflineTranslationItem Capture(Track track,LibraryPreparationTarget? member=null)
    {
        using var lyrics=new LyricsRepository(Path.Combine(root,"lyrics.db"),true);var binding=lyrics.FindReadOnly(track)??throw new InvalidDataException("Missing timed original");
        var doc=new LrcParser().Parse(binding.Raw,binding.Plain) with{Instrumental=binding.Instrumental};
        if(!doc.IsSynced||doc.Instrumental||!binding.Manual&&!LibraryPrefetcher.QualityOf(binding,track))throw new InvalidDataException("Missing reliable timed original");
        if(member is not null)LibrarySongActions.Check(root,member);
        return new(track,binding.Id,TranslationAlignment.Hash(doc.Raw),member);
    }
    public void Enqueue(IReadOnlyList<OfflineTranslationItem> selected)
    {
        PersonalLibraryArchive.EnsureOutsideSoftware(root);
        using var lease=new FileStream(Path.Combine(root,"offline-translation.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        if(selected.Count>5000||selected.Any(i=>!ArgosLanguages.IsSelection(i.SourceLanguage)||string.IsNullOrWhiteSpace(i.BindingId)||!PersonalLibraryArchive.IsHash(i.SourceHash)))throw new InvalidDataException("Invalid song selection");
        if(selected.Count==0)return;
        var previous=ReadQueue().Where(i=>i.State=="Pending").ToList();
        foreach(var item in selected){previous.RemoveAll(i=>i.Track==item.Track&&i.SourceHash==item.SourceHash);previous.Add(item with{State="Pending",Detail=null});}
        if(previous.Count>5000)throw new InvalidDataException("Too many pending songs");Save(previous);
    }
    public static bool EnglishCompatible(string text)
    {
        int letters=0;
        foreach(char c in text)if(char.IsLetter(c)){if(!char.IsAsciiLetter(c))return false;letters++;}
        return letters>0;
    }
    public async Task RunAsync(IOfflineTranslationEngine engine,IProgress<OfflineTranslationProgress>? progress,CancellationToken token)
    {
        using var lease=new FileStream(Path.Combine(root,"offline-translation.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        var items=ReadQueue().ToArray();
        void Report(bool running)=>progress?.Report(new(items.Length,items.Count(i=>i.State=="Completed"),items.Count(i=>i.State=="Skipped"),items.Count(i=>i.State=="Failed"),items.Count(i=>i.State=="Pending"),running){Failures=items.Where(i=>i.State=="Failed").Take(3).ToArray()});
        Report(true);
        try
        {
            for(int i=0;i<items.Length;i++)
            {
                token.ThrowIfCancellationRequested();if(items[i].State!="Pending")continue;
                try{bool saved=await TranslateAsync(items[i],engine,token);items[i]=items[i] with{State=saved?"Completed":"Skipped",Detail=saved?"Argos Translate":"Existing translation"};}
                catch(OperationCanceledException){throw;}
                catch(Exception e) when(e is IOException or InvalidDataException or System.Text.Json.JsonException or Microsoft.Data.Sqlite.SqliteException)
                {items[i]=items[i] with{State="Failed",Detail=e.Message.Contains("Missing source language model",StringComparison.Ordinal)?"离线语言模型未准备，请先准备所需语言组件。":e.Message.Contains("source language",StringComparison.Ordinal)?"无法确定或暂不支持原词语言，请指定语言后重试。":e.Message.Contains("Original",StringComparison.Ordinal)?"原词已变化，请重新选择。":"机翻失败或歌词组过长，请重试。"};}
                Save(items);Report(true);
            }
        }
        finally{Save(items);Report(false);}
    }
    public async Task<bool> TranslateAsync(OfflineTranslationItem item,IOfflineTranslationEngine engine,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();if(selectionCurrent?.Invoke()==false)throw new OperationCanceledException("Selection changed");using var lyrics=new LyricsRepository(Path.Combine(root,"lyrics.db"),true);
        var binding=lyrics.FindReadOnly(item.Track)??throw new InvalidDataException("Original missing");
        if(binding.Id!=item.BindingId||binding.Plain||binding.Instrumental||!binding.Manual&&!LibraryPrefetcher.QualityOf(binding,item.Track)||TranslationAlignment.Hash(binding.Raw)!=item.SourceHash)throw new InvalidDataException("Original changed");
        var doc=new LrcParser().Parse(binding.Raw);using(var translations=new TranslationRepository(root))if(translations.FindActive(item.Track,doc) is not null)return false;
        if(item.Member is not null)LibrarySongActions.Check(root,item.Member);
        bool japaneseOrKorean=item.SourceLanguage is "ja" or "ko"||doc.Groups.Any(g=>g.Text.Any(c=>c is >= '\u3040' and <= '\u30ff' or >= '\uac00' and <= '\ud7af'));
        var groups=doc.Groups.Select((g,i)=>(Index:i,Text:g.Text)).Where(g=>TranslationAlignment.NeedsChineseTranslation(g.Text,japaneseOrKorean)).ToArray();
        if(groups.Length==0)return false;
        if(groups.Length>2000)throw new InvalidDataException("Too many original groups");
        string language=ArgosLanguages.Resolve(string.Join("\n",groups.Select(g=>g.Text)),item.SourceLanguage,languageDetector);
        if(language is "ja" or "ko" && !japaneseOrKorean)groups=doc.Groups.Select((g,i)=>(Index:i,Text:g.Text)).Where(g=>TranslationAlignment.NeedsChineseTranslation(g.Text,true)).ToArray();
        if(groups.Length>2000)throw new InvalidDataException("Too many original groups");
        var translated=await engine.TranslateAsync(groups.Select(g=>g.Text).ToArray(),language,token);token.ThrowIfCancellationRequested();if(selectionCurrent?.Invoke()==false)throw new OperationCanceledException("Selection changed");
        if(translated.Count!=groups.Length||translated.Any(s=>string.IsNullOrWhiteSpace(s)||s.Length>4000||!TranslationAlignment.ContainsHan(s)))throw new InvalidDataException("Invalid Chinese output");
        string review=language=="en"?"离线基础机翻 · 仅检查原词哈希与逐组对应 · 未人工校对":"离线基础机翻 · 经英语中转 · 仅检查原词哈希与逐组对应 · 未人工校对";
        var result=new ChineseTranslation(item.SourceHash,groups.Select((g,i)=>new TranslationLine(g.Index,translated[i])).ToArray(),"Argos Translate · 基础机翻",ArgosLanguages.SourceUrl(language),review,DateTimeOffset.UtcNow,true);
        TranslationRepository.Validate(result);
        using var db=Open(root);Execute(db,"ATTACH DATABASE $p AS originals",("$p",Path.Combine(root,"lyrics.db")));using var tx=db.BeginTransaction(deferred:false);
        token.ThrowIfCancellationRequested();
        if(item.Member is not null)LibrarySongActions.CheckAtWrite(db,item.Member);
        string? raw=Scalar(db,"SELECT d.Raw FROM originals.TrackLyricsBindings b JOIN originals.LyricsDocuments d ON d.Id=b.DocumentId JOIN originals.Tracks t ON t.Id=b.TrackId WHERE b.Id=$id AND t.Fingerprint=$f AND d.Plain=0 AND d.Instrumental=0 AND ($duration IS NULL OR t.Duration IS NULL OR abs(t.Duration-$duration)<=5000)",("$id",item.BindingId),("$f",item.Track.Fingerprint),("$duration",item.Track.DurationMs));
        if(raw is null||TranslationAlignment.Hash(raw)!=item.SourceHash)throw new InvalidDataException("Original changed during translation");
        using var command=Command(db,"INSERT INTO Translations(Fingerprint,SourceHash,Duration,Title,Artist,Album,Content,Manual,Revision) VALUES($f,$h,$d,$t,$a,$al,$c,0,$r) ON CONFLICT(Fingerprint,SourceHash) DO NOTHING",("$f",item.Track.Fingerprint),("$h",item.SourceHash),("$d",item.Track.DurationMs),("$t",item.Track.Title),("$a",item.Track.Artist),("$al",item.Track.Album),("$c",JsonSerializer.Serialize(result)),("$r",Guid.NewGuid().ToString("N")));
        token.ThrowIfCancellationRequested();if(selectionCurrent?.Invoke()==false)throw new OperationCanceledException("Selection changed");bool added=command.ExecuteNonQuery()==1;tx.Commit();return added;
    }
}
