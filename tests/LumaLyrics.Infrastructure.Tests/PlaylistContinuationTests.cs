using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.Infrastructure.Tests;

public sealed class PlaylistContinuationTests
{
    static readonly MusicLibrarySong A=new("fixture","a",new("Original A","Artist","Album",180000));
    static readonly MusicLibrarySong B=new("fixture","b",new("Original B","Artist","Album",190000));
    static readonly MusicLibrarySong C=new("fixture","c",new("Original C","Artist","Album",200000));
    static MusicPlaylist List(params MusicLibrarySong[] songs)=>new("fixture","original","原创歌单",songs.Length,songs,true);
    static MusicQueueSnapshot Queue(Track current,params Track[] upcoming)=>new(current,upcoming.Select(t=>new MusicQueueEntry(t)).ToArray(),true,true,DateTimeOffset.UtcNow,"");
    static readonly MusicPlaybackModeState Ordered=new(false,MusicRepeatMode.None);
    [Fact] public void ExactRepeatedOccurrenceKeepsItsOwnSuffix()
    {
        var selection=new MusicPlaylistSelection(List(A,B,A,C),2);
        Assert.True(MusicPlaylistContinuation.Confirms(selection,Ordered,Queue(A.Track,C.Track)));
        Assert.False(MusicPlaylistContinuation.Confirms(selection,Ordered,Queue(A.Track,B.Track,A.Track,C.Track)));
        Assert.False(MusicPlaylistContinuation.Confirms(selection,Ordered,Queue(A.Track)));
    }
    [Theory] [InlineData(-1)] [InlineData(4)] public void InvalidIndexesCannotPlay(int index)=>Assert.False(MusicPlaylistContinuation.CanSelect(new(List(A,B,A,C),index)));
    [Fact] public void IncompleteAndInvalidOptionsCannotPlay()
    {
        Assert.False(MusicPlaylistContinuation.CanSelect(new(List(A) with{Complete=false},0)));
        Assert.False(MusicPlaylistContinuation.CanSelect(new(List(A),0,new(Repeat:(MusicRepeatMode)99))));
    }
    [Fact] public void OrderedWholePlaylistStillRequiresFirstRecording()
    {
        var selection=new MusicPlaylistSelection(List(A,B,C));
        Assert.True(MusicPlaylistContinuation.Confirms(selection,Ordered,Queue(A.Track,B.Track,C.Track)));
        Assert.False(MusicPlaylistContinuation.Confirms(selection,Ordered,Queue(B.Track,C.Track)));
    }
    [Fact] public void ShuffleRetainsAllOtherOccurrencesRegardlessOfOriginalPosition()
    {
        var selection=new MusicPlaylistSelection(List(A,B,A,C),2,new(true));var mode=new MusicPlaybackModeState(true,MusicRepeatMode.None);
        Assert.True(MusicPlaylistContinuation.Confirms(selection,mode,Queue(A.Track,C.Track,A.Track,B.Track)));
        Assert.False(MusicPlaylistContinuation.Confirms(selection,mode,Queue(A.Track,C.Track,B.Track,B.Track)));
        Assert.False(MusicPlaylistContinuation.Confirms(selection,mode,Queue(A.Track,C.Track)));
        Assert.False(MusicPlaylistContinuation.Confirms(selection,mode,Queue(A.Track,C.Track,A.Track,B.Track) with{IsComplete=false}));
    }
    [Fact] public void WholeShuffleAcceptsNativeChosenFirstAndVerifiedRemainingMultiset()
    {
        var selection=new MusicPlaylistSelection(List(A,B,C),Options:new(true));var mode=new MusicPlaybackModeState(true,MusicRepeatMode.List);
        Assert.True(MusicPlaylistContinuation.Confirms(selection,mode,Queue(B.Track,C.Track,A.Track)));
        Assert.False(MusicPlaylistContinuation.Confirms(selection,mode,Queue(B.Track,C.Track,B.Track)));
    }
    [Theory] [InlineData(MusicRepeatMode.None)] [InlineData(MusicRepeatMode.List)] [InlineData(MusicRepeatMode.One)]
    public void RepeatIsConfirmedSeparatelyFromRemainingQueue(MusicRepeatMode repeat)
    {
        var selection=new MusicPlaylistSelection(List(A,B,C),1,new(false,repeat));
        Assert.True(MusicPlaylistContinuation.Confirms(selection,new(false,repeat),Queue(B.Track,C.Track)));
        Assert.False(MusicPlaylistContinuation.Confirms(selection,new(false,(MusicRepeatMode)(((int)repeat+1)%3)),Queue(B.Track,C.Track)));
    }
    [Fact] public void RepeatAllMayExposeAValidatedWrapWithoutFabricatingIt()
    {
        var selection=new MusicPlaylistSelection(List(A,B,C),1,new(false,MusicRepeatMode.List));
        Assert.True(MusicPlaylistContinuation.Confirms(selection,new(false,MusicRepeatMode.List),Queue(B.Track,C.Track,A.Track,B.Track)));
        Assert.False(MusicPlaylistContinuation.Confirms(selection,Ordered,Queue(B.Track,C.Track,A.Track,B.Track)));
        Assert.False(MusicPlaylistContinuation.Confirms(selection with{Options=null},Ordered,Queue(B.Track,C.Track,A.Track,B.Track)));
    }
    [Fact] public void UnknownModesAndIncompleteMetadataCannotConfirm()
    {
        var selection=new MusicPlaylistSelection(List(A,B),0);
        Assert.False(MusicPlaylistContinuation.Confirms(selection,new(null,null),Queue(A.Track,B.Track)));
        Assert.False(MusicPlaylistContinuation.Confirms(selection,Ordered,Queue(A.Track,B.Track) with{Upcoming=[new(B.Track,false)]}));
    }
    [Fact] public void LastOccurrenceRequiresExplicitEmptyQueue()
    {
        var selection=new MusicPlaylistSelection(List(A,B,A),2);
        Assert.True(MusicPlaylistContinuation.Confirms(selection,Ordered,Queue(A.Track)));
        Assert.False(MusicPlaylistContinuation.Confirms(selection,Ordered,Queue(A.Track) with{IsComplete=false}));
    }
    [Fact] public void PartialOrderedQueueRequiresTenMatchingRemainingOccurrences()
    {
        var playlist=List(new[]{A}.Concat(Enumerable.Repeat(B,15)).Append(C).ToArray());var selection=new MusicPlaylistSelection(playlist,0);
        Assert.True(MusicPlaylistContinuation.Confirms(selection,Ordered,Queue(A.Track,Enumerable.Repeat(B.Track,10).ToArray()) with{IsComplete=false}));
        Assert.False(MusicPlaylistContinuation.Confirms(selection,Ordered,Queue(A.Track,Enumerable.Repeat(B.Track,9).ToArray()) with{IsComplete=false}));
        Assert.False(MusicPlaylistContinuation.Confirms(selection,Ordered,Queue(A.Track,Enumerable.Repeat(B.Track,10).ToArray())));
        Assert.False(MusicPlaylistContinuation.Confirms(selection,Ordered,Queue(A.Track,new[]{C.Track}.Concat(Enumerable.Repeat(B.Track,9)).ToArray()) with{IsComplete=false}));
    }
    [Fact] public void ShuffleCannotSubstituteAnotherCurrentOccurrenceOrMode()
    {
        var selection=new MusicPlaylistSelection(List(A,B,C),1,new(true));
        Assert.False(MusicPlaylistContinuation.Confirms(selection,new(true,MusicRepeatMode.None),Queue(A.Track,B.Track,C.Track)));
        Assert.False(MusicPlaylistContinuation.Confirms(selection,Ordered,Queue(B.Track,C.Track)));
    }
    [Fact] public void ClaimedCompletePlaylistWithDifferentCountIsRejected()=>Assert.False(MusicPlaylistContinuation.CanSelect(new(List(A,B) with{DeclaredCount=3},0)));
    [Theory] [InlineData("不重复",MusicRepeatMode.None)] [InlineData("Repeat All",MusicRepeatMode.List)] [InlineData("Repeat One",MusicRepeatMode.One)]
    public void RepeatNamesUseExactSupportedLabels(string label,MusicRepeatMode expected)=>Assert.Equal(expected,MusicPlaybackModeRules.ParseRepeat(label));
    [Fact] public void UnknownRepeatLabelDoesNotGuess()=>Assert.Null(MusicPlaybackModeRules.ParseRepeat("Repeat something unknown"));
}
