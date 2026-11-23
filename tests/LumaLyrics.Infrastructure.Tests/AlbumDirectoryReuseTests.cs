using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using LumaLyrics.Core;

namespace LumaLyrics.Infrastructure.Tests;

public sealed class AlbumDirectoryReuseTests
{
    const long AlbumId=301,FirstSongId=501;
    const string Original="[00:03]The daylight reaches the water\n[00:33]We watch the colors drift past\n[01:05]The quiet harbor leads us home";
    static readonly Track[] Recordings=
    [
        new("First Light","Harbor Ensemble","Northern Skies",83000),
        new("After the Rain","Harbor Ensemble","Northern Skies",94000),
        new("Evening Bells","Harbor Ensemble","Northern Skies",105000),
        new("Bonus Track","Harbor Ensemble","Northern Skies",99000)
    ];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FirstTrackRemembersAnAlbumAndLaterTracksUseItsDirectory(bool nested)
    {
        var fixture=new Fixture(_=>Task.FromResult(Detail(nested:nested)));
        using var source=new NetEaseTranslationProvider(fixture,TimeSpan.Zero);

        Assert.Equal(FirstSongId,Assert.Single(await source.LookupAsync(Recordings[0],default)).Id);
        Assert.Equal(1,fixture.Searches);
        Assert.Equal(0,fixture.AlbumRequests);
        Assert.Equal(FirstSongId+1,Assert.Single(await source.LookupAsync(Recordings[1],default)).Id);
        Assert.Equal(1,fixture.Searches);
        Assert.Equal(1,fixture.AlbumRequests);
        var normalized=Recordings[2] with{Artist="Harbor-Ensemble",Album="NORTHERN SKIES"};
        Assert.Equal(FirstSongId+2,Assert.Single(await source.LookupAsync(normalized,default)).Id);
        Assert.Equal(1,fixture.Searches);
        Assert.Equal(1,fixture.AlbumRequests);
        Assert.Equal(FirstSongId+3,Assert.Single(await source.LookupAsync(Recordings[3],default)).Id);
        Assert.Equal(2,fixture.Searches);
        Assert.Equal(1,fixture.AlbumRequests);
        Assert.Equal(Enumerable.Range(0,4).Select(i=>FirstSongId+i),fixture.LyricIds);
    }

    [Fact]
    public async Task ConcurrentTracksShareOneInFlightAlbumRequest()
    {
        var started=Signal();
        var release=Signal();
        var fixture=new Fixture(async token=>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(token);
            return Detail();
        });
        using var source=new NetEaseTranslationProvider(fixture,TimeSpan.Zero);
        using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await source.LookupAsync(Recordings[0],cancel.Token);

        var second=source.LookupAsync(Recordings[1],cancel.Token);
        await started.Task.WaitAsync(cancel.Token);
        var third=source.LookupAsync(Recordings[2],cancel.Token);
        release.TrySetResult();
        var tracks=await Task.WhenAll(second,third);

        Assert.Equal(new[]{FirstSongId+1,FirstSongId+2},tracks.Select(results=>Assert.Single(results).Id));
        Assert.Equal(1,fixture.Searches);
        Assert.Equal(1,fixture.AlbumRequests);
    }

    [Theory]
    [InlineData("wrong name")]
    [InlineData("wrong album id")]
    [InlineData("wrong recording duration")]
    [InlineData("wrong recording version")]
    [InlineData("wrong song album id")]
    [InlineData("single-track release")]
    [InlineData("too few parsed tracks")]
    [InlineData("not found")]
    [InlineData("service unavailable")]
    [InlineData("timeout")]
    [InlineData("truncated response")]
    [InlineData("malformed")]
    public async Task UnusableAlbumDetailFallsBackToRecordingSearch(string issue)
    {
        var fixture=new Fixture(_=>Task.FromResult(issue switch
        {
            "wrong name"=>Detail(albumName:"Different Edition"),
            "wrong album id"=>Detail(albumId:AlbumId+1),
            "wrong recording duration"=>Detail(second:Song(1,Recordings[1] with{DurationMs=119000},id:999)),
            "wrong recording version"=>Detail(second:Song(1,Recordings[1] with{Title=Recordings[1].Title+" (Live)"},id:999)),
            "wrong song album id"=>Detail(second:Song(1,albumId:AlbumId+2,id:999)),
            "single-track release"=>Detail(count:1),
            "too few parsed tracks"=>Detail(songs:[Song(1)]),
            "not found"=>new(HttpStatusCode.NotFound),
            "service unavailable"=>new(HttpStatusCode.ServiceUnavailable),
            "timeout"=>throw new OperationCanceledException("Simulated non-caller album timeout."),
            "truncated response"=>throw new IOException("Simulated interrupted album response."),
            _=>new(HttpStatusCode.OK){Content=new StringContent("{")}
        }));
        using var source=new NetEaseTranslationProvider(fixture,TimeSpan.Zero);

        await source.LookupAsync(Recordings[0],default);
        var result=Assert.Single(await source.LookupAsync(Recordings[1],default));

        Assert.Equal(FirstSongId+1,result.Id);
        Assert.Equal(2,fixture.Searches);
        Assert.Equal(1,fixture.AlbumRequests);
        Assert.Equal(new[]{FirstSongId,FirstSongId+1},fixture.LyricIds);
    }

    [Theory]
    [InlineData(405)]
    [InlineData(429)]
    public async Task AlbumRateLimitPropagatesWithoutARecordingSearch(int code)
    {
        var fixture=new Fixture(_=>Task.FromResult(Json(new{code})));
        using var source=new NetEaseTranslationProvider(fixture,TimeSpan.Zero);
        await source.LookupAsync(Recordings[0],default);

        var error=await Assert.ThrowsAsync<LyricsProviderException>(()=>source.LookupAsync(Recordings[1],default));

        Assert.Equal(code,error.StatusCode);
        Assert.Equal(1,fixture.Searches);
        Assert.Equal(1,fixture.AlbumRequests);
        Assert.Equal(FirstSongId,Assert.Single(fixture.LyricIds));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DirectoryLyricsStillRequireAutomaticAndOfflineChecks(bool acceptable)
    {
        var fixture=new Fixture(_=>Task.FromResult(Detail()),acceptable?Original:"[00:00.00-1]Credit");
        using var source=new NetEaseTranslationProvider(fixture,TimeSpan.Zero);
        await source.LookupAsync(Recordings[0],default);
        var result=Assert.Single(await source.LookupAsync(Recordings[1],default));
        var candidate=result.OriginalCandidateFor(Recordings[1]);

        Assert.Equal(1,fixture.Searches);
        Assert.Equal(1,fixture.AlbumRequests);
        Assert.Null(candidate.CatalogMatch);
        Assert.True(NetEaseTranslationProvider.RecordingMatches(Recordings[1],candidate.Track));
        Assert.Equal(acceptable,AutomaticLyricsMatcher.Select(Recordings[1],[candidate]) is not null);
        Assert.Equal(acceptable,AutomaticLyricsMatcher.Assess(candidate).Accepted);

        var payload=Assert.IsType<NetEaseOriginal>(JsonSerializer.Deserialize<NetEaseOriginal>(candidate.RawJson));
        Assert.Equal(AlbumId,payload.CatalogAlbumId);
        Assert.Equal(Recordings[1],payload.Track);
        Assert.Null(payload.ReleaseEvidence);
        var binding=new LyricsBinding("binding","recording",candidate.Content,candidate.SourceLabel,false,0,false,
            ProviderJson:candidate.RawJson);
        Assert.Equal(acceptable,LibraryPrefetcher.QualityOf(binding,Recordings[1]));
        Assert.False(LibraryPrefetcher.QualityOf(binding,Recordings[1] with{DurationMs=120000}));
        Assert.False(LibraryPrefetcher.QualityOf(binding,Recordings[1] with{Title="After the Rain (Live)"}));
    }

    [Fact]
    public async Task ClearDuringAlbumRequestDiscardsTheOldDirectory()
    {
        var started=Signal();
        var release=Signal();
        var fixture=new Fixture(async token=>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(token);
            return Detail();
        });
        using var source=new ResilientLyricsProvider(new EmptyPrimary(),new NetEaseTranslationProvider(fixture,TimeSpan.Zero));
        using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await source.LookupAsync(Recordings[0],cancel.Token);
        var second=source.LookupAsync(Recordings[1],cancel.Token);
        await started.Task.WaitAsync(cancel.Token);

        source.ClearSessionCache();
        release.TrySetResult();
        Assert.Single(await second);
        Assert.Single(await source.LookupAsync(Recordings[2],cancel.Token));

        Assert.Equal(2,fixture.Searches);
        Assert.Equal(1,fixture.AlbumRequests);
    }

    static TaskCompletionSource Signal()=>new(TaskCreationOptions.RunContinuationsAsynchronously);
    static HttpResponseMessage Detail(bool nested=false,int count=3,string? albumName=null,long albumId=AlbumId,
        object? second=null,object[]? songs=null)
    {
        var rows=songs??[Song(0),second??Song(1),Song(2)];
        string name=albumName??Recordings[0].Album;
        var artist=new{name=Recordings[0].Artist};
        return nested?Json(new{code=200,album=new{id=albumId,name,artist,size=count,songs=rows}})
            :Json(new{code=200,album=new{id=albumId,name,artist,size=count},songs=rows});
    }
    static object Song(int index,Track? recording=null,long albumId=AlbumId,long? id=null)
    {
        var track=recording??Recordings[index];
        return new{id=id??(FirstSongId+index),name=track.Title,artists=new[]{new{name=track.Artist}},
            album=new{id=albumId,name=track.Album},duration=track.DurationMs,disc=1,position=index+1};
    }
    static HttpResponseMessage Json(object value)=>new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(value))};

    sealed class Fixture(Func<CancellationToken,Task<HttpResponseMessage>> readAlbum,string lyric=Original):HttpMessageHandler
    {
        readonly ConcurrentQueue<Uri> requests=new();
        public int Searches=>requests.Count(uri=>uri.AbsolutePath=="/api/search/get");
        public int AlbumRequests=>requests.Count(uri=>uri.AbsolutePath==$"/api/album/{AlbumId}");
        public IEnumerable<long> LyricIds=>requests.Where(uri=>uri.AbsolutePath=="/api/song/lyric")
            .Select(uri=>long.Parse(uri.Query.Split('&',2)[0][4..]));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            var uri=request.RequestUri!;
            requests.Enqueue(uri);
            if(uri.AbsolutePath==$"/api/album/{AlbumId}")return readAlbum(token);
            if(uri.AbsolutePath=="/api/song/lyric")
                return Task.FromResult(Json(new{code=200,lrc=new{lyric},tlyric=new{lyric="[00:03]晨光照到水面"}}));
            Assert.Equal("/api/search/get",uri.AbsolutePath);
            var query=Uri.UnescapeDataString(uri.Query);
            int index=Array.FindIndex(Recordings,track=>query.StartsWith("?s="+track.Title+" ",StringComparison.Ordinal));
            return Task.FromResult(Json(new{code=200,result=new{songs=index>=0?new[]{Song(index)}:[]}}));
        }
    }

    sealed class EmptyPrimary:ILyricsProvider
    {
        public Task<IReadOnlyList<LyricsCandidate>> LookupAsync(Track track,CancellationToken token)
            =>Task.FromResult<IReadOnlyList<LyricsCandidate>>([]);
    }
}
