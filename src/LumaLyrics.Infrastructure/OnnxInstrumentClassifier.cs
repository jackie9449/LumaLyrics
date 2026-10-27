using System.Security.Cryptography;
using LumaLyrics.Core;
using Microsoft.ML.OnnxRuntime;

namespace LumaLyrics.Infrastructure;

/// <summary>Fixed waveform graph including the pinned official preprocessing. Owned by one engine.</summary>
public sealed class OnnxInstrumentClassifier : IInstrumentClassifier
{
    readonly InferenceSession session;
    readonly InstrumentInferenceProfile profile;
    readonly object gate = new();
    bool disposed;
    public string ProfileId { get; }
    public int SampleRate => profile.SampleRate;
    public int WindowSamples => profile.WindowSamples;
    public int HopSamples => profile.HopSamples;

    public OnnxInstrumentClassifier(string modelPath, InstrumentInferenceProfile profile)
    {
        profile.Validate();
        this.profile = profile with { LabelMap = new(profile.LabelMap), Thresholds = new(profile.Thresholds) };
        ProfileId = this.profile.ProfileId;
        using var file = new FileStream(modelPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length is <= 0 or > InstrumentModelStore.MaximumModelBytes
            || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(file), Convert.FromHexString(profile.Sha256)))
            throw new InvalidDataException("Instrument model checksum failed.");
        using var options = new SessionOptions
        {
            IntraOpNumThreads = 2, InterOpNumThreads = 1,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR
        };
        session = new InferenceSession(modelPath, options);
        try
        {
            if (session.InputMetadata.Count != 1 || session.OutputMetadata.Count != 1
                || !session.InputMetadata.TryGetValue(profile.InputName, out var input)
                || !session.OutputMetadata.TryGetValue(profile.OutputName, out var output)
                || input.ElementType != typeof(float) || output.ElementType != typeof(float)
                || !input.Dimensions.SequenceEqual(new[] { profile.WindowSamples })
                || !output.Dimensions.SequenceEqual(new[] { profile.OutputClassCount }))
                throw new InvalidDataException("Instrument model tensor schema does not match its profile.");
        }
        catch { session.Dispose(); throw; }
    }

    /// <summary>Raw owned scores for local numerical parity checks, not a public accuracy claim.</summary>
    public float[] Scores(float[] mono, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (mono.Length != WindowSamples) throw new ArgumentException("Wrong waveform length.", nameof(mono));
        foreach (float value in mono)
            if (!float.IsFinite(value) || value is < -1 or > 1) throw new ArgumentException("Invalid normalized waveform.", nameof(mono));
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            using var input = OrtValue.CreateTensorValueFromMemory(mono, new long[] { WindowSamples });
            using var options = new RunOptions();
            using var cancellation = token.Register(() => options.Terminate = true);
            try
            {
                token.ThrowIfCancellationRequested();
                using var outputs = session.Run(options, new[] { profile.InputName }, new[] { input }, new[] { profile.OutputName });
                token.ThrowIfCancellationRequested();
                var scores = outputs[0].GetTensorDataAsSpan<float>().ToArray();
                if (scores.Length != profile.OutputClassCount || scores.Any(value => !float.IsFinite(value) || value is < 0 or > 1))
                    throw new InvalidDataException("Invalid model score vector.");
                return scores;
            }
            catch (OnnxRuntimeException) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
        }
    }

    public ValueTask<IReadOnlyDictionary<InstrumentKind, float>> ClassifyAsync(float[] mono, CancellationToken token)
    {
        var scores = Scores(mono, token);
        var grouped = new Dictionary<InstrumentKind, float>();
        foreach (var label in profile.LabelMap)
            grouped[label.Value] = Math.Max(grouped.GetValueOrDefault(label.Value), scores[label.Key]);
        return ValueTask.FromResult<IReadOnlyDictionary<InstrumentKind, float>>(grouped);
    }

    public ValueTask DisposeAsync()
    {
        lock (gate) { if (!disposed) { disposed = true; session.Dispose(); } }
        return ValueTask.CompletedTask;
    }
}
