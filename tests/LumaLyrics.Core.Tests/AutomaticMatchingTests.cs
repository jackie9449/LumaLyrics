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
    [Fact] public void PlaceholdersCannotPassAndEmptySyncCanUsePlain()
    {
        Assert.False(AutomaticLyricsMatcher.Assess(Candidate() with{SyncedLyrics=null,PlainLyrics="暂无歌词"}).Accepted);
        var full=Candidate() with{SyncedLyrics=""};Assert.True(AutomaticLyricsMatcher.Assess(full).Accepted);Assert.False(AutomaticLyricsMatcher.Assess(full).Synced);
    }
}
