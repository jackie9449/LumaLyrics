using LumaLyrics.Core;

namespace LumaLyrics.Infrastructure.Tests;

public sealed class InstrumentEngineTests
{
    sealed class DeferredClassifier : IInstrumentClassifier
    {
        public TaskCompletionSource<IReadOnlyDictionary<InstrumentKind, float>> Pending { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string ProfileId => "fixture";
        public int SampleRate => 1000;
        public int WindowSamples => 1000;
        public int HopSamples => 500;
        public int Calls;
        public bool Disposed;
        public ValueTask<IReadOnlyDictionary<InstrumentKind, float>> ClassifyAsync(float[] mono, CancellationToken token)
        { Calls++; return new(Pending.Task); }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
    static readonly InstrumentEpoch A = new("AppleMusic", 1, 1, 0);
    static InstrumentWindow Window() => new(A, 0, 1000, 0, 1000, 1000, new float[1000]);

    [Fact]
    public async Task ASlowOldRecordingCannotPublishEvenWhenInferenceIgnoresCancellation()
    {
        var classifier = new DeferredClassifier();
        await using var engine = new InstrumentEngine(classifier);
        InstrumentEpoch current = A;
        InstrumentFrame? accepted = null;
        var session = new InstrumentSession(engine, () => current, frame => accepted = frame);
        var work = session.SubmitAsync(Window(), default);
        current = A with { Revision = 2 };
        classifier.Pending.SetResult(new Dictionary<InstrumentKind, float> { [InstrumentKind.Piano] = .9f });
        await work;
        Assert.Null(accepted);
    }

    [Fact]
    public async Task AlreadyStaleWindowsNeverStartInference()
    {
        var classifier = new DeferredClassifier();
        await using var engine = new InstrumentEngine(classifier);
        var session = new InstrumentSession(engine, () => A with { Discontinuity = 1 }, _ => Assert.Fail("Stale window was accepted."));
        await session.SubmitAsync(Window(), default);
        Assert.Equal(0, classifier.Calls);
    }

    [Fact]
    public async Task OutputScoresAreValidatedAndCopied()
    {
        var classifier = new DeferredClassifier();
        await using var engine = new InstrumentEngine(classifier);
        var values = new Dictionary<InstrumentKind, float> { [InstrumentKind.Piano] = .9f };
        classifier.Pending.SetResult(values);
        var frame = await engine.AnalyzeAsync(Window(), default);
        values[InstrumentKind.Piano] = .1f;
        Assert.Equal(.9f, frame.Scores[InstrumentKind.Piano]);
    }

    [Fact]
    public async Task InvalidScoresAndPostDisposeCallsAreRejected()
    {
        var classifier = new DeferredClassifier();
        var engine = new InstrumentEngine(classifier);
        classifier.Pending.SetResult(new Dictionary<InstrumentKind, float> { [InstrumentKind.Piano] = float.NaN });
        await Assert.ThrowsAsync<InvalidDataException>(() => engine.AnalyzeAsync(Window(), default));
        await engine.DisposeAsync();
        await engine.DisposeAsync();
        Assert.True(classifier.Disposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => engine.AnalyzeAsync(Window(), default));
    }
}
