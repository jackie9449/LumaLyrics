using System.Diagnostics;
using LumaLyrics.Core;

namespace LumaLyrics.Core.Tests;

public sealed class InstrumentPresentationTests
{
    [Fact]
    public void DetailedVocabularyRetainsPersistedIdsAndOmitsOnlyKnownFamilyFallbacks()
    {
        Assert.Equal(Enumerable.Range(0, 8), new[] { InstrumentKind.Drums, InstrumentKind.Bass, InstrumentKind.Guitar, InstrumentKind.Piano,
            InstrumentKind.Strings, InstrumentKind.Brass, InstrumentKind.Woodwind, InstrumentKind.Synth }.Select(kind => (int)kind));
        Assert.Equal(new[] { InstrumentKind.Piano, InstrumentKind.Cello, InstrumentKind.Trumpet, InstrumentKind.Violin },
            InstrumentVocabulary.VisibleKinds([InstrumentKind.Strings, InstrumentKind.Cello, InstrumentKind.Violin, InstrumentKind.Brass, InstrumentKind.Trumpet, InstrumentKind.Piano]));
        Assert.Equal(new[] { InstrumentKind.Woodwind }, InstrumentVocabulary.VisibleKinds([InstrumentKind.Woodwind]));
        Assert.Equal("钢琴", InstrumentVocabulary.Name(InstrumentKind.Piano));
        Assert.Equal("小提琴", InstrumentVocabulary.Name(InstrumentKind.Violin));
        Assert.Equal(Enum.GetValues<InstrumentKind>().Length, Enum.GetValues<InstrumentKind>().Select(InstrumentVocabulary.Name).Distinct().Count());
        Assert.Throws<ArgumentOutOfRangeException>(() => InstrumentVocabulary.Name((InstrumentKind)999));
    }

    [Fact]
    public void PerSongRostersContainOnlyObservedInstrumentsAndResetForAnotherSong()
    {
        var kinds = new[] { InstrumentKind.Flute, InstrumentKind.Violin, InstrumentKind.Guitar, InstrumentKind.Piano };
        var view = new InstrumentPresentation(kinds.ToDictionary(kind => kind, _ => new ActivityParameters(.5f, .3f, 1, 0)));
        view.Begin(Epoch, false);
        view.Apply(new(Epoch, T(1000), null, null, "fixture", new Dictionary<InstrumentKind, float>
            { [InstrumentKind.Flute] = .9f, [InstrumentKind.Violin] = .8f, [InstrumentKind.Guitar] = .1f, [InstrumentKind.Piano] = .1f }));
        Assert.Equal(new[] { InstrumentKind.Flute, InstrumentKind.Violin }, view.View(PlayState.Playing, T(1000)).Icons.Keys.Order());
        view.Begin(Epoch with { Revision = 2 }, false);
        Assert.Empty(view.View(PlayState.Playing, T(1100)).Icons);
        view.Apply(new(Epoch with { Revision = 2 }, T(1500), null, null, "fixture", new Dictionary<InstrumentKind, float>
            { [InstrumentKind.Guitar] = .9f, [InstrumentKind.Piano] = .7f }));
        Assert.Equal(new[] { InstrumentKind.Guitar, InstrumentKind.Piano }, view.View(PlayState.Playing, T(1500)).Icons.Keys.Order());
    }

    [Fact]
    public void WholeFileRosterIsAvailableAtStartAndDetailedFallbacksStayConsistent()
    {
        var timeline = new InstrumentTimeline(new("fixture", "ensemble", 10000, null), "fixture", [new(0, 10000)],
            [new(0, 10000, new() { [InstrumentKind.Strings] = .9f, [InstrumentKind.Violin] = .9f, [InstrumentKind.Cello] = .8f })],
            [new(InstrumentKind.Violin, 2000, 8000), new(InstrumentKind.Cello, 5000, 9000)],
            [InstrumentKind.Strings, InstrumentKind.Violin, InstrumentKind.Cello], true, 1);
        var first = InstrumentPresentation.FromTimeline(timeline, 0, PlayState.Playing);
        Assert.True(first.Complete);
        Assert.Equal(new[] { InstrumentKind.Cello, InstrumentKind.Violin }, first.Icons.Keys.Order());
        Assert.All(first.Icons.Values, value => Assert.Equal(InstrumentVisualState.Dim, value));
        var later = InstrumentPresentation.FromTimeline(timeline, 3000, PlayState.Playing);
        Assert.Equal(InstrumentVisualState.Active, later.Icons[InstrumentKind.Violin]);
        Assert.Equal(InstrumentVisualState.Dim, later.Icons[InstrumentKind.Cello]);
        Assert.All(InstrumentPresentation.FromTimeline(timeline, null, PlayState.Playing).Icons.Values,
            value => Assert.Equal(InstrumentVisualState.Unknown, value));
    }
    [Fact]
    public void GuitarBassAndDrumsAreIndependentAndCanBeActiveTogether()
    {
        var kinds = new[] { InstrumentKind.Guitar, InstrumentKind.Bass, InstrumentKind.Drums };
        var view = new InstrumentPresentation(kinds.ToDictionary(kind => kind, _ => new ActivityParameters(.5f, .3f, 2, 0)));
        view.Begin(Epoch, false);
        InstrumentFrame Scores(double time, float guitar, float bass, float drums) => new(Epoch, T(time), null, null, "fixture",
            new Dictionary<InstrumentKind, float> { [InstrumentKind.Guitar] = guitar, [InstrumentKind.Bass] = bass, [InstrumentKind.Drums] = drums });
        view.Apply(Scores(1000, .9f, .1f, .9f)); view.Apply(Scores(1500, .9f, .1f, .9f));
        Assert.Equal(2, view.View(PlayState.Playing, T(1500)).Icons.Count);
        view.Apply(Scores(2000, .9f, .9f, .9f)); view.Apply(Scores(2500, .9f, .9f, .9f));
        Assert.Equal(3, view.View(PlayState.Playing, T(2500)).Icons.Count);
        Assert.All(view.View(PlayState.Playing, T(2500)).Icons.Values, value => Assert.Equal(InstrumentVisualState.Active, value));
        view.Apply(Scores(3000, .1f, .9f, .1f));
        Assert.Equal(InstrumentVisualState.Dim, view.View(PlayState.Playing, T(3000)).Icons[InstrumentKind.Guitar]);
        Assert.Equal(InstrumentVisualState.Active, view.View(PlayState.Playing, T(3000)).Icons[InstrumentKind.Bass]);
        Assert.Equal(InstrumentVisualState.Dim, view.View(PlayState.Playing, T(3000)).Icons[InstrumentKind.Drums]);
    }
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
