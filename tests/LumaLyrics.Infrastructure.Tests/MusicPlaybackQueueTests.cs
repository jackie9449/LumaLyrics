using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.Infrastructure.Tests;

public sealed class MusicPlaybackQueueTests
{
    [Theory]
    [InlineData("插播",true)] [InlineData("Play Next",true)] [InlineData("  play next  ",true)] [InlineData("次に再生",true)]
    [InlineData("最后播放",false)] [InlineData("Play Last",false)] [InlineData("Play Next 微光",false)] [InlineData("播放微光",false)]
    [InlineData("添加到播放列表",false)] [InlineData("清除",false)] [InlineData("",false)] [InlineData("从资料库删除",false)]
    public void PlayNextRequiresACompleteSupportedCommand(string command,bool expected)=>Assert.Equal(expected,MusicPlaybackQueue.IsPlayNextCommand(command));

    [Fact] public void NativeRowPreservesEditionArtistAlbumAndDuration()
    {
        var item=MusicPlaybackQueue.ParseNativeRow(["微光之间 (Live)","浮光 — 夜航 (Live)","1:02:03"]);
        Assert.Equal(new Track("微光之间 (Live)","浮光","夜航 (Live)",3723000),item!.Track);
    }
    [Fact] public void NativeOrderAndRepeatedRecordingsAreNotSortedOrDeduplicated()
    {
        string[][] rows=[["夜航","浮光 — 海面","3:20"],["微光","浮光 — 海面","3:00"],["夜航","浮光 — 海面","3:20"]];
        var queue=new MusicQueueSnapshot(null,rows.Select(r=>MusicPlaybackQueue.ParseNativeRow(r)!).ToArray(),true,true,DateTimeOffset.UtcNow,"");
        Assert.Equal(new[]{"夜航","微光","夜航"},queue.Upcoming.Select(x=>x.Track.Title));Assert.Equal(queue.Upcoming[0],queue.Upcoming[2]);
    }
    [Fact] public void MissingDurationDoesNotInventTimingAndUnknownMetadataIsNotARecording()
    {
        Assert.Null(MusicPlaybackQueue.ParseNativeRow(["微光"]));Assert.Null(MusicPlaybackQueue.ParseNativeRow(["微光"," "]));
        var item=MusicPlaybackQueue.ParseNativeRow(["微光","浮光","时长未知"]);Assert.Null(item!.Track.DurationMs);Assert.Empty(item.Track.Album);
    }
    [Fact] public void AmbiguousArtistAlbumSeparatorIsNotGuessed()
    {
        var track=MusicPlaybackQueue.ParseNativeRow(["微光","浮光 — 乐队 — 夜航","3:00"])!.Track;
        Assert.Equal("浮光 — 乐队 — 夜航",track.Artist);Assert.Empty(track.Album);
    }
    [Fact] public void CoverLookupRequiresOneExactLibraryRecording()
    {
        var track=new Track("微光","浮光","夜航",180000);
        var song=new MusicLibrarySong("fixture","1",track);
        Assert.Same(song,MusicPlaybackQueue.MatchLibrarySong(track,[song,song with{Track=track with{Title="微光 (Live)"}}]));
        Assert.Null(MusicPlaybackQueue.MatchLibrarySong(track,[song,song with{PersistentId="2"}]));
        Assert.Null(MusicPlaybackQueue.MatchLibrarySong(track,[song with{Track=track with{Album="夜航 (Deluxe)"}}]));
    }
    [Fact] public void UnavailableQueueIsDistinctFromAnActuallyEmptyQueue()
    {
        var unavailable=MusicQueueSnapshot.Unavailable("客户端未打开");var empty=new MusicQueueSnapshot(null,[],true,true,DateTimeOffset.UtcNow,"");
        Assert.False(unavailable.Available);Assert.False(unavailable.IsComplete);Assert.True(empty.Available);Assert.True(empty.IsComplete);
    }
    [Fact] public void ExistingNextRecordingAloneCannotConfirmARepeatedInsertion()
    {
        var song=new MusicLibrarySong("fixture","1",new("夜航","浮光","原创",180000));var first=new MusicQueueEntry(song.Track,Inserted:true);
        var before=new MusicQueueSnapshot(null,[first],true,true,DateTimeOffset.UtcNow,"");
        Assert.False(MusicPlaybackQueue.ConfirmsInsertion(song,before,before));
        Assert.True(MusicPlaybackQueue.ConfirmsInsertion(song,before,before with{Upcoming=[first,first]}));
        Assert.False(MusicPlaybackQueue.ConfirmsInsertion(song,before,before with{Upcoming=[first,first],IsComplete=false}));
    }
    [Fact] public void InsertionRequiresTheExactRecordingAtTheHeadOfTheActualQueue()
    {
        var song=new MusicLibrarySong("fixture","1",new("夜航","浮光","原创",180000));var wrong=new MusicQueueEntry(song.Track with{Album="原创 (Live)"});var requested=new MusicQueueEntry(song.Track);
        var before=new MusicQueueSnapshot(null,[wrong],true,true,DateTimeOffset.UtcNow,"");
        Assert.False(MusicPlaybackQueue.ConfirmsInsertion(song,before,before with{Upcoming=[wrong,requested]}));
        Assert.False(MusicPlaybackQueue.ConfirmsInsertion(song,before,MusicQueueSnapshot.Unavailable("unavailable")));
        Assert.True(MusicPlaybackQueue.ConfirmsInsertion(song,before,before with{Upcoming=[requested,wrong]}));
    }

    static MusicQueueSnapshot JumpFixture()=>new(new("微光","浮光","原创",180000),
        [new(new("夜航","浮光","原创",200000),Inserted:true),new(new("海面","浮光","原创",220000)),new(new("夜航","浮光","原创",200000))],true,true,DateTimeOffset.UtcNow,"");

    [Fact] public void QueueSelectionRevalidatesTheWholeSequenceAndCurrentRecording()
    {
        var queue=JumpFixture();var selection=new MusicQueueSelection(queue,2);
        Assert.True(MusicPlaybackQueue.MatchesSelection(selection,queue with{CapturedUtc=DateTimeOffset.UtcNow.AddSeconds(2),Message="refreshed"}));
        Assert.False(MusicPlaybackQueue.MatchesSelection(selection,queue with{Current=queue.Upcoming[0].Track}));
        Assert.False(MusicPlaybackQueue.MatchesSelection(selection,queue with{Upcoming=queue.Upcoming.Reverse().ToArray()}));
        Assert.False(MusicPlaybackQueue.MatchesSelection(selection,queue with{Upcoming=queue.Upcoming.Skip(1).ToArray()}));
        Assert.False(MusicPlaybackQueue.MatchesSelection(selection,queue with{IsComplete=false}));
    }
    [Theory] [InlineData(-1)] [InlineData(3)] [InlineData(int.MaxValue)]
    public void QueueSelectionRejectsInvalidIndexes(int index)=>Assert.False(MusicPlaybackQueue.CanSelect(new(JumpFixture(),index)));

    [Fact] public void QueueSelectionRejectsUnavailableOrIncompleteRecordingIdentity()
    {
        var queue=JumpFixture();
        Assert.False(MusicPlaybackQueue.CanSelect(new(queue with{Available=false},0)));
        Assert.False(MusicPlaybackQueue.CanSelect(new(queue with{Current=null},0)));
        Assert.False(MusicPlaybackQueue.CanSelect(new(queue with{Upcoming=[queue.Upcoming[0] with{MetadataComplete=false}]},0)));
        Assert.False(MusicPlaybackQueue.CanSelect(new(queue with{Upcoming=[new(new("夜航","","原创",null))]},0)));
    }
    [Fact] public void JumpConfirmationDistinguishesRepeatedOccurrencesByTheRemainingQueue()
    {
        var queue=JumpFixture();var after=queue with{Current=queue.Upcoming[2].Track,Upcoming=[]};
        Assert.True(MusicPlaybackQueue.ConfirmsJump(new(queue,2),after));
        Assert.False(MusicPlaybackQueue.ConfirmsJump(new(queue,0),after));
        Assert.True(MusicPlaybackQueue.ConfirmsJump(new(queue,0),queue with{Current=queue.Upcoming[0].Track,Upcoming=queue.Upcoming.Skip(1).ToArray()}));
        Assert.False(MusicPlaybackQueue.ConfirmsJump(new(queue,2),queue));
    }
    [Fact] public void JumpConfirmationRejectsWrongEditionUnavailableOrPartialResult()
    {
        var queue=JumpFixture();var selection=new MusicQueueSelection(queue,1);var after=queue with{Current=queue.Upcoming[1].Track,Upcoming=queue.Upcoming.Skip(2).ToArray()};
        Assert.True(MusicPlaybackQueue.ConfirmsJump(selection,after));
        Assert.False(MusicPlaybackQueue.ConfirmsJump(selection,after with{Available=false}));
        Assert.False(MusicPlaybackQueue.ConfirmsJump(selection,after with{IsComplete=false}));
        Assert.False(MusicPlaybackQueue.ConfirmsJump(selection,after with{Current=after.Current! with{Album="原创 (Live)"}}));
    }
    [Fact] public void PartialSnapshotCanConfirmOnlyItsKnownRemainingPrefix()
    {
        var queue=JumpFixture() with{IsComplete=false};var after=queue with{Current=queue.Upcoming[0].Track,Upcoming=queue.Upcoming.Skip(1).Append(new(new("远方","浮光","原创",180000))).ToArray()};
        Assert.True(MusicPlaybackQueue.MatchesSelection(new(queue,0),queue));
        Assert.True(MusicPlaybackQueue.ConfirmsJump(new(queue,0),after));
        Assert.False(MusicPlaybackQueue.ConfirmsJump(new(queue,2),after with{Current=queue.Upcoming[2].Track,Upcoming=[]}));
    }
}
