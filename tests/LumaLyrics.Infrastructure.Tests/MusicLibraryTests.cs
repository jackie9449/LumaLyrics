using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.Infrastructure.Tests;

public sealed class MusicLibraryTests:IDisposable
{
    readonly string root=Path.Combine(Path.GetTempPath(),"Luma-music-browser-"+Guid.NewGuid().ToString("N"));
    public MusicLibraryTests()=>Directory.CreateDirectory(root);
    public void Dispose(){Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(root,true);}
    static MusicLibrarySong Song(string title="微光之间",string artist="浮光",string album="原创测试")=>new("fixture","A1",new(title,artist,album,180000));

    [Fact] public void BrowserIncludesPendingAndManualSongsWithoutStartingDownloads()
    {
        using(var repo=new LyricsRepository(Path.Combine(root,"lyrics.db")))
        {
            var tracks=new[]{new LibraryTrack("1",Song().Track),new LibraryTrack("2",new("未下载的歌","浮光","新专辑",200000))};
            repo.SyncLibrary(new("fixture",2,tracks,"fixture-hash"));repo.Save(tracks[0].Track,"个人歌词","手工",true,plain:true);
        }
        var library=new MusicLibraryCatalog(root,Path.Combine(root,"missing.musicdb")).Load();
        Assert.Equal(2,library.Songs.Count);Assert.Contains(library.Songs,s=>s.Title=="未下载的歌");
        Assert.Equal("Manual",library.Songs.Single(s=>s.Title=="微光之间").LyricsKind);Assert.NotEmpty(library.Message);
        using var check=new LyricsRepository(Path.Combine(root,"lyrics.db"));Assert.Equal(1,check.GetLibraryProgress().Pending);Assert.Equal(1,check.GetLibraryProgress().Manual);
        Assert.DoesNotContain(Directory.GetFiles(root,"*",SearchOption.AllDirectories),f=>f.EndsWith(".lrc") || f.EndsWith(".img"));
    }
    [Fact] public void SearchMatchesTitleArtistAndAlbumWithMultipleTermsAndUnicodePunctuation()
    {
        var song=Song("光 · Between Waves","浮光乐队","夜色 Vol. 2");
        Assert.True(MusicLibraryCatalog.Matches(song,"  WAVES  乐队 vol2 "));Assert.True(MusicLibraryCatalog.Matches(song,"光"));
        Assert.False(MusicLibraryCatalog.Matches(song,"Waves 现场"));Assert.True(MusicLibraryCatalog.Matches(song,""));
    }
    [Theory] [InlineData("Play “微光之间”",true)] [InlineData("播放微光之间",true)] [InlineData("再生 微光之间",true)] [InlineData("播放微光之间 Live",false)] [InlineData("Play Next 微光之间",false)] [InlineData("删除微光之间",false)] [InlineData("Add 微光之间 to Library",false)]
    public void OnlyTheExactPlayMenuCommandCanBeInvoked(string command,bool expected)=>Assert.Equal(expected,MusicLibraryPlayback.IsPlayCommand(command,"微光之间"));
    [Fact] public void NativeRowMatchRequiresArtistAlbumAndCompatibleDuration()
    {
        Assert.True(MusicLibraryPlayback.NamesMatch(Song().Track,["微光之间","浮光","原创测试","3:00"]));
        Assert.False(MusicLibraryPlayback.NamesMatch(Song().Track,["微光之间 Live","浮光","原创测试","3:00"]));
        Assert.False(MusicLibraryPlayback.NamesMatch(Song().Track,["微光之间","翻唱者","原创测试","3:00"]));
        Assert.False(MusicLibraryPlayback.NamesMatch(Song().Track,["微光之间","浮光","另一张专辑","3:00"]));
        Assert.False(MusicLibraryPlayback.NamesMatch(Song().Track,["微光之间","浮光","原创测试","3:20"]));
        Assert.False(MusicLibraryPlayback.NamesMatch(Song().Track,["微光之间","浮光","3:00"]));
    }
    [Fact] public void PlaybackConfirmationRequiresRequestedRecordingAndUnderstandsAppleSessionMetadata()
    {
        var expected=Song().Track;
        Assert.True(MusicLibraryPlayback.IsRequestedRecording(expected,new("微光之间","浮光 — 原创测试","",179000)));
        Assert.False(MusicLibraryPlayback.IsRequestedRecording(expected,expected with{Title="另一首"}));
        Assert.False(MusicLibraryPlayback.IsRequestedRecording(expected,expected with{Artist="翻唱者"}));
        Assert.False(MusicLibraryPlayback.IsRequestedRecording(expected,expected with{Album="现场录音"}));
        Assert.False(MusicLibraryPlayback.IsRequestedRecording(expected,expected with{DurationMs=210000}));
        Assert.False(MusicLibraryPlayback.IsRequestedRecording(expected,expected with{Album=""}));
        Assert.False(MusicLibraryPlayback.IsRequestedRecording(new("","","",null),new("","","",null)));
    }
    [Fact] public void CancelledAndEmptyLibraryLoadsDoNotCreatePersonalDatabases()
    {
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(()=>new MusicLibraryCatalog(root,Path.Combine(root,"Library.musicdb")).Load(cancellation.Token));
        Assert.Empty(new MusicLibraryCatalog(root,Path.Combine(root,"Library.musicdb")).Load().Songs);
        Assert.Empty(Directory.GetFiles(root));
    }
}
