using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;

namespace LumaLyrics.Infrastructure.Tests;

public sealed class ProviderPacingTests
{
    static readonly Track Track=new("Own song","Own artist","Own album",10000);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MinimumIntervalRunsFromRequestStartWithoutOverlappingRequests(bool netEase)
    {
        var interval=TimeSpan.FromMilliseconds(800);
        var latency=TimeSpan.FromMilliseconds(640);
        var clock=Stopwatch.StartNew();
        var starts=new ConcurrentQueue<TimeSpan>();
        var firstResponse=new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
        int requests=0;
        var handler=new Handler(async token=>
        {
            var request=Interlocked.Increment(ref requests);
            starts.Enqueue(clock.Elapsed);
            if(request==1)
            {
                await Task.Delay(latency,token);
                firstResponse.SetResult(clock.Elapsed);
            }
            string json=netEase?"""{"code":200,"result":{"songs":[]}}"""
                :"""{"id":1,"trackName":"Own song","artistName":"Own artist","albumName":"Own album","duration":10,"syncedLyrics":"[00:01]First original verse\n[00:04]Another original verse\n[00:08]Last original verse"}""";
            return new(HttpStatusCode.OK){Content=new StringContent(json)};
        });

        using IDisposable provider=netEase?new NetEaseTranslationProvider(handler,interval):new LrclibProvider(handler,minimumInterval:interval);
        Task Lookup()=>provider is NetEaseTranslationProvider secondary?secondary.LookupAsync(Track,default):((LrclibProvider)provider).LookupAsync(Track,default);
        await Task.WhenAll(Lookup(),Lookup()).WaitAsync(TimeSpan.FromSeconds(8));

        var times=starts.ToArray();
        Assert.Equal(2,times.Length);
        Assert.True(times[1]-times[0]>=interval-TimeSpan.FromMilliseconds(35));
        Assert.InRange(times[1]-await firstResponse.Task,TimeSpan.Zero,interval-TimeSpan.FromMilliseconds(120));
    }

    sealed class Handler(Func<CancellationToken,Task<HttpResponseMessage>> send):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>send(token);
    }
}
