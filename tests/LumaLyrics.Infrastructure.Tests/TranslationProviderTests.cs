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
        using var p=new NetEaseTranslationProvider(new Handler((r,c)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests))),TimeSpan.Zero);
        Assert.Equal(429,(await Assert.ThrowsAsync<LyricsProviderException>(()=>p.LookupAsync(Track,default))).StatusCode);
        using var cancel=new CancellationTokenSource();cancel.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>p.LookupAsync(Track,cancel.Token));
    }
}
