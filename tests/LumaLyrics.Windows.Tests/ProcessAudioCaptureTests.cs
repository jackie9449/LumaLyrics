using LumaLyrics.Core;
using LumaLyrics.Windows;

namespace LumaLyrics.Windows.Tests;

public sealed class ProcessAudioCaptureTests
{
    [Fact]
    public async Task CancelledCaptureNeverOpensTheNativeClient()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await using var capture = new ProcessAudioCapture();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var chunk in capture.ReadAsync(new(1, DateTime.UnixEpoch, "test"), cancel.Token))
                Assert.Fail("Cancelled capture yielded data.");
        });
        Assert.Equal(0, capture.Packets);
    }

    [Fact]
    public async Task CaptureRejectsAnUnverifiedTarget()
    {
        await using var capture = new ProcessAudioCapture();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var chunk in capture.ReadAsync(new((uint)Environment.ProcessId, DateTime.UnixEpoch, "test"), default))
                Assert.Fail("Unverified capture yielded data.");
        });
    }

    [Fact]
    public async Task DisposeIsIdempotentAndPreventsNewCapture()
    {
        var capture = new ProcessAudioCapture();
        await capture.DisposeAsync();
        await capture.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            await foreach (var chunk in capture.ReadAsync(new(1, DateTime.UnixEpoch, "test"), default))
                Assert.Fail("Disposed capture yielded data.");
        });
    }
}
