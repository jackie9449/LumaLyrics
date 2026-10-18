using System.Windows.Threading;
using System.IO;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
using LumaLyrics.Windows;
namespace LumaLyrics.App;

public sealed class PlaybackCoordinator : IAsyncDisposable
{
    readonly Dispatcher dispatcher; readonly SettingsStore store;
    readonly LrclibProvider provider = new(); readonly LrcParser parser = new();
    readonly LyricsRepository repository; readonly PlaybackClock clock = new();
    CancellationTokenSource? lookup; readonly RequestVersion requests=new(); bool disposed;
    readonly CancellationTokenSource lifetime=new();CancellationTokenSource? libraryCancellation;Task? libraryTask,libraryMonitor;
    volatile bool foregroundLookupActive;
    DateTimeOffset nextLookupRetry;
    public LibraryProgress LibraryStatus {get;private set;}=new(0,0,0,0,false,"等待扫描本机 Apple Music 资料库");
    public AppSettings Settings { get; }
    public GsmtcPlaybackSource Source { get; }
    public PlaybackSnapshot? Snapshot { get; private set; }
    public Track? ManualTrack { get; private set; }
    public Track? ActiveTrack => Snapshot?.Track ?? ManualTrack;
    public LyricsDocument? Document { get; private set; }
    public LyricsBinding? Binding { get; private set; }
    public IReadOnlyList<LyricsCandidate> Candidates { get; private set; } = [];
    public string LyricsStatus { get; private set; } = "播放后会自动查找歌词";
    public event Action? Updated;
    public event Action<byte[]?>? ArtworkChanged;
    public PlaybackCoordinator(Dispatcher dispatcher, SettingsStore store, AppSettings settings)
    {
        this.dispatcher = dispatcher; this.store = store; Settings = settings;
        repository = new(Path.Combine(store.Root,"lyrics.db")); Source = new(settings.PreferredSession);
        LibraryStatus=repository.GetLibraryProgress(settings.AutoPrefetchLibrary?"准备继续后台预缓存":"后台预缓存已关闭");
        Source.Changed += s => dispatcher.BeginInvoke(() => Receive(s));
        Source.Error += e => dispatcher.BeginInvoke(() => { store.Log(e); LyricsStatus = e; Updated?.Invoke(); });
    }
    public async Task StartAsync()
    {
        try{await Source.StartAsync();}catch(Exception e){store.Log("Playback start: "+e.GetType().Name);LyricsStatus="播放连接暂不可用，资料库仍可后台预缓存";Updated?.Invoke();}
        if(Settings.OnlineLookup&&Settings.AutoPrefetchLibrary)File.Delete(LibraryPrefetcher.PausePath(Path.Combine(store.Root,"lyrics.db")));StartLibraryPrefetch();
        libraryMonitor=Task.Run(async()=>{try{using var timer=new PeriodicTimer(TimeSpan.FromMinutes(5));while(await timer.WaitForNextTickAsync(lifetime.Token))await dispatcher.InvokeAsync(StartLibraryPrefetch);}catch(OperationCanceledException){}},lifetime.Token);
    }
    public void StartLibraryPrefetch()
    {
        if(disposed||!Settings.OnlineLookup||!Settings.AutoPrefetchLibrary||libraryTask is{IsCompleted:false})return;
        var path=Settings.LibraryPath??AppleMusicLibraryReader.Discover();
        if(path is null){LibraryStatus=new(0,0,0,0,false,"未找到本机资料库，可在资料库窗口选择 Library.musicdb");Updated?.Invoke();return;}
        libraryCancellation?.Dispose();libraryCancellation=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var prefetcher=new LibraryPrefetcher(Path.Combine(store.Root,"lyrics.db"),provider,()=>foregroundLookupActive);
        prefetcher.Progress+=progress=>{if(!disposed)dispatcher.BeginInvoke(()=>{LibraryStatus=progress;Updated?.Invoke();});};
        prefetcher.Cached+=track=>{if(!disposed)_=dispatcher.BeginInvoke(()=>{if(Snapshot?.Track.Fingerprint==track.Fingerprint && Binding?.Manual!=true){var b=repository.Find(track);if(b is not null)Apply(b);}});};
        libraryTask=Task.Run(async()=>{try{await prefetcher.RunAsync(path,libraryCancellation.Token);}catch(Exception e){if(!disposed)_=dispatcher.BeginInvoke(()=>{LibraryStatus=LibraryStatus with{Running=false,Message="资料库扫描失败："+e.Message};Updated?.Invoke();});}},libraryCancellation.Token);
    }
    public void PauseLibraryPrefetch(){File.WriteAllText(LibraryPrefetcher.PausePath(Path.Combine(store.Root,"lyrics.db")),"");libraryCancellation?.Cancel();}
    public async Task ResumeLibraryPrefetchAsync(bool retryDeferred=false)
    {
        if(libraryCancellation?.IsCancellationRequested==true && libraryTask is not null){try{await libraryTask;}catch(OperationCanceledException){}}
        if(retryDeferred)repository.ResetLibraryRetries();
        File.Delete(LibraryPrefetcher.PausePath(Path.Combine(store.Root,"lyrics.db")));
        if(!disposed)StartLibraryPrefetch();
    }
    public double? Position => clock.PositionMs;
    public PositionQuality Quality => clock.Quality;
    void Receive(PlaybackSnapshot? s)
    {
        if (disposed) return;
        var old = Snapshot;
        if(libraryTask?.IsCompleted==true)
        {
            try{var p=LibraryPrefetcher.ProgressPath(Path.Combine(store.Root,"lyrics.db"));if(File.Exists(p))LibraryStatus=System.Text.Json.JsonSerializer.Deserialize<LibraryProgress>(File.ReadAllText(p))??LibraryStatus;}catch(IOException){}catch(System.Text.Json.JsonException){}
        }
        bool changed = old?.SessionId != s?.SessionId || old?.Generation != s?.Generation || old?.Revision != s?.Revision;
        bool durationArrived = s?.Track.DurationMs is not null && (old?.Track.DurationMs is null || Math.Abs((old.Track.DurationMs ?? 0) - s.Track.DurationMs.Value) > 5000);
        Snapshot = s; clock.Update(s);
        if (changed)
        {
            foregroundLookupActive=false;
            lookup?.Cancel(); requests.Next(); Document = null; Binding = null; Candidates = [];
            LyricsStatus = s is null ? "等待 Apple Music；也可以手动查找和导入" : "正在查找歌词…";
            ArtworkChanged?.Invoke(null);
            if (s is not null) { _ = LookupAsync(false); _ = LoadArtworkAsync(s.Generation, s.Revision); }
        }
        else if (durationArrived && s is not null && Binding is null) _ = LookupAsync(false);
        else if(s is not null && Binding is null && Settings.OnlineLookup && !foregroundLookupActive && DateTimeOffset.UtcNow>=nextLookupRetry)_=LookupAsync(false);
        Updated?.Invoke();
    }
    async Task LoadArtworkAsync(long generation, long revision)
    {
        try { var bytes = await Source.GetArtworkAsync(); if (!disposed && Snapshot?.Generation == generation && Snapshot.Revision == revision) ArtworkChanged?.Invoke(bytes); }
        catch (Exception e) { store.Log("Artwork: " + e.GetType().Name); }
    }
    public async Task LookupAsync(bool force)
    {
        nextLookupRetry=DateTimeOffset.UtcNow.AddMinutes(1);
        foregroundLookupActive=false;
        lookup?.Cancel(); lookup?.Dispose(); lookup = new(); var token = lookup.Token;
        var serial = requests.Next(); var s = Snapshot;
        if (s is null || string.IsNullOrWhiteSpace(s.Track.Title)) return;
        bool Current() => !disposed && !token.IsCancellationRequested && requests.IsCurrent(serial) && Snapshot?.Generation == s.Generation && Snapshot?.Revision == s.Revision;
        try
        {
            if (!force)
            {
                var cached = repository.Find(s.Track);
                if (cached is not null && (cached.Manual || LibraryPrefetcher.QualityOf(cached,s.Track))) { Apply(cached); return; }
            }
            if (!Settings.OnlineLookup) { LyricsStatus = "联网查词已关闭，可导入本地歌词"; Updated?.Invoke(); return; }
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
                File.WriteAllText(Path.Combine(dir,best.Id + ".json"),best.RawJson);
                Apply(repository.Save(s.Track,best.Content,$"LRCLIB #{best.Id} · 自动检查",false,best.Instrumental,providerJson:best.RawJson));
            }
            else { LyricsStatus = results.Count > 0 ? "候选未通过版本或完整性检查，稍后自动重试" : "暂未找到可靠歌词，稍后自动重试"; repository.RememberFailure(s.Track);Updated?.Invoke(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (Current()) { LyricsStatus = e is LyricsProviderException ? e.Message : "歌词加载失败，可重试或导入"; store.Log("Lookup: " + e.GetType().Name+" "+e.Message); Updated?.Invoke(); } }
        finally{if(requests.IsCurrent(serial))foregroundLookupActive=false;}
    }
    void Apply(LyricsBinding binding)
    {
        Binding = binding; Document = parser.Parse(binding.Raw,binding.Plain) with { Instrumental = binding.Instrumental };
        LyricsStatus = binding.Instrumental ? "纯音乐" : $"{(Document.IsSynced ? "逐行歌词" : "静态歌词")} · {binding.Source}{(binding.Manual ? " · 已锁定绑定" : "")}";
        Updated?.Invoke();
    }
    public void Import(Track target, string raw, string source, bool plain)
    {
        lookup?.Cancel(); requests.Next();foregroundLookupActive=false;
        var b = repository.Save(target,raw,source,true,plain:plain);
        if (Snapshot is null) ManualTrack=target;
        if (ActiveTrack?.Fingerprint == target.Fingerprint) Apply(b);
    }
    public void SelectCandidate(Track target,LyricsCandidate candidate)
    {
        lookup?.Cancel(); requests.Next();foregroundLookupActive=false;
        var dir = Path.Combine(store.Root,"cache","responses"); Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir,candidate.Id + ".json"),candidate.RawJson);
        var b = repository.Save(target,candidate.Content,$"LRCLIB #{candidate.Id}",true,candidate.Instrumental,providerJson:candidate.RawJson);
        if (Snapshot is null) ManualTrack=target;
        if (ActiveTrack?.Fingerprint == target.Fingerprint) Apply(b);
    }
    public void SetOffset(double ms)
    {
        if (Binding is null) return; ms = Math.Clamp(ms,-30000,30000); repository.SetOffset(Binding.Id,ms); Binding = Binding with { OffsetMs=ms }; Updated?.Invoke();
    }
    public async Task RestoreAutoAsync() { if (Snapshot is null) return; repository.RestoreAutomatic(Snapshot.Track); Binding=null; Document=null; await LookupAsync(true); }
    public async Task LookupManualAsync(Track track)
    {
        lookup?.Cancel();lookup=new();var token=lookup.Token;var serial=requests.Next();
        try { var results=await provider.LookupAsync(track,token);if(token.IsCancellationRequested || disposed || !requests.IsCurrent(serial))return;ManualTrack=track;Candidates=results;LyricsStatus=$"找到 {results.Count} 个候选，请确认版本";Updated?.Invoke(); }
        catch(OperationCanceledException) { }
        catch(Exception e) { if(requests.IsCurrent(serial) && !disposed){LyricsStatus=e.Message;Updated?.Invoke();} }
    }
    public void ClearCache()
    {
        repository.ClearNetworkCache();var dir=Path.Combine(store.Root,"cache","responses");if(Directory.Exists(dir))foreach(var f in Directory.GetFiles(dir))File.Delete(f);
        if(Binding?.Manual==false){Binding=null;Document=null;LyricsStatus="网络缓存已清理，可以重新搜索";Updated?.Invoke();}
    }
    public void SaveSettings() => store.Save(Settings);
    public void SettingsChanged()
    {
        if(!Settings.OnlineLookup){lookup?.Cancel();requests.Next();foregroundLookupActive=false;if(Binding is null)LyricsStatus="联网查词已关闭，可导入本地歌词";}
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
        disposed=true;lookup?.Cancel();PauseLibraryPrefetch();lifetime.Cancel();await Source.DisposeAsync();
        if(libraryTask is not null){try{await libraryTask;}catch(OperationCanceledException){}}
        if(libraryMonitor is not null){try{await libraryMonitor;}catch(OperationCanceledException){}}
        // Let a canceled HTTP continuation finish before disposing its semaphore.
        provider.Dispose();repository.Dispose(); store.Save(Settings);
    }
}
