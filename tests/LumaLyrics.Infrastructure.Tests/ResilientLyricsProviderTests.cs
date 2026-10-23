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
}
