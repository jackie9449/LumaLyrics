using System.Net;
using System.Text.Json;
using LumaLyrics.Core;
namespace LumaLyrics.Infrastructure.Tests;
public class TranslationProviderTests
{
    sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send):HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken c)=>send(r,c);}
    static readonly Track Track=new("Own song","Own artist","Own album",80000);
    static HttpResponseMessage Response(string json)=>new(HttpStatusCode.OK){Content=new StringContent(json)};
    [Theory][InlineData("Own song (Live)","Own artist",80000,false)][InlineData("Own song","Other artist",80000,false)][InlineData("Own song","Own artist",90000,false)][InlineData("Own song","Own artist",81000,true)]
    public void RequiresMatchingRecording(string title,string artist,double duration,bool accepted)=>Assert.Equal(accepted,NetEaseTranslationProvider.RecordingMatches(Track,Track with{Title=title,Artist=artist,DurationMs=duration}));
    [Fact] public async Task FetchesOnlyCheckedRecordingWithExplicitOriginalAndTranslation()
    {
        int requests=0;using var p=new NetEaseTranslationProvider(new Handler((r,c)=>
        {
            requests++;Assert.Contains("LumaLyrics",r.Headers.UserAgent.ToString());Assert.Equal("music.163.com",r.RequestUri!.Host);
            return Task.FromResult(Response(requests==1?"{\"code\":200,\"result\":{\"songs\":[{\"id\":42,\"name\":\"Own song\",\"artists\":[{\"name\":\"Own artist\"}],\"album\":{\"name\":\"Own album\"},\"duration\":80000}]}}":JsonSerializer.Serialize(new{code=200,lrc=new{lyric="[00:01]Own original"},tlyric=new{lyric="[00:01]原创译文"}})));
        }),TimeSpan.Zero);
        var found=Assert.Single(await p.LookupAsync(Track,default));Assert.Equal(42,found.Id);Assert.Contains("原创译文",found.Translated);Assert.Equal(2,requests);
    }
    [Theory]
    [InlineData(false,true,true,1)]
    [InlineData(true,false,true,1)]
    [InlineData(false,false,false,1)]
    [InlineData(false,false,true,3)]
    public async Task StopsOnAcceptedOriginalWhenTranslationIsAvailableOrUnneeded(bool chinese,bool firstHasTranslation,bool preferTranslation,int expectedFetches)
    {
        const string chineseOriginal="[00:01]清风吹过小窗\n[00:25]归途灯光微亮\n[01:00]我沿着小路回家";
        const string foreignOriginal="[00:01]The wind comes through the window\n[00:25]Lights shine along the lane\n[01:00]I follow the road home";
        var lyricFetches=new List<long>();
        using var source=new ResilientLyricsProvider(new LrclibProvider(new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))),minimumInterval:TimeSpan.Zero),
            new NetEaseTranslationProvider(new Handler((request,_)=>
            {
                var uri=request.RequestUri!;
                if(uri.AbsolutePath.Contains("/search/"))
                {
                    var songs=Enumerable.Range(1,4).Select(id=>new{id,name=Track.Title,artists=new[]{new{name=Track.Artist}},album=new{name=Track.Album},duration=Track.DurationMs});
                    return Task.FromResult(Response(JsonSerializer.Serialize(new{code=200,result=new{songs}})));
                }
                long id=long.Parse(uri.Query.Split('&')[0][4..]);
                lyricFetches.Add(id);
                string translated=(id==1 && firstHasTranslation || id==3)?chineseOriginal:"";
                return Task.FromResult(Response(JsonSerializer.Serialize(new{code=200,lrc=new{lyric=chinese?chineseOriginal:foreignOriginal},tlyric=new{lyric=translated}})));
            }),TimeSpan.Zero));

        var found=Assert.IsType<LyricsLookupResult>(await source.LookupAsync(Track,default,preferTranslation));
        Assert.Equal(Enumerable.Range(1,expectedFetches).Select(id=>(long)id),lyricFetches);
        Assert.NotNull(AutomaticLyricsMatcher.Select(Track,found));
        Assert.Equal(lyricFetches,found.TranslationCandidates!.Select(candidate=>candidate.Id));
        if(expectedFetches==3)Assert.Equal(chineseOriginal,found.TranslationCandidates![2].Translated);
    }
    [Fact] public async Task RejectsMalformedAndOversizedResponses()
    {
        foreach(var content in new[]{"<html>bad",new string('x',2*1024*1024+1),"{\"code\":403}"})
        {using var p=new NetEaseTranslationProvider(new Handler((r,c)=>Task.FromResult(Response(content))),TimeSpan.Zero);await Assert.ThrowsAsync<LyricsProviderException>(()=>p.LookupAsync(Track,default));}
    }
    [Fact] public async Task UnknownDurationNeverCausesSpeculativeLookup(){using var p=new NetEaseTranslationProvider(new Handler((r,c)=>throw new Exception("Must not request")),TimeSpan.Zero);Assert.Empty(await p.LookupAsync(Track with{DurationMs=null},default));}
    [Theory][InlineData("{\"code\":200,\"result\":null}")][InlineData("{\"code\":200,\"result\":{\"songs\":[null,42,{\"id\":null,\"duration\":null}]}}")]
    public async Task NullOrUnusableMetadataDoesNotCrashOrRequestLyrics(string json)
    {int count=0;using var p=new NetEaseTranslationProvider(new Handler((r,c)=>{count++;return Task.FromResult(Response(json));}),TimeSpan.Zero);Assert.Empty(await p.LookupAsync(Track,default));Assert.Equal(1,count);}
    [Fact] public async Task PropagatesCancellationAndRateLimiting()
    {
        int requests=0;
        using var p=new NetEaseTranslationProvider(new Handler((r,c)=>
        {
            requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        }),TimeSpan.Zero);
        Assert.Equal(429,(await Assert.ThrowsAsync<LyricsProviderException>(()=>p.LookupAsync(Track,default))).StatusCode);
        Assert.Equal(429,(await Assert.ThrowsAsync<LyricsProviderException>(()=>p.LookupAsync(Track,default).WaitAsync(TimeSpan.FromSeconds(1)))).StatusCode);
        using var cancel=new CancellationTokenSource();cancel.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>p.LookupAsync(Track,cancel.Token));
        Assert.Equal(1,requests);
    }
    [Theory]
    [InlineData(405)]
    [InlineData(429)]
    public async Task JsonBackoffDelaysTheNextRequest(int code)
    {
        int requests=0;
        using var provider=new NetEaseTranslationProvider(new Handler((_,_)=>
        {
            requests++;
            return Task.FromResult(Response(JsonSerializer.Serialize(new{code})));
        }),TimeSpan.Zero);
        Assert.Equal(code,(await Assert.ThrowsAsync<LyricsProviderException>(()=>provider.LookupAsync(Track,default))).StatusCode);
        var cooldown=await Assert.ThrowsAsync<LyricsProviderException>(()=>provider.LookupAsync(Track,default).WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal(429,cooldown.StatusCode);
        Assert.Equal(1,requests);
        using var cancel=new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>provider.LookupAsync(Track,cancel.Token));
        Assert.Equal(1,requests);
    }
}
