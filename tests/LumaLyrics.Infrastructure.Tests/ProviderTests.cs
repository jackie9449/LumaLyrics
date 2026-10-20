using System.Net;
using System.Net.Http.Headers;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.Infrastructure.Tests;
public class ProviderTests
{
    sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send):HttpMessageHandler {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken c)=>send(r,c);}
    static readonly Track track=new("A & B","Singer","Album",null);
    [Fact] public async Task HandlesEmptyResultsAndEscapesParameters()
    {
        using var p=new LrclibProvider(new Handler((r,c)=>{Assert.Contains("A%20%26%20B",r.RequestUri!.OriginalString);Assert.Contains("LumaLyrics",r.Headers.UserAgent.ToString());return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("[]")});}));Assert.Empty(await p.LookupAsync(track,default));
    }
    [Fact] public async Task MissingFieldsDegradeWithoutCrash(){using var p=new LrclibProvider(new Handler((r,c)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("[{\"id\":1,\"plainLyrics\":\"Own test lyrics\",\"unknown\":true}]")})));var result=await p.LookupAsync(track,default);Assert.Single(result);Assert.Null(result[0].Track.DurationMs);}
    [Fact] public async Task NonJsonResponseHasUsefulError(){using var p=new LrclibProvider(new Handler((r,c)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("<html>bad</html>")})));await Assert.ThrowsAsync<LyricsProviderException>(()=>p.LookupAsync(track,default));}
    [Fact] public async Task OversizeResponseIsRejected(){using var p=new LrclibProvider(new Handler((r,c)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(new string('a',2*1024*1024+1))})));await Assert.ThrowsAsync<LyricsProviderException>(()=>p.LookupAsync(track,default));}
    [Fact] public async Task RetryAfterCanBeCanceledWithoutSecondRequest()
    {
        int count=0;using var cancel=new CancellationTokenSource();using var p=new LrclibProvider(new Handler((r,c)=>{count++;var result=new HttpResponseMessage(HttpStatusCode.TooManyRequests){Content=new StringContent("not JSON")};result.Headers.RetryAfter=new RetryConditionHeaderValue(TimeSpan.FromHours(1));cancel.Cancel();return Task.FromResult(result);}));await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>p.LookupAsync(track,cancel.Token));Assert.Equal(1,count);
    }
    [Fact] public async Task CancellationPropagates(){using var cancel=new CancellationTokenSource();cancel.Cancel();using var p=new LrclibProvider(new Handler((r,c)=>throw new Exception("Must not issue request")));await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>p.LookupAsync(track,cancel.Token));}
    [Fact] public async Task NullDurationAndIdAreTolerated(){using var p=new LrclibProvider(new Handler((r,c)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("[{\"id\":null,\"duration\":null,\"plainLyrics\":\"Test\"}]")})));var results=await p.LookupAsync(track,default);Assert.Single(results);Assert.Null(results[0].Track.DurationMs);}
    [Fact] public async Task SmallArtistSearchIsSharedWithoutRepeatingMissingSongs()
    {
        int requests=0;using var p=new LrclibProvider(new Handler((r,c)=>{requests++;Assert.Contains("search?q=",r.RequestUri!.OriginalString);return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("[]")});}),minimumInterval:TimeSpan.Zero);
        Assert.Empty(await p.LookupLibraryAsync(track,default));Assert.Empty(await p.LookupLibraryAsync(track with{Title="Another own song"},default));Assert.Equal(1,requests);
    }
    [Fact] public async Task FullArtistPageCannotProveAnAbsentSongIsMissing()
    {
        int requests=0;var full="["+string.Join(",",Enumerable.Range(1,20).Select(i=>$"{{\"id\":{i},\"trackName\":\"Other\",\"artistName\":\"Singer\"}}"))+"]";
        using var p=new LrclibProvider(new Handler((r,c)=>{requests++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(requests==1?full:"[]")});}),searchOnly:true,minimumInterval:TimeSpan.Zero);
        Assert.Empty(await p.LookupLibraryAsync(track,default));Assert.Equal(2,requests);
    }
}
