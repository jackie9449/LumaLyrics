using System.Runtime.CompilerServices;
using LumaLyrics.Core;

namespace LumaLyrics.Infrastructure.Tests;

public sealed class InstrumentFileAnalysisTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "Luma-file-analysis-" + Guid.NewGuid().ToString("N"));
    readonly string source;
    readonly InstrumentRecordingKey key = new("explicit-local-file", "authored-source", 1500, null);
    public InstrumentFileAnalysisTests()
    { Directory.CreateDirectory(root); source = Path.Combine(root, "authored.wav"); File.WriteAllBytes(source, [1, 2, 3, 4]); }
    sealed class AuthoredClassifier : IInstrumentClassifier
    {
        public string ProfileId => "authored-fixture-not-a-production-model";
        public int SampleRate => 16000;
        public int WindowSamples => 15600;
        public int HopSamples => 7680;
        public List<float[]> Inputs = [];
        public ValueTask<IReadOnlyDictionary<InstrumentKind, float>> ClassifyAsync(float[] mono, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Inputs.Add(mono.ToArray()); return ValueTask.FromResult<IReadOnlyDictionary<InstrumentKind, float>>(new Dictionary<InstrumentKind, float> { [InstrumentKind.Piano] = .9f }); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    static InstrumentEngine Engine(AuthoredClassifier classifier) => new(classifier,
        new Dictionary<InstrumentKind, ActivityParameters> { [InstrumentKind.Piano] = new(.7f, .4f, 1, 0) });
    static async IAsyncEnumerable<PcmChunk> Audio(int count, int packet, [EnumeratorCancellation] CancellationToken token)
    {
        for (int index = 0; index < count; index += packet)
        {
            token.ThrowIfCancellationRequested();
            int length = Math.Min(packet, count - index);
            yield return new(Enumerable.Range(index, length).Select(i => (float)(.1 * Math.Sin(i * .1))).ToArray(), 16000, 1, index, false, false);
            await Task.Yield();
        }
    }
    [Fact]
    public async Task CompleteAnalysisUsesActualSamplesHashAndOwnedTailPadding()
    {
        var classifier = new AuthoredClassifier(); await using var engine = Engine(classifier);
        using var repository = new InstrumentRepository(root);
        var analysis = new InstrumentFileAnalysis(engine, repository, (_, token) => Audio(24000, 137, token));
        var timeline = await analysis.AnalyzeAsync(source, key, null, default);
        Assert.True(timeline.Complete); Assert.Equal(1500, timeline.Recording.DurationMs);
        Assert.NotNull(timeline.Recording.AudioSha256); Assert.Equal(InstrumentKind.Piano, Assert.Single(timeline.Roster));
        Assert.All(timeline.Coverage, span => Assert.True(span.EndMs <= 1500));
        Assert.Equal(source, repository.ReadFileBinding(timeline.Recording)!.FullPath);
        var last = classifier.Inputs.Last(); Assert.Equal(15600, last.Length); Assert.Equal(0f, last[^1]);
        var second = await analysis.AnalyzeAsync(source, timeline.Recording, null, default);
        Assert.Equal(2, second.Revision); Assert.Equal(timeline.Scores.Length, second.Scores.Length);
    }
    [Fact]
    public async Task PacketSizesCannotChangeClassifierInputsOrCacheCoverage()
    {
        var a = new AuthoredClassifier(); var b = new AuthoredClassifier();
        await using var first = Engine(a); await using var second = Engine(b);
        using var repository = new InstrumentRepository(root);
        var firstAnalysis = new InstrumentFileAnalysis(first, repository, (_, token) => Audio(24000, 24000, token));
        var secondAnalysis = new InstrumentFileAnalysis(second, repository, (_, token) => Audio(24000, 137, token));
        var x = await firstAnalysis.AnalyzeAsync(source, key, null, default);
        var y = await secondAnalysis.AnalyzeAsync(source, key, null, default);
        Assert.Equal(a.Inputs.Count, b.Inputs.Count);
        for (int index = 0; index < a.Inputs.Count; index++) Assert.Equal(a.Inputs[index], b.Inputs[index]);
        Assert.Equal(x.Coverage, y.Coverage);
    }
    [Fact]
    public async Task CancellationAndTruncationLeavePreviousCommittedAnalysisUntouched()
    {
        var classifier = new AuthoredClassifier(); await using var engine = Engine(classifier);
        using var repository = new InstrumentRepository(root);
        var valid = new InstrumentFileAnalysis(engine, repository, (_, token) => Audio(24000, 1600, token));
        var saved = await valid.AnalyzeAsync(source, key, null, default);
        var shortFile = new InstrumentFileAnalysis(engine, repository, (_, token) => Audio(8000, 1000, token));
        await Assert.ThrowsAsync<InvalidDataException>(() => shortFile.AnalyzeAsync(source, saved.Recording, null, default));
        using var cancel = new CancellationTokenSource();
        async IAsyncEnumerable<PcmChunk> Cancelled([EnumeratorCancellation] CancellationToken token)
        {
            await foreach (var chunk in Audio(24000, 1600, token))
            { yield return chunk; if (classifier.Inputs.Count > 1) cancel.Cancel(); }
        }
        var cancelled = new InstrumentFileAnalysis(engine, repository, (_, token) => Cancelled(token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled.AnalyzeAsync(source, saved.Recording, null, cancel.Token));
        Assert.Equal(saved.Revision, repository.Load(saved.Recording, engine.ProfileId)!.Revision);
    }
    [Fact]
    public async Task WrongContentOrDurationCannotCreateAnAnalysis()
    {
        var classifier = new AuthoredClassifier(); await using var engine = Engine(classifier);
        using var repository = new InstrumentRepository(root);
        var analysis = new InstrumentFileAnalysis(engine, repository, (_, token) => Audio(24000, 1600, token));
        await Assert.ThrowsAsync<InvalidDataException>(() => analysis.AnalyzeAsync(source, key with { AudioSha256 = new('a', 64) }, null, default));
        await Assert.ThrowsAsync<InvalidDataException>(() => analysis.AnalyzeAsync(source, key with { DurationMs = 1000 }, null, default));
        Assert.Empty(repository.FindRecordings(key.Source, key.PersistentId));
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
