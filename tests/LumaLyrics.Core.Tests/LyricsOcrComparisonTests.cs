namespace LumaLyrics.Core.Tests;

public class LyricsOcrComparisonTests
{
    static readonly Track Track=new("Own test song","Own artist","Own album",60000);
    static readonly LyricsDocument Document=new LrcParser().Parse("[00:01]We sail home\n[00:08]Across the quiet bay\n[00:16]星光照亮归途\n[00:23]We sail home\n[00:31]Here we belong");
    static LyricsOcrObservation Observation(params string[] lines)=>new(Track,TranslationAlignment.Hash(Document.Raw),DateTimeOffset.UtcNow,8000,"en-US",new(10,20,300,400),lines);
    [Fact] public void NormalizesPunctuationCaseAndCjkSpacingButReportsPartialCoverage()
    {
        var result=LyricsOcrComparison.Compare(Track,Document,Observation("ACROSS, the quiet bay!","星 光 照 亮 归 途"));
        Assert.True(result.Accepted);Assert.All(result.Lines,line=>Assert.Equal(LyricsOcrMatch.Matched,line.Match));Assert.Equal(2,result.ObservedGroups);Assert.Equal(5,result.TotalGroups);Assert.False(result.FullSongReviewed);
    }
    [Fact] public void JoinsWrappedVisibleLinesWithoutMakingTimestamps()
    {
        var result=LyricsOcrComparison.Compare(Track,Document,Observation("Across the quiet","bay"));
        var line=Assert.Single(result.Lines);Assert.Equal(1,line.GroupIndex);Assert.Equal(LyricsOcrMatch.Matched,line.Match);Assert.Equal(1,result.ObservedGroups);
    }
    [Fact] public void RepeatedTextDoesNotVerifyAnOccurrenceOrTiming()
    {
        var result=LyricsOcrComparison.Compare(Track,Document,Observation("We sail home"));
        Assert.Equal(LyricsOcrMatch.Ambiguous,Assert.Single(result.Lines).Match);Assert.Equal(0,result.ObservedGroups);
    }
    [Fact] public void RecognitionErrorIsOnlyASimilarCandidate()
    {
        var result=LyricsOcrComparison.Compare(Track,Document,Observation("Across the qulet bay"));
        Assert.Equal(LyricsOcrMatch.Similar,Assert.Single(result.Lines).Match);Assert.Equal(0,result.ObservedGroups);
    }
    [Fact] public void ChangedHashOrRecordingRejectsEvidence()
    {
        Assert.False(LyricsOcrComparison.Compare(Track,Document,Observation("Own text") with{SourceHash=new string('a',64)}).Accepted);
        Assert.False(LyricsOcrComparison.Compare(Track,Document,Observation("Own text") with{Track=Track with{Title="Another song"}}).Accepted);
    }
    [Fact] public void UnrelatedVisibleTextIsNotMatched()
    {
        var result=LyricsOcrComparison.Compare(Track,Document,Observation("No reference in the document"));
        Assert.Equal(LyricsOcrMatch.Unmatched,Assert.Single(result.Lines).Match);Assert.Equal(0,result.ObservedGroups);
    }
}
