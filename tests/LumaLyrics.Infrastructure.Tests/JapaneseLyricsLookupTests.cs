using System.Net;
using System.Text.Json;
using LumaLyrics.Core;

namespace LumaLyrics.Infrastructure.Tests;

public sealed class JapaneseLyricsLookupTests
{
    static readonly Track Local=new("Kaze no Oto","Sora Hikari","Kotoba no Umi - Single",180000);
    static readonly Track Source=new("カゼノオト","ソラヒカリ","コトバノウミ",179500);
    const string Original="[00:01]静かな窓にひかりが差す\n[00:40]空を見上げて歩き出す\n[01:52]あたらしい風が吹く";
    const string Chinese="[00:01]光照进安静的窗\n[00:40]抬头望着天空启程\n[01:52]一阵新风吹来";

    [Fact]
    public async Task NetEaseRetriesWithKanaAndReusesCheckedRecordingForTranslation()
    {
        var requests=new List<string>();
        var requestHandler=new Handler(uri=>
        {
            string query=Uri.UnescapeDataString(uri.Query);
            requests.Add(uri.AbsolutePath+query);
            if(uri.AbsolutePath.EndsWith("/lyric",StringComparison.Ordinal))
                return Json(new{code=200,lrc=new{lyric=Original},tlyric=new{lyric=Chinese}});
            object[] songs=query.Contains(Source.Title+" "+Source.Artist,StringComparison.Ordinal)
                ?[CatalogItem(21,Source with{Album=Source.Album+" (ライブ)"}),CatalogItem(22,Source),CatalogItem(23,Source with{DurationMs=175000})]:[];
            return Json(new{code=200,result=new{songs}});
        });
        using var netease=new NetEaseTranslationProvider(requestHandler,TimeSpan.Zero);
        using var provider=new ResilientLyricsProvider(new EmptyOriginalProvider(),netease);
        var result=await provider.CompleteLookupAsync(Local,null,default);
        var candidate=AutomaticLyricsMatcher.Select(Local,result)!.Candidate;
        Assert.Equal(22,candidate.Id);
        Assert.Equal(Source,candidate.Track);
        Assert.Equal(3,requests.Count);
        Assert.Contains(Local.Title+" "+Local.Artist,requests[0]);
        Assert.Contains(Source.Title+" "+Source.Artist,requests[1]);
        Assert.Contains("id=22",requests[2]);

        string root=Path.Combine(Path.GetTempPath(),"LumaKanaLookup-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var lyrics=new LyricsRepository(Path.Combine(root,"lyrics.db"));
            var binding=lyrics.Save(Local,candidate.Content,candidate.SourceLabel,false,providerJson:candidate.RawJson);
            Assert.True(LibraryPrefetcher.QualityOf(binding,Local));
            Assert.Null(lyrics.FindReadOnly(Source));
            var workflow=new TranslationWorkflow(root,netease);
            var translation=await workflow.PrepareAsync(Local,true,false,default,result.TranslationCandidates);
            Assert.Equal(TranslationLookupState.Found,translation.Lookup.State);
            using var translations=new TranslationRepository(root,true);
            var saved=translations.Find(Local,new LrcParser().Parse(Original));
            Assert.NotNull(saved);
            Assert.Equal(TranslationAlignment.Hash(Original),saved.SourceHash);
            Assert.Equal(3,saved.Lines.Count);
            Assert.Equal(3,requests.Count);
        }
        finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(root,true);}
    }

    [Fact]
    public async Task LrclibExpandedLookupCanAdoptKanaWithoutRepeatingExactLookup()
    {
        var requests=new List<string>();
        using var lrclib=new LrclibProvider(new Handler(uri=>
        {
            string query=Uri.UnescapeDataString(uri.Query);
            requests.Add(uri.AbsolutePath+query);
            if(uri.AbsolutePath.EndsWith("/get",StringComparison.Ordinal))return new HttpResponseMessage(HttpStatusCode.NotFound);
            return query.Contains(Source.Title+" "+Source.Artist,StringComparison.Ordinal)
                ?Json(new[]{new{id=22,trackName=Source.Title,artistName=Source.Artist,albumName=Source.Album,duration=179.5,syncedLyrics=Original}})
                :Json(Array.Empty<object>());
        }),minimumInterval:TimeSpan.Zero);
        using var provider=new ResilientLyricsProvider(lrclib,new EmptyTranslationProvider());
        var first=await provider.LookupExactAsync(Local,default);
        Assert.Empty(first!);
        var candidates=await provider.CompleteLookupAsync(Local,first,default);
        Assert.Equal(Source,AutomaticLyricsMatcher.Select(Local,candidates)!.Candidate.Track);
        Assert.Single(requests,q=>q.StartsWith("/api/get?",StringComparison.Ordinal));
        Assert.Single(requests,q=>q.Contains(Source.Title+" "+Source.Artist,StringComparison.Ordinal));
        Assert.Equal(5,requests.Count);
    }

    [Fact]
    public void OrdinaryEnglishAndUnresolvedKanjiDoNotGetKanaGuesses()
    {
        foreach(var track in new[]{new Track("Weather Report","The Rainmakers","Storm",180000),new Track("東京","東京楽団","東京の歌",180000)})
            Assert.Equal(3,ManualLyricsSearch.Queries(track).Count);
    }

    [Fact]
    public void JapaneseAdditionalCreditsRequireMatchingAlbumAndActualArtist()
    {
        var extra=Source with{Artist=Source.Artist+" & Own producer"};
        Assert.True(NetEaseTranslationProvider.RecordingMatches(Local,extra));
        Assert.False(NetEaseTranslationProvider.RecordingMatches(Local,extra with{Album="ホカノアルバム"}));
        Assert.False(NetEaseTranslationProvider.RecordingMatches(Local,extra with{Artist="ソラヒカル & Own producer"}));
        Assert.False(NetEaseTranslationProvider.RecordingMatches(Local,extra with{DurationMs=178900}));
    }

    static object CatalogItem(long id,Track track)=>new{id,name=track.Title,artists=new[]{new{name=track.Artist}},album=new{name=track.Album},duration=track.DurationMs};
    static HttpResponseMessage Json(object data)=>new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(data))};
    sealed class Handler(Func<Uri,HttpResponseMessage> send):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
            =>Task.FromResult(send(request.RequestUri!));
    }
    sealed class EmptyOriginalProvider:ILyricsProvider
    {
        public Task<IReadOnlyList<LyricsCandidate>> LookupAsync(Track track,CancellationToken token)=>Task.FromResult<IReadOnlyList<LyricsCandidate>>([]);
    }
    sealed class EmptyTranslationProvider:ITranslationProvider
    {
        public Task<IReadOnlyList<TranslationCandidate>> LookupAsync(Track track,CancellationToken token)=>Task.FromResult<IReadOnlyList<TranslationCandidate>>([]);
    }
}
