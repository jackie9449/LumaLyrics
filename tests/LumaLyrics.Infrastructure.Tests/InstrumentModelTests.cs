using System.Net;
using System.Security.Cryptography;
using LumaLyrics.Core;

namespace LumaLyrics.Infrastructure.Tests;

public sealed class InstrumentModelTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "Luma-instrument-model-" + Guid.NewGuid().ToString("N"));
    static readonly byte[] Weights = [1, 2, 3, 4];
    static InstrumentModelManifest Manifest() => new()
    {
        Id = "fixture", DownloadUri = new("https://storage.googleapis.com/audioset/fixture.onnx"),
        Bytes = Weights.Length, Sha256 = Convert.ToHexString(SHA256.HashData(Weights)).ToLowerInvariant(),
        SourceWeightsSha256 = new('a', 64), SourceRevision = new('b', 40),
        LicenseUri = new("https://www.tensorflow.org/hub/tutorials/yamnet"),
        SampleRate = 16000, WindowSamples = 15600, HopSamples = 7680,
        InputName = "waveform", OutputName = "scores", OutputClassCount = 521,
        PreprocessingVersion = "fixture-1", LabelMap = new() { [0] = InstrumentKind.Piano },
        Thresholds = new() { [InstrumentKind.Piano] = new(.65f, .4f, 2, 750) }
    };

    [Fact]
    public async Task ChangedWeightsAreRejected()
    {
        Directory.CreateDirectory(root);
        string file = Path.Combine(root, "fixture.onnx");
        await File.WriteAllBytesAsync(file, [1, 2, 3]);
        var store = new InstrumentModelStore(root);
        Assert.False(await store.ValidateAsync(file, Manifest().Sha256, default));
    }

    [Fact]
    public async Task VerifiedDownloadIsAtomicAndReusedWithoutNetworking()
    {
        var handler = new FakeHandler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Weights) });
        using var http = new HttpClient(handler);
        var store = new InstrumentModelStore(root, http);
        string file = await store.EnsureAsync(Manifest(), default);
        Assert.Equal(Weights, await File.ReadAllBytesAsync(file));
        Assert.Equal(file, await store.EnsureAsync(Manifest(), default));
        Assert.Equal(1, handler.Calls);
        Assert.Empty(Directory.GetFiles(root, "*.partial", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task CorruptDownloadDoesNotLeaveAModelOrDestroyEarlierVersion()
    {
        var handler = new FakeHandler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent([4, 3, 2, 1]) });
        using var http = new HttpClient(handler);
        var store = new InstrumentModelStore(root, http);
        Directory.CreateDirectory(root);
        string previous = Path.Combine(root, "older.onnx");
        await File.WriteAllBytesAsync(previous, Weights);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.EnsureAsync(Manifest(), default));
        Assert.Equal(Weights, await File.ReadAllBytesAsync(previous));
        Assert.Empty(Directory.GetFiles(root, "*.partial", SearchOption.AllDirectories));
        Assert.Single(Directory.GetFiles(root, "*.onnx", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task RedirectToAnUnapprovedHostIsRejectedBeforeASecondRequest()
    {
        var handler = new FakeHandler(_ => new(HttpStatusCode.Redirect)
        {
            Headers = { Location = new Uri("https://example.com/model.onnx") }
        });
        using var http = new HttpClient(handler);
        var store = new InstrumentModelStore(root, http);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.EnsureAsync(Manifest(), default));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task OversizedAndAlreadyCancelledDownloadsAreNotInstalled()
    {
        var handler = new FakeHandler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[5]) });
        using var http = new HttpClient(handler);
        var store = new InstrumentModelStore(root, http);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.EnsureAsync(Manifest(), default));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.EnsureAsync(Manifest(), cancel.Token));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public void ProfileIncludesLabelsThresholdsAndPreprocessing()
    {
        var first = Manifest();
        Assert.NotEqual(first.ProfileId, (first with { PreprocessingVersion = "fixture-2" }).ProfileId);
        Assert.NotEqual(first.ProfileId, (first with { LabelMap = new() { [1] = InstrumentKind.Piano } }).ProfileId);
        Assert.NotEqual(first.ProfileId, (first with { Thresholds = new() { [InstrumentKind.Piano] = new(.7f, .4f, 2, 750) } }).ProfileId);
        Assert.Throws<ArgumentException>(() => (first with { Id = "../escape" }).Validate());
        Assert.Throws<ArgumentException>(() => (first with { Sha256 = "bad" }).Validate());
    }

    sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Calls++;
            var response = respond(request); response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
