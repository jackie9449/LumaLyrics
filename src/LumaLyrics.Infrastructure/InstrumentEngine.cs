using System.Collections.ObjectModel;
using LumaLyrics.Core;

namespace LumaLyrics.Infrastructure;

/// <summary>Owns a classifier and serializes use; cancelled results never escape this boundary.</summary>
public sealed class InstrumentEngine : IAsyncDisposable
{
    readonly IInstrumentClassifier classifier;
    readonly SemaphoreSlim inferenceGate = new(1);
    readonly CancellationTokenSource lifetime = new();
    readonly object gate = new();
    Task? disposal;
    bool disposed;
    readonly int recentSilenceSamples;

    public string ProfileId { get; }
    public int SampleRate => classifier.SampleRate;
    public int WindowSamples => classifier.WindowSamples;
    public int HopSamples => classifier.HopSamples;
    public IReadOnlyDictionary<InstrumentKind, ActivityParameters> ActivityParameters { get; }
    public IReadOnlyDictionary<InstrumentKind, ActivityParameters> RosterParameters { get; }
    public InstrumentEngine(IInstrumentClassifier classifier, IReadOnlyDictionary<InstrumentKind, ActivityParameters>? activityParameters = null,
        IReadOnlyDictionary<InstrumentKind, ActivityParameters>? rosterParameters = null, int? recentSilenceSamples = null)
    {
        this.classifier = classifier;
        this.recentSilenceSamples = recentSilenceSamples ?? (classifier as OnnxInstrumentClassifier)?.RecentSilenceSamples ?? 0;
        if (this.recentSilenceSamples < 0 || this.recentSilenceSamples > classifier.WindowSamples) throw new ArgumentOutOfRangeException(nameof(recentSilenceSamples));
        var parameters = activityParameters ?? (classifier as OnnxInstrumentClassifier)?.ActivityParameters ?? new Dictionary<InstrumentKind, ActivityParameters>();
        foreach (var pair in parameters)
        { if (!Enum.IsDefined(pair.Key)) throw new ArgumentException("Invalid instrument family."); _ = new InstrumentActivityFilter(pair.Value); }
        ActivityParameters = new ReadOnlyDictionary<InstrumentKind, ActivityParameters>(parameters.ToDictionary());
        var roster = rosterParameters ?? (classifier as OnnxInstrumentClassifier)?.RosterParameters ?? parameters;
        if (!roster.Keys.Order().SequenceEqual(parameters.Keys.Order())) throw new ArgumentException("Roster rules must cover the activity vocabulary.");
        foreach (var rule in roster.Values) _ = new InstrumentActivityFilter(rule);
        RosterParameters = new ReadOnlyDictionary<InstrumentKind, ActivityParameters>(roster.ToDictionary());
        ProfileId = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            ClassifierProfile = classifier.ProfileId, Resampling = InstrumentResampler.Version, Downmix = "mean-all-channels-v1",
            DigitalSilence = "observed-zero-scores-v2", RecentSilenceSamples = this.recentSilenceSamples,
            classifier.SampleRate, classifier.WindowSamples, classifier.HopSamples,
            Activity = parameters.OrderBy(pair => pair.Key).ToArray(), Roster = roster.OrderBy(pair => pair.Key).ToArray()
        }))).ToLowerInvariant();
    }

    public async Task<InstrumentFrame> AnalyzeAsync(InstrumentWindow window, CancellationToken token)
    {
        CancellationTokenSource linked;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, token);
        }
        using (linked)
        {
            linked.Token.ThrowIfCancellationRequested();
            if (window.SampleRate != classifier.SampleRate || window.Mono.Length != classifier.WindowSamples
                || window.StartTicks < 0 || window.EndTicks <= window.StartTicks || window.StartMs.HasValue != window.EndMs.HasValue
                || window.StartMs.HasValue && (!double.IsFinite(window.StartMs.Value) || !double.IsFinite(window.EndMs!.Value)
                    || window.StartMs < 0 || window.EndMs <= window.StartMs))
                throw new ArgumentException("Invalid instrument analysis window.", nameof(window));
            foreach (float sample in window.Mono)
                if (!float.IsFinite(sample) || sample is < -1 or > 1) throw new ArgumentException("Invalid audio sample.", nameof(window));
            // A learned prior can be high on an entirely empty waveform. Only
            // exact digital silence is suppressed; quiet music retains scores.
            bool digitalSilence = !window.Mono.Any(sample => sample != 0);
            if (!digitalSilence && recentSilenceSamples > 0)
            {
                // Ignore offline padding beyond the observed range. Zero padding
                // cannot claim that the actual last music samples were silent.
                double duration = window.StartMs is { } start && window.EndMs is { } end ? end - start
                    : (window.EndTicks - window.StartTicks) * (1000d / System.Diagnostics.Stopwatch.Frequency);
                int observedSamples = (int)Math.Clamp(Math.Round(duration * SampleRate / 1000), 0, WindowSamples);
                if (observedSamples >= recentSilenceSamples)
                {
                    digitalSilence = true;
                    for (int i = observedSamples - recentSilenceSamples; i < observedSamples; i++)
                        if (window.Mono[i] != 0) { digitalSilence = false; break; }
                }
            }
            await inferenceGate.WaitAsync(linked.Token);
            try
            {
                linked.Token.ThrowIfCancellationRequested();
                var scores = await classifier.ClassifyAsync(window.Mono, linked.Token);
                linked.Token.ThrowIfCancellationRequested();
                var copy = new Dictionary<InstrumentKind, float>();
                foreach (var score in scores)
                {
                    if (!Enum.IsDefined(score.Key) || !float.IsFinite(score.Value) || score.Value is < 0 or > 1)
                        throw new InvalidDataException("Invalid instrument classifier output.");
                    copy.Add(score.Key, digitalSilence ? 0 : score.Value);
                }
                return new(window.Epoch, window.EndTicks, window.StartMs, window.EndMs, ProfileId,
                    new ReadOnlyDictionary<InstrumentKind, float>(copy));
            }
            finally { inferenceGate.Release(); }
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (disposal is null) { disposed = true; lifetime.Cancel(); disposal = DisposeCoreAsync(); }
            return new(disposal);
        }
    }

    async Task DisposeCoreAsync()
    {
        await inferenceGate.WaitAsync();
        try { await classifier.DisposeAsync(); }
        finally { inferenceGate.Release(); lifetime.Dispose(); }
    }
}
