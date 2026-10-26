using System.Net;
using System.Text.Json;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;

namespace LumaLyrics.Infrastructure.Tests;

public sealed class AutomaticLyricsSearchTests
{
    static readonly Track track=new("原创归途","浮光工作室","原创专辑",120000);
    const string original="[00:00]微风推开了窗\n[00:35]灯光映照着归途\n[01:10]让微光陪你前行";
    sealed class Primary:ILyricsProvider,IAutomaticLyricsSearch
    {
        public int ExpandedCalls {get;private set;}
        public Func<CancellationToken,Task<IReadOnlyList<LyricsCandidate>>> Ordinary {get;init;}=_=>Task.FromResult<IReadOnlyList<LyricsCandidate>>([]);
        public Func<CancellationToken,Task<IReadOnlyList<LyricsCandidate>>> Expanded {get;init;}=_=>Task.FromResult<IReadOnlyList<LyricsCandidate>>([Candidate()]);
        public Task<IReadOnlyList<LyricsCandidate>> LookupAsync(Track input,CancellationToken token)=>Ordinary(token);
        public Task<IReadOnlyList<LyricsCandidate>> SearchAutomaticAsync(Track input,IReadOnlyList<LyricsCandidate> known,CancellationToken token)
        {ExpandedCalls++;return Expanded(token);}
    }
    sealed class Secondary:ITranslationProvider,IAutomaticLyricsSearch
    {
        public int ExpandedCalls {get;private set;}
        public Func<CancellationToken,Task<IReadOnlyList<TranslationCandidate>>> Ordinary {get;init;}=_=>Task.FromResult<IReadOnlyList<TranslationCandidate>>([]);
        public Func<CancellationToken,Task<IReadOnlyList<LyricsCandidate>>> Expanded {get;init;}=_=>Task.FromResult<IReadOnlyList<LyricsCandidate>>([]);
        public Task<IReadOnlyList<TranslationCandidate>> LookupAsync(Track input,CancellationToken token)=>Ordinary(token);
        public Task<IReadOnlyList<LyricsCandidate>> SearchAutomaticAsync(Track input,IReadOnlyList<LyricsCandidate> known,CancellationToken token)
        {ExpandedCalls++;return Expanded(token);}
    }
    static LyricsCandidate Candidate()=>new(1,track,original,null,false);

    [Fact]
    public async Task OrdinaryLookupCanDiscoverAnExactTimedRecordingThroughSupplementarySearch()
    {
        var primary=new Primary();using var provider=new ResilientLyricsProvider(primary,new Secondary());
        var found=await provider.LookupAsync(track,default);
        Assert.NotNull(AutomaticLyricsMatcher.Select(track,found));Assert.Equal(1,primary.ExpandedCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AcceptedOrdinaryResultsDoNotNeedExpansion(bool fromSecondary)
    {
        var primary=new Primary{Ordinary=_=>Task.FromResult<IReadOnlyList<LyricsCandidate>>(fromSecondary?[]:[Candidate()])};
        var secondary=new Secondary{Ordinary=_=>fromSecondary?Task.FromResult<IReadOnlyList<TranslationCandidate>>([new(2,track,original,"","")]):throw new Exception("Primary fast path must not query secondary")};
        using var provider=new ResilientLyricsProvider(primary,secondary);
        Assert.NotNull(AutomaticLyricsMatcher.Select(track,await provider.LookupAsync(track,default)));
        Assert.Equal(0,primary.ExpandedCalls);Assert.Equal(0,secondary.ExpandedCalls);
    }

    [Fact]
    public async Task ConfirmedInstrumentalDoesNotExpand()
    {
        var primary=new Primary{Ordinary=_=>Task.FromResult<IReadOnlyList<LyricsCandidate>>([Candidate() with{Instrumental=true,SyncedLyrics=null}])};
        var secondary=new Secondary();using var provider=new ResilientLyricsProvider(primary,secondary);
        Assert.True(AutomaticLyricsMatcher.Select(track,await provider.LookupAsync(track,default))!.Candidate.Instrumental);
        Assert.Equal(0,primary.ExpandedCalls);Assert.Equal(0,secondary.ExpandedCalls);
    }

    [Theory]
    [InlineData("artist")]
    [InlineData("cover")]
    [InlineData("live")]
    [InlineData("duration")]
    [InlineData("missingDuration")]
    [InlineData("plain")]
    [InlineData("truncated")]
    [InlineData("placeholder")]
    public async Task WiderDiscoveryNeverRelaxesAutomaticAcceptance(string change)
    {
        var candidate=change switch
        {
            "artist"=>Candidate() with{Track=track with{Artist="其他歌手"}},
            "cover"=>Candidate() with{Track=track with{Title=track.Title+" (Cover 浮光工作室)",Artist="翻唱歌手"}},
            "live"=>Candidate() with{Track=track with{Title=track.Title+" (Live)"}},
            "duration"=>Candidate() with{Track=track with{DurationMs=160000}},
            "missingDuration"=>Candidate() with{Track=track with{DurationMs=null}},
            "plain"=>Candidate() with{SyncedLyrics=null,PlainLyrics="原创静态文字"},
            "truncated"=>Candidate() with{SyncedLyrics="[00:00]原创一行\n[00:01]原创二行\n[00:02]原创三行"},
            _=>Candidate() with{SyncedLyrics="lyrics not available"}
        };
        var primary=new Primary{Expanded=_=>Task.FromResult<IReadOnlyList<LyricsCandidate>>([candidate])};
        using var provider=new ResilientLyricsProvider(primary,new Secondary());
        var found=await provider.LookupAsync(track,default);
        Assert.Single(found);Assert.Null(AutomaticLyricsMatcher.Select(track,found));
        Assert.Equal(candidate.Track,found[0].Track);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public async Task UnknownOrInvalidPlaybackDurationNeverExpands(double? duration)
    {
        var primary=new Primary();var secondary=new Secondary();using var provider=new ResilientLyricsProvider(primary,secondary);
        await provider.LookupAsync(track with{DurationMs=duration},default);
        Assert.Equal(0,primary.ExpandedCalls);Assert.Equal(0,secondary.ExpandedCalls);
    }

    [Theory]
    [InlineData(429)]
    [InlineData(503)]
    public async Task UnavailableSourceIsNotRetriedButReachableSourceCanExpand(int status)
    {
        var primary=new Primary{Ordinary=_=>throw new LyricsProviderException("unavailable",status)};
        var secondary=new Secondary{Expanded=_=>Task.FromResult<IReadOnlyList<LyricsCandidate>>([Candidate() with{ProviderName="NetEase"}])};
        using var provider=new ResilientLyricsProvider(primary,secondary);
        var found=Assert.IsType<LyricsLookupResult>(await provider.LookupAsync(track,default));
        Assert.NotNull(AutomaticLyricsMatcher.Select(track,found));Assert.NotEmpty(found.UnavailableSources);
        Assert.Equal(0,primary.ExpandedCalls);Assert.Equal(1,secondary.ExpandedCalls);
    }

    [Fact]
    public async Task PartiallyUnavailablePrimaryIsNotExpandedAgain()
    {
        var primary=new Primary{Ordinary=_=>Task.FromResult<IReadOnlyList<LyricsCandidate>>(new LyricsLookupResult([],["LRCLIB（503）"]))};
        var secondary=new Secondary();using var provider=new ResilientLyricsProvider(primary,secondary);
        await provider.LookupAsync(track,default);Assert.Equal(0,primary.ExpandedCalls);Assert.Equal(1,secondary.ExpandedCalls);
    }

    [Fact]
    public async Task FallbackFailurePreservesSuggestionsAndUnavailableStatus()
    {
        var candidate=Candidate() with{PlainLyrics="原创文字",SyncedLyrics=null};
        var primary=new Primary{Ordinary=_=>Task.FromResult<IReadOnlyList<LyricsCandidate>>([candidate]),Expanded=_=>throw new LyricsProviderException("limited",429)};
        using var provider=new ResilientLyricsProvider(primary,new Secondary());
        var found=Assert.IsType<LyricsLookupResult>(await provider.LookupAsync(track,default));
        Assert.Equal(candidate,Assert.Single(found));Assert.Contains("429",Assert.Single(found.UnavailableSources));
    }

    [Fact]
    public async Task CancellationDuringExpansionPropagates()
    {
        using var cancel=new CancellationTokenSource();
        var primary=new Primary{Expanded=async token=>{cancel.Cancel();await Task.Delay(Timeout.Infinite,token);return [];}};
        using var provider=new ResilientLyricsProvider(primary,new Secondary());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>provider.LookupAsync(track,cancel.Token));
    }

    [Fact]
    public async Task IndependentExpansionsStartConcurrentlyAndDeduplicateBySourceAndId()
    {
        var first=new TaskCompletionSource();var second=new TaskCompletionSource();var release=new TaskCompletionSource();
        var primary=new Primary{Expanded=async _=>{first.SetResult();await release.Task;return [Candidate(),Candidate()];}};
        var secondary=new Secondary{Expanded=async _=>{second.SetResult();await release.Task;return [Candidate() with{ProviderName="NetEase"}];}};
        using var provider=new ResilientLyricsProvider(primary,secondary);var pending=provider.LookupAsync(track,default);
        try{await Task.WhenAll(first.Task,second.Task).WaitAsync(TimeSpan.FromSeconds(2));}
        finally{release.SetResult();}
        Assert.Equal(2,(await pending).Count);
    }

    sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> send):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(send(request));}
    static HttpResponseMessage Json(object data)=>new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(data))};

    [Fact]
    public async Task PrimarySupplementarySearchIsBoundedAndPreservesSourceProvenance()
    {
        var paths=new List<string>();
        using var primary=new LrclibProvider(new Handler(request=>
        {
            paths.Add(request.RequestUri!.OriginalString);
            if(request.RequestUri.AbsolutePath.EndsWith("/get"))return new HttpResponseMessage(HttpStatusCode.NotFound);
            if(request.RequestUri.Query.Contains("artist_name="))return Json(Array.Empty<object>());
            return Json(new[]{new{id=5,trackName=track.Title,artistName=track.Artist,albumName=track.Album,duration=120,syncedLyrics=original}});
        }),minimumInterval:TimeSpan.Zero);
        using var provider=new ResilientLyricsProvider(primary,new Secondary());
        var found=await provider.LookupAsync(track,default);
        Assert.Equal(4,paths.Count);Assert.Contains(paths,p=>p.Contains("search?q="));
        Assert.DoesNotContain(paths.Skip(2),p=>p.Contains("artist_name="));
        var selected=AutomaticLyricsMatcher.Select(track,found)!;Assert.NotNull(selected);
        var root=Path.Combine(Path.GetTempPath(),"LumaAutomaticSearch-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            using var repository=new LyricsRepository(Path.Combine(root,"lyrics.db"));
            var binding=repository.Save(track,selected.Candidate.Content,"LRCLIB · 自动检查",false,providerJson:selected.Candidate.RawJson);
            Assert.True(LibraryPrefetcher.QualityOf(binding,track));
            var manual=repository.Save(track,"[00:00]个人手动原创文字","Manual",true);
            Assert.Equal(manual.Id,repository.Save(track,selected.Candidate.Content,"Automatic",false,providerJson:selected.Candidate.RawJson).Id);
        }
        finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(root,true);}
    }

    [Fact]
    public async Task SecondaryFiltersRecordingMetadataBeforeFetchingAtMostThreeUnseenLyrics()
    {
        var requested=new List<long>();int searches=0;
        using var secondary=new NetEaseTranslationProvider(new Handler(request=>
        {
            if(request.RequestUri!.AbsolutePath.Contains("/search/"))
            {
                searches++;
                return Json(new{code=200,result=new{songs=new object[]
                {
                    new{id=91,name=track.Title,artists=new[]{new{name="其他歌手"}},duration=120000},
                    new{id=92,name=track.Title+" (Live)",artists=new[]{new{name=track.Artist}},duration=120000},
                    new{id=93,name=track.Title,artists=new[]{new{name=track.Artist}},duration=160000},
                    new{id=94,name=track.Title,artists=new[]{new{name=track.Artist}}}
                }.Concat(Enumerable.Range(1,6).Select(id=>(object)new{id,name=track.Title,artists=new[]{new{name=track.Artist}},album=new{name=track.Album},duration=120000})).ToArray()}});
            }
            long id=long.Parse(request.RequestUri.Query.Split('&')[0].Split('=')[1]);requested.Add(id);
            return Json(new{code=200,lrc=new{lyric=original}});
        }),TimeSpan.Zero);
        var found=await secondary.SearchAutomaticAsync(track,[Candidate() with{ProviderName="NetEase"}],default);
        Assert.Equal(3,found.Count);Assert.Equal(new long[]{2,3,4},requested);Assert.InRange(searches,1,2);
        Assert.All(found,c=>Assert.Equal(track.Artist,c.Track.Artist));
        Assert.All(found,c=>Assert.True(LibraryPrefetcher.QualityOf(new("b","t",c.Content,c.SourceLabel,false,0,false,false,c.RawJson),track)));
    }

    [Fact]
    public async Task SecondaryVerifiedAdditionalCreditsRemainInProvenance()
    {
        string credits=track.Artist+" & 伴唱者";
        using var source=new NetEaseTranslationProvider(new Handler(request=>request.RequestUri!.AbsolutePath.Contains("/search/")
            ?Json(new{code=200,result=new{songs=new[]{new{id=10,name=track.Title,artists=new[]{new{name=credits}},album=new{name=track.Album},duration=120000}}}})
            :Json(new{code=200,lrc=new{lyric=original}})),TimeSpan.Zero);
        var candidate=Assert.Single(await source.SearchAutomaticAsync(track,[],default));
        var saved=JsonSerializer.Deserialize<NetEaseOriginal>(candidate.RawJson)!;
        Assert.Equal(credits,saved.Track.Artist);Assert.NotNull(AutomaticLyricsMatcher.Select(track,[candidate]));
        Assert.True(LibraryPrefetcher.QualityOf(new("b","t",candidate.Content,candidate.SourceLabel,false,0,false,false,candidate.RawJson),track));
    }
}
