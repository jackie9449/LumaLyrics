using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.Infrastructure.Tests;

public sealed class MusicAlbumTests
{
    static MusicLibrarySong Song(string id,string album="光",string artist="浮光",string albumArtist="",string? albumId=null,string source="fixture")=>new(source,id,new("曲目 "+id,artist,album,180000),AlbumArtist:albumArtist,AlbumPersistentId:albumId);
    [Fact] public void SameNameAlbumsByDifferentArtistsOrSourcesStaySeparate()
    {
        var albums=MusicLibraryAlbums.Group([Song("1"),Song("2",artist:"另一位"),Song("3",source:"another")]);
        Assert.Equal(3,albums.Count);Assert.Equal(3,albums.Select(a=>a.Key).Distinct().Count());
    }
    [Fact] public void NativeAlbumIdentityKeepsCompilationTracksTogetherAndEditionsSeparate()
    {
        var albums=MusicLibraryAlbums.Group([Song("1",artist:"艺人甲",albumId:"ABC1"),Song("2",artist:"艺人乙",albumId:"ABC1"),Song("3",albumId:"ABC2")]);
        Assert.Equal(2,albums.Count);var compilation=albums.Single(a=>a.Songs.Count==2);Assert.Equal("群星",compilation.Artist);
    }
    [Fact] public void SharedNativeArtworkReferenceNeverOverridesConflictingOrMissingAlbumMetadata()
    {
        var albums=MusicLibraryAlbums.Group([Song("1",album:"晨光",albumId:"ABC1"),Song("2",album:"夜色",albumId:"ABC1"),Song("3",album:"",albumId:"ABC1"),Song("4",album:"",albumId:"ABC1"),Song("5",album:"晨光",albumArtist:"艺人甲",albumId:"ABC1"),Song("6",album:"晨光",albumArtist:"艺人乙",albumId:"ABC1")]);
        Assert.Equal(6,albums.Count);Assert.All(albums,a=>Assert.Single(a.Songs));
    }
    [Fact] public void AlbumArtistProvidesConservativeCompilationFallback()
    {
        var albums=MusicLibraryAlbums.Group([Song("1",artist:"艺人甲",albumArtist:"Various Artists"),Song("2",artist:"艺人乙",albumArtist:"Various Artists")]);
        Assert.Equal("Various Artists",Assert.Single(albums).Artist);Assert.Equal(2,albums[0].Songs.Count);
    }
    [Fact] public void MissingAlbumNamesAreNotMergedAndEditionPunctuationIsRetained()
    {
        var albums=MusicLibraryAlbums.Group([Song("1",album:""),Song("2",album:""),Song("3",album:"光 - 现场"),Song("4",album:"光现场")]);
        Assert.Equal(4,albums.Count);Assert.Equal(2,albums.Count(a=>a.Title=="未命名专辑"));
    }
    [Fact] public void GroupingRetainsAllSongsAndKeysRemainStableWhenInputOrderChanges()
    {
        var songs=new[]{Song("2"),Song("1"),Song("3",album:"夜色")};var first=MusicLibraryAlbums.Group(songs);var second=MusicLibraryAlbums.Group(songs.Reverse().ToArray());
        Assert.Equal(3,first.Sum(a=>a.Songs.Count));Assert.Equal(first.Select(a=>a.Key),second.Select(a=>a.Key));Assert.Equal(new[]{"1","2"},first.Single(a=>a.Title=="光").Songs.Select(s=>s.PersistentId));
    }
    [Fact] public void AlbumSearchMatchesAlbumArtistAndTrackAndCoversKeepFallbacks()
    {
        var album=Assert.Single(MusicLibraryAlbums.Group([Song("1") with{CoverPath="first.png"},Song("2") with{CoverPath="second.png"},Song("3") with{CoverPath="first.png"}]));
        Assert.True(album.Matches("光 曲目2"));Assert.False(album.Matches("光 另一个"));Assert.Equal(new[]{"first.png","second.png"},album.CoverPaths);Assert.Empty(MusicLibraryAlbums.Group([]));
    }
    [Fact] public void PlaybackAnchorMustBeAUniqueRecordingInOneNamedAlbumAndSource()
    {
        var album=MusicLibraryAlbums.Group([Song("1"),Song("2")]).Single();
        Assert.NotNull(MusicLibraryPlayback.AlbumAnchor(album));
        Assert.Null(MusicLibraryPlayback.AlbumAnchor(album with{Songs=[]}));
        Assert.Null(MusicLibraryPlayback.AlbumAnchor(album with{Songs=[Song("1",album:"")]}));
        Assert.Null(MusicLibraryPlayback.AlbumAnchor(album with{Songs=[Song("1"),Song("2",source:"other")]}));
        var duplicate=Song("1") with{PersistentId="duplicate"};
        Assert.Null(MusicLibraryPlayback.AlbumAnchor(album with{Songs=[Song("1"),duplicate]}));
        Assert.Equal("2",MusicLibraryPlayback.AlbumAnchor(album with{Songs=[Song("1"),duplicate,Song("2")]})!.PersistentId);
    }
    [Fact] public void NativeAlbumContextRequiresExactEditionArtistAndMatchingVisibleTrackRows()
    {
        var album=MusicLibraryAlbums.Group([Song("1",albumArtist:"浮光"),Song("2",albumArtist:"浮光")]).Single();
        IReadOnlyList<IReadOnlyList<string>> rows=new[]{new[]{"1","曲目 1","3:00"},new[]{"2","曲目 2","3:00"}};
        Assert.True(MusicLibraryPlayback.AlbumContextMatches(album,"光",["浮光"],rows));
        Assert.False(MusicLibraryPlayback.AlbumContextMatches(album,"光 - 现场",["浮光"],rows));
        Assert.False(MusicLibraryPlayback.AlbumContextMatches(album,"光",["另一位"],rows));
        Assert.False(MusicLibraryPlayback.AlbumContextMatches(album,"光",["浮光"],new[]{new[]{"曲目 1","3:30"}}));
        Assert.False(MusicLibraryPlayback.AlbumContextMatches(album,"光",["浮光"],new[]{new[]{"曲目 99","3:00"}}));
        Assert.False(MusicLibraryPlayback.AlbumContextMatches(album,"光",["浮光"],[]));
        // A virtualized album exposes only some rows; native album identity and every
        // exposed recording still have to agree before invoking its album Play button.
        Assert.True(MusicLibraryPlayback.AlbumContextMatches(album,"光",["浮光"],new[]{new[]{"曲目 2","3:00"}}));
    }
    [Fact] public void NativeAlbumContextSupportsCompilationAndMemberConfirmation()
    {
        var album=MusicLibraryAlbums.Group([Song("1",artist:"艺人甲",albumArtist:"Various Artists"),Song("2",artist:"艺人乙",albumArtist:"Various Artists")]).Single();
        Assert.True(MusicLibraryPlayback.AlbumContextMatches(album,"光",["Various Artists"],new[]{new[]{"曲目 1","3:00"},new[]{"曲目 2","3:00"}}));
        Assert.True(MusicLibraryPlayback.IsRequestedAlbum(album,new("曲目 2","艺人乙 — 光","",179000)));
        Assert.False(MusicLibraryPlayback.IsRequestedAlbum(album,new("曲目 2","艺人丙","光",179000)));
        Assert.False(MusicLibraryPlayback.IsRequestedAlbum(album,new("曲目 2","艺人乙","光 - 现场",179000)));
        Assert.False(MusicLibraryPlayback.IsRequestedAlbum(album,new("曲目 3","艺人乙","光",179000)));
    }
}
