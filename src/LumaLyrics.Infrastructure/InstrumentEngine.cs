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

    public string ProfileId { get; }
    public int SampleRate => classifier.SampleRate;
    public int WindowSamples => classifier.WindowSamples;
    public int HopSamples => classifier.HopSamples;
    public IReadOnlyDictionary<InstrumentKind, ActivityParameters> ActivityParameters { get; }
    public InstrumentEngine(IInstrumentClassifier classifier, IReadOnlyDictionary<InstrumentKind, ActivityParameters>? activityParameters = null)
    {
        this.classifier = classifier;
        var parameters = activityParameters ?? (classifier as OnnxInstrumentClassifier)?.ActivityParameters ?? new Dictionary<InstrumentKind, ActivityParameters>();
        foreach (var pair in parameters)
        { if (!Enum.IsDefined(pair.Key)) throw new ArgumentException("Invalid instrument family."); _ = new InstrumentActivityFilter(pair.Value); }
        ActivityParameters = new ReadOnlyDictionary<InstrumentKind, ActivityParameters>(parameters.ToDictionary());
        ProfileId = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            ClassifierProfile = classifier.ProfileId, Resampling = InstrumentResampler.Version, Downmix = "mean-all-channels-v1",
            classifier.SampleRate, classifier.WindowSamples, classifier.HopSamples,
            Activity = parameters.OrderBy(pair => pair.Key).ToArray()
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
                    copy.Add(score.Key, score.Value);
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
