using System.Diagnostics;
using System.IO;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
using LumaLyrics.Windows;

namespace LumaLyrics.App;

/// <summary>Audio stays off the dispatcher. Raw source events revoke ownership immediately.</summary>
public sealed class InstrumentController : IAsyncDisposable
{
    readonly IPlaybackSource source;
    readonly Func<PlaybackSnapshot?> latest;
    readonly Func<PlaybackSnapshot, InstrumentEpoch, Action<InstrumentFrame>, CancellationToken, Task>? worker;
    readonly string root;
    readonly object gate = new();
    readonly InstrumentEpochGuard guard = new();
    readonly CancellationTokenSource lifetime = new();
    readonly Task supervisor;
    CancellationTokenSource? running;
    Task? run;
    Task? disposal;
    PlaybackSnapshot? observed;
    InstrumentPresentation? presentation;
    InstrumentTimeline? binding;
    InstrumentEpoch? bindingEpoch;
    bool enabled, background, demand, seeking, disposed;
    long serial;
    DateTime retryAfter;
    string message = "等待 Apple Music 播放";
    string modelId = InstrumentModelCatalog.StringsId;
    InstrumentStatus status = InstrumentStatus.Preparing;

    public InstrumentController(GsmtcPlaybackSource source, string root)
        : this(source, () => source.Latest, root, null) { }
    internal InstrumentController(IPlaybackSource source, Func<PlaybackSnapshot?> latest, string root,
        Func<PlaybackSnapshot, InstrumentEpoch, Action<InstrumentFrame>, CancellationToken, Task>? worker)
    {
        this.source = source; this.latest = latest; this.root = root; this.worker = worker;
        source.Changed += Observe;
        Observe(latest());
        supervisor = Task.Run(SuperviseAsync);
    }
    public void Configure(bool enabled, bool background, string? requestedModelId = null)
    {
        lock (gate)
        {
            string nextModelId = requestedModelId is null ? modelId : InstrumentModelCatalog.Get(requestedModelId).Id;
            if (disposed || this.enabled == enabled && this.background == background && modelId == nextModelId) return;
            if (modelId != nextModelId) { modelId = nextModelId; presentation = null; presentationEpoch = null; binding = null; bindingEpoch = null; }
            this.enabled = enabled; this.background = background; Invalidate();
        }
    }
    public void SetDemand(bool value)
    {
        lock (gate) { if (disposed || demand == value) return; demand = value; if (!background) Invalidate(); }
    }
    public void SetSeeking(bool value)
    { lock (gate) { if (disposed || seeking == value) return; seeking = value; Invalidate(); } }
    public void Restart() { lock (gate) { if (!disposed) Invalidate(); } }
    void Invalidate()
    {
        serial++; guard.Clear(); running?.Cancel(); retryAfter = DateTime.MinValue;
        status = InstrumentStatus.Preparing; message = "准备本地乐器识别…";
    }
    static bool SameRecording(PlaybackSnapshot? a, PlaybackSnapshot? b) => a is not null && b is not null
        && a.SessionId == b.SessionId && a.Generation == b.Generation && a.Revision == b.Revision;
    static InstrumentEpoch Epoch(PlaybackSnapshot s) => new(s.SessionId, s.Generation, s.Revision, 0);
    void Observe(PlaybackSnapshot? next)
    {
        lock (gate)
        {
            if (disposed) return;
            bool same = SameRecording(observed, next);
            bool seek = false;
            if (same && observed!.PositionMs is { } before && next!.PositionMs is { } after)
            {
                double elapsed = Math.Max(0, Stopwatch.GetElapsedTime(observed.MonotonicTicks, next.MonotonicTicks).TotalMilliseconds);
                double expected = before + (observed.Status == PlayState.Playing ? elapsed * (observed.Rate ?? 1) : 0);
                seek = Math.Abs(after - expected) > 1500;
            }
            if (!same || seek || observed?.Status != next?.Status || observed?.Rate != next?.Rate)
            {
                Invalidate();
                if (!same) { presentation = null; binding = null; bindingEpoch = null; }
            }
            observed = next;
        }
    }
    public bool Bind(InstrumentTimeline timeline, PlaybackSnapshot expected)
    {
        lock (gate)
        {
            if (disposed || !SameRecording(expected, latest()) || !SameRecording(expected, observed)) return false;
            // Deliberate, session-only version confirmation. Never infer native IDs from titles.
            binding = timeline; bindingEpoch = Epoch(expected); return true;
        }
    }
    public void ClearBinding() { lock (gate) { if (disposed) return; binding = null; bindingEpoch = null; } }
    public InstrumentViewState View(double? position)
    {
        lock (gate)
        {
            if (!enabled) return new(InstrumentStatus.Disabled, false, new Dictionary<InstrumentKind, InstrumentVisualState>(), "");
            var current = latest();
            if (!SameRecording(observed, current)) return Empty(InstrumentStatus.Preparing, "等待当前歌曲的音频…");
            if (seeking) position = null;
            if (binding is not null && bindingEpoch == Epoch(current!))
                return Experimental(InstrumentPresentation.FromTimeline(binding, position, current!.Status));
            if (current!.Status != PlayState.Playing)
                return Experimental(presentation is not null ? presentation.View(current.Status, Stopwatch.GetTimestamp()) : Empty(InstrumentStatus.Paused, "播放后开始识别"));
            if (!demand && !background) return Empty(InstrumentStatus.Paused, "页面隐藏，识别已暂停");
            if (status == InstrumentStatus.Learning && presentation is not null && guard.Current is not null)
                return Experimental(presentation.View(current.Status, Stopwatch.GetTimestamp()));
            if (presentation is not null)
            {
                var unknown = presentation.View(PlayState.Playing, Stopwatch.GetTimestamp() + Stopwatch.Frequency * 3);
                return unknown with { Status = status, Message = message };
            }
            return Empty(status, message);
        }
    }
    static InstrumentViewState Empty(InstrumentStatus state, string text) => new(state, false, new Dictionary<InstrumentKind, InstrumentVisualState>(), text);
    InstrumentViewState Experimental(InstrumentViewState state) => state with { Message = (modelId == InstrumentModelCatalog.RockId ? "实验摇滚三类 · " : "实验弦乐 · ") + state.Message };
    async Task SuperviseAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
            while (await timer.WaitForNextTickAsync(lifetime.Token))
            {
                Task? previous;
                lock (gate) previous = run;
                if (previous is { IsCompleted: false }) continue;
                if (previous is not null) await previous; // RunAsync handles ordinary failures.
                PlaybackSnapshot? snapshot; long owner; CancellationToken token;
                lock (gate)
                {
                    run = null; running?.Dispose(); running = null;
                    snapshot = latest();
                    if (disposed || !enabled || seeking || (!demand && !background) || snapshot?.Status != PlayState.Playing || DateTime.UtcNow < retryAfter) continue;
                    if (!SameRecording(observed, snapshot)) continue;
                    if (snapshot.Rate is { } rate && Math.Abs(rate - 1) > .001)
                    { status = InstrumentStatus.Unavailable; message = "变速播放暂不支持乐器识别"; continue; }
                    owner = serial; running = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); token = running.Token;
                    var epoch = Epoch(snapshot); guard.Set(epoch);
                    presentation ??= new(InstrumentModelCatalog.Get(modelId).Profile.Thresholds);
                    presentation.Begin(epoch, retainRoster: true);
                    status = InstrumentStatus.Preparing; message = "正在连接 Apple Music 音频…";
                    run = Task.Run(() => RunAsync(snapshot, owner, token), CancellationToken.None);
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }
    async Task RunAsync(PlaybackSnapshot snapshot, long owner, CancellationToken token)
    {
        void State(InstrumentStatus value, string text)
        { lock (gate) { if (serial != owner || token.IsCancellationRequested) return; status = value; message = text; } }
        try
        {
            token.ThrowIfCancellationRequested();
            if (worker is not null)
            { await worker(snapshot, guard.Current!, frame => Accept(frame, snapshot, owner, token), token); return; }
            InstrumentInferenceProfile profile; lock (gate) profile = InstrumentModelCatalog.Get(modelId).Profile;
            var models = new InstrumentModelStore(root); string path = models.LocalPath(profile);
            if (!await models.ValidateAsync(path, profile.Sha256, token))
            { State(InstrumentStatus.Unavailable, "请在设置 → 乐器识别中准备实验模型"); return; }
            var target = await AppleMusicAudioTargetResolver.ResolveAsync(snapshot, token);
            if (target is null) { State(InstrumentStatus.Unavailable, "尚未找到当前 Apple Music 的有效音频进程"); return; }
            var helper = CaptureHelper();
            await using var engine = new InstrumentEngine(new OnnxInstrumentClassifier(path, profile));
            await using var capture = new ProcessAudioCaptureBroker(helper.Host, helper.Probe);
            var analysis = new InstrumentLiveAnalysis(engine, guard);
            var active = analysis.RunAsync(capture, target, frame => Accept(frame, snapshot, owner, token), token);
            using var startupScope = CancellationTokenSource.CreateLinkedTokenSource(token);
            var startup = Task.Delay(TimeSpan.FromSeconds(7), startupScope.Token);
            if (await Task.WhenAny(active, startup) == startup && !token.IsCancellationRequested && !analysis.HasEffectiveSignal)
                State(InstrumentStatus.Unavailable, "尚未取得有效音乐信号，继续等待…");
            try { await active; } finally { startupScope.Cancel(); }
            if (!token.IsCancellationRequested) State(InstrumentStatus.Unavailable, "音频连接已结束，稍后重试");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (error is not OutOfMemoryException)
        { State(InstrumentStatus.Unavailable, error is PlatformNotSupportedException ? "当前系统不支持应用音频采集" : "乐器识别暂不可用，请检查模型和音频输出"); }
        finally
        { lock (gate) { if (serial == owner) { guard.Clear(); retryAfter = DateTime.UtcNow.AddSeconds(5); } } }
    }
    void Accept(InstrumentFrame frame, PlaybackSnapshot snapshot, long owner, CancellationToken token)
    {
        lock (gate)
        {
            var current = latest();
            if (serial != owner || token.IsCancellationRequested || !guard.IsCurrent(frame.Epoch) || !SameRecording(snapshot, current)
                || current?.Status != PlayState.Playing || current.Rate is { } rate && Math.Abs(rate - 1) > .001) return;
            if (presentationEpoch != frame.Epoch) { presentation!.Begin(frame.Epoch, true); presentationEpoch = frame.Epoch; }
            if (presentation!.Apply(frame)) { status = InstrumentStatus.Learning; message = "实时识别 · 清单逐步完善"; }
        }
    }
    InstrumentEpoch? presentationEpoch;
    internal static (string Host, string Probe) CaptureHelper()
    {
        string probe = Path.Combine(AppContext.BaseDirectory, "LumaLyrics.Probe.dll");
        string host = Path.Combine(AppContext.BaseDirectory, "runtime", "dotnet.exe");
        if (File.Exists(probe) && File.Exists(host)) return (host, probe);
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (!File.Exists(Path.Combine(directory.FullName, "LumaLyrics.slnx"))) continue;
            host = Path.Combine(directory.FullName, ".tools", "dotnet", "dotnet.exe");
            probe = Path.Combine(directory.FullName, "tools", "LumaLyrics.Probe", "bin", "Release", "net10.0-windows10.0.19041.0", "LumaLyrics.Probe.dll");
            if (File.Exists(host) && File.Exists(probe)) return (host, probe);
        }
        throw new FileNotFoundException("Packaged capture helper is missing.");
    }
    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (disposal is not null) return new(disposal);
            disposed = true; source.Changed -= Observe; Invalidate(); lifetime.Cancel();
            disposal = DisposeCoreAsync(); return new(disposal);
        }
    }
    async Task DisposeCoreAsync()
    {
        await supervisor;
        Task? pending; lock (gate) pending = run;
        if (pending is not null) await pending;
        running?.Dispose(); lifetime.Dispose();
    }
}
