using LumaLyrics.Core;

namespace LumaLyrics.Core.Tests;

public sealed class CollaborativeLyricsMatchingTests
{
    static readonly Track Duet = new("Own song", "Singer A & Singer B", "Own album", 180000);
    static readonly string[] Lines = Enumerable.Range(1, 6).Select(i => $"This is our original test verse number {i}").ToArray();
    static LyricsCandidate Candidate(Track track, long id = 1) => new(id, track,
        string.Join('\n', Lines.Select((line, i) => $"[{i * 25 / 60:00}:{i * 25 % 60:00}]{line}")), string.Join('\n', Lines), false);

    [Theory]
    [InlineData("Singer A", "Own album", 180450)]
    [InlineData("Singer B feat. Singer A, Composer C", "Own album", 181000)]
    [InlineData("Singer B (ft. Singer A), Composer C", "Own album", 180000)]
    [InlineData("Singer B ＆ Singer A、Singer C", "Own album - EP", 179000)]
    public void SameReleaseWithSubsetCreditsGetsPartialArtistWeight(string artist, string album, double duration)
    {
        var source = Candidate(Duet with { Artist = artist, Album = album, DurationMs = duration });
        var match = Assert.IsType<AutomaticLyricsChoice>(AutomaticLyricsMatcher.Select(Duet, [source]));
        Assert.Equal(.92, match.MatchScore, 8);
        Assert.Same(source, match.Candidate);
        Assert.Equal(source.Track, AutomaticLyricsMatcher.SelectStatic(Duet, [source with { SyncedLyrics = null }])?.Track);

        var exact = Candidate(Duet, 2);
        Assert.Same(exact, AutomaticLyricsMatcher.Select(Duet, [source, exact])?.Candidate);
    }

    [Fact]
    public void AndCanSpellOutTheSameTwoCreditsOnTheSameRelease()
    {
        var joined = Candidate(Duet with { Artist = "Singer B AND Singer A" });
        Assert.Equal(1, MatchRules.Score(Duet, joined).Score, 8);
        Assert.NotNull(AutomaticLyricsMatcher.Select(Duet, [joined]));
        Assert.Null(AutomaticLyricsMatcher.Select(Duet, [joined with { Track = joined.Track with { Album = "Another edition" } }]));
    }

    [Theory]
    [InlineData("Singer A & Singer C", "Own album", 180000d)]
    [InlineData("Singer", "Own album", 180000d)]
    [InlineData("Singer A", "", 180000d)]
    [InlineData("Singer A", "Other album", 180000d)]
    [InlineData("Singer A", "Own album (Live)", 180000d)]
    [InlineData("Singer A", "Own album", 181001d)]
    [InlineData("Singer A", "Own album", null)]
    public void PartialCreditsCannotOverrideConflictingOrMissingRecordingInformation(string artist, string album, double? duration)
    {
        var source = Candidate(Duet with { Artist = artist, Album = album, DurationMs = duration });
        Assert.Null(AutomaticLyricsMatcher.Select(Duet, [source]));
        Assert.Null(AutomaticLyricsMatcher.SelectStatic(Duet, [source with { SyncedLyrics = null }]));
    }

    [Theory]
    [InlineData("Belle and Sebastian", "Belle")]
    [InlineData("AC/DC", "AC")]
    public void NamesWithJoinersDoNotSupplyStandalonePartialCredits(string artist, string fragment)
    {
        var local = Duet with { Artist = artist };
        Assert.Null(AutomaticLyricsMatcher.Select(local, [Candidate(local with { Artist = fragment })]));
    }

    [Fact]
    public void PartialCreditRequiresLyricsBeforeItCanBeAdopted()
    {
        var source = Candidate(Duet with { Artist = "Singer A" });
        Assert.Null(AutomaticLyricsMatcher.Select(Duet, [source with { SyncedLyrics = "[00:01]Incomplete", PlainLyrics = null }]));
        Assert.Null(AutomaticLyricsMatcher.Select(Duet, [source with { Instrumental = true, SyncedLyrics = null, PlainLyrics = null }]));
    }

    static readonly Track LondonCast = new("Angel of Music", "Sarah Brightman & Janet Devenish",
        "The Phantom of the Opera (Original 1986 London Cast)", 140107);
    const string CastCredit = "\"The Phantom Of The Opera\" Original London Cast, Sarah Brightman, Andrew Lloyd Webber, Janet Devenish, Richard Stilgoe, Charles Hart";
    const string Show = "The Phantom of the Opera";
    static readonly Track EntrActe = new("Entr'acte", "Michael Reed & Studio Orchestra", Show + " (Original 1986 London Cast)", 181693);

    [Theory]
    [InlineData("Original London Cast", 183000d)]
    [InlineData("The Original 1986 London Cast Recording", 183693d)]
    [InlineData("\"The Phantom Of The Opera\" Original London Cast", 183000d)]
    public void CollectiveCastCreditCanMatchASpecificPerformerOnTheSameRelease(string credit, double duration)
    {
        var source = Candidate(EntrActe with { Title = "Entr’Acte", Artist = credit, DurationMs = duration }, 30151647);
        var match = Assert.IsType<AutomaticLyricsChoice>(AutomaticLyricsMatcher.Select(EntrActe, [source]));
        Assert.Equal(.90, match.MatchScore, 8);
        Assert.Same(source, match.Candidate);
        Assert.Equal(source.Track, AutomaticLyricsMatcher.SelectStatic(EntrActe, [source with { SyncedLyrics = null }])?.Track);
        var complete = Candidate(EntrActe, 2);
        Assert.Same(complete, AutomaticLyricsMatcher.Select(EntrActe, [source, complete])?.Candidate);
    }

    [Theory]
    [InlineData("Original Broadway Cast", Show + " (Original 1986 London Cast)", 183000d)]
    [InlineData("Original 1990 London Cast", Show + " (Original 1986 London Cast)", 183000d)]
    [InlineData("Another Show Original London Cast", Show + " (Original 1986 London Cast)", 183000d)]
    [InlineData("Original Cast", Show + " (Original 1986 London Cast)", 183000d)]
    [InlineData("Various Artists", Show + " (Original 1986 London Cast)", 183000d)]
    [InlineData("Original London Cast", Show, 183000d)]
    [InlineData("Original London Cast", Show + " (Original London Cast)", 183000d)]
    [InlineData("Original London Cast", "Highlights from " + Show + " (Original 1986 London Cast)", 183000d)]
    [InlineData("Original London Cast", Show + " (Original 1986 London Cast)", 183694d)]
    [InlineData("Original London Cast", Show + " (Original 1986 London Cast)", null)]
    public void CollectiveCastCreditNeedsTheCompleteReleaseAndAPreciseDuration(string credit, string album, double? duration)
    {
        var source = Candidate(EntrActe with { Artist = credit, Album = album, DurationMs = duration });
        Assert.Null(AutomaticLyricsMatcher.Select(EntrActe, [source]));
        Assert.Null(AutomaticLyricsMatcher.SelectStatic(EntrActe, [source with { SyncedLyrics = null }]));
    }

    [Fact]
    public void CollectiveCastCreditCannotOverrideAnExplicitlyConflictingCast()
    {
        var local = EntrActe with { Artist = "Michael Reed & Original Broadway Cast" };
        var source = Candidate(EntrActe with { Artist = "Original London Cast", DurationMs = 183000 });
        Assert.Null(AutomaticLyricsMatcher.Select(local, [source]));
    }

    [Fact]
    public void CollectiveCastCreditCannotConfirmAnInstrumentalOrBypassLyricsReview()
    {
        var source = Candidate(EntrActe with { Artist = "Original London Cast", DurationMs = 183000 });
        Assert.Null(AutomaticLyricsMatcher.Select(EntrActe, [source with { Instrumental = true, SyncedLyrics = null, PlainLyrics = null }]));
        Assert.Null(AutomaticLyricsMatcher.Select(EntrActe, [source with { SyncedLyrics = "[00:01]Incomplete", PlainLyrics = null }]));
        var subtitle = source with { Track = source.Track with { Title = "Entr'acte: Act Two: Six Months Later" } };
        Assert.Null(AutomaticLyricsMatcher.Select(EntrActe, [subtitle]));
    }

    [Fact]
    public void LondonRecordingMatchesSourceWithBothSingersAndSameCastCredit()
    {
        var cast = Candidate(LondonCast with { Artist = CastCredit, Album = Show, DurationMs = 140000 }, 37130570);
        var highlights = Candidate(LondonCast with { Artist = "Sarah Brightman and Janet Devenish",
            Album = "The Phantom Of The Opera (Highlights) [Original London Cast]", DurationMs = 140640 }, 21582975);
        Assert.False(AutomaticLyricsMatcher.Decide(LondonCast, highlights).Accepted);
        Assert.Same(cast, AutomaticLyricsMatcher.Select(LondonCast, [highlights, cast])?.Candidate);
        var explicitlyNamedAlbum = cast with { Track = cast.Track with { Artist = "Sarah Brightman", Album = "The Phantom of the Opera [Original London Cast Recording]" } };
        Assert.True(AutomaticLyricsMatcher.Decide(LondonCast, explicitlyNamedAlbum).Accepted);
    }

    [Theory]
    [InlineData("Sarah Brightman, Janet Devenish, Composer C", Show)]
    [InlineData("The Phantom of the Opera Original London Cast, Sarah Brightman", Show)]
    [InlineData("The Phantom of the Opera Original Broadway Cast, Sarah Brightman, Janet Devenish", Show)]
    [InlineData("The Phantom of the Opera Original 1990 London Cast, Sarah Brightman, Janet Devenish", Show)]
    [InlineData("Another Show Original London Cast, Sarah Brightman, Janet Devenish", Show)]
    [InlineData(CastCredit + ", The Phantom of the Opera Original Broadway Cast", Show)]
    [InlineData(CastCredit, "The Phantom of the Opera (Original 1990 London Cast)")]
    [InlineData(CastCredit, "The Phantom of the Opera (Highlights)")]
    [InlineData(CastCredit, "The Phantom of the Opera (2026 Remix)")]
    public void AGenericOrConflictingEditionCannotUseCastCreditAsProof(string artist, string album)
    {
        var source = Candidate(LondonCast with { Artist = artist, Album = album });
        Assert.Null(AutomaticLyricsMatcher.Select(LondonCast, [source]));
    }
}
