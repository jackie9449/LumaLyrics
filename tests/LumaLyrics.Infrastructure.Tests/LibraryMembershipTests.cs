using System.Text;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
using Microsoft.Data.Sqlite;

namespace LumaLyrics.Infrastructure.Tests;

public sealed class LibraryMembershipTests:IDisposable
{
    readonly string root=Path.Combine(Path.GetTempPath(),"Luma-membership-"+Guid.NewGuid().ToString("N"));
    public LibraryMembershipTests()=>Directory.CreateDirectory(root);
    public void Dispose(){SqliteConnection.ClearAllPools();Directory.Delete(root,true);}

    [Fact] public void NativeMasterMembershipExcludesCachedCatalogRecordings()
    {
        var bytes=LibraryTests.Library(3,playlists:(w,_)=>MusicDbPlaylistFixture.Write(w,new PlaylistFixture([1,3],Name:"A localized internal name")));
        var snapshot=new AppleMusicLibraryReader().Parse(bytes);
        Assert.Equal(3,snapshot.StoredTrackCount);Assert.Equal(2,snapshot.DeclaredCount);
        Assert.Equal(new[]{"Track 1","Track 3"},snapshot.Tracks.Select(t=>t.Track.Title));
    }
    [Fact] public void EmptyNativeLibraryDoesNotAdoptCachedTracks()
    {
        var snapshot=new AppleMusicLibraryReader().Parse(LibraryTests.Library(3,playlists:(w,_)=>MusicDbPlaylistFixture.Write(w,new PlaylistFixture([]))));
        Assert.Equal(3,snapshot.StoredTrackCount);Assert.Equal(0,snapshot.DeclaredCount);Assert.Empty(snapshot.Tracks);
        Assert.Empty(new AppleMusicLibraryReader().Parse(LibraryTests.Library(0)).Tracks);
    }
    [Fact] public void MissingMembershipDoesNotFallBackToTheEntireTrackTable()=>
        Assert.Throws<InvalidDataException>(()=>new AppleMusicLibraryReader().Parse(LibraryTests.Library(playlists:(_,_)=>{})));
    [Theory]
    [InlineData("音乐")]
    [InlineData("Music")]
    [InlineData("####!####")]
    public void PlaylistNamesCannotImpersonateTheNativeMaster(string name)=>
        Assert.Throws<InvalidDataException>(()=>new AppleMusicLibraryReader().Parse(LibraryTests.Library(playlists:(w,_)=>MusicDbPlaylistFixture.Write(w,new PlaylistFixture([1,2],Master:false,Name:name)))));
    [Fact] public void OtherPlaylistsCannotAddTheirRecordingsToLibraryMembership()
    {
        var snapshot=new AppleMusicLibraryReader().Parse(LibraryTests.Library(3,playlists:(w,_)=>MusicDbPlaylistFixture.Write(w,new PlaylistFixture([2,3],Master:false),new PlaylistFixture([1]))));
        Assert.Equal("Track 1",Assert.Single(snapshot.Tracks).Track.Title);
    }
    [Fact] public void DanglingReferencesAreRejected()=>
        Assert.Throws<InvalidDataException>(()=>new AppleMusicLibraryReader().Parse(LibraryTests.Library(playlists:(w,_)=>MusicDbPlaylistFixture.Write(w,new PlaylistFixture([1,99])))));
    [Fact] public void DuplicateMasterReferencesAreRejected()=>
        Assert.Throws<InvalidDataException>(()=>new AppleMusicLibraryReader().Parse(LibraryTests.Library(playlists:(w,_)=>MusicDbPlaylistFixture.Write(w,new PlaylistFixture([1,1])))));
    [Fact] public void PartialMemberListsAreRejected()=>
        Assert.Throws<InvalidDataException>(()=>new AppleMusicLibraryReader().Parse(LibraryTests.Library(playlists:(w,_)=>MusicDbPlaylistFixture.Write(w,new PlaylistFixture([1],Declared:2)))));
    [Fact] public void MultipleMastersAreRejected()=>
        Assert.Throws<InvalidDataException>(()=>new AppleMusicLibraryReader().Parse(LibraryTests.Library(playlists:(w,_)=>MusicDbPlaylistFixture.Write(w,new PlaylistFixture([1]),new PlaylistFixture([2])))));
    [Fact] public void DamagedNativeReferenceIsRejected()=>
        Assert.Throws<InvalidDataException>(()=>new AppleMusicLibraryReader().Parse(LibraryTests.Library(playlists:(w,_)=>MusicDbPlaylistFixture.Write(w,new PlaylistFixture([1],InvalidReference:true)))));

    [Fact] public async Task BackgroundPreparationQueriesOnlyConfirmedLibraryMembers()
    {
        string file=Path.Combine(root,"Library.musicdb");
        File.WriteAllBytes(file,LibraryTests.Library(3,playlists:(w,_)=>MusicDbPlaylistFixture.Write(w,new PlaylistFixture([1,3]))));
        var provider=new RecordingProvider();
        await new LibraryPrefetcher(Path.Combine(root,"lyrics.db"),provider,pacing:TimeSpan.Zero).RunAsync(file,default);
        Assert.Equal(new[]{"Track 1","Track 3"},provider.Requests);
        using var repository=new LyricsRepository(Path.Combine(root,"lyrics.db"));Assert.Equal(2,repository.GetLibraryProgress().Total);
    }
    sealed class RecordingProvider:ILyricsProvider
    {
        public List<string> Requests {get;}=[];
        public Task<IReadOnlyList<LyricsCandidate>> LookupAsync(Track track,CancellationToken token)
        {Requests.Add(track.Title);return Task.FromResult<IReadOnlyList<LyricsCandidate>>([]);}
    }

    [Fact] public void LibraryRefreshRemovesForeignRowsFromBrowsingAndWorkWithoutDeletingPersonalLyrics()
    {
        string source=Path.Combine(root,"Library.musicdb"),database=Path.Combine(root,"lyrics.db");
        var own=new LibraryTrack("1",new("Own song","Own artist","Own album",180000));
        var cached=new LibraryTrack("2",new("Catalog song","Catalog artist","Catalog album",190000));
        using(var repository=new LyricsRepository(database))
        {
            repository.SyncLibrary(new(source,2,[own,cached],"old"));
            var binding=repository.Save(cached.Track,"A personal original","manual",true,plain:true);repository.SetOffset(binding.Id,123);
            repository.SyncLibrary(new(source,1,[own],"new"));
            var retained=repository.Find(cached.Track)!;Assert.True(retained.Manual);Assert.Equal(123,retained.OffsetMs);Assert.Equal("A personal original",retained.Raw);
            Assert.Equal(1,repository.GetLibraryProgress().Total);Assert.Equal("1",Assert.Single(repository.GetLibraryQueue()).PersistentId);
        }
        // An old instance can reactivate a catalog row. It still cannot turn it
        // into verified offline membership in the new reader/worker.
        using(var db=new SqliteConnection($"Data Source={database}")){db.Open();using var c=db.CreateCommand();c.CommandText="UPDATE LibraryTracks SET Active=1";c.ExecuteNonQuery();}
        var result=new MusicLibraryCatalog(root,source).Load();Assert.Equal("Own song",Assert.Single(result.Songs).Title);
        using var reopened=new LyricsRepository(database);Assert.Equal(1,reopened.GetLibraryProgress().Total);Assert.Equal("1",Assert.Single(reopened.GetLibraryQueue()).PersistentId);
    }
    [Fact] public void OfflineFallbackDoesNotMergeAFormerSelectedLibrary()
    {
        var a=Path.Combine(root,"first.musicdb");var b=Path.Combine(root,"second.musicdb");
        using(var repository=new LyricsRepository(Path.Combine(root,"lyrics.db")))
        {
            repository.SyncLibrary(new(a,1,[new("1",new("First","Artist","Album",1000))],"a"));
            repository.SyncLibrary(new(b,1,[new("2",new("Second","Artist","Album",1000))],"b"));
        }
        Assert.Equal("Second",Assert.Single(new MusicLibraryCatalog(root,b).Load().Songs).Title);
        var previous=new MusicLibraryCatalog(root,a).Load();Assert.Empty(previous.Songs);Assert.True(previous.Unavailable);
    }
    [Fact] public void LegacyRowsWithoutAConfirmedMembershipSetCannotBecomeAnOfflineLibrary()
    {
        using(var repository=new LyricsRepository(Path.Combine(root,"lyrics.db")))repository.SyncLibrary(new("old",1,[new("1",new("Unconfirmed","Artist","Album",1000))],"old"));
        using(var db=new SqliteConnection($"Data Source={Path.Combine(root,"lyrics.db")}")){db.Open();using var c=db.CreateCommand();c.CommandText="DROP TABLE LibraryMembership";c.ExecuteNonQuery();}
        var result=new MusicLibraryCatalog(root,Path.Combine(root,"unavailable.musicdb")).Load();Assert.Empty(result.Songs);Assert.True(result.Unavailable);
    }
}

internal sealed record PlaylistFixture(ulong[] Members,bool Master=true,string Name="Original fixture",int? Declared=null,bool InvalidReference=false);
internal static class MusicDbPlaylistFixture
{
    internal static void Write(BinaryWriter writer,params PlaylistFixture[] playlists)
    {
        writer.Write("lPma"u8);writer.Write(12);writer.Write(playlists.Length);
        foreach(var playlist in playlists)
        {
            byte[] title=Encoding.Unicode.GetBytes(playlist.Name);var name=new byte[36+title.Length];"boma"u8.CopyTo(name);BitConverter.GetBytes(20).CopyTo(name,4);BitConverter.GetBytes(name.Length).CopyTo(name,8);BitConverter.GetBytes(200).CopyTo(name,12);BitConverter.GetBytes(1).CopyTo(name,20);BitConverter.GetBytes(title.Length).CopyTo(name,24);title.CopyTo(name,36);
            var header=new byte[48];"lpma"u8.CopyTo(header);BitConverter.GetBytes(header.Length).CopyTo(header,4);BitConverter.GetBytes(header.Length+name.Length+88*playlist.Members.Length).CopyTo(header,8);BitConverter.GetBytes(1+playlist.Members.Length).CopyTo(header,12);BitConverter.GetBytes(playlist.Declared??playlist.Members.Length).CopyTo(header,16);BitConverter.GetBytes(playlist.Master?1:0).CopyTo(header,44);writer.Write(header);writer.Write(name);
            foreach(ulong id in playlist.Members)
            {
                var reference=new byte[88];"boma"u8.CopyTo(reference);BitConverter.GetBytes(20).CopyTo(reference,4);BitConverter.GetBytes(reference.Length).CopyTo(reference,8);BitConverter.GetBytes(206).CopyTo(reference,12);(playlist.InvalidReference?"bad!"u8:"ipfa"u8).CopyTo(reference.AsSpan(20));BitConverter.GetBytes(68).CopyTo(reference,24);BitConverter.GetBytes(id).CopyTo(reference,40);writer.Write(reference);
            }
        }
    }
}
