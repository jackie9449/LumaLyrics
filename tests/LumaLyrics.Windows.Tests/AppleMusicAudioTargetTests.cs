using LumaLyrics.Core;

namespace LumaLyrics.Windows.Tests;

public sealed class AppleMusicAudioTargetTests
{
    static readonly AudioTarget Owner = new(10, DateTime.UnixEpoch, "fixture-package", "fixture-package!AppleMusic");
    static readonly AudioTarget Renderer = Owner with { ProcessId = 20 };

    [Fact]
    public void FindsAnActivePackageRendererOutsideTheUiProcessTree()
    {
        AppleMusicRenderSession[] sessions = [new(Owner, "AppleMusic", 0, 0), new(Renderer, "AMPLibraryAgent", 1, .5f)];
        Assert.Equal(Renderer, AppleMusicAudioTargetResolver.SelectRenderTarget(Owner, sessions));
    }

    [Fact]
    public void DuplicateEndpointSessionsDoNotCreateAmbiguousProcesses()
    {
        AppleMusicRenderSession[] sessions = [new(Renderer, "AMPLibraryAgent", 1, .5f), new(Renderer, "AMPLibraryAgent", 1, .2f)];
        Assert.Equal(Renderer, AppleMusicAudioTargetResolver.SelectRenderTarget(Owner, sessions));
    }

    [Fact]
    public void AmbiguousPackageRenderersAreNotChosenByVolume()
    {
        AppleMusicRenderSession[] sessions = [new(Renderer, "renderer-a", 1, .5f), new(Renderer with { ProcessId = 30 }, "renderer-b", 1, .9f)];
        Assert.Null(AppleMusicAudioTargetResolver.SelectRenderTarget(Owner, sessions));
    }

    [Fact]
    public void AForeignPackageOrDifferentSourceCannotBecomeTheTarget()
    {
        AppleMusicRenderSession[] sessions = [new(Renderer with { PackageFamily = "other-package" }, "other", 1, .9f),
            new(Renderer with { SourceAppId = "other-source" }, "other", 1, .9f), new(Owner, "AppleMusic", 0, 0)];
        Assert.Null(AppleMusicAudioTargetResolver.SelectRenderTarget(Owner, sessions));
    }
}
