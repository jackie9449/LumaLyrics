using System.Diagnostics;
using LumaLyrics.Core;

namespace LumaLyrics.Core.Tests;

public sealed class InstrumentPresentationTests
{
    static long T(double ms) => (long)(ms * Stopwatch.Frequency / 1000);
    static readonly InstrumentEpoch Epoch = new("fixture", 1, 1, 0);
    static InstrumentPresentation Presenter() => new(new Dictionary<InstrumentKind, ActivityParameters> { [InstrumentKind.Piano] = new(.7f, .4f, 2, 500) });
    static InstrumentFrame Frame(double ms, float score) => new(Epoch, T(ms), null, null, "fixture", new Dictionary<InstrumentKind, float> { [InstrumentKind.Piano] = score });
    [Fact]
    public void RosterRequiresStableEvidenceAndSurvivesPauseAndSilentFrames()
    {
        var view = Presenter(); view.Begin(Epoch, false);
        view.Apply(Frame(1000, .9f)); Assert.Empty(view.View(PlayState.Playing, T(1000)).Icons);
        view.Apply(Frame(1500, .9f)); Assert.Equal(InstrumentVisualState.Active, view.View(PlayState.Playing, T(1500)).Icons[InstrumentKind.Piano]);
        Assert.Equal(InstrumentVisualState.Dim, view.View(PlayState.Paused, T(1500)).Icons[InstrumentKind.Piano]);
        view.Apply(Frame(2000, .1f)); Assert.Equal(InstrumentVisualState.Dim, view.View(PlayState.Playing, T(2000)).Icons[InstrumentKind.Piano]);
        Assert.Equal(InstrumentVisualState.Unknown, view.View(PlayState.Playing, T(5000)).Icons[InstrumentKind.Piano]);
    }
    [Fact]
    public void OldRecordingAndDiscontinuityCannotReuseAnActiveState()
    {
        var view = Presenter(); view.Begin(Epoch, false); view.Apply(Frame(1000, .9f)); view.Apply(Frame(1500, .9f));
        var seek = Epoch with { Discontinuity = 1 }; view.Begin(seek, true);
        Assert.False(view.Apply(Frame(1600, .9f)));
        Assert.Equal(InstrumentVisualState.Unknown, view.View(PlayState.Playing, T(1600)).Icons[InstrumentKind.Piano]);
        view.Begin(Epoch with { Revision = 2 }, false); Assert.Empty(view.View(PlayState.Playing, T(1700)).Icons);
    }
    [Fact]
    public void CachedGapsRemainUnknownAndPausedIconsNeverGlow()
    {
        var key = new InstrumentRecordingKey("fixture", "id", 30000, null);
        var timeline = new InstrumentTimeline(key, "fixture", [new(0, 10000)],
            [new(0, 10000, new() { [InstrumentKind.Piano] = .9f })], [new(InstrumentKind.Piano, 1000, 5000)], [InstrumentKind.Piano], false, 1);
        Assert.Equal(InstrumentVisualState.Active, InstrumentPresentation.FromTimeline(timeline, 2000, PlayState.Playing).Icons[InstrumentKind.Piano]);
        Assert.Equal(InstrumentVisualState.Unknown, InstrumentPresentation.FromTimeline(timeline, 20000, PlayState.Playing).Icons[InstrumentKind.Piano]);
        Assert.Equal(InstrumentVisualState.Dim, InstrumentPresentation.FromTimeline(timeline, 2000, PlayState.Paused).Icons[InstrumentKind.Piano]);
    }
}
