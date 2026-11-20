using System.Net;
using System.Text.Json;
using LumaLyrics.Core;

namespace LumaLyrics.Infrastructure.Tests;

public sealed class LocalizedReleaseLookupTests
{
    const long AppleAlbumId=600001,AppleSongId=601001,SourceAlbumId=710001,SourceSongId=711001;
    static readonly Track[] Published=[new("Kaze No Josho","Sora Choir","Yume No Izumi",180400),
        new("Hoshi No Yakusoku","Sora Choir","Yume No Izumi",203200)];
    static readonly Track[] Japanese=[new("風の序章","そら合唱団","夢の泉",180400),
        new("星の約束","そら合唱団","夢の泉",203200)];
    static readonly Track[] Source=[Japanese[0] with{Artist="Aki",DurationMs=180440},
        Japanese[1] with{Artist="Aoi & Nene",DurationMs=203240}];
    const string Original="[00:04]静かな窓にひかりが差す\n[01:20]空を見上げて歩き出す\n[02:25]あたらしい風が吹く";
    const string Chinese="[00:04]光照进安静的窗\n[01:20]抬头望着天空启程\n[02:25]一阵新风吹来";

    [Fact]
    public async Task AlbumEvidenceResolvesLocalNamesAndDifferentVocalCreditsAndIsReused()
    {
        var fixture=new Fixture();
        using var provider=fixture.Provider();
        var track=Published[0];
        var session=track with{Artist=track.Artist+" — "+track.Album,Album="",DurationMs=180000};
        // Concurrent songs in the same release share a single set of catalogue requests.
        var both=await Task.WhenAll(provider.CompleteLookupAsync(session,null,default),
            provider.CompleteLookupAsync(Published[1],null,default));
        var result=both[0];
        var selected=Assert.IsType<AutomaticLyricsChoice>(AutomaticLyricsMatcher.Select(session,result));
        Assert.Equal(SourceSongId,selected.Candidate.Id);
        Assert.Equal(Source[0],selected.Candidate.Track);
        Assert.Contains("发行曲目已核对",MatchRules.Score(track,selected.Candidate).Reason);
        Assert.Equal(SourceSongId+1,AutomaticLyricsMatcher.Select(Published[1],both[1])?.Candidate.Id);
        Assert.Equal(SourceSongId,AutomaticLyricsMatcher.Select(Japanese[0],result)?.Candidate.Id);
        Assert.Null(AutomaticLyricsMatcher.Select(track with{Title=track.Title+" (Live)"},result));
        Assert.Null(AutomaticLyricsMatcher.Select(track with{Artist="Different group"},result));
        Assert.Equal(3,fixture.AppleRequests.Count);
        Assert.Single(fixture.NetEaseRequests,path=>path.Equals($"/api/album/{SourceAlbumId}",StringComparison.Ordinal));
        Assert.Equal(2,fixture.NetEaseRequests.Count(path=>path.StartsWith("/api/song/lyric?",StringComparison.Ordinal)));

        string root=Path.Combine(Path.GetTempPath(),"LumaCatalogue-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var database=Path.Combine(root,"lyrics.db");
            using(var repo=new LyricsRepository(database))
                repo.Save(track,selected.Candidate.Content,selected.Candidate.SourceLabel,false,providerJson:selected.Candidate.RawJson);
            using(var repo=new LyricsRepository(database,true))
            {
                var saved=Assert.IsType<LyricsBinding>(repo.FindReadOnly(track));
                Assert.True(LibraryPrefetcher.QualityOf(saved,track));
                var raw=Assert.IsType<NetEaseOriginal>(JsonSerializer.Deserialize<NetEaseOriginal>(saved.ProviderJson!));
                var proof=Assert.IsType<NetEaseReleaseEvidence>(raw.ReleaseEvidence);
                Assert.Equal(AppleAlbumId,proof.AppleReleaseId);
                Assert.Equal(AppleSongId,proof.AppleSongId);
                Assert.False(LibraryPrefetcher.QualityOf(saved with
                {
                    ProviderJson=JsonSerializer.Serialize(raw with{ReleaseEvidence=proof with{SourceAlbumId=888}})
                },track));
            }
            var translated=await new TranslationWorkflow(root,provider).PrepareAsync(track,true,false,default,result.TranslationCandidates);
            Assert.Equal(TranslationLookupState.Found,translated.Lookup.State);
            using var translations=new TranslationRepository(root,true);
            Assert.Equal(TranslationAlignment.Hash(Original),translations.Find(track,new LrcParser().Parse(Original))?.SourceHash);
            Assert.Equal(3,fixture.AppleRequests.Count);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root,true);
        }
    }

    [Theory]
    [InlineData("album-id")]
    [InlineData("album-artist")]
    [InlineData("positions")]
    [InlineData("extra-track")]
    [InlineData("duration")]
    [InlineData("version")]
    [InlineData("other-issue-date")]
    [InlineData("ambiguous")]
    [InlineData("apple-id")]
    [InlineData("apple-incomplete")]
    [InlineData("missing-title-credit")]
    [InlineData("single-different-artist")]
    public async Task UnconfirmedReleaseRemainsManual(string change)
    {
        var fixture=new Fixture(change);
        using var provider=fixture.Provider();
        var automatic=await provider.LookupAsync(Published[0],default);
        Assert.Empty(automatic);
        Assert.Equal("来源返回了曲目，但未能确认相同录音",LyricsLookupDiagnosis.Describe(Published[0],automatic));
        Assert.DoesNotContain(fixture.NetEaseRequests,path=>path.StartsWith("/api/song/lyric?",StringComparison.Ordinal));
        var manual=Assert.Single(await provider.SearchManualAsync(Published[0],default));
        Assert.Null(manual.CatalogMatch);
        Assert.False(AutomaticLyricsMatcher.Decide(Published[0],manual).Accepted);
    }

    [Fact]
    public async Task ManualSearchUsesReleaseWhenKeywordsReturnNoResults()
    {
        var fixture=new Fixture("album-search",keywordHit:false);
        using var provider=fixture.Provider();
        var candidate=Assert.Single(await provider.SearchManualAsync(Published[0],default));
        Assert.Equal(SourceSongId,candidate.Id);
        Assert.True(AutomaticLyricsMatcher.Decide(Published[0],candidate).Accepted);
        Assert.Contains(fixture.AppleRequests,path=>path.Contains("entity=album",StringComparison.Ordinal));
    }

    [Fact]
    public async Task CommonReleaseTitleIsRefinedWithThePublishedArtist()
    {
        var fixture=new Fixture("common-title");
        using var provider=fixture.Provider();
        var result=await provider.LookupAsync(Published[0],default);
        Assert.Equal(SourceSongId,AutomaticLyricsMatcher.Select(Published[0],result)?.Candidate.Id);
        Assert.Equal(2,fixture.NetEaseRequests.Count(path=>path.Contains("type=10",StringComparison.Ordinal)));
    }

    [Fact]
    public async Task AnExplicitGuestCreditCanBeInEitherTheSongTitleOrTheCredits()
    {
        var fixture=new Fixture("title-credit");
        using var provider=fixture.Provider();
        var result=await provider.LookupAsync(Published[0],default);
        Assert.Equal(SourceSongId,AutomaticLyricsMatcher.Select(Published[0],result)?.Candidate.Id);
    }

    [Fact]
    public async Task NeighbouringTrackCanCorroborateAnEditionWithoutPassingItsOwnDurationGate()
    {
        var fixture=new Fixture("reference-duration");
        using var provider=fixture.Provider();
        var first=await provider.LookupAsync(Published[0],default);
        var second=await provider.LookupAsync(Published[1],default);
        Assert.Equal(SourceSongId,AutomaticLyricsMatcher.Select(Published[0],first)?.Candidate.Id);
        Assert.Empty(second);
        Assert.Equal(3,fixture.AppleRequests.Count);
    }

    [Theory]
    [InlineData("", "已找到对应曲目，但来源未提供原词")]
    [InlineData("[00:00.00-1]Composer credit", "歌词时间标签无效，需手动确认")]
    public async Task VerifiedReleaseDoesNotOverrideLyricsChecks(string lyrics,string reason)
    {
        using var provider=new Fixture(lyric:lyrics,keywordHit:false).Provider();
        var result=await provider.LookupAsync(Published[0],default);
        Assert.Null(AutomaticLyricsMatcher.Select(Published[0],result));
        Assert.Null(AutomaticLyricsMatcher.SelectStatic(Published[0],result));
        Assert.Equal(reason,LyricsLookupDiagnosis.Describe(Published[0],result));
    }

    [Fact]
    public async Task OrdinaryMatchDoesNotQueryAppleCatalogue()
    {
        var fixture=new Fixture("ordinary");
        using var provider=fixture.Provider();
        var result=await provider.LookupAsync(Published[0],default);
        Assert.Equal(SourceSongId,AutomaticLyricsMatcher.Select(Published[0],result)?.Candidate.Id);
        Assert.Empty(fixture.AppleRequests);
    }

    [Fact]
    public async Task FailedCatalogueRequestCanBeRetried()
    {
        var fixture=new Fixture(){AppleUnavailable=true};
        using var provider=fixture.Provider();
        var unconfirmed=await provider.LookupAsync(Published[0],default);
        Assert.Empty(unconfirmed);
        Assert.True(LyricsLookupDiagnosis.Unavailable(unconfirmed));

        fixture.AppleUnavailable=false;
        var recovered=await provider.LookupAsync(Published[0],default);
        Assert.Equal(SourceSongId,AutomaticLyricsMatcher.Select(Published[0],recovered)?.Candidate.Id);
    }

    [Fact]
    public async Task CatalogueFailurePreservesManualSuggestions()
    {
        var fixture=new Fixture(){AppleUnavailable=true};
        using var provider=fixture.Provider();
        var results=await provider.SearchManualAsync(Published[0],default);
        Assert.True(LyricsLookupDiagnosis.Unavailable(results));
        var candidate=Assert.Single(results);
        Assert.Equal(SourceSongId,candidate.Id);
        Assert.Null(candidate.CatalogMatch);
    }

    [Fact]
    public async Task CallerCanCancelInFlightCatalogueLookup()
    {
        var began=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fixture=new Fixture();
        using var provider=fixture.Provider(new Handler(async (_,token)=>
        {
            began.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan,token);
            throw new InvalidOperationException("Request should be cancelled.");
        }));
        using var cancel=new CancellationTokenSource();
        var lookup=provider.LookupAsync(Published[0],cancel.Token);
        await began.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async()=>await lookup);
    }

    [Fact]
    public void EarlierAlbumCacheRemainsReadableOffline()
    {
        var track=new Track("Itoshiki Rufenburg","Ariabl'eyeS","Kegarenaki Ruriiro No Rosenkreuz",248308);
        var source=new Track("愛しきルーフェンブルク","LUNA & Rena","穢れなき瑠璃色のローゼンクロイツ",248346);
        var payload=new NetEaseOriginal("NetEase",558560456,source,Original,"https://music.163.com/#/song?id=558560456",CatalogAlbumId:38636065);
        var binding=new LyricsBinding("own-binding","own-track",Original,"NetEase #558560456",false,0,false,
            ProviderJson:JsonSerializer.Serialize(payload));
        Assert.True(LibraryPrefetcher.QualityOf(binding,track));
        Assert.False(LibraryPrefetcher.QualityOf(binding,track with{Title="Other song"}));
    }

    sealed class Fixture(string change="",string lyric=Original,bool keywordHit=true)
    {
        internal List<string> AppleRequests {get;}=[];
        internal List<string> NetEaseRequests {get;}=[];
        internal bool AppleUnavailable {get;set;}
        int Count=>change=="single-different-artist"?1:2;

        internal ResilientLyricsProvider Provider(HttpMessageHandler? apple=null)
            =>new(new EmptyOriginalProvider(),new NetEaseTranslationProvider(
                new Handler((uri,_)=>Task.FromResult(NetEase(uri))),TimeSpan.Zero,
                apple??new Handler((uri,_)=>Task.FromResult(Apple(uri)))));

        HttpResponseMessage Apple(Uri uri)
        {
            AppleRequests.Add(uri.PathAndQuery);
            if(AppleUnavailable)return new(HttpStatusCode.ServiceUnavailable);
            bool jp=uri.Query.Contains("country=jp",StringComparison.Ordinal);
            var tracks=jp?Japanese:Published;
            object header=new{wrapperType="collection",collectionId=AppleAlbumId,artistId=3321,
                collectionName=tracks[0].Album,artistName=tracks[0].Artist,trackCount=Count,releaseDate="2021-09-04T07:00:00Z"};
            var songs=tracks.Take(Count).Select((track,i)=>(object)new
            {
                kind="song",collectionId=AppleAlbumId,trackId=AppleSongId+i+(jp && change=="apple-id"?50:0),
                collectionName=track.Album,trackName=i==1 && change is "title-credit" or "missing-title-credit"
                    ?track.Title+" (with Nene)":track.Title,
                artistName=track.Artist,trackTimeMillis=(long)track.DurationMs!,
                discNumber=1,trackNumber=i+1
            }).ToArray();
            object[] results=uri.AbsolutePath=="/lookup"
                ?[header,.. (jp && change=="apple-incomplete"?songs.Take(1):songs)]
                :uri.Query.Contains("entity=album",StringComparison.Ordinal)?[header]:change=="album-search"?[]:songs;
            return Json(new{results});
        }

        HttpResponseMessage NetEase(Uri uri)
        {
            NetEaseRequests.Add(uri.PathAndQuery);
            if(uri.AbsolutePath.StartsWith("/api/album/",StringComparison.Ordinal))
            {
                long albumId=long.Parse(uri.Segments[^1]);
                var songs=Enumerable.Range(0,Count).Select(i=>Song(i,albumId,detail:true)).ToList();
                if(change=="extra-track")songs.Add(Song(1,albumId,detail:true));
                // The service order can differ from the disc and track numbers.
                songs.Reverse();
                return Json(new{code=200,album=Album(albumId,songs)});
            }
            if(uri.AbsolutePath=="/api/song/lyric")
                return Json(new{code=200,nolyric=lyric.Length==0,lrc=new{lyric},tlyric=new{lyric=Chinese}});
            if(uri.Query.Contains("type=10",StringComparison.Ordinal))
            {
                object[] albums=change=="ambiguous"?[Album(SourceAlbumId),Album(SourceAlbumId+1)]:[Album(SourceAlbumId)];
                var input=Uri.UnescapeDataString(uri.Query);
                int albumCount=change=="common-title" && !input.Contains(Japanese[0].Artist,StringComparison.Ordinal)?100:albums.Length;
                return Json(new{code=200,result=new{albumCount,albums}});
            }
            object[] keywordSongs=keywordHit?[Song(0,SourceAlbumId,detail:false)]:[];
            return Json(new{code=200,result=new{songs=keywordSongs}});
        }

        object Album(long id,IReadOnlyList<object>? songs=null)=>new
        {
            id,name=Japanese[0].Album,artist=new{name=change=="album-artist"?"Miscredited group":Japanese[0].Artist},
            size=Count,publishTime=change=="other-issue-date"?1700000000000:1630713600000,songs
        };

        object Song(int i,long albumId,bool detail)
        {
            var track=change=="ordinary"?Published[i]:Source[i];
            if(detail)
            {
                if(change=="version")track=track with{Title=track.Title+" (Ballade Version)"};
                if(change=="duration")track=track with{DurationMs=track.DurationMs+2000};
                if(change=="title-credit" && i==1)track=track with{Artist=Japanese[i].Artist+" & Nene"};
                if(change=="reference-duration" && i==1)track=track with{DurationMs=track.DurationMs+1300};
            }
            return new
            {
                id=SourceSongId+(albumId-SourceAlbumId)*10+i,name=track.Title,
                artists=track.Artist.Split(" & ").Select(name=>new{name}),duration=track.DurationMs,
                album=new{id=detail && change=="album-id"?albumId+100:albumId,name=track.Album},
                disc="1",position=detail && change=="positions"?1:i+1
            };
        }
    }

    static HttpResponseMessage Json(object value)=>new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(value))};
    sealed class Handler(Func<Uri,CancellationToken,Task<HttpResponseMessage>> send):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
            =>send(request.RequestUri!,token);
    }
    sealed class EmptyOriginalProvider:ILyricsProvider
    {
        public Task<IReadOnlyList<LyricsCandidate>> LookupAsync(Track track,CancellationToken token)
            =>Task.FromResult<IReadOnlyList<LyricsCandidate>>([]);
    }
}
