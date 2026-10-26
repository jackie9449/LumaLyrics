using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.Infrastructure.Tests;

public sealed class PlaylistTests
{
    const string Metadata="<?xml version=\"1.0\"?><!DOCTYPE plist SYSTEM \"https://example.invalid/never-load.dtd\"><plist><dict><key>universal-library-id</key><string>test.playlist</string></dict></plist>";
    static LibrarySnapshot Snapshot(ulong[] members,int? declared=null,string metadata=Metadata)=>new AppleMusicLibraryReader().Parse(LibraryTests.Library(3,playlists:(w,_)=>MusicDbPlaylistFixture.Write(w,new PlaylistFixture([1]),new PlaylistFixture(members,false,"原创歌单",declared,Metadata:metadata))));
    [Fact] public void PreservesPlaylistOrderAndRepeatedOccurrencesOutsideMaster()
    {
        var snapshot=Snapshot([3,2,3,1]);var playlist=Assert.Single(snapshot.Playlists);
        Assert.Equal(new[]{"Track 3","Track 2","Track 3","Track 1"},playlist.Tracks.Select(t=>t.Track.Title));
        Assert.Equal("test.playlist",playlist.Id);Assert.True(playlist.Complete);Assert.Equal("Track 1",Assert.Single(snapshot.Tracks).Track.Title);
    }
    [Fact] public void InternalListsWithoutDisplayMetadataStayHidden()
    {
        var snapshot=new AppleMusicLibraryReader().Parse(LibraryTests.Library(playlists:(w,_)=>MusicDbPlaylistFixture.Write(w,new PlaylistFixture([1]),new PlaylistFixture([2],false,"A misleading user-like name"))));
        Assert.Empty(snapshot.Playlists);
    }
    [Theory] [InlineData(4)] [InlineData(1)] public void PartialCountsCannotBecomePlayable(int declared)=>Assert.False(Assert.Single(Snapshot([2,3],declared).Playlists).Complete);
    [Fact] public void MissingCloudTracksAreExplicitlyIncomplete()
    {
        var playlist=Assert.Single(Snapshot([2,99,2]).Playlists);Assert.False(playlist.Complete);Assert.Equal(3,playlist.DeclaredCount);Assert.Equal(2,playlist.Tracks.Count);
    }
    [Fact] public void InvalidXmlCannotConfirmPlaylist()=>Assert.False(Assert.Single(Snapshot([2],metadata:"<plist>broken").Playlists).Complete);
    [Fact] public void XmlEntitiesAreNeverResolved()=>Assert.False(Assert.Single(Snapshot([2],metadata:"<!DOCTYPE plist [<!ENTITY secret SYSTEM 'file:///never-read'>]><plist><dict><key>universal-library-id</key><string>&secret;</string></dict></plist>").Playlists).Complete);
    [Fact] public void CatalogKeepsPlaylistTracksOutOfGlobalMembership()
    {
        string root=Path.Combine(Path.GetTempPath(),"Luma-playlists-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            string path=Path.Combine(root,"Library.musicdb");File.WriteAllBytes(path,LibraryTests.Library(3,playlists:(w,_)=>MusicDbPlaylistFixture.Write(w,new PlaylistFixture([1]),new PlaylistFixture([3,2,3],false,"原创歌单",Metadata:Metadata))));
            var result=new MusicLibraryCatalog(root,path).Load();Assert.Equal("Track 1",Assert.Single(result.Songs).Title);
            var playlist=Assert.Single(result.Playlists);Assert.True(playlist.CanPlay);Assert.Equal(new[]{"Track 3","Track 2","Track 3"},playlist.Songs.Select(s=>s.Title));
        }
        finally{Directory.Delete(root,true);}
    }
    [Fact] public void CatalogBlocksDuplicateNativeNamesAndMarksUnavailableSources()
    {
        string root=Path.Combine(Path.GetTempPath(),"Luma-playlists-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            string path=Path.Combine(root,"Library.musicdb");
            Assert.True(new MusicLibraryCatalog(root,path).Load().PlaylistsUnavailable);
            File.WriteAllBytes(path,LibraryTests.Library(playlists:(w,_)=>MusicDbPlaylistFixture.Write(w,new PlaylistFixture([1]),
                new PlaylistFixture([1],false,"同名歌单",Metadata:Metadata),new PlaylistFixture([2],false,"同名歌单",Metadata:Metadata.Replace("test.playlist","another.playlist")))));
            var result=new MusicLibraryCatalog(root,path).Load();Assert.Equal(2,result.Playlists.Count);
            Assert.All(result.Playlists,p=>Assert.False(p.CanPlay));Assert.Equal(2,result.Playlists.Select(p=>p.Key).Distinct().Count());
        }
        finally{Directory.Delete(root,true);}
    }
    static readonly MusicLibrarySong A=new("fixture","a",new("Original A","Artist","Album",180000));
    static readonly MusicLibrarySong B=new("fixture","b",new("Original B","Artist","Album",190000));
    static MusicPlaylist Playlist(params MusicLibrarySong[] songs)=>new("fixture","test","原创歌单",songs.Length,songs,true);
    static MusicQueueSnapshot Queue(Track current,params Track[] upcoming)=>new(current,upcoming.Select(t=>new MusicQueueEntry(t)).ToArray(),true,true,DateTimeOffset.UtcNow,"");
    [Fact] public void ConfirmationRequiresWholeOrderedQueueIncludingDuplicates()
    {
        var p=Playlist(A,B,A);Assert.True(MusicPlaylistPlayback.Confirms(p,Queue(A.Track,B.Track,A.Track)));
        Assert.False(MusicPlaylistPlayback.Confirms(p,Queue(A.Track,A.Track,B.Track)));
        Assert.False(MusicPlaylistPlayback.Confirms(p,Queue(A.Track,B.Track)));
        Assert.False(MusicPlaylistPlayback.Confirms(p,Queue(A.Track)));
    }
    [Fact] public void FinalSongRequiresExplicitCompleteEmptyQueue()
    {
        Assert.True(MusicPlaylistPlayback.Confirms(Playlist(A),Queue(A.Track)));
        Assert.False(MusicPlaylistPlayback.Confirms(Playlist(A),Queue(A.Track) with{IsComplete=false}));
    }
    [Fact] public void PartialQueueRequiresSubstantialOrderedEvidence()
    {
        var p=Playlist(new[]{A}.Concat(Enumerable.Repeat(B,15)).ToArray());
        Assert.False(MusicPlaylistPlayback.Confirms(p,Queue(A.Track,B.Track) with{IsComplete=false}));
        Assert.True(MusicPlaylistPlayback.Confirms(p,Queue(A.Track,Enumerable.Repeat(B.Track,10).ToArray()) with{IsComplete=false}));
    }
    [Fact] public void StaleRenamedOrReorderedPlaylistCannotBePlayed()
    {
        var p=Playlist(A,B,A);var fresh=new LibraryPlaylist(p.Id,p.Name,3,p.Songs.Select(s=>new LibraryTrack(s.PersistentId,s.Track)).ToArray(),true);
        Assert.True(MusicPlaylistPlayback.IsCurrent(p,[fresh]));
        Assert.False(MusicPlaylistPlayback.IsCurrent(p,[fresh with{Name="Renamed"}]));
        Assert.False(MusicPlaylistPlayback.IsCurrent(p,[fresh with{Tracks=fresh.Tracks.Reverse().Skip(1).Append(fresh.Tracks[0]).ToArray()}]));
        Assert.False(MusicPlaylistPlayback.IsCurrent(p,[fresh,fresh with{Id="other"}]));
    }
    [Fact] public void PlaylistSearchIncludesTrackMetadata()=>Assert.True(Playlist(A,B).Matches("Original Artist"));
    [Fact] public void IncompleteAndDuplicateNamesCannotBePlayed()
    {
        Assert.False((Playlist(A) with{Complete=false}).CanPlay);Assert.False((Playlist(A) with{UniqueName=false}).CanPlay);Assert.False(Playlist().CanPlay);
    }
}
