using LumaLyrics.Core;
namespace LumaLyrics.Core.Tests;
public class LyricsTests
{
    [Fact] public void ParsesFractionsMultistampsSortingAndBilingualGroups()
    {
        var d=new LrcParser().Parse("[01:02.3]third\n[00:01.20][00:02.123]first\n[00:01.20]第二行\n[62:00]long\n[00:03]");
        Assert.Equal(5,d.Groups.Count);Assert.Equal(1200,d.Groups[0].TimeMs);Assert.Equal(new[]{"first","第二行"},d.Groups[0].Lines);Assert.Equal(2123,d.Groups[1].TimeMs);Assert.Equal("",d.Groups[2].Text);Assert.Equal(62300,d.Groups[3].TimeMs);Assert.Equal(3720000,d.Groups[4].TimeMs);
    }
    [Fact] public void InvalidTimesWarnAndDoNotCrash()
    {
        var d=new LrcParser().Parse("[00:60]bad\n[-01:04]bad\n[9999:00]bad\n[ar:Test]\n[00:01]valid");Assert.Single(d.Groups);Assert.Equal(3,d.Warnings.Count);
    }
    [Fact] public void OffsetTagMeansEarlierWhilePositiveUserOffsetMeansLater()
    {
        var d=new LrcParser().Parse("[offset:+500]\n[00:02]A\n[00:03]B");Assert.Equal(-500,d.FileOffsetMs);Assert.Equal(-1,d.FindIndex(1499));Assert.Equal(0,d.FindIndex(1500));Assert.Equal(-1,d.FindIndex(1500,100));Assert.Equal(0,d.FindIndex(1600,100));Assert.Equal(1,d.FindIndex(2600,100));
    }
    [Fact] public void PlainTextNeverGetsArtificialTiming(){var d=new LrcParser().Parse("[00:01]Literal TXT",true);Assert.False(d.IsSynced);Assert.Equal("[00:01]Literal TXT",d.Raw);}
    [Fact] public void EmptyTimestampEndsPreviousLine(){var d=new LrcParser().Parse("[00:01]A\n[00:02]\n[00:03]B");Assert.Equal("",d.Groups[d.FindIndex(2500)].Text);Assert.Equal(2,d.FindIndex(99999));}
    [Theory][InlineData("Song (Live)","Song",false)][InlineData("Song (Remix)","Song (Live)",false)][InlineData("Ｓｏｎｇ", "song",true)]
    public void RecordingVersionsStayDistinct(string a,string b,bool eligible){var t=new Track(a,"Singer","Album",200000);var c=new LyricsCandidate(1,new(b,"Singer","Album",200000),"[00:01]A",null,false);Assert.Equal(eligible,MatchRules.Score(t,c).Eligible);}
    [Fact] public void MissingDurationWrongArtistAndAmbiguityCannotAutoSelect()
    {
        var t=new Track("Song","Singer","Album",200000);LyricsCandidate C(long id,Track track)=>new(id,track,"[00:01]A",null,false);
        Assert.NotNull(MatchRules.AutoSelect(t,[C(1,t)]));Assert.Null(MatchRules.AutoSelect(t,[C(1,t),C(2,t)]));Assert.Null(MatchRules.AutoSelect(t with{DurationMs=null},[C(1,t)]));Assert.Null(MatchRules.AutoSelect(t,[C(1,t with{Artist="Other"})]));Assert.Null(MatchRules.AutoSelect(t,[C(1,t with{DurationMs=210000})]));
    }
    [Fact] public void JointArtistsCanMatchWithoutIgnoringMissingContributors()
    {
        var t=new Track("Song","Singer A feat. Singer B","Album",200000);var c=new LyricsCandidate(1,t with{Artist="Singer B & Singer A"},"[00:01]A",null,false);Assert.True(MatchRules.Score(t,c).Eligible);Assert.False(MatchRules.Score(t,c with{Track=c.Track with{Artist="Singer A"}}).Eligible);
    }
    [Fact] public void AlbumVersionConflictVetoesAutomaticSelection(){var t=new Track("Song","Singer","Album",200000);var c=new LyricsCandidate(1,t with{Album="Album (Live)"},"[00:01]A",null,false);Assert.False(MatchRules.Score(t,c).Eligible);}
}
