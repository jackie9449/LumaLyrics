using System.Net;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.Infrastructure.Tests;

public sealed class ResilientLyricsProviderTests
{
    static readonly Track recording=new("原创片段","浮光工作室","原创测试",120000);
    const string original="[00:00.00]风从海面吹来\n[00:35.00]灯照亮了归途\n[01:10.00]让微光伴你前行";
    sealed class Primary(Func<CancellationToken,Task<IReadOnlyList<LyricsCandidate>>> action):ILyricsProvider{public Task<IReadOnlyList<LyricsCandidate>> LookupAsync(Track track,CancellationToken token)=>action(token);}
    sealed class Secondary(Func<CancellationToken,Task<IReadOnlyList<TranslationCandidate>>> action):ITranslationProvider{public Task<IReadOnlyList<TranslationCandidate>> LookupAsync(Track track,CancellationToken token)=>action(token);}
    static TranslationCandidate Fallback()=>new(21,recording,original,"","https://music.163.com/#/song?id=21");
    [Fact] public async Task CombinedAppleArtistKeepsTheCanonicalFallbackMatch()
    {
        using var provider=new ResilientLyricsProvider(new Primary(_=>Task.FromResult<IReadOnlyList<LyricsCandidate>>([])),new Secondary(_=>Task.FromResult<IReadOnlyList<TranslationCandidate>>([Fallback()])));
        var input=recording with{Artist=recording.Artist+" — "+recording.Album,Album=""};var result=await provider.LookupAsync(input,default);
        Assert.Equal(recording.Artist,AutomaticLyricsMatcher.Select(input,result)!.Candidate.Track.Artist);
    }
    [Fact] public async Task OverloadedPrimaryFallsBackToACheckedOriginalWithCorrectProvenance()
    {
        using var provider=new ResilientLyricsProvider(new Primary(_=>throw new LyricsProviderException("busy",503)),new Secondary(_=>Task.FromResult<IReadOnlyList<TranslationCandidate>>([Fallback()])));
        var result=await provider.LookupAsync(recording,default);var chosen=AutomaticLyricsMatcher.Select(recording,result);
        Assert.NotNull(chosen);Assert.Equal("NetEase",chosen.Candidate.ProviderName);Assert.Contains("NetEase",chosen.Candidate.SourceLabel);
    }
    [Fact] public async Task ACheckedPrimaryOriginalDoesNotCallTheFallback()
    {
        using var provider=new ResilientLyricsProvider(new Primary(_=>Task.FromResult<IReadOnlyList<LyricsCandidate>>([new(1,recording,original,null,false)])),new Secondary(_=>throw new Exception("No fallback request expected")));
        Assert.Single(await provider.LookupAsync(recording,default));
    }
    [Fact] public async Task OneUnavailableSourceAndAnEmptyReachableSourceAreNotAnOrdinaryMissingResult()
    {
        using var provider=new ResilientLyricsProvider(new Primary(_=>throw new LyricsProviderException("busy",503)),new Secondary(_=>Task.FromResult<IReadOnlyList<TranslationCandidate>>([])));
        var result=Assert.IsType<LyricsLookupResult>(await provider.LookupAsync(recording,default));Assert.Empty(result);Assert.Single(result.UnavailableSources);
    }
    [Fact] public async Task TwoUnavailableSourcesPreserveAServiceError()
    {
        using var provider=new ResilientLyricsProvider(new Primary(_=>throw new LyricsProviderException("busy",503)),new Secondary(_=>throw new LyricsProviderException("limited",429)));
        await Assert.ThrowsAsync<LyricsProviderException>(()=>provider.LookupAsync(recording,default));
    }
    [Fact] public async Task CancelledPrimaryDoesNotFallThroughToAnotherService()
    {
        using var source=new CancellationTokenSource();source.Cancel();
        using var provider=new ResilientLyricsProvider(new Primary(token=>Task.FromCanceled<IReadOnlyList<LyricsCandidate>>(token)),new Secondary(_=>throw new Exception("Cancelled request must not leak")));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>provider.LookupAsync(recording,source.Token));
    }
    [Fact] public async Task AConflictingFallbackRecordingCannotReplaceAnExactInstrumental()
    {
        var instrumental=new LyricsCandidate(3,recording,null,null,true);
        using var provider=new ResilientLyricsProvider(new Primary(_=>Task.FromResult<IReadOnlyList<LyricsCandidate>>([instrumental])),new Secondary(_=>Task.FromResult<IReadOnlyList<TranslationCandidate>>([Fallback() with{Track=recording with{Title="原创片段 (Live)"}}])));
        var result=await provider.LookupAsync(recording,default);Assert.True(AutomaticLyricsMatcher.Select(recording,result)!.Candidate.Instrumental);
    }

    [Fact]
    public async Task RetrySkipsACompletedEmptyPrimaryAndRecoveryClearsTheRememberedFlag()
    {
        int primaryCalls=0,expansionCalls=0,secondaryCalls=0;
        bool recovered=false;
        using var provider=new ResilientLyricsProvider(new ExpandingPrimary(
            _=>{primaryCalls++;return Task.FromResult<IReadOnlyList<LyricsCandidate>>([]);},
            _=>{expansionCalls++;return Task.FromResult<IReadOnlyList<LyricsCandidate>>([]);}),
            new Secondary(_=>
            {
                secondaryCalls++;
                if(!recovered)throw new HttpRequestException("Synthetic NetEase outage.");
                return Task.FromResult<IReadOnlyList<TranslationCandidate>>([Fallback()]);
            }));
        var partial=await provider.LookupAsync(recording,default);
        Assert.True(LyricsLookupDiagnosis.Unavailable(partial));
        Assert.Equal((1,1,1),(primaryCalls,expansionCalls,secondaryCalls));

        recovered=true;
        var ready=await provider.LookupAsync(recording,default);
        Assert.Equal(21,AutomaticLyricsMatcher.Select(recording,ready)?.Candidate.Id);
        Assert.Equal((1,1,2),(primaryCalls,expansionCalls,secondaryCalls));
        await provider.LookupAsync(recording,default);
        Assert.Equal((2,1,3),(primaryCalls,expansionCalls,secondaryCalls));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreviouslyEmptySourceIsSkippedWithoutHidingTheOtherSourcesAvailability(bool primaryEmpty)
    {
        int primaryCalls=0,secondaryCalls=0;
        bool unavailable=true;
        using var provider=new ResilientLyricsProvider(new Primary(_=>
        {
            primaryCalls++;
            if(!primaryEmpty && unavailable)throw new LyricsProviderException("Synthetic LRCLIB outage.",503);
            return Task.FromResult<IReadOnlyList<LyricsCandidate>>([]);
        }),new Secondary(_=>
        {
            secondaryCalls++;
            if(primaryEmpty && unavailable)throw new LyricsProviderException("Synthetic NetEase outage.",503);
            return Task.FromResult<IReadOnlyList<TranslationCandidate>>([]);
        }));
        Assert.True(LyricsLookupDiagnosis.Unavailable(await provider.LookupAsync(recording,default)));

        var failure=await Assert.ThrowsAsync<LyricsProviderException>(()=>provider.LookupAsync(recording,default));
        Assert.StartsWith("歌词来源均暂不可用",failure.Message);
        Assert.Equal(primaryEmpty?(1,2):(2,1),(primaryCalls,secondaryCalls));

        unavailable=false;
        var clean=Assert.IsType<LyricsLookupResult>(await provider.LookupAsync(recording,default));
        Assert.Empty(clean);
        Assert.Empty(clean.UnavailableSources);
        Assert.Equal(primaryEmpty?(1,3):(3,1),(primaryCalls,secondaryCalls));
    }

    [Fact]
    public async Task ClearingAndBypassingEmptyMemoryRequerySourcesAndManualSearchIgnoresIt()
    {
        int primaryCalls=0,secondaryCalls=0;
        using var provider=new ResilientLyricsProvider(new Primary(_=>
        {
            primaryCalls++;
            return Task.FromResult<IReadOnlyList<LyricsCandidate>>([]);
        }),new Secondary(_=>
        {
            secondaryCalls++;
            throw new LyricsProviderException("Synthetic NetEase outage.",503);
        }));
        await provider.LookupAsync(recording,default);
        await Assert.ThrowsAsync<LyricsProviderException>(()=>provider.LookupAsync(recording,default));
        Assert.Equal((1,2),(primaryCalls,secondaryCalls));

        Assert.True(LyricsLookupDiagnosis.Unavailable(await provider.LookupAsync(recording,default,bypassSessionCache:true)));
        Assert.Equal((2,3),(primaryCalls,secondaryCalls));
        Assert.True(LyricsLookupDiagnosis.Unavailable(await provider.SearchManualAsync(recording,default)));
        Assert.Equal((3,4),(primaryCalls,secondaryCalls));
        await Assert.ThrowsAsync<LyricsProviderException>(()=>provider.LookupAsync(recording,default));
        Assert.Equal((3,5),(primaryCalls,secondaryCalls));

        provider.ClearSessionCache();
        await provider.LookupAsync(recording,default,bypassSessionCache:true);
        await provider.LookupAsync(recording,default);
        Assert.Equal((5,7),(primaryCalls,secondaryCalls));
    }

    [Fact]
    public async Task ManualSearchWithCleanPrimaryAndFailedSecondaryDoesNotWriteEmptyMemory()
    {
        int primaryCalls=0;
        using var provider=new ResilientLyricsProvider(new Primary(_=>
        {
            primaryCalls++;
            return Task.FromResult<IReadOnlyList<LyricsCandidate>>([]);
        }),new Secondary(_=>throw new LyricsProviderException("Synthetic NetEase outage.",503)));

        Assert.True(LyricsLookupDiagnosis.Unavailable(await provider.SearchManualAsync(recording,default)));
        Assert.True(LyricsLookupDiagnosis.Unavailable(await provider.LookupAsync(recording,default)));
        Assert.Equal(2,primaryCalls);
    }

    [Fact]
    public async Task EmptyMemoryUsesTheRoundedDurationAsWellAsTheFingerprint()
    {
        int primaryCalls=0;
        using var provider=new ResilientLyricsProvider(new Primary(_=>
        {
            primaryCalls++;
            return Task.FromResult<IReadOnlyList<LyricsCandidate>>([]);
        }),new Secondary(_=>throw new LyricsProviderException("Synthetic NetEase outage.",503)));
        await provider.LookupAsync(recording,default);

        await Assert.ThrowsAsync<LyricsProviderException>(()=>provider.LookupAsync(recording with{DurationMs=120400},default));
        Assert.Equal(1,primaryCalls);
        Assert.True(LyricsLookupDiagnosis.Unavailable(await provider.LookupAsync(recording with{DurationMs=120600},default)));
        Assert.Equal(2,primaryCalls);
    }

    [Fact]
    public async Task AnExplicitExactResultCanSupersedeRememberedEmptyPrimary()
    {
        int primaryRequests=0;
        using var provider=new ResilientLyricsProvider(new LrclibProvider(
            new EmptyHttpSource(()=>primaryRequests++),minimumInterval:TimeSpan.Zero),
            new Secondary(_=>throw new LyricsProviderException("Synthetic NetEase outage.",503)));
        await provider.LookupAsync(recording,default);
        int previousRequests=primaryRequests;
        Assert.True(previousRequests>0);

        var exact=new LyricsLookupResult([new(3,recording,original,null,false)]);
        var recovered=await provider.CompleteLookupAsync(recording,exact,default);
        Assert.Equal(3,AutomaticLyricsMatcher.Select(recording,recovered)?.Candidate.Id);
        Assert.Equal(previousRequests,primaryRequests);
        await provider.LookupAsync(recording,default);
        Assert.True(primaryRequests>previousRequests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrimaryAvailabilityIncludesItsPartialOrExpandedResult(bool failedDuringExpansion)
    {
        int lookups=0,expansions=0;
        var unavailable=new LyricsLookupResult([],["Synthetic LRCLIB source error"]);
        using var provider=new ResilientLyricsProvider(new ExpandingPrimary(_=>
        {
            lookups++;
            return Task.FromResult<IReadOnlyList<LyricsCandidate>>(failedDuringExpansion?[]:unavailable);
        },_=>
        {
            expansions++;
            return Task.FromResult<IReadOnlyList<LyricsCandidate>>(unavailable);
        }),new Secondary(_=>throw new LyricsProviderException("Synthetic NetEase outage.",503)));

        Assert.True(LyricsLookupDiagnosis.Unavailable(await provider.LookupAsync(recording,default)));
        Assert.True(LyricsLookupDiagnosis.Unavailable(await provider.LookupAsync(recording,default)));
        Assert.Equal(2,lookups);
        Assert.Equal(failedDuringExpansion?2:0,expansions);
    }

    [Fact]
    public async Task AFailedSecondaryExpansionDoesNotMarkSecondaryCleanlyEmpty()
    {
        int secondaryCalls=0,expansions=0;
        using var provider=new ResilientLyricsProvider(new Primary(_=>throw new LyricsProviderException("Synthetic LRCLIB outage.",503)),
            new ExpandingSecondary(_=>
            {
                secondaryCalls++;
                return Task.FromResult<IReadOnlyList<TranslationCandidate>>(secondaryCalls==1?[]:[Fallback()]);
            },_=>
            {
                expansions++;
                return Task.FromResult<IReadOnlyList<LyricsCandidate>>(new LyricsLookupResult([],["Synthetic NetEase search error"]));
            }));

        Assert.True(LyricsLookupDiagnosis.Unavailable(await provider.LookupAsync(recording,default)));
        var recovered=await provider.LookupAsync(recording,default);
        Assert.Equal(21,AutomaticLyricsMatcher.Select(recording,recovered)?.Candidate.Id);
        Assert.Equal((2,1),(secondaryCalls,expansions));
    }

    [Fact]
    public async Task PartialResponseCanStillSupplyACheckedStaticFallbackWhenTheOtherSourceIsSkipped()
    {
        int primaryCalls=0,secondaryCalls=0;
        const string text="The tide comes back at sunrise.\nWe head for the harbor lights.";
        using var provider=new ResilientLyricsProvider(new Primary(_=>
        {
            if(++primaryCalls==1)throw new LyricsProviderException("Synthetic LRCLIB outage.",503);
            return Task.FromResult<IReadOnlyList<LyricsCandidate>>(new LyricsLookupResult(
                [new(1,recording,null,text,false)],["Synthetic incomplete LRCLIB search"]));
        }),new Secondary(_=>
        {
            secondaryCalls++;
            return Task.FromResult<IReadOnlyList<TranslationCandidate>>([]);
        }));
        await provider.LookupAsync(recording,default);
        var partial=await provider.LookupAsync(recording,default);

        Assert.True(LyricsLookupDiagnosis.Unavailable(partial));
        Assert.Equal(text,AutomaticLyricsMatcher.SelectStatic(recording,partial)?.PlainLyrics);
        Assert.Equal(1,secondaryCalls);
    }

    sealed class ExpandingPrimary(Func<CancellationToken,Task<IReadOnlyList<LyricsCandidate>>> lookup,
        Func<CancellationToken,Task<IReadOnlyList<LyricsCandidate>>> expand):ILyricsProvider,IAutomaticLyricsSearch
    {
        public Task<IReadOnlyList<LyricsCandidate>> LookupAsync(Track track,CancellationToken token)=>lookup(token);
        public Task<IReadOnlyList<LyricsCandidate>> SearchAutomaticAsync(Track track,IReadOnlyList<LyricsCandidate> known,CancellationToken token)=>expand(token);
    }
    sealed class ExpandingSecondary(Func<CancellationToken,Task<IReadOnlyList<TranslationCandidate>>> lookup,
        Func<CancellationToken,Task<IReadOnlyList<LyricsCandidate>>> expand):ITranslationProvider,IAutomaticLyricsSearch
    {
        public Task<IReadOnlyList<TranslationCandidate>> LookupAsync(Track track,CancellationToken token)=>lookup(token);
        public Task<IReadOnlyList<LyricsCandidate>> SearchAutomaticAsync(Track track,IReadOnlyList<LyricsCandidate> known,CancellationToken token)=>expand(token);
    }
    sealed class EmptyHttpSource(Action onRequest):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            onRequest();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
