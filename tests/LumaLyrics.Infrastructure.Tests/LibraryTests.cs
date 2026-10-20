using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.Infrastructure.Tests;
public class LibraryTests : IDisposable
{
    readonly string dir=Path.Combine(Path.GetTempPath(),"Luma-library-"+Guid.NewGuid().ToString("N"));
    public LibraryTests(){Directory.CreateDirectory(dir);}
    static byte[] Library(int count=2,int? declared=null)
    {
        using var content=new MemoryStream();using var writer=new BinaryWriter(content,Encoding.UTF8,true);
        writer.Write("ltma"u8);writer.Write(12);writer.Write(declared??count);
        for(int i=0;i<count;i++)
        {
            writer.Write("itma"u8);writer.Write(24);writer.Write(0);writer.Write(4);writer.Write((ulong)(i+1));
            void Text(int type,string value){var b=Encoding.Unicode.GetBytes(value);var chunk=new byte[36+b.Length];"boma"u8.CopyTo(chunk);BitConverter.GetBytes(20).CopyTo(chunk,4);BitConverter.GetBytes(chunk.Length).CopyTo(chunk,8);BitConverter.GetBytes(type).CopyTo(chunk,12);BitConverter.GetBytes(1).CopyTo(chunk,20);BitConverter.GetBytes(b.Length).CopyTo(chunk,24);b.CopyTo(chunk,36);writer.Write(chunk);}
            Text(2,$"Track {i+1}");Text(3,"Album");Text(4,"Artist");var numeric=new byte[180];"boma"u8.CopyTo(numeric);BitConverter.GetBytes(20).CopyTo(numeric,4);BitConverter.GetBytes(180).CopyTo(numeric,8);BitConverter.GetBytes(1).CopyTo(numeric,12);BitConverter.GetBytes(180000).CopyTo(numeric,176);writer.Write(numeric);
        }
        using var zipped=new MemoryStream();using(var z=new ZLibStream(zipped,CompressionLevel.SmallestSize,true))z.Write(content.ToArray());var compressed=zipped.ToArray();int crypt=compressed.Length-compressed.Length%16;
        var file=new byte[160+compressed.Length];"hfma"u8.CopyTo(file);BitConverter.GetBytes(160).CopyTo(file,4);BitConverter.GetBytes(file.Length).CopyTo(file,8);BitConverter.GetBytes(crypt).CopyTo(file,84);
        using var aes=Aes.Create();aes.Key=Encoding.ASCII.GetBytes("BHUILuilfghuila3");aes.EncryptEcb(compressed.AsSpan(0,crypt),file.AsSpan(160),PaddingMode.None);compressed.AsSpan(crypt).CopyTo(file.AsSpan(160+crypt));return file;
    }
    [Fact] public void ReadsFullTrackCountAndDoesNotModifySource()
    {
        var bytes=Library();var file=Path.Combine(dir,"Library.musicdb");File.WriteAllBytes(file,bytes);var snapshot=new AppleMusicLibraryReader().Read(file);Assert.Equal(2,snapshot.DeclaredCount);Assert.Equal(2,snapshot.Tracks.Count);Assert.All(snapshot.Tracks,x=>Assert.Equal(180000,x.Track.DurationMs));Assert.Equal(bytes,File.ReadAllBytes(file));
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
}
