using System.Net;
using System.Text.Json;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;

namespace LumaLyrics.Infrastructure.Tests;

public sealed class ManualLyricsSearchTests
{
    sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>send(request,token);}
    static HttpResponseMessage Json(object value)=>new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(value))};
    static readonly Track query=new("原创归途","浮光原声","",null);

    [Fact]
    public async Task ManualSecondarySearchFindsLyricsWithoutDurationOrExactArtist()
    {
        int requests=0;
        using var source=new NetEaseTranslationProvider(new Handler((request,_)=>
        {
            requests++;
            return Task.FromResult(request.RequestUri!.AbsolutePath.Contains("/search/")
                ?Json(new{code=200,result=new{songs=new[]{new{id=12,name="原创归途",artists=new[]{new{name="浮光"}},album=new{name="现场版"},duration=99000}}}})
                :Json(new{code=200,lrc=new{lyric="[00:00]这是原创测试文字"}}));
        }),TimeSpan.Zero);
        var found=await source.SearchManualAsync(query,default);
        var candidate=Assert.Single(found);Assert.True(requests>0);
        Assert.Equal("浮光",candidate.Track.Artist);Assert.Equal("现场版",candidate.Track.Album);Assert.Equal(99000,candidate.Track.DurationMs);
    }

    [Fact]
    public async Task ManualPrimarySearchFallsBackWhenArtistIsTooRestrictive()
    {
        using var source=new LrclibProvider(new Handler((request,_)=>Task.FromResult(
            request.RequestUri!.Query.Contains("artist_name=")
                ?Json(Array.Empty<object>())
                :Json(new[]{new{id=13,trackName="原创归途",artistName="浮光",albumName="现场版",duration=99,plainLyrics="原创测试文字"}}))),minimumInterval:TimeSpan.Zero);
        var found=await source.SearchManualAsync(query,default);
        Assert.NotEmpty(found);
    }

    [Fact]
    public async Task AutomaticSearchStillRejectsUnknownDurationAndArtistMismatch()
    {
        int requests=0;
        using var secondary=new NetEaseTranslationProvider(new Handler((_,_)=>
        {
            requests++;return Task.FromResult(Json(new{code=200,result=new{songs=new[]{new{id=12,name="原创归途",artists=new[]{new{name="其他歌手"}},duration=99000}}}}));
        }),TimeSpan.Zero);
        Assert.Empty(await secondary.LookupAsync(query,default));Assert.Equal(0,requests);
        Assert.Empty(await secondary.LookupAsync(query with{DurationMs=99000},default));Assert.Equal(1,requests);
        int primaryRequests=0;
        using var primary=new LrclibProvider(new Handler((_,_)=>{primaryRequests++;return Task.FromResult(Json(Array.Empty<object>()));}),minimumInterval:TimeSpan.Zero);
        Assert.Empty(await primary.LookupAsync(query,default));Assert.Equal(1,primaryRequests);
    }

    [Fact]
    public async Task TitleOnlySecondarySearchIncludesStaticLyricsWithoutInventingTiming()
    {
        int requests=0;
        using var source=new NetEaseTranslationProvider(new Handler((request,_)=>
        {
            requests++;return Task.FromResult(request.RequestUri!.AbsolutePath.Contains("/search/")
                ?Json(new{code=200,result=new{songs=new[]{new{id=12,name="原创归途",artists=new[]{new{name="浮光"}},duration=99000}}}})
                :Json(new{code=200,lrc=new{lyric="这是原创静态测试文字"}}));
        }),TimeSpan.Zero);
        var result=Assert.Single(await source.SearchManualAsync(query with{Artist=""},default));
        Assert.Null(result.SyncedLyrics);Assert.Equal("这是原创静态测试文字",result.PlainLyrics);Assert.False(result.Instrumental);Assert.Equal(2,requests);
    }

    [Theory]
    [InlineData("",false)]
    [InlineData("[00:00]纯音乐，请欣赏",true)]
    public async Task MissingLyricsAreNotInstrumental(string lyrics,bool expectedInstrumental)
    {
        using var source=new NetEaseTranslationProvider(new Handler((request,_)=>Task.FromResult(request.RequestUri!.AbsolutePath.Contains("/search/")
            ?Json(new{code=200,result=new{songs=new[]{new{id=12,name="原创归途",artists=new[]{new{name="浮光"}},duration=99000}}}})
            :Json(new{code=200,lrc=new{lyric=lyrics}}))),TimeSpan.Zero);
        var found=await source.SearchManualAsync(query,default);
        if(expectedInstrumental){var result=Assert.Single(found);Assert.True(result.Instrumental);Assert.Empty(result.Content);Assert.Contains("InstrumentalEvidence",result.RawJson);}
        else Assert.Empty(found);
    }

    [Fact]
    public async Task SearchQueriesAreEscapedBoundedAndNeverConstrainAlbumOrDuration()
    {
        var paths=new List<string>();using var source=new LrclibProvider(new Handler((request,_)=>
        {paths.Add(request.RequestUri!.OriginalString);return Task.FromResult(Json(Array.Empty<object>()));}),minimumInterval:TimeSpan.Zero);
        await source.SearchManualAsync(new("A & B (Live)","Singer原声","Album",123456),default);
        Assert.Equal(3,paths.Count);Assert.Contains(paths,p=>p.Contains("A%20%26%20B%20%28Live%29"));
        Assert.Contains(paths,p=>p.Contains("search?q=A%20%26%20B%20Singer"));
        Assert.Contains(paths,p=>p.EndsWith("search?track_name=A%20%26%20B"));
        Assert.All(paths,p=>{Assert.DoesNotContain("album_name",p);Assert.DoesNotContain("duration",p);Assert.DoesNotContain("/get?",p);});
    }

    [Fact]
    public async Task PrimaryRetainsResultsAndStopsOnRateLimit()
    {
        int calls=0;using var source=new LrclibProvider(new Handler((_,_)=>
        {
            calls++;return Task.FromResult(calls==1?Json(new[]{new{id=13,trackName="原创归途",artistName="浮光",plainLyrics="原创文字"}}):new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        }),minimumInterval:TimeSpan.Zero);
        var found=Assert.IsType<LyricsLookupResult>(await source.SearchManualAsync(query,default));
        Assert.Single(found);Assert.Single(found.UnavailableSources);Assert.Equal(2,calls);
        await source.SearchManualAsync(query,default);Assert.Equal(2,calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SecondaryFetchesEarlierCatalogResultsAfterLaterSearchFailure(bool malformed)
    {
        int searches=0;
        using var source=new NetEaseTranslationProvider(new Handler((request,_)=>
        {
            if(!request.RequestUri!.AbsolutePath.Contains("/search/"))return Task.FromResult(Json(new{code=200,lrc=new{lyric="[00:00]原创文字"}}));
            return Task.FromResult(++searches==1?Json(new{code=200,result=new{songs=new[]{new{id=12,name="原创归途",artists=new[]{new{name="浮光"}}}}}})
                :malformed?new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("<html>bad")}:new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }),TimeSpan.Zero);
        var found=Assert.IsType<LyricsLookupResult>(await source.SearchManualAsync(query,default));
        Assert.Single(found);Assert.Single(found.UnavailableSources);Assert.Equal(2,searches);
    }

    [Fact]
    public async Task SecondaryRetainsEarlierLyricsOnLaterLyricFailure()
    {
        int lyrics=0;
        using var source=new NetEaseTranslationProvider(new Handler((request,_)=>
        {
            if(request.RequestUri!.AbsolutePath.Contains("/search/"))return Task.FromResult(Json(new{code=200,result=new{songs=new[]{new{id=12,name="原创归途"},new{id=13,name="原创归途"}}}}));
            return Task.FromResult(++lyrics==1?Json(new{code=200,lrc=new{lyric="[00:00]原创文字"}}):new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }),TimeSpan.Zero);
        var found=Assert.IsType<LyricsLookupResult>(await source.SearchManualAsync(query,default));
        Assert.Single(found);Assert.Single(found.UnavailableSources);Assert.Equal(2,lyrics);
    }

    [Fact]
    public async Task SecondaryLimitsLyricFetchesAndSkipsUnusableIds()
    {
        int lyrics=0,searches=0;
        using var source=new NetEaseTranslationProvider(new Handler((request,_)=>
        {
            if(request.RequestUri!.AbsolutePath.Contains("/search/"))
            {
                searches++;return Task.FromResult(Json(new{code=200,result=new{songs=new object[]{null!,42,new{id="bad",name="bad"},new{id=0,name="bad"}}
                    .Concat(Enumerable.Range(1,30).Select(id=>(object)new{id,name="原创归途"})).ToArray()}}));
            }
            lyrics++;return Task.FromResult(Json(new{code=200,lrc=new{lyric="[00:00]原创文字"}}));
        }),TimeSpan.Zero);
        Assert.Equal(8,(await source.SearchManualAsync(query,default)).Count);Assert.Equal(8,lyrics);Assert.InRange(searches,1,3);
    }

    [Fact]
    public void RankingRetainsAlternateVersionsAndRealArtistCredits()
    {
        var desired=new LyricsCandidate(1,new("原创归途 (Cover 浮光)","演唱者","",null),"[00:00]原创文字",null,false);
        var unrelated=new LyricsCandidate(2,new("原创归途","其他歌手","",null),"[00:00]其他原创文字",null,false);
        var found=ManualLyricsSearch.Candidates(query,[unrelated,desired,desired,desired with{ProviderName="NetEase"}]);
        Assert.Equal(3,found.Count);Assert.Equal(desired,found[0]);Assert.Equal("演唱者",found[0].Track.Artist);
        Assert.False(MatchRules.Score(query,found[0]).Eligible);
        Assert.Equal(100,ManualLyricsSearch.Candidates(query,Enumerable.Range(1,150).Select(id=>desired with{Id=id})).Count);
    }

    sealed class Source(Func<CancellationToken,Task<IReadOnlyList<LyricsCandidate>>> search):ILyricsProvider,ITranslationProvider,IManualLyricsSearch
    {
        public Task<IReadOnlyList<LyricsCandidate>> LookupAsync(Track track,CancellationToken token)=>throw new InvalidOperationException("Manual search must not call automatic lookup");
        Task<IReadOnlyList<TranslationCandidate>> ITranslationProvider.LookupAsync(Track track,CancellationToken token)=>throw new InvalidOperationException("Manual search must not call automatic translation lookup");
        public Task<IReadOnlyList<LyricsCandidate>> SearchManualAsync(Track track,CancellationToken token)=>search(token);
    }
    [Fact]
    public async Task BothSourcesStartConcurrentlyAndKeepSameIdFromDifferentSources()
    {
        var firstStarted=new TaskCompletionSource();var secondStarted=new TaskCompletionSource();var release=new TaskCompletionSource();
        var candidate=new LyricsCandidate(1,query,"[00:00]原创文字",null,false);
        using var provider=new ResilientLyricsProvider(new Source(async _=>{firstStarted.SetResult();await release.Task;return [candidate];}),
            new Source(async _=>{secondStarted.SetResult();await release.Task;return [candidate with{ProviderName="NetEase"}];}));
        var pending=provider.SearchManualAsync(query,default);
        try{await Task.WhenAll(firstStarted.Task,secondStarted.Task).WaitAsync(TimeSpan.FromSeconds(2));}
        finally{release.SetResult();}
        Assert.Equal(2,(await pending).Count);
    }

    [Fact]
    public async Task UnavailableSourceDoesNotHideAnotherSourcesCandidates()
    {
        var candidate=new LyricsCandidate(1,query,"[00:00]原创文字",null,false){ProviderName="NetEase"};
        using var provider=new ResilientLyricsProvider(new Source(_=>throw new LyricsProviderException("busy",503)),new Source(_=>Task.FromResult<IReadOnlyList<LyricsCandidate>>([candidate])));
        var found=Assert.IsType<LyricsLookupResult>(await provider.SearchManualAsync(query,default));
        Assert.Equal(candidate,Assert.Single(found));Assert.Contains("503",Assert.Single(found.UnavailableSources));
    }

    [Fact]
    public async Task CancellationAndEmptyQueriesDoNotLeakRequests()
    {
        int calls=0;
        using var primary=new LrclibProvider(new Handler((_,_)=>{calls++;throw new Exception("No request expected");}));
        using var secondary=new NetEaseTranslationProvider(new Handler((_,_)=>{calls++;throw new Exception("No request expected");}));
        using var canceled=new CancellationTokenSource();canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>primary.SearchManualAsync(query,canceled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>secondary.SearchManualAsync(query,canceled.Token));
        Assert.Empty(await primary.SearchManualAsync(query with{Title=" "},default));
        Assert.Empty(await secondary.SearchManualAsync(query with{Title=" "},default));Assert.Equal(0,calls);
    }

    [Fact]
    public async Task InFlightCancellationIsPropagated()
    {
        using var cancel=new CancellationTokenSource();
        using var source=new LrclibProvider(new Handler(async (_,token)=>{cancel.Cancel();await Task.Delay(Timeout.Infinite,token);return Json(Array.Empty<object>());}));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>source.SearchManualAsync(query,cancel.Token));
    }
}
