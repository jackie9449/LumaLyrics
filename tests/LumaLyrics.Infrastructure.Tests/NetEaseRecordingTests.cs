using System.Net;
using System.Text.Json;
using LumaLyrics.Core;
namespace LumaLyrics.Infrastructure.Tests;

public class NetEaseRecordingTests
{
    static readonly Track Local=new("Own original","Singer A & Singer B","Own album - EP",180000);
    [Theory]
    [InlineData("Singer B & Composer & Singer A","Own album",180400,true)]
    [InlineData("Singer B & Composer","Own album",180400,false)]
    [InlineData("Singer B & Composer & Singer A","Other album",180400,false)]
    [InlineData("Singer B & Composer & Singer A","Own album",183000,false)]
    public void ExtraCreditsRequireSameTitleAlbumAndPreciseDuration(string artist,string album,double duration,bool accepted)
    {Assert.Equal(accepted,NetEaseTranslationProvider.RecordingMatches(Local,Local with{Artist=artist,Album=album,DurationMs=duration}));}
    sealed class Handler(string lyric,bool noLyric=false):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            object value=request.RequestUri!.AbsolutePath.Contains("search")?new{code=200,result=new{songs=new[]{new{id=91,name=Local.Title,artists=new[]{new{name=Local.Artist}},album=new{name=Local.Album},duration=Local.DurationMs}}}}:new{code=200,nolyric=noLyric,lrc=new{lyric}};
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(value))});
        }
    }
    [Theory][InlineData("[99:00.00]纯音乐，请欣赏",true)][InlineData("",false)][InlineData("[00:01]We sing\n[00:05]纯音乐，请欣赏",false)]
    public async Task OnlyAnExplicitWholeResponseMarkerCanProduceInstrumental(string raw,bool instrumental)
    {
        using var provider=new NetEaseTranslationProvider(new Handler(raw,true),TimeSpan.Zero);
        var result=await provider.LookupAsync(Local,default);
        if(raw.Length==0){Assert.Empty(result);return;}
        var candidate=Assert.Single(result).OriginalCandidate;Assert.Equal(instrumental,candidate.Instrumental);
        if(instrumental)
        {
            Assert.Empty(candidate.Content);var binding=new LyricsBinding("b","t","",candidate.SourceLabel,false,0,true,false,candidate.RawJson);
            Assert.True(LibraryPrefetcher.QualityOf(binding,Local));
        }
    }
}
