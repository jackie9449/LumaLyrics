using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using LumaLyrics.Core;

namespace LumaLyrics.Infrastructure.Tests;

public sealed class InstrumentLiveAnalysisTests
{
    static readonly InstrumentEpoch A = new("AppleMusic", 1, 1, 0);
    static readonly AudioTarget Target = new(123, DateTime.UnixEpoch, "fixture", "AppleMusic");
    sealed class Capture : IProcessAudioCapture
    {
        public Channel<PcmChunk> Input { get; } = Channel.CreateUnbounded<PcmChunk>();
        public async IAsyncEnumerable<PcmChunk> ReadAsync(AudioTarget target, [EnumeratorCancellation] CancellationToken token)
        { await foreach (var chunk in Input.Reader.ReadAllAsync(token)) yield return chunk; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    sealed class Slow : IInstrumentClassifier
    {
        public string ProfileId => "fixture";
        public int SampleRate => 8000;
        public int WindowSamples => 800;
        public int HopSamples => 400;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<IReadOnlyDictionary<InstrumentKind, float>> ClassifyAsync(float[] mono, CancellationToken token)
        { Entered.TrySetResult(); await Release.Task; return new Dictionary<InstrumentKind, float> { [InstrumentKind.Strings] = .9f }; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    static PcmChunk Chunk(int samples, long ticks, bool gap = false) => new(Enumerable.Repeat(.1f, samples).ToArray(), 8000, 1, ticks, gap, false);

    [Fact]
    public async Task SlowInferenceKeepsOnlyTheLatestTwoWaitingWindows()
    {
        var classifier = new Slow(); await using var engine = new InstrumentEngine(classifier);
        var guard = new InstrumentEpochGuard(); guard.Set(A);
        var analysis = new InstrumentLiveAnalysis(engine, guard); var capture = new Capture();
        var accepted = new List<InstrumentFrame>();
        long origin = Stopwatch.GetTimestamp() - Stopwatch.Frequency;
        var work = analysis.RunAsync(capture, Target, accepted.Add, default);
        capture.Input.Writer.TryWrite(Chunk(800, origin));
        await classifier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        capture.Input.Writer.TryWrite(Chunk(7200, origin + Stopwatch.Frequency / 10)); capture.Input.Writer.TryComplete();
        for (int i = 0; i < 200 && analysis.DroppedWindows < 10; i++) await Task.Delay(5);
        classifier.Release.TrySetResult(); await work.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(analysis.HasEffectiveSignal); Assert.True(analysis.DroppedWindows >= 10);
        Assert.InRange(accepted.Count, 1, 3); Assert.True(accepted[^1].EndTicks >= origin + Stopwatch.Frequency * .95);
        Assert.All(accepted, frame => Assert.Null(frame.StartMs));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InFlightInferenceCannotEscapeARevokedEpochOrCancellation(bool cancel)
    {
        var classifier = new Slow(); await using var engine = new InstrumentEngine(classifier);
        var guard = new InstrumentEpochGuard(); guard.Set(A);
        var analysis = new InstrumentLiveAnalysis(engine, guard); var capture = new Capture();
        using var stop = new CancellationTokenSource(); int accepted = 0;
        var work = analysis.RunAsync(capture, Target, _ => accepted++, stop.Token);
        capture.Input.Writer.TryWrite(Chunk(800, Stopwatch.GetTimestamp() - Stopwatch.Frequency / 10));
        await classifier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (cancel) stop.Cancel(); else guard.Set(A with { Revision = 2 });
        capture.Input.Writer.TryComplete(); classifier.Release.TrySetResult();
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work.WaitAsync(TimeSpan.FromSeconds(5)));
        else await work.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, accepted);
    }

    [Fact]
    public async Task DiscontinuityRevokesTheInflightWindowAndStartsFreshContext()
    {
        var classifier = new Slow(); await using var engine = new InstrumentEngine(classifier);
        var guard = new InstrumentEpochGuard(); guard.Set(A);
        var analysis = new InstrumentLiveAnalysis(engine, guard); var capture = new Capture();
        var accepted = new List<InstrumentFrame>();
        var work = analysis.RunAsync(capture, Target, accepted.Add, default);
        capture.Input.Writer.TryWrite(Chunk(800, Stopwatch.GetTimestamp() - Stopwatch.Frequency / 2));
        await classifier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        capture.Input.Writer.TryWrite(Chunk(800, Stopwatch.GetTimestamp() - Stopwatch.Frequency / 10, true)); capture.Input.Writer.TryComplete();
        for (int i = 0; i < 200 && guard.Current?.Discontinuity == 0; i++) await Task.Delay(5);
        classifier.Release.TrySetResult(); await work.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(accepted); Assert.Equal(1, accepted[0].Epoch.Discontinuity);
    }
}
