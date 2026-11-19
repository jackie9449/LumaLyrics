using System.Net;
using System.Text.Json;
using LumaLyrics.Core;

namespace LumaLyrics.Infrastructure.Tests;

public sealed class NetEaseVerifiedReleaseTests
{
    const long AlbumId=38636065,SongId=558560456;
    static readonly Track Apple=new("Itoshiki Rufenburg","Ariabl'eyeS","Kegarenaki Ruriiro No Rosenkreuz",248308);
    static readonly Track Source=new("愛しきルーフェンブルク","LUNA & Rena","穢れなき瑠璃色のローゼンクロイツ",248346);
    const string Original="[00:04]静かな窓にひかりが差す\n[01:40]空を見上げて歩き出す\n[03:35]あたらしい風が吹く";
    const string Chinese="[00:04]光照进安静的窗\n[01:40]抬头望着天空启程\n[03:35]一阵新风吹来";

    [Fact]
    public async Task AcceptsLocalizedTitleAndVocalCreditsAndRetainsProvenanceForBatchReuse()
    {
        using var handler=new Handler(CatalogSong(SongId,AlbumId,Source));
        using var provider=Provider(handler);
        var session=Apple with{Artist=Apple.Artist+" — "+Apple.Album,Album="",DurationMs=248000};
        var result=await provider.CompleteLookupAsync(session,null,default);
        var selected=Assert.IsType<AutomaticLyricsChoice>(AutomaticLyricsMatcher.Select(session,result));
        Assert.Equal(SongId,selected.Candidate.Id);
        Assert.Equal(Source,selected.Candidate.Track);
        Assert.Contains("发行曲目已核对",MatchRules.Score(Apple,selected.Candidate).Reason);
        Assert.NotNull(AutomaticLyricsMatcher.Select(Apple with{Title=Source.Title,Album=Source.Album},result));
        Assert.Equal(2,handler.Requests.Count);
        Assert.StartsWith("/api/song/detail",handler.Requests[0]);
        Assert.Contains($"id={SongId}",handler.Requests[1]);

        string root=Path.Combine(Path.GetTempPath(),"LumaVerifiedLyrics-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using(var repository=new LyricsRepository(Path.Combine(root,"lyrics.db")))
            {
                var candidate=selected.Candidate;
                var binding=repository.Save(Apple,candidate.Content,candidate.SourceLabel,false,providerJson:candidate.RawJson);
                Assert.True(LibraryPrefetcher.QualityOf(binding,Apple));
                Assert.False(LibraryPrefetcher.QualityOf(binding,Apple with{Album=Apple.Album+" (Live)"}));
                Assert.False(LibraryPrefetcher.QualityOf(binding with{ProviderJson=candidate.RawJson.Replace(AlbumId.ToString(), "5")},Apple));
            }
            var resultWithTranslation=await new TranslationWorkflow(root,provider).PrepareAsync(Apple,true,false,default,result.TranslationCandidates);
            Assert.Equal(TranslationLookupState.Found,resultWithTranslation.Lookup.State);
            using var translations=new TranslationRepository(root,true);
            Assert.Equal(TranslationAlignment.Hash(Original),translations.Find(Apple,new LrcParser().Parse(Original))?.SourceHash);
            Assert.Equal(2,handler.Requests.Count);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root,true);
        }
    }

    [Theory]
    [InlineData("song-id")]
    [InlineData("album-id")]
    [InlineData("album")]
    [InlineData("credits")]
    [InlineData("duration")]
    [InlineData("version")]
    public async Task UnverifiedCatalogRowsRemainAvailableForManualSearch(string changed)
    {
        var recording=changed switch
        {
            "album"=>Source with{Album="Other release"},
            "credits"=>Source with{Artist="LUNA & Rena & Someone else"},
            "duration"=>Source with{DurationMs=250000},
            "version"=>Source with{Title=Source.Title+" (Live)"},
            _=>Source
        };
        using var handler=new Handler(CatalogSong(changed=="song-id"?3:SongId,changed=="album-id"?5:AlbumId,recording));
        using var provider=Provider(handler);
        var automatic=await provider.LookupAsync(Apple,default);
        Assert.Empty(automatic);
        Assert.Equal("来源返回了曲目，但未能确认相同录音",LyricsLookupDiagnosis.Describe(Apple,automatic));
        Assert.DoesNotContain(handler.Requests,path=>path.Contains("/lyric",StringComparison.Ordinal));

        var manual=Assert.Single(await provider.SearchManualAsync(Apple,default));
        Assert.Null(manual.CatalogMatch);
        Assert.False(AutomaticLyricsMatcher.Decide(Apple,manual).Accepted);
    }

    [Fact]
    public async Task VerifiedSongIsSuggestedEvenWhenKeywordSearchFindsNothing()
    {
        using var handler=new Handler(CatalogSong(SongId,AlbumId,Source),keywordHit:false);
        using var provider=Provider(handler);
        var candidate=Assert.Single(await provider.SearchManualAsync(Apple,default));
        Assert.Equal(SongId,candidate.Id);
        Assert.True(AutomaticLyricsMatcher.Decide(Apple,candidate).Accepted);
    }

    [Theory]
    [InlineData("", "已找到对应曲目，但来源未提供原词")]
    [InlineData("[00:00.00-1]Composer credit", "歌词时间标签无效，需手动确认")]
    public async Task ConfirmedRecordingWithoutUsableLyricsDoesNotBecomeInstrumental(string lyric,string reason)
    {
        using var handler=new Handler(CatalogSong(SongId,AlbumId,Source),lyric,keywordHit:false);
        using var provider=Provider(handler);
        var result=await provider.LookupAsync(Apple,default);
        Assert.Null(AutomaticLyricsMatcher.Select(Apple,result));
        Assert.Null(AutomaticLyricsMatcher.SelectStatic(Apple,result));
        Assert.Equal(reason,LyricsLookupDiagnosis.Describe(Apple,result));
    }

    static ResilientLyricsProvider Provider(Handler handler)
        =>new(new EmptyOriginalProvider(),new NetEaseTranslationProvider(handler,TimeSpan.Zero));

    static object CatalogSong(long id,long albumId,Track track)=>new
    {
        id,name=track.Title,artists=track.Artist.Split(" & ").Select(name=>new{name}),
        album=new{id=albumId,name=track.Album},duration=track.DurationMs
    };

    sealed class Handler(object song,string lyric=Original,bool keywordHit=true):HttpMessageHandler
    {
        internal List<string> Requests {get;}=[];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            var uri=request.RequestUri!;
            Requests.Add(uri.PathAndQuery);
            object response=uri.AbsolutePath switch
            {
                "/api/song/detail"=>new{code=200,songs=new[]{song}},
                "/api/song/lyric"=>new{code=200,nolyric=lyric.Length==0,lrc=new{lyric},tlyric=new{lyric=Chinese}},
                _=>new{code=200,result=new{songs=keywordHit?new[]{song}:Array.Empty<object>()}}
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(response))});
        }
    }

    sealed class EmptyOriginalProvider:ILyricsProvider
    {
        public Task<IReadOnlyList<LyricsCandidate>> LookupAsync(Track track,CancellationToken token)
            =>Task.FromResult<IReadOnlyList<LyricsCandidate>>([]);
    }
}
