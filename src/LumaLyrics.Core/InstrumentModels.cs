namespace LumaLyrics.Core;

public enum InstrumentKind { Drums, Bass, Guitar, Piano, Strings, Brass, Woodwind, Synth }
public enum InstrumentStatus { Disabled, Preparing, Learning, Ready, Paused, Unavailable }
public enum InstrumentVisualState { Unknown, Dim, Active }
public sealed record InstrumentEpoch(string SessionId, long Generation, long Revision, long Discontinuity);
public sealed record InstrumentRecordingKey(string Source, string PersistentId, long DurationMs, string? AudioSha256);
public sealed record InstrumentSpan(double StartMs, double EndMs);
public sealed record InstrumentInterval(InstrumentKind Kind, double StartMs, double EndMs);
public sealed record InstrumentScorePoint(double StartMs, double EndMs, Dictionary<InstrumentKind, float> Scores);
public sealed record ActivityParameters(float On, float Off, int AttackFrames, double ReleaseMs);
public sealed record AudioTarget(uint ProcessId, DateTime StartTimeUtc, string PackageFamily, string? SourceAppId = null);

/// <summary>Owned interleaved samples. Timestamps use Stopwatch ticks, never UTC.</summary>
public sealed record PcmChunk(float[] Samples, int SampleRate, int Channels, long FirstSampleTicks,
    bool Discontinuous, bool Silent);
public sealed record InstrumentWindow(InstrumentEpoch Epoch, long StartTicks, long EndTicks,
    double? StartMs, double? EndMs, int SampleRate, float[] Mono);
public sealed record InstrumentFrame(InstrumentEpoch Epoch, long EndTicks, double? StartMs, double? EndMs,
    string ProfileId, IReadOnlyDictionary<InstrumentKind, float> Scores);
public sealed record InstrumentTimeline(InstrumentRecordingKey Recording, string ProfileId,
    InstrumentSpan[] Coverage, InstrumentScorePoint[] Scores, InstrumentInterval[] Intervals,
    InstrumentKind[] Roster, bool Complete, long Revision);
public sealed record InstrumentViewState(InstrumentStatus Status, bool Complete,
    IReadOnlyDictionary<InstrumentKind, InstrumentVisualState> Icons, string Message);

public interface IProcessAudioCapture : IAsyncDisposable
{
    IAsyncEnumerable<PcmChunk> ReadAsync(AudioTarget target, CancellationToken token);
}

public interface IInstrumentClassifier : IAsyncDisposable
{
    string ProfileId { get; }
    int SampleRate { get; }
    int WindowSamples { get; }
    int HopSamples { get; }
    ValueTask<IReadOnlyDictionary<InstrumentKind, float>> ClassifyAsync(float[] mono, CancellationToken token);
}
