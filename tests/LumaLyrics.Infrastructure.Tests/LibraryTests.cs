using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
using Microsoft.Data.Sqlite;
namespace LumaLyrics.Infrastructure.Tests;
public class LibraryTests : IDisposable
{
    readonly string dir=Path.Combine(Path.GetTempPath(),"Luma-library-"+Guid.NewGuid().ToString("N"));
    public LibraryTests(){Directory.CreateDirectory(dir);}
    internal static byte[] Library(int count=2,int? declared=null,string? albumArtist=null,Action<BinaryWriter,int>? playlists=null,
        Func<int,string>? title=null,Func<int,int>? duration=null)
    {
        using var content=new MemoryStream();using var writer=new BinaryWriter(content,Encoding.UTF8,true);
        writer.Write("ltma"u8);writer.Write(12);writer.Write(declared??count);
        for(int i=0;i<count;i++)
        {
            writer.Write("itma"u8);writer.Write(24);writer.Write(0);writer.Write(albumArtist is null?4:5);writer.Write((ulong)(i+1));
            void Text(int type,string value){var b=Encoding.Unicode.GetBytes(value);var chunk=new byte[36+b.Length];"boma"u8.CopyTo(chunk);BitConverter.GetBytes(20).CopyTo(chunk,4);BitConverter.GetBytes(chunk.Length).CopyTo(chunk,8);BitConverter.GetBytes(type).CopyTo(chunk,12);BitConverter.GetBytes(1).CopyTo(chunk,20);BitConverter.GetBytes(b.Length).CopyTo(chunk,24);b.CopyTo(chunk,36);writer.Write(chunk);}
            Text(2,title?.Invoke(i)??$"Track {i+1}");Text(3,"Album");Text(4,"Artist");if(albumArtist is not null)Text(0x1B,albumArtist);var numeric=new byte[180];"boma"u8.CopyTo(numeric);BitConverter.GetBytes(20).CopyTo(numeric,4);BitConverter.GetBytes(180).CopyTo(numeric,8);BitConverter.GetBytes(1).CopyTo(numeric,12);BitConverter.GetBytes(duration?.Invoke(i)??180000).CopyTo(numeric,176);writer.Write(numeric);
        }
        if(playlists is null)MusicDbPlaylistFixture.Write(writer,new PlaylistFixture(Enumerable.Range(1,count).Select(i=>(ulong)i).ToArray()));else playlists(writer,count);
        using var zipped=new MemoryStream();using(var z=new ZLibStream(zipped,CompressionLevel.SmallestSize,true))z.Write(content.ToArray());var compressed=zipped.ToArray();int crypt=compressed.Length-compressed.Length%16;
        var file=new byte[160+compressed.Length];"hfma"u8.CopyTo(file);BitConverter.GetBytes(160).CopyTo(file,4);BitConverter.GetBytes(file.Length).CopyTo(file,8);BitConverter.GetBytes(crypt).CopyTo(file,84);
        using var aes=Aes.Create();aes.Key=Encoding.ASCII.GetBytes("BHUILuilfghuila3");aes.EncryptEcb(compressed.AsSpan(0,crypt),file.AsSpan(160),PaddingMode.None);compressed.AsSpan(crypt).CopyTo(file.AsSpan(160+crypt));return file;
    }
    [Fact] public void ReadsFullTrackCountAndDoesNotModifySource()
    {
        var bytes=Library();var file=Path.Combine(dir,"Library.musicdb");File.WriteAllBytes(file,bytes);var snapshot=new AppleMusicLibraryReader().Read(file);Assert.Equal(2,snapshot.DeclaredCount);Assert.Equal(2,snapshot.Tracks.Count);Assert.All(snapshot.Tracks,x=>Assert.Equal(180000,x.Track.DurationMs));Assert.Equal(bytes,File.ReadAllBytes(file));
    }
    [Fact] public void AlbumArtistMetadataDoesNotReplaceTheTrackArtist()
    {
        var library=new AppleMusicLibraryReader().Parse(Library(albumArtist:"Various Artists"));Assert.All(library.Tracks,t=>{Assert.Equal("Various Artists",t.AlbumArtist);Assert.Equal("Artist",t.Track.Artist);});
    }
    [Fact] public void InvalidEnvelopeAndPartialTrackSectionAreRejected()
    {
        Assert.Throws<InvalidDataException>(()=>new AppleMusicLibraryReader().Parse(new byte[160]));Assert.Throws<InvalidDataException>(()=>new AppleMusicLibraryReader().Parse(Library(1,2)));
        var data=Library();data[160]^=0xff;Assert.Throws<InvalidDataException>(()=>new AppleMusicLibraryReader().Parse(data));
    }
    sealed class Provider : ILyricsProvider
    {
        public int Requests;public Task<IReadOnlyList<LyricsCandidate>> LookupAsync(Track t,CancellationToken token)
        {
            Requests++;var lines=Enumerable.Range(0,6).Select(i=>$"This is self authored verse number {i}").ToArray();
            return Task.FromResult<IReadOnlyList<LyricsCandidate>>([new(1,t,string.Join("\n",lines.Select((s,i)=>$"[{i*25/60:00}:{i*25%60:00}]{s}")),string.Join("\n",lines),false)]);
        }
    }
    [Fact] public async Task PrefetchPersistsProgressAndRestartSkipsCachedSongs()
    {
        var file=Path.Combine(dir,"Library.musicdb");File.WriteAllBytes(file,Library());var db=Path.Combine(dir,"lyrics.db");var provider=new Provider();var worker=new LibraryPrefetcher(db,provider,pacing:TimeSpan.Zero);
        await worker.RunAsync(file,default,1);Assert.Equal(1,provider.Requests);await worker.RunAsync(file,default);Assert.Equal(2,provider.Requests);await worker.RunAsync(file,default);Assert.Equal(2,provider.Requests);
        using var repo=new LyricsRepository(db);Assert.Equal(2,repo.GetLibraryProgress().Cached);
    }
    [Fact] public async Task ManualLyricsAreNotOverwrittenDuringPrefetch()
    {
        var file=Path.Combine(dir,"Library.musicdb");File.WriteAllBytes(file,Library(1));var db=Path.Combine(dir,"lyrics.db");var track=new Track("Track 1","Artist","Album",180000);
        using(var repo=new LyricsRepository(db)){repo.Save(track,"user confirmed text","本地导入",true,plain:true);}
        var provider=new Provider();await new LibraryPrefetcher(db,provider,pacing:TimeSpan.Zero).RunAsync(file,default);Assert.Equal(0,provider.Requests);
        using var reopened=new LyricsRepository(db);Assert.Equal("user confirmed text",reopened.Find(track)!.Raw);Assert.Equal(1,reopened.GetLibraryProgress().Manual);Assert.Equal(0,reopened.GetLibraryProgress().Cached);
    }
    [Fact] public async Task PrefetchReadsCachedAndManualBindingsWithoutUpdatingTrackRows()
    {
        var path=Path.Combine(dir,"Library.musicdb");File.WriteAllBytes(path,Library());
        var snapshot=new AppleMusicLibraryReader().Read(path);
        var database=Path.Combine(dir,"lyrics.db");
        var manual=snapshot.Tracks[1];
        using(var repository=new LyricsRepository(database))
        {
            repository.SyncLibrary(snapshot);
            repository.Save(snapshot.Tracks[0].Track with{DurationMs=177000},"[00:00]Morning light falls softly\n[01:15]I walk along the river\n[02:10]The road will lead me home","Checked fixture",false);
            repository.Save(manual.Track with{DurationMs=179000},"Personal lyric fixture","Manual",true,plain:true);
            repository.SetLibraryState(new(path,manual.PersistentId,manual.Track),"Pending","Recheck this binding");
        }
        using var db=new SqliteConnection($"Data Source={database}");db.Open();
        using var command=db.CreateCommand();
        command.CommandText="CREATE TRIGGER RejectTrackWrites BEFORE UPDATE ON Tracks BEGIN SELECT RAISE(ABORT, 'prefetch updated Tracks'); END";
        command.ExecuteNonQuery();

        var provider=new Provider();
        await new LibraryPrefetcher(database,provider,pacing:TimeSpan.Zero).RunAsync(path,default);
        Assert.Equal(0,provider.Requests);
        command.CommandText="SELECT Duration FROM Tracks ORDER BY Title";
        using var reader=command.ExecuteReader();
        Assert.True(reader.Read());Assert.Equal(177000,reader.GetDouble(0));
        Assert.True(reader.Read());Assert.Equal(179000,reader.GetDouble(0));
        Assert.False(reader.Read());
        using var readOnly=new LyricsRepository(database,true);
        var progress=readOnly.GetLibraryProgress();Assert.Equal(1,progress.Cached);Assert.Equal(1,progress.Manual);
    }

    [Theory]
    [InlineData("Missing")]
    [InlineData("Incomplete")]
    [InlineData("Error")]
    public async Task DuplicateLookupFailuresAreDeferredPerMemberAndOnlyDeduplicatedWithinTheRun(string expectedState)
    {
        var path=Path.Combine(dir,"Library.musicdb");
        File.WriteAllBytes(path,Library(3,title:_=>"Shared recording label",duration:i=>i<2?180000:215000));
        var database=Path.Combine(dir,"lyrics.db");
        var provider=new LookupProvider(track=>expectedState switch
        {
            "Error"=>throw new LyricsProviderException("Synthetic source error"),
            "Incomplete"=>[new(1,track with{Artist="Another artist"},null,"Original fixture",false)],
            _=>[]
        });
        var worker=new LibraryPrefetcher(database,provider,pacing:TimeSpan.Zero);
        await worker.RunAsync(path,default);
        Assert.Equal(new double?[]{180000,215000},provider.Requests.Select(t=>t.DurationMs));

        using(var db=new SqliteConnection($"Data Source={database}"))
        {
            db.Open();using var command=db.CreateCommand();
            command.CommandText="SELECT COUNT(*) FROM LibraryTracks WHERE State=$state AND Detail IS NOT NULL AND RetryUtc IS NOT NULL";
            command.Parameters.AddWithValue("$state",expectedState);
            Assert.Equal(3L,command.ExecuteScalar());
        }
        using(var repository=new LyricsRepository(database))repository.ResetLibraryRetries();
        await worker.RunAsync(path,default);
        Assert.Equal(4,provider.Requests.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrefetchThrottlesIntermediateProgressAndReportsTheTerminalState(bool errors)
    {
        var path=Path.Combine(dir,"Library.musicdb");File.WriteAllBytes(path,Library(6));
        var database=Path.Combine(dir,"lyrics.db");
        var provider=new LookupProvider(_=>errors?throw new LyricsProviderException("Synthetic source error"):[]);
        var worker=new LibraryPrefetcher(database,provider,pacing:TimeSpan.Zero);
        var reports=new List<(long Time,LibraryProgress Progress)>();
        worker.Progress+=progress=>reports.Add((Environment.TickCount64,progress));

        await worker.RunAsync(path,default);

        Assert.True(reports[0].Progress.Running);
        Assert.Equal(0,reports[0].Progress.Errors+reports[0].Progress.Unavailable);
        for(int i=1;i<reports.Count-1;i++)Assert.True(reports[i].Time-reports[i-1].Time>=500);
        var final=reports[^1].Progress;
        Assert.False(final.Running);
        Assert.Equal(errors?4:6,provider.Requests.Count);
        Assert.Equal(errors?4:0,final.Errors);Assert.Equal(errors?0:6,final.Unavailable);
        Assert.Contains(errors?"连续失败":"本轮完成",final.Message);
        var saved=System.Text.Json.JsonSerializer.Deserialize<LibraryProgress>(File.ReadAllText(LibraryPrefetcher.ProgressPath(database)));
        Assert.Equal(final,saved);
    }

    sealed class LookupProvider(Func<Track,IReadOnlyList<LyricsCandidate>> lookup):ILyricsProvider
    {
        public List<Track> Requests {get;}=[];
        public Task<IReadOnlyList<LyricsCandidate>> LookupAsync(Track track,CancellationToken token)
        {Requests.Add(track);return Task.FromResult(lookup(track));}
    }
    [Fact] public async Task OldAutomaticStaticLyricsAreReplacedByTimedLyrics()
    {
        var file=Path.Combine(dir,"Library.musicdb");File.WriteAllBytes(file,Library(1));var db=Path.Combine(dir,"lyrics.db");var track=new Track("Track 1","Artist","Album",180000);
        using(var repo=new LyricsRepository(db))repo.Save(track,"Old automatic untimed text","LRCLIB #1",false,plain:true);
        // Simulate the v4 state which older releases considered a completed cache.
        using(var connection=new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db}")){connection.Open();using var c=connection.CreateCommand();c.CommandText="PRAGMA user_version=4";c.ExecuteNonQuery();}
        var provider=new Provider();await new LibraryPrefetcher(db,provider,pacing:TimeSpan.Zero).RunAsync(file,default);
        using var reopened=new LyricsRepository(db);Assert.Equal(1,provider.Requests);Assert.True(new LrcParser().Parse(reopened.Find(track)!.Raw).IsSynced);Assert.Equal(1,reopened.GetLibraryProgress().Synced);Assert.True(File.Exists(db+".pre-v5.backup"));
    }
    [Fact] public void RestoringAutomaticBindingRequeuesPreservedManualLibraryTrack()
    {
        var db=Path.Combine(dir,"lyrics.db");using var repo=new LyricsRepository(db);var track=new Track("Track 1","Artist","Album",180000);repo.SyncLibrary(new("fixture",1,[new("01",track)],"hash"));repo.Save(track,"Own static text","本地导入",true,plain:true);
        Assert.Empty(repo.GetLibraryQueue());Assert.Equal(1,repo.GetLibraryProgress().Manual);repo.RestoreAutomatic(track);Assert.Single(repo.GetLibraryQueue());Assert.Equal(1,repo.GetLibraryProgress().Pending);
    }
    [Fact] public async Task CompletedStateIsRecheckedWhenItsDocumentFailsTheCurrentPolicy()
    {
        var file=Path.Combine(dir,"Library.musicdb");File.WriteAllBytes(file,Library(1));var db=Path.Combine(dir,"lyrics.db");var track=new Track("Track 1","Artist","Album",180000);
        using(var repo=new LyricsRepository(db)){repo.SyncLibrary(new(file,1,[new("0000000000000001",track)],"hash"));repo.Save(track,"Old automatic static content","LRCLIB #1",false,plain:true);}
        using(var connection=new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db}")){connection.Open();using var c=connection.CreateCommand();c.CommandText="UPDATE LibraryTracks SET State='Cached',LyricsKind='Synced'";c.ExecuteNonQuery();}
        var provider=new Provider();await new LibraryPrefetcher(db,provider,pacing:TimeSpan.Zero).RunAsync(file,default);Assert.Equal(1,provider.Requests);
        using var reopened=new LyricsRepository(db);Assert.True(LibraryPrefetcher.QualityOf(reopened.Find(track)!,track));
    }
    [Fact] public void CombinedArtistFindsCanonicalCache()
    {
        var db=Path.Combine(dir,"lyrics.db");using var repo=new LyricsRepository(db);repo.Save(new("Song","Artist","Album",180000),"own text","本地导入",true,plain:true);Assert.NotNull(repo.Find(new("Song","Artist — Album","",180000)));
    }
    [Fact] public async Task PauseAndExclusiveLeasePreventBackgroundRequests()
    {
        var file=Path.Combine(dir,"Library.musicdb");File.WriteAllBytes(file,Library(1));var db=Path.Combine(dir,"lyrics.db");var provider=new Provider();
        File.WriteAllText(LibraryPrefetcher.PausePath(db),"");await new LibraryPrefetcher(db,provider,pacing:TimeSpan.Zero).RunAsync(file,default);Assert.Equal(0,provider.Requests);File.Delete(LibraryPrefetcher.PausePath(db));
        using var lease=new FileStream(Path.Combine(dir,"library-prefetch.lock"),FileMode.Open,FileAccess.ReadWrite,FileShare.None);
        await new LibraryPrefetcher(db,provider,pacing:TimeSpan.Zero).RunAsync(file,default);Assert.Equal(0,provider.Requests);Assert.True(LibraryPrefetcher.IsRunning(db));lease.Dispose();Assert.False(LibraryPrefetcher.IsRunning(db));
    }
    sealed class WaitingProvider : ILyricsProvider
    {
        public TaskCompletionSource<bool> Started=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<IReadOnlyList<LyricsCandidate>> LookupAsync(Track track,CancellationToken token){Started.TrySetResult(true);await Task.Delay(Timeout.Infinite,token);return [];}
    }
    [Fact] public async Task CrossProcessPauseInterruptsAnInflightRequest()
    {
        var file=Path.Combine(dir,"Library.musicdb");File.WriteAllBytes(file,Library(1));var db=Path.Combine(dir,"lyrics.db");var provider=new WaitingProvider();
        var worker=new LibraryPrefetcher(db,provider,pacing:TimeSpan.Zero);var run=worker.RunAsync(file,default);await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        File.WriteAllText(LibraryPrefetcher.PausePath(db),"");await run.WaitAsync(TimeSpan.FromSeconds(3));
        using var repo=new LyricsRepository(db);Assert.Equal(1,repo.GetLibraryProgress().Pending);Assert.Equal(0,repo.GetLibraryProgress().Cached);
        var progress=System.Text.Json.JsonSerializer.Deserialize<LibraryProgress>(File.ReadAllText(LibraryPrefetcher.ProgressPath(db)));Assert.False(progress!.Running);
    }
    public void Dispose(){Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(dir,true);}
    [Fact] public void ExplicitRetryClearsFailedLookupAndRequeuesOnlyUnfinishedActiveTracks()
    {
        using var repository=new LyricsRepository(Path.Combine(dir,"lyrics.db"));var tracks=Enumerable.Range(1,3).Select(i=>new LibraryTrack(i.ToString(),new("Own "+i,"Own artist","Own album",10000))).ToArray();
        repository.SyncLibrary(new("own",3,tracks,"own"));repository.Save(tracks[1].Track,"own manual","own",true,plain:true);repository.Save(tracks[2].Track,"","own",false,true);
        var missing=new LibraryQueueItem("own","1",tracks[0].Track);repository.SetLibraryState(missing,"Missing","own source unavailable");repository.RememberFailure(tracks[0].Track);
        repository.ResetLibraryRetries();Assert.False(repository.HasFailure(tracks[0].Track));Assert.Equal(1,repository.GetLibraryProgress().Pending);Assert.Equal(1,repository.GetLibraryProgress().Manual);Assert.Equal(1,repository.GetLibraryProgress().Instrumental);Assert.Single(repository.GetLibraryQueue());
    }
}
