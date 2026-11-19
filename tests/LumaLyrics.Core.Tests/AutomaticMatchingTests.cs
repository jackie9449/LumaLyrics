using LumaLyrics.Core;
namespace LumaLyrics.Core.Tests;
public class AutomaticMatchingTests
{
    static readonly Track track=new("Original Song","Artist","Album",180000);
    static readonly string plain=string.Join("\n",Enumerable.Range(1,6).Select(i=>$"This is our original test verse number {i}"));
    static string Timed(bool truncate=false)=>string.Join("\n",Enumerable.Range(1,truncate?2:6).Select(i=>$"[{(i-1)*25/60:00}:{(i-1)*25%60:00}]This is our original test verse number {i}"));
    static LyricsCandidate Candidate(long id=1)=>new(id,track,Timed(),plain,false);
    [Fact] public void AppleCombinedMetadataIsResolvedWithoutChangingExplicitAlbum()
    {
        Assert.Equal(track,TrackMetadata.FromAppleSession(track with{Artist="Artist — Album",Album=""}));Assert.Equal(track,TrackMetadata.FromAppleSession(track));
    }
    [Fact] public void DuplicateCorrectRecordingsAreSelectedAutomatically()
    {
        var choice=AutomaticLyricsMatcher.Select(track,[Candidate(2),Candidate(1)]);Assert.NotNull(choice);Assert.Equal(1,choice.Candidate.Id);Assert.Equal(1,choice.Assessment.TextCoverage);
    }
    [Fact] public void TruncatedLyricsAreRejectedAndFullVersionWins()
    {
        var bad=Candidate(1) with{SyncedLyrics=Timed(true)};Assert.False(AutomaticLyricsMatcher.Assess(bad).Accepted);Assert.Equal(2,AutomaticLyricsMatcher.Select(track,[bad,Candidate(2)])!.Candidate.Id);
    }
    [Fact] public void TailCoverageAndOutOfRangeTimesFail()
    {
        var early=Candidate() with{SyncedLyrics=Timed().Replace("02:05","00:25").Replace("01:40","00:20").Replace("01:15","00:15").Replace("00:50","00:10")};Assert.False(AutomaticLyricsMatcher.Assess(early).Accepted);
        Assert.False(AutomaticLyricsMatcher.Assess(Candidate() with{SyncedLyrics=Timed()+"\n[04:00]out of range"}).Accepted);
    }
    [Fact] public void MissingVersesFailPlainTextCoverage()
    {
        var bad=Candidate() with{PlainLyrics=plain+"\nAn entirely missing verse with several additional sentences about a different part of our original test story"};Assert.False(AutomaticLyricsMatcher.Assess(bad).Accepted);
    }
    [Fact] public void ArtistDurationAndRecordingVersionStillProtectAgainstWrongSong()
    {
        Assert.Null(AutomaticLyricsMatcher.Select(track,[Candidate() with{Track=track with{Artist="Other"}}]));
        Assert.Null(AutomaticLyricsMatcher.Select(track,[Candidate() with{Track=track with{DurationMs=220000}}]));
        Assert.Null(AutomaticLyricsMatcher.Select(track,[Candidate() with{Track=track with{Album="Album (Live)"}}]));
    }
    [Fact] public void PlaceholdersAndUntimedLyricsCannotBeAutomaticallyAccepted()
    {
        Assert.False(AutomaticLyricsMatcher.Assess(Candidate() with{SyncedLyrics=null,PlainLyrics="暂无歌词"}).Accepted);
        var full=Candidate() with{SyncedLyrics=""};Assert.False(AutomaticLyricsMatcher.Assess(full).Accepted);Assert.Null(AutomaticLyricsMatcher.Select(track,[full]));
    }
    [Fact] public void InstrumentalFlagCannotContradictVocalLyrics()
    {
        Assert.False(AutomaticLyricsMatcher.Assess(Candidate() with{Instrumental=true}).Accepted);
        Assert.True(AutomaticLyricsMatcher.Assess(new(1,track,null,null,true)).Accepted);
        Assert.Null(AutomaticLyricsMatcher.Select(track,[new(1,track,null,null,false)]));
    }
    [Fact] public void StaticFallbackUsesIndependentPlainTextOrTrulyUntimedProviderText()
    {
        var broken=Candidate() with{SyncedLyrics="[00:99]Malformed source time"};
        Assert.Null(AutomaticLyricsMatcher.Select(track,[broken]));
        var fallback=Assert.IsType<LyricsCandidate>(AutomaticLyricsMatcher.SelectStatic(track,[broken]));
        Assert.Equal(plain,fallback.PlainLyrics);Assert.Null(fallback.SyncedLyrics);
        var textOnly=broken with{SyncedLyrics="[Verse 1]\nWe carry our own light",PlainLyrics=null};
        Assert.Equal(textOnly.SyncedLyrics,AutomaticLyricsMatcher.SelectStatic(track,[textOnly])?.PlainLyrics);
    }
    [Fact] public void StaticFallbackRejectsWrongRecordingAndContentWithoutUsableLyrics()
    {
        var text=Candidate() with{SyncedLyrics=null};
        Assert.Null(AutomaticLyricsMatcher.SelectStatic(track,[
            text with{Track=track with{Album="Album (Live)"}},text with{Track=track with{DurationMs=210000}},
            text with{PlainLyrics="纯音乐，请欣赏"},text with{PlainLyrics="lyrics not available"},
            text with{PlainLyrics="[ar:Artist]"},text with{PlainLyrics="[00:99]This time is malformed"},
            text with{SyncedLyrics=Timed(),PlainLyrics=null}]));
    }
    [Fact] public void DifferentAlbumNeedsVeryCloseRecordingDuration()
    {
        Assert.Null(AutomaticLyricsMatcher.Select(track,[Candidate() with{Track=track with{Album="Other edition",DurationMs=184000}}]));
    }
    [Fact] public void ConflictingRecordsCannotSilentlyTurnAVocalSongIntoAnInstrumental()
    {
        var instrumental=new LyricsCandidate(1,track,null,null,true);
        var partial=Candidate(2) with{SyncedLyrics=null};
        Assert.Null(AutomaticLyricsMatcher.Select(track,[instrumental,partial]));
        Assert.Null(AutomaticLyricsMatcher.Select(track,[instrumental,partial with{PlainLyrics="帰ろう"}]));
        Assert.False(AutomaticLyricsMatcher.Select(track,[instrumental,Candidate(3)])!.Candidate.Instrumental);
    }
    static LyricsCandidate LongTail()
    {
        var lines=Enumerable.Range(0,8).Select(i=>$"We carry our own light along the river number {i}").ToArray();
        return new(88,track with{DurationMs=479000},string.Join("\n",lines.Select((t,i)=>$"[{(40+i*30)/60:00}:{(40+i*30)%60:00}]{t}")),string.Join("\n",lines),false);
    }
    [Fact]public void MatchingFullTextWithWellSpreadCuesCanHaveALongTail()
    {
        var c=LongTail();var a=AutomaticLyricsMatcher.Assess(c);Assert.True(a.Accepted,a.Reason);Assert.Equal(1,a.TextCoverage);Assert.NotNull(AutomaticLyricsMatcher.Select(c.Track,[c]));
    }
    [Fact]public void TailStillNeedsFullOrderedTextAndSufficientTimelineEvidence()
    {
        var c=LongTail();Assert.False(AutomaticLyricsMatcher.Assess(c with{PlainLyrics=null}).Accepted);
        Assert.False(AutomaticLyricsMatcher.Assess(c with{PlainLyrics=string.Join("\n",c.PlainLyrics!.Split('\n').Reverse())}).Accepted);
        Assert.False(AutomaticLyricsMatcher.Assess(c with{PlainLyrics=c.PlainLyrics+"\nA missing final verse about the distant shore"}).Accepted);
    }
    [Fact]public void SectionMarkersCannotPretendThatLyricsReachTheSongEnd()
    {
        var c=LongTail() with{PlainLyrics=null};Assert.False(AutomaticLyricsMatcher.Assess(c with{SyncedLyrics=c.SyncedLyrics+"\n[07:58][Instrumental]"}).Accepted);
    }
    [Fact]public void APerfectMetadataScoreStillExplainsAContentRejection()
    {
        var c=LongTail() with{PlainLyrics=null};var d=AutomaticLyricsMatcher.Decide(c.Track,c,[c]);Assert.Equal(1,d.MatchScore,8);Assert.False(d.Accepted);Assert.Contains("全文",d.Reason);
        Assert.Contains("歌手",AutomaticLyricsMatcher.Decide(c.Track,c with{Track=c.Track with{Artist="Another"}},[c]).Reason);
    }
}
