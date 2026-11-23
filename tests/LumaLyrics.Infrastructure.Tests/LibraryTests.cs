using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

    [Fact] public async Task PrefetchCompletesTheExactSweepBeforeFallbackAndReusesExactMisses()
    {
        var (path,database)=BatchLibrary(4,i=>i switch
        {
            0=>"First recording",
            1=>"Exact recording",
            2=>"exact recording",
            _=>"Last recording"
        });
        var requests=new ConcurrentQueue<Uri>();
        using var provider=new ResilientLyricsProvider(
            new LrclibProvider(new BatchHandler((uri,_)=>
            {
                requests.Enqueue(uri);
                if(uri.AbsolutePath!="/api/get")return Task.FromResult(Json("[]"));
                return Task.FromResult(QueryValue(uri)=="Exact recording"
                    ?ExactOriginal("Exact recording"):new HttpResponseMessage(HttpStatusCode.NotFound));
            }),minimumInterval:TimeSpan.Zero),
            new NetEaseTranslationProvider(new BatchHandler((uri,_)=>
            {
                requests.Enqueue(uri);
                return Task.FromResult(EmptyNetEase());
            }),TimeSpan.Zero));
        var worker=new LibraryPrefetcher(database,provider);
        var cached=new List<Track>();
        worker.Cached+=cached.Add;

        await worker.RunAsync(path,default);

        var exact=requests.Where(uri=>uri.AbsolutePath=="/api/get").ToArray();
        Assert.Equal(new[]{"First recording","Exact recording","Last recording"},exact.Select(QueryValue));
        Assert.Equal(exact,requests.Take(3));
        Assert.Equal(new[]{"First recording","Last recording"},requests.Where(IsOrdinaryLrclibSearch).Select(QueryValue).Order());
        Assert.Equal("Exact recording",Assert.Single(cached).Title);
        using var repository=new LyricsRepository(database,true);
        var progress=repository.GetLibraryProgress();
        Assert.Equal(2,progress.Synced);
        Assert.Equal(2,progress.Unavailable);
    }

    [Fact] public async Task PrefetchLeavesTheExactSweepWhenLrclibIsRateLimitedAndUsesNetEase()
    {
        var (path,database)=BatchLibrary(3);
        var primaryRequests=new ConcurrentQueue<Uri>();
        var secondarySearches=new ConcurrentQueue<string>();
        int foregroundChecks=0;
        using var provider=new ResilientLyricsProvider(
            new LrclibProvider(new BatchHandler((uri,_)=>
            {
                primaryRequests.Enqueue(uri);
                var rateLimited=new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                rateLimited.Headers.RetryAfter=new RetryConditionHeaderValue(TimeSpan.FromMinutes(1));
                return Task.FromResult(rateLimited);
            }),minimumInterval:TimeSpan.Zero),
            new NetEaseTranslationProvider(new BatchHandler((uri,_)=>
            {
                if(uri.AbsolutePath=="/api/song/lyric")
                    return Task.FromResult(Json(JsonSerializer.Serialize(new{code=200,lrc=new{lyric=BatchOriginal}})));
                var query=QueryValue(uri);
                if(query.EndsWith(" Artist",StringComparison.Ordinal))secondarySearches.Enqueue(query);
                return Task.FromResult(query=="Track 1 Artist"
                    ?Json("""{"code":200,"result":{"songs":[{"id":1,"name":"Track 1","artists":[{"name":"Artist"}],"album":{"name":"Album"},"duration":10000}]}}""")
                    :EmptyNetEase());
            }),TimeSpan.Zero));
        var worker=new LibraryPrefetcher(database,provider,()=>
        {
            Interlocked.Increment(ref foregroundChecks);
            return false;
        });

        await worker.RunAsync(path,default);

        Assert.Equal("Track 1",QueryValue(Assert.Single(primaryRequests)));
        Assert.Equal(4,foregroundChecks); // One exact probe and three fallback admissions.
        Assert.Equal(new[]{"Track 1 Artist","Track 2 Artist","Track 3 Artist"},secondarySearches.Order());
        using var repository=new LyricsRepository(database,true);
        var original=repository.FindReadOnly(new("Track 1","Artist","Album",10000));
        Assert.Equal(BatchOriginal,original?.Raw);
        Assert.StartsWith("NetEase #1",original?.Source);
    }

    [Fact] public async Task PrefetchWithOneSlotRunsTheCompleteChainForTheFirstRecording()
    {
        var (path,database)=BatchLibrary(3);
        var requests=new ConcurrentQueue<string>();
        using var provider=new ResilientLyricsProvider(
            new LrclibProvider(new BatchHandler((uri,_)=>
            {
                requests.Enqueue(uri.AbsolutePath+" "+QueryValue(uri));
                return Task.FromResult(uri.AbsolutePath=="/api/get"?new HttpResponseMessage(HttpStatusCode.NotFound):Json("[]"));
            }),minimumInterval:TimeSpan.Zero),
            new NetEaseTranslationProvider(new BatchHandler((uri,_)=>
            {
                requests.Enqueue(uri.AbsolutePath+" "+QueryValue(uri));
                return Task.FromResult(EmptyNetEase());
            }),TimeSpan.Zero));

        await new LibraryPrefetcher(database,provider).RunAsync(path,default,maximum:1);

        Assert.Equal(new[]{"/api/get Track 1","/api/search Track 1","/api/search/get Track 1 Artist"},requests.Take(3));
        Assert.Single(requests,request=>request.StartsWith("/api/get ",StringComparison.Ordinal));
        using var repository=new LyricsRepository(database,true);
        Assert.Equal(2,repository.GetLibraryProgress().Pending);
    }

    [Fact] public async Task PrefetchReservesOnlyTheSlotsLeftAfterExactSavesEvenWithConcurrentFallback()
    {
        var (path,database)=BatchLibrary(7,i=>i<2?"Exact recording":"Fallback "+i);
        var requested=new ConcurrentQueue<string>();
        var twoWorkersStarted=Signal();
        var release=Signal();
        int requests=0;
        using var provider=new ResilientLyricsProvider(
            new LrclibProvider(new BatchHandler((uri,_)=>
            {
                var response=uri.AbsolutePath!="/api/get"?Json("[]"):QueryValue(uri)=="Exact recording"
                    ?ExactOriginal("Exact recording"):new HttpResponseMessage(HttpStatusCode.NotFound);
                return Task.FromResult(response);
            }),minimumInterval:TimeSpan.Zero),
            new TranslationLookup(async(track,token)=>
            {
                requested.Enqueue(track.Title);
                if(Interlocked.Increment(ref requests)==2)twoWorkersStarted.TrySetResult();
                await release.Task.WaitAsync(token);
                return [];
            }));
        using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var run=new LibraryPrefetcher(database,provider).RunAsync(path,cancel.Token,maximum:3);
        try
        {
            await twoWorkersStarted.Task.WaitAsync(cancel.Token);
            using var repository=new LyricsRepository(database,true);
            Assert.Equal(2,repository.GetLibraryProgress().Synced);
        }
        finally{release.TrySetResult();await run;}

        Assert.Equal(new[]{"Fallback 2","Fallback 3"},requested.Order());
        using var readOnly=new LyricsRepository(database,true);
        Assert.Equal(2,readOnly.GetLibraryProgress().Unavailable);
        Assert.Equal(3,readOnly.GetLibraryProgress().Pending);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrefetchOverlapsHostsAndCancelsAllInflightWorkOnPause(bool pause)
    {
        var (path,database)=BatchLibrary(4);
        var primarySearches=new ConcurrentQueue<string>();
        var secondarySearches=new ConcurrentQueue<string>();
        var primaryStarted=Signal();
        var secondaryStarted=Signal();
        var thirdPrimaryStarted=Signal();
        var primaryCancelled=Signal();
        var secondaryCancelled=Signal();
        var releasePrimary=Signal();
        var releaseSecondary=Signal();
        int primaryActive=0,secondaryActive=0;
        using var provider=new ResilientLyricsProvider(
            new LrclibProvider(new BatchHandler(async(uri,token)=>
            {
                Assert.Equal(1,Interlocked.Increment(ref primaryActive));
                try
                {
                    if(uri.AbsolutePath=="/api/get")return new(HttpStatusCode.NotFound);
                    if(!IsOrdinaryLrclibSearch(uri))return Json("[]");
                    string title=QueryValue(uri);
                    primarySearches.Enqueue(title);
                    if(title=="Track 2")
                    {
                        using var registration=token.Register(()=>primaryCancelled.TrySetResult());
                        primaryStarted.TrySetResult();
                        await releasePrimary.Task;
                    }
                    if(title=="Track 3")thirdPrimaryStarted.TrySetResult();
                    return Json("[]");
                }
                finally{Interlocked.Decrement(ref primaryActive);}
            }),minimumInterval:TimeSpan.Zero),
            new NetEaseTranslationProvider(new BatchHandler(async(uri,token)=>
            {
                Assert.Equal(1,Interlocked.Increment(ref secondaryActive));
                try
                {
                    string query=QueryValue(uri);
                    if(query.EndsWith(" Artist",StringComparison.Ordinal))secondarySearches.Enqueue(query);
                    if(query=="Track 1 Artist")
                    {
                        using var registration=token.Register(()=>secondaryCancelled.TrySetResult());
                        secondaryStarted.TrySetResult();
                        await releaseSecondary.Task;
                    }
                    return EmptyNetEase();
                }
                finally{Interlocked.Decrement(ref secondaryActive);}
            }),TimeSpan.Zero));
        using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var run=new LibraryPrefetcher(database,provider).RunAsync(path,cancel.Token);
        try
        {
            await Task.WhenAll(secondaryStarted.Task,primaryStarted.Task).WaitAsync(cancel.Token);
            Assert.Equal(new[]{"Track 1","Track 2"},primarySearches);
            Assert.Equal("Track 1 Artist",Assert.Single(secondarySearches));
            Assert.Equal(1,Volatile.Read(ref primaryActive));
            Assert.Equal(1,Volatile.Read(ref secondaryActive));
            if(pause)
            {
                File.WriteAllText(LibraryPrefetcher.PausePath(database),"");
                await Task.WhenAll(primaryCancelled.Task,secondaryCancelled.Task).WaitAsync(cancel.Token);
            }
            else
            {
                releasePrimary.TrySetResult();
                await thirdPrimaryStarted.Task.WaitAsync(cancel.Token);
            }
        }
        finally
        {
            releasePrimary.TrySetResult();
            releaseSecondary.TrySetResult();
            await run;
        }

        using var repository=new LyricsRepository(database,true);
        var progress=repository.GetLibraryProgress();
        Assert.Equal(pause?4:0,progress.Pending);
        Assert.Equal(pause?0:4,progress.Unavailable);
        Assert.Equal(pause?2:4,primarySearches.Count);
        Assert.Equal(pause?1:4,secondarySearches.Count);
        var terminal=JsonSerializer.Deserialize<LibraryProgress>(File.ReadAllText(LibraryPrefetcher.ProgressPath(database)));
        Assert.False(terminal!.Running);
        Assert.Contains(pause?"已暂停":"本轮完成",terminal.Message);
    }

    [Fact] public async Task PrefetchStopsRecordingErrorsAtFourWithThreeFallbackWorkers()
    {
        var (path,database)=BatchLibrary(8);
        var threeWorkersStarted=Signal();
        var release=Signal();
        int requests=0;
        using var provider=new ResilientLyricsProvider(new UnavailableLyricsProvider(),
            new TranslationLookup(async(_,token)=>
            {
                if(Interlocked.Increment(ref requests)==3)threeWorkersStarted.TrySetResult();
                await release.Task.WaitAsync(token);
                throw new LyricsProviderException("Synthetic source error");
            }));
        using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var run=new LibraryPrefetcher(database,provider).RunAsync(path,cancel.Token);
        try{await threeWorkersStarted.Task.WaitAsync(cancel.Token);}
        finally{release.TrySetResult();await run;}

        using var repository=new LyricsRepository(database,true);
        var progress=repository.GetLibraryProgress();
        Assert.Equal(4,progress.Errors);
        Assert.Equal(4,progress.Pending);
        Assert.InRange(requests,4,6);
        var terminal=JsonSerializer.Deserialize<LibraryProgress>(File.ReadAllText(LibraryPrefetcher.ProgressPath(database)));
        Assert.False(terminal!.Running);
        Assert.Contains("连续失败",terminal.Message);
    }

    (string Path,string Database) BatchLibrary(int count,Func<int,string>? title=null)
    {
        string path=Path.Combine(dir,"Library.musicdb");
        File.WriteAllBytes(path,Library(count,title:title,duration:_=>10000));
        return (path,Path.Combine(dir,"lyrics.db"));
    }
    const string BatchOriginal="[00:01]A light returns across the harbor\n[00:04]The river bends past the morning fields\n[00:08]We walk the quiet road back home";
    static TaskCompletionSource Signal()=>new(TaskCreationOptions.RunContinuationsAsynchronously);
    static bool IsOrdinaryLrclibSearch(Uri uri)=>uri.AbsolutePath=="/api/search"
        && uri.Query.StartsWith("?track_name=",StringComparison.Ordinal) && uri.Query.Contains("&artist_name=",StringComparison.Ordinal);
    static string QueryValue(Uri uri)=>Uri.UnescapeDataString(uri.Query.Split('&',2)[0].Split('=',2)[1]);
    static HttpResponseMessage Json(string json)=>new(HttpStatusCode.OK){Content=new StringContent(json)};
    static HttpResponseMessage EmptyNetEase()=>Json("""{"code":200,"result":{"songs":[]}}""");
    static HttpResponseMessage ExactOriginal(string title)=>Json(JsonSerializer.Serialize(
        new{id=1,trackName=title,artistName="Artist",albumName="Album",duration=10,syncedLyrics=BatchOriginal}));
    sealed class BatchHandler(Func<Uri,CancellationToken,Task<HttpResponseMessage>> send):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>send(request.RequestUri!,token);
    }
    sealed class TranslationLookup(Func<Track,CancellationToken,Task<IReadOnlyList<TranslationCandidate>>> lookup):ITranslationProvider
    {
        public Task<IReadOnlyList<TranslationCandidate>> LookupAsync(Track track,CancellationToken token)=>lookup(track,token);
    }
    sealed class UnavailableLyricsProvider:ILyricsProvider
    {
        public Task<IReadOnlyList<LyricsCandidate>> LookupAsync(Track track,CancellationToken token)
            =>throw new LyricsProviderException("Synthetic source error");
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
