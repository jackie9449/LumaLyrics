using System.Windows.Threading;
using System.IO;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
using LumaLyrics.Windows;
namespace LumaLyrics.App;

public sealed partial class PlaybackCoordinator : IAsyncDisposable
{
    readonly Dispatcher dispatcher; readonly SettingsStore store;
    readonly ResilientLyricsProvider provider = new(); readonly LrcParser parser = new();
    readonly ILyricsProvider manualProvider;
    readonly AppleOriginalLyricsReader appleOriginals=new();
    readonly LyricsRepository repository; readonly PlaybackClock clock = new();
    CancellationTokenSource? lookup; readonly RequestVersion requests=new(); bool disposed;
    readonly CancellationTokenSource lifetime=new();CancellationTokenSource? libraryCancellation;Task? libraryTask,libraryMonitor;
    CancellationTokenSource? artworkCancellation;Task? artworkTask;
    volatile bool foregroundLookupActive;
    DateTimeOffset nextLookupRetry;
    long artworkRequestVersion;
    public LibraryProgress LibraryStatus {get;private set;}=new(0,0,0,0,false,"等待扫描本机 Apple Music 资料库");
    public ArtworkProgress ArtworkStatus {get;private set;}=new(0,0,0,0,0,false,"等待扫描封面");
    public AppSettings Settings { get; }
    public string DataRoot=>store.Root;
    public GsmtcPlaybackSource Source { get; }
    public PlaybackSnapshot? Snapshot { get; private set; }
    public Track? ManualTrack { get; private set; }
    public Track? ActiveTrack => Snapshot?.Track ?? ManualTrack;
    public LyricsDocument? Document { get; private set; }
    public LyricsBinding? Binding { get; private set; }
    public IReadOnlyList<LyricsCandidate> Candidates { get; private set; } = [];
    public string LyricsStatus { get; private set; } = "播放后会自动查找歌词";
    public string AppleOriginalStatus {get;private set;}="只读取已展开的 Apple Music 动态歌词；不会自动打开原生窗口或面板。";
    public event Action? Updated;
    public event Action<byte[]?>? ArtworkChanged;
    public PlaybackCoordinator(Dispatcher dispatcher, SettingsStore store, AppSettings settings)
        : this(dispatcher,store,settings,null) { }
    internal PlaybackCoordinator(Dispatcher dispatcher, SettingsStore store, AppSettings settings,ILyricsProvider? manualProvider)
    {
        this.dispatcher = dispatcher; this.store = store; Settings = settings;
        this.manualProvider=manualProvider??provider;
        repository = new(Path.Combine(store.Root,"lyrics.db")); Source = new(settings.PreferredSession);
        translations=new(store.Root);TranslationProgress=translations.Progress();
        LibraryStatus=repository.GetLibraryProgress(settings.AutoPrefetchLibrary?"准备继续后台预缓存":"后台预缓存已关闭");
        using(var artwork=new ArtworkCache(store.Root))ArtworkStatus=artwork.GetProgress();
        Source.Changed += s => dispatcher.BeginInvoke(() => Receive(s));
        Source.Error += e => dispatcher.BeginInvoke(() => { store.Log(e); LyricsStatus = e; Updated?.Invoke(); });
    }
    public async Task StartAsync()
    {
        try{await Source.StartAsync();}catch(Exception e){store.Log("Playback start: "+e.GetType().Name);LyricsStatus="播放连接暂不可用，资料库仍可后台预缓存";Updated?.Invoke();}
        if(Settings.OnlineLookup&&Settings.AutoPrefetchLibrary)File.Delete(LibraryPrefetcher.PausePath(Path.Combine(store.Root,"lyrics.db")));StartLibraryPrefetch();
        libraryMonitor=Task.Run(async()=>
        {
            try
            {
                var nextScan=DateTimeOffset.UtcNow.AddMinutes(5);using var timer=new PeriodicTimer(TimeSpan.FromSeconds(2));
                while(await timer.WaitForNextTickAsync(lifetime.Token))await dispatcher.InvokeAsync(()=>
                {
                    RefreshExternalProgress();if(DateTimeOffset.UtcNow>=nextScan){nextScan=DateTimeOffset.UtcNow.AddMinutes(5);StartLibraryPrefetch();}
                });
            }
            catch(OperationCanceledException){}
        },lifetime.Token);
    }
    public void StartLibraryPrefetch()
    {
        if(disposed||!Settings.OnlineLookup||!Settings.AutoPrefetchLibrary)return;
        var path=Settings.LibraryPath??AppleMusicLibraryReader.Discover();
        if(path is null){LibraryStatus=new(0,0,0,0,false,"未找到本机资料库，可在资料库窗口选择 Library.musicdb");Updated?.Invoke();return;}
        if(libraryTask is not {IsCompleted:false})StartLyricsPrefetch(path);
        if(artworkTask is not {IsCompleted:false})StartArtworkPrefetch(path);
    }
    void StartLyricsPrefetch(string path)
    {
        libraryCancellation?.Dispose();libraryCancellation=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);var token=libraryCancellation.Token;
        var prefetcher=new LibraryPrefetcher(Path.Combine(store.Root,"lyrics.db"),provider,()=>foregroundLookupActive);
        prefetcher.Progress+=progress=>{if(!disposed)dispatcher.BeginInvoke(()=>{LibraryStatus=progress;Updated?.Invoke();});};
        prefetcher.Cached+=track=>{if(!disposed)_=dispatcher.BeginInvoke(()=>{if(disposed)return;if(Snapshot is { } s && TrackMetadata.FromAppleSession(s.Track).Fingerprint==track.Fingerprint && Binding?.Manual!=true){var b=repository.Find(s.Track);if(b is not null)Apply(b);}});};
        libraryTask=Task.Run(async()=>{try{await prefetcher.RunAsync(path,token);}catch(Exception e){if(!disposed)_=dispatcher.BeginInvoke(()=>{LibraryStatus=LibraryStatus with{Running=false,Message="资料库扫描失败："+e.Message};Updated?.Invoke();});}},token);
    }
    void StartArtworkPrefetch(string path)
    {
        artworkCancellation?.Dispose();artworkCancellation=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);var token=artworkCancellation.Token;
        artworkTask=Task.Run(async()=>
        {
            using var worker=new LibraryArtworkImporter(store.Root);
            worker.Progress+=progress=>{if(!disposed)_=dispatcher.BeginInvoke(()=>{ArtworkStatus=progress;Updated?.Invoke();});};
            worker.Cached+=track=>{if(!disposed)_=dispatcher.BeginInvoke(()=>
            {
                if(disposed || Snapshot is not { } s)return;var current=TrackMetadata.FromAppleSession(s.Track);
                if(current.Fingerprint==track.Fingerprint || current.Album.Length>0 && MatchRules.Normalize(current.Artist)==MatchRules.Normalize(track.Artist) && MatchRules.Normalize(current.Album)==MatchRules.Normalize(track.Album))_=LoadArtworkAsync(s);
            });};
            try{await worker.RunAsync(path,token);}
            catch(Exception e){if(!disposed)_=dispatcher.BeginInvoke(()=>{ArtworkStatus=ArtworkStatus with{Running=false,Message="封面扫描失败："+e.Message};Updated?.Invoke();});}
        },token);
    }
    void RefreshExternalProgress()
    {
        if(disposed)return;
        try
        {
            var path=LibraryPrefetcher.ProgressPath(Path.Combine(store.Root,"lyrics.db"));if(libraryTask is not {IsCompleted:false} && File.Exists(path))LibraryStatus=(System.Text.Json.JsonSerializer.Deserialize<LibraryProgress>(File.ReadAllText(path))??LibraryStatus) with{Running=LibraryPrefetcher.IsRunning(Path.Combine(store.Root,"lyrics.db"))};
            path=LibraryArtworkImporter.ProgressPath(store.Root);if(artworkTask is not {IsCompleted:false} && File.Exists(path))ArtworkStatus=(System.Text.Json.JsonSerializer.Deserialize<ArtworkProgress>(File.ReadAllText(path))??ArtworkStatus) with{Running=LibraryArtworkImporter.IsRunning(store.Root)};
            RefreshTranslationProgress();
            if(ActiveTrack is { } active && Binding is null && repository.Find(active) is { } imported && (imported.Manual || LibraryPrefetcher.QualityOf(imported,active)))Apply(imported);
            RefreshTranslation();
            Updated?.Invoke();
        }
        catch(IOException){}catch(System.Text.Json.JsonException){}
    }
    public void PauseLibraryPrefetch(){File.WriteAllText(LibraryPrefetcher.PausePath(Path.Combine(store.Root,"lyrics.db")),"");libraryCancellation?.Cancel();artworkCancellation?.Cancel();}
    public async Task ResumeLibraryPrefetchAsync(bool retryDeferred=false)
    {
        if(libraryCancellation?.IsCancellationRequested==true && libraryTask is not null){try{await libraryTask;}catch(OperationCanceledException){}}
        if(artworkCancellation?.IsCancellationRequested==true && artworkTask is not null){try{await artworkTask;}catch(OperationCanceledException){}}
        if(retryDeferred){repository.ResetLibraryRetries();using var artwork=new ArtworkCache(store.Root);artwork.ResetRetries();}
        File.Delete(LibraryPrefetcher.PausePath(Path.Combine(store.Root,"lyrics.db")));
        if(!disposed)StartLibraryPrefetch();
    }
    public double? Position => clock.PositionMs;
    public PositionQuality Quality => clock.Quality;
    void Receive(PlaybackSnapshot? s)
    {
        if (disposed) return;
        var old = Snapshot;
        bool changed = old?.SessionId != s?.SessionId || old?.Generation != s?.Generation || old?.Revision != s?.Revision;
        bool durationArrived = s?.Track.DurationMs is not null && (old?.Track.DurationMs is null || Math.Abs((old.Track.DurationMs ?? 0) - s.Track.DurationMs.Value) > 5000);
        Snapshot = s; clock.Update(s);
        if (changed)
        {
            foregroundLookupActive=false;
            lookup?.Cancel(); requests.Next(); Document = null; Binding = null; Candidates = [];Translation=null;
            LyricsStatus = s is null ? "等待 Apple Music；也可以手动查找和导入" : "正在查找歌词…";
            ArtworkChanged?.Invoke(null);
            Interlocked.Increment(ref artworkRequestVersion);
            if (s is not null) { _ = LookupAsync(false); _ = LoadArtworkAsync(s); }
        }
        else if (durationArrived && s is not null && Binding is null) _ = LookupAsync(false);
        else if(s is not null && Binding is null && Settings.OnlineLookup && !foregroundLookupActive && DateTimeOffset.UtcNow>=nextLookupRetry)_=LookupAsync(false);
        Updated?.Invoke();
    }
    async Task LoadArtworkAsync(PlaybackSnapshot expected)
    {
        var serial=Interlocked.Increment(ref artworkRequestVersion);
        try
        {
            var cached=await Task.Run(()=>{using var artwork=new ArtworkCache(store.Root);return artwork.Find(expected.Track);},lifetime.Token);
            byte[]? bytes=cached?.Bytes;
            if(cached?.HighResolution!=true)
            {
                var session=await Source.GetArtworkAsync();
                if(session is not null)
                {
                    try{var size=ImageDimensions.Read(session);if(cached is null || (long)size.Width*size.Height>(long)cached.Width*cached.Height)bytes=session;}
                    catch(InvalidDataException){if(cached is null)bytes=session;}
                }
            }
            if(!disposed && serial==artworkRequestVersion && Snapshot?.SessionId==expected.SessionId && Snapshot.Generation==expected.Generation && Snapshot.Revision==expected.Revision)ArtworkChanged?.Invoke(bytes);
        }
        catch(OperationCanceledException){}
        catch (Exception e) { store.Log("Artwork: " + e.GetType().Name); }
    }
    async Task LoadManualArtworkAsync(Track expected)
    {
        var serial=Interlocked.Increment(ref artworkRequestVersion);
        try
        {
            var cached=await Task.Run(()=>{using var artwork=new ArtworkCache(store.Root);return artwork.Find(expected);},lifetime.Token);
            if(!disposed && serial==artworkRequestVersion && Snapshot is null && ManualTrack==expected)ArtworkChanged?.Invoke(cached?.Bytes);
        }
        catch(OperationCanceledException){}
        catch(Exception e){store.Log("Manual artwork: "+e.GetType().Name);}
    }
    public Task RefreshArtworkAsync()=>Snapshot is { } s?LoadArtworkAsync(s):ManualTrack is { } track?LoadManualArtworkAsync(track):Task.CompletedTask;
    void SetManualTrack(Track track)
    {
        ManualTrack=track;ArtworkChanged?.Invoke(null);_=LoadManualArtworkAsync(track);
    }
    public async Task LookupAsync(bool force)
    {
        nextLookupRetry=DateTimeOffset.UtcNow.AddMinutes(1);
        foregroundLookupActive=false;
        lookup?.Cancel(); lookup?.Dispose(); lookup = new(); var token = lookup.Token;
        var serial = requests.Next(); var s = Snapshot;
        if (s is null || string.IsNullOrWhiteSpace(s.Track.Title)) return;
        bool Current() => !disposed && !token.IsCancellationRequested && requests.IsCurrent(serial) && Snapshot?.SessionId==s.SessionId && Snapshot?.Generation == s.Generation && Snapshot?.Revision == s.Revision && Source.Latest is { } latest && latest.SessionId==s.SessionId && latest.Generation==s.Generation && latest.Revision==s.Revision;
        try
        {
            if(repository.Find(s.Track) is {Manual:true} locked){AppleOriginalStatus="已保留当前歌曲的手动歌词绑定。";Apply(locked);return;}
            if (!force)
            {
                var cached = repository.Find(s.Track);
                if (cached is not null && (cached.Manual || LibraryPrefetcher.QualityOf(cached,s.Track))) { Apply(cached); return; }
            }
            foregroundLookupActive=true;
            if(Settings.AppleMusicOriginalLyrics)
            {
                AppleOriginalStatus="正在检查 Apple Music 已加载的原词…";Updated?.Invoke();
                var native=await appleOriginals.ReadAsync(s,()=>Source.Latest,token);
                if(!Current())return;
                if(native is not null)
                {
                    AppleOriginalStatus="已取得 Apple Music 原词，录音、文字及时间范围检查通过。";
                    Apply(repository.Save(s.Track,native.Lyrics.Lrc,"Apple Music · 本机 TTML · 自动检查",false,providerJson:native.Provenance));return;
                }
                AppleOriginalStatus="当前未取得可核对的 TTML。可在 Apple Music 展开动态歌词后重新查词；自动读取不会弹出窗口。";
            }
            if (!Settings.OnlineLookup) { LyricsStatus = "未取得可核对的本机原词；联网查词已关闭，可导入本地歌词"; Updated?.Invoke(); return; }
            if (!force && repository.HasFailure(s.Track)) { LyricsStatus = "暂未找到可靠歌词，后台稍后自动重试"; Updated?.Invoke(); return; }
            LyricsStatus = "正在查找歌词…"; Updated?.Invoke();
            foregroundLookupActive=true;
            await Task.Delay(300,token);
            var results = await provider.LookupAsync(s.Track,token);
            if (!Current()) return;
            Candidates = results.OrderByDescending(c => MatchRules.Score(s.Track,c).Score).ToArray();
            var selection = AutomaticLyricsMatcher.Select(s.Track,Candidates);
            if (selection is not null)
            {
                var best=selection.Candidate;
                // Preserve the complete provider response locally, separately from parsed lyrics.
                var dir = Path.Combine(store.Root,"cache","responses"); Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir,(best.ProviderName=="NetEase"?"netease-":"lrclib-")+best.Id+".json"),best.RawJson);
                Apply(repository.Save(s.Track,best.Content,best.SourceLabel+" · 自动检查",false,best.Instrumental,providerJson:best.RawJson));
            }
            else { bool partial=results is LyricsLookupResult{UnavailableSources.Count:>0};LyricsStatus = partial?"部分歌词来源暂不可用，已查询可用来源，稍后自动重试":results.Count > 0 ? "候选未通过版本或完整性检查，稍后自动重试" : "暂未找到可靠歌词，稍后自动重试";if(!partial)repository.RememberFailure(s.Track);Updated?.Invoke(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (Current()) { LyricsStatus = e is LyricsProviderException ? e.Message : "歌词加载失败，可重试或导入"; store.Log("Lookup: " + e.GetType().Name+" "+e.Message); Updated?.Invoke(); } }
        finally{if(requests.IsCurrent(serial))foregroundLookupActive=false;}
    }
    void Apply(LyricsBinding binding)
    {
        Binding = binding; Document = parser.Parse(binding.Raw,binding.Plain) with { Instrumental = binding.Instrumental };
        RefreshTranslation();
        LyricsStatus = binding.Instrumental ? "纯音乐" : $"{(Document.IsSynced ? "逐行歌词" : "静态歌词")} · {binding.Source}{(binding.Manual ? " · 已锁定绑定" : "")}";
        Updated?.Invoke();
    }
    public void Import(Track target, string raw, string source, bool plain)
    {
        lookup?.Cancel(); requests.Next();foregroundLookupActive=false;
        var b = repository.Save(target,raw,source,true,plain:plain);
        if (Snapshot is null) SetManualTrack(target);
        if (ActiveTrack?.Fingerprint == target.Fingerprint) Apply(b);
    }
    public void SelectCandidate(Track target,LyricsCandidate candidate)
    {
        lookup?.Cancel(); requests.Next();foregroundLookupActive=false;
        var dir = Path.Combine(store.Root,"cache","responses"); Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir,(candidate.ProviderName=="NetEase"?"netease-":"lrclib-")+candidate.Id+".json"),candidate.RawJson);
        var b = repository.Save(target,candidate.Content,candidate.SourceLabel,true,candidate.Instrumental,providerJson:candidate.RawJson);
        if (Snapshot is null) SetManualTrack(target);
        if (ActiveTrack?.Fingerprint == target.Fingerprint) Apply(b);
    }
    public void SetOffset(double ms)
    {
        if (Binding is null) return; ms = Math.Clamp(ms,-30000,30000); repository.SetOffset(Binding.Id,ms); Binding = Binding with { OffsetMs=ms }; Updated?.Invoke();
    }
    public async Task RestoreAutoAsync() { if (Snapshot is null) return; repository.RestoreAutomatic(Snapshot.Track); Binding=null; Document=null; await LookupAsync(true); }
    public async Task<IReadOnlyList<LyricsCandidate>?> LookupManualAsync(Track track,CancellationToken cancellationToken=default)
    {
        lookup?.Cancel();lookup?.Dispose();lookup=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,lifetime.Token);
        var token=lookup.Token;var serial=requests.Next();foregroundLookupActive=true;
        try
        {
            var results=manualProvider is IManualLyricsSearch search?await search.SearchManualAsync(track,token):await manualProvider.LookupAsync(track,token);
            if(token.IsCancellationRequested || disposed || !requests.IsCurrent(serial))return null;
            if(Snapshot is null)SetManualTrack(track);
            Candidates=results;
            var unavailable=(results as LyricsLookupResult)?.UnavailableSources??[];
            int synced=results.Count(c=>!c.Instrumental && !string.IsNullOrWhiteSpace(c.SyncedLyrics));
            int plain=results.Count(c=>!c.Instrumental && string.IsNullOrWhiteSpace(c.SyncedLyrics));
            LyricsStatus=results.Count==0?"未找到候选，可只填歌名或简化歌手名重试。":$"找到 {results.Count} 个候选：同步 {synced} · 静态 {plain} · 纯音乐 {results.Count(c=>c.Instrumental)}。请选择录音版本。";
            if(unavailable.Count>0)LyricsStatus+=" 部分来源暂不可用："+string.Join("、",unavailable)+"。可稍后重试。";
            Updated?.Invoke();return results;
        }
        catch(OperationCanceledException) { return null; }
        catch(Exception e) { if(!token.IsCancellationRequested && requests.IsCurrent(serial) && !disposed){LyricsStatus=e.Message;Updated?.Invoke();}return null; }
        finally{if(requests.IsCurrent(serial))foregroundLookupActive=false;}
    }
    public void ClearCache()
    {
        repository.ClearNetworkCache();var dir=Path.Combine(store.Root,"cache","responses");if(Directory.Exists(dir))foreach(var f in Directory.GetFiles(dir))File.Delete(f);
        LyricsStatus="查词响应缓存已清理，个人歌词与封面已保留";Updated?.Invoke();
    }
    public void SaveSettings() => store.Save(Settings);
    public void SettingsChanged()
    {
        if(!Settings.OnlineLookup){lookup?.Cancel();requests.Next();foregroundLookupActive=false;if(Binding is null)LyricsStatus="联网查词已关闭，可导入本地歌词";}
        if(!Settings.OnlineLookup)PauseTranslationPreparation();
        if(!Settings.OnlineLookup||!Settings.AutoPrefetchLibrary)PauseLibraryPrefetch();else _=ResumeLibraryPrefetchAsync();
        Updated?.Invoke();
    }
    public async Task ReconnectAsync()
    {
        clock.Invalidate();
        try{await Source.ReconnectAsync();}
        catch(Exception e){store.Log("Reconnect: "+e.GetType().Name);LyricsStatus="重新连接失败，请确认 Apple Music 已打开";Updated?.Invoke();}
    }
    public void Resume() => _ = ReconnectAsync();
    public async ValueTask DisposeAsync()
    {
        disposed=true;lookup?.Cancel();PauseLibraryPrefetch();translationCancellation?.Cancel();lifetime.Cancel();await Source.DisposeAsync();
        if(libraryTask is not null){try{await libraryTask;}catch(OperationCanceledException){}}
        if(artworkTask is not null){try{await artworkTask;}catch(OperationCanceledException){}}
        if(libraryMonitor is not null){try{await libraryMonitor;}catch(OperationCanceledException){}}
        if(translationTask is not null){try{await translationTask;}catch(OperationCanceledException){}}
        translations.Dispose();translationCancellation?.Dispose();
        // Let a canceled HTTP continuation finish before disposing its semaphore.
        provider.Dispose();repository.Dispose();libraryCancellation?.Dispose();artworkCancellation?.Dispose();lifetime.Dispose();store.Save(Settings);
    }
}
