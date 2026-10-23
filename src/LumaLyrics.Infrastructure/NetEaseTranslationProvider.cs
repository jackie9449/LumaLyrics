using System.Net;
using System.Text;
using System.Text.Json;
using LumaLyrics.Core;
namespace LumaLyrics.Infrastructure;

public sealed record TranslationCandidate(long Id,Track Track,string Original,string Translated,string SourceUrl)
{
    public bool Instrumental {get;init;}
    public string? InstrumentalEvidence {get;init;}
    public LyricsCandidate OriginalCandidate=>new(Id,Track,Instrumental?null:Original,null,Instrumental,JsonSerializer.Serialize(new NetEaseOriginal("NetEase",Id,Track,Original,SourceUrl,Instrumental,InstrumentalEvidence))){ProviderName="NetEase"};
    public LyricsCandidate OriginalCandidateFor(Track local)=>OriginalCandidate with{Track=Track with{Artist=local.Artist}};
}
public sealed record NetEaseOriginal(string Provider,long Id,Track Track,string Original,string SourceUrl,bool Instrumental=false,string? InstrumentalEvidence=null);
public interface ITranslationProvider
{
    Task<IReadOnlyList<TranslationCandidate>> LookupAsync(Track track,CancellationToken token);
}

public sealed class NetEaseTranslationProvider : ITranslationProvider,IDisposable
{
    readonly HttpClient client;readonly SemaphoreSlim gate=new(1,1);readonly TimeSpan interval;DateTimeOffset next;
    public NetEaseTranslationProvider(HttpMessageHandler? handler=null,TimeSpan? minimumInterval=null)
    {
        interval=minimumInterval??TimeSpan.FromSeconds(1);
        client=handler is null?new():new(handler);client.BaseAddress=new("https://music.163.com/");client.Timeout=TimeSpan.FromSeconds(20);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LumaLyrics/0.15.0 (https://github.com/jackie9449/LumaLyrics)");
        client.DefaultRequestHeaders.Referrer=client.BaseAddress;
    }
    async Task<string> GetAsync(string uri,CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            var delay=next-DateTimeOffset.UtcNow;if(delay>TimeSpan.Zero)await Task.Delay(delay,token);
            using var response=await client.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,token);
            next=DateTimeOffset.UtcNow+interval;
            if(response.StatusCode==HttpStatusCode.TooManyRequests)
            {next=response.Headers.RetryAfter?.Date??DateTimeOffset.UtcNow+(response.Headers.RetryAfter?.Delta??TimeSpan.FromMinutes(1));throw new LyricsProviderException("中文译文来源暂时限流，稍后重试。",429);}
            if(!response.IsSuccessStatusCode)throw new LyricsProviderException($"中文译文来源暂不可用（{(int)response.StatusCode}）。",(int)response.StatusCode);
            if(response.Content.Headers.ContentLength>2*1024*1024)throw new LyricsProviderException("译文响应超过 2 MB。");
            using var input=await response.Content.ReadAsStreamAsync(token);using var output=new MemoryStream();var buffer=new byte[8192];int count;
            while((count=await input.ReadAsync(buffer,token))>0){if(output.Length+count>2*1024*1024)throw new LyricsProviderException("译文响应超过 2 MB。");await output.WriteAsync(buffer.AsMemory(0,count),token);}
            return Encoding.UTF8.GetString(output.ToArray());
        }
        catch(TaskCanceledException) when(!token.IsCancellationRequested){throw new LyricsProviderException("中文译文查询超时，稍后重试。");}
        catch(HttpRequestException){throw new LyricsProviderException("无法连接中文译文来源，稍后重试。");}
        finally{gate.Release();}
    }
    public static bool RecordingMatches(Track local,Track candidate)
    {
        local=TrackMetadata.FromAppleSession(local);
        var score=MatchRules.Score(local,new(0,candidate,null,null,false));
        return local.DurationMs is >0 && candidate.DurationMs is >0 && (score.Eligible && score.Score>=.89 || NetEaseRecording.AdditionalCreditsMatch(local,candidate));
    }
    public async Task<IReadOnlyList<TranslationCandidate>> LookupAsync(Track input,CancellationToken token)
    {
        var track=TrackMetadata.FromAppleSession(input);if(track.DurationMs is null || string.IsNullOrWhiteSpace(track.Title) || string.IsNullOrWhiteSpace(track.Artist))return [];
        try
        {
            var search=await GetAsync("api/search/get?s="+Uri.EscapeDataString(track.Title+" "+track.Artist)+"&type=1&limit=30",token);
            using var json=JsonDocument.Parse(search);CheckCode(json.RootElement);
            if(!json.RootElement.TryGetProperty("result",out var result)||result.ValueKind!=JsonValueKind.Object||!result.TryGetProperty("songs",out var songs)||songs.ValueKind!=JsonValueKind.Array)return [];
            var candidates=new List<(long Id,Track Track)>();
            foreach(var song in songs.EnumerateArray())
            {
                if(song.ValueKind!=JsonValueKind.Object)continue;
                string title=Text(song,"name");var artists=song.TryGetProperty("artists",out var ar)&&ar.ValueKind==JsonValueKind.Array?string.Join(" & ",ar.EnumerateArray().Select(a=>Text(a,"name"))):"";
                string album=song.TryGetProperty("album",out var al)?Text(al,"name"):"";
                double? duration=song.TryGetProperty("duration",out var d)&&d.ValueKind==JsonValueKind.Number&&d.TryGetDouble(out var ms)?ms:null;
                if(song.TryGetProperty("id",out var id)&&id.ValueKind==JsonValueKind.Number&&id.TryGetInt64(out var n)&&n>0)
                {var candidate=new Track(title,artists,album,duration);if(RecordingMatches(track,candidate))candidates.Add((n,candidate));}
            }
            var found=new List<TranslationCandidate>();
            foreach(var item in candidates.OrderByDescending(c=>MatchRules.Normalize(c.Track.Album)==MatchRules.Normalize(track.Album)).ThenBy(c=>Math.Abs(c.Track.DurationMs!.Value-track.DurationMs.Value)).DistinctBy(c=>c.Id).Take(3))
            {
                var raw=await GetAsync($"api/song/lyric?id={item.Id}&lv=-1&kv=-1&tv=-1",token);using var lyrics=JsonDocument.Parse(raw);CheckCode(lyrics.RootElement);
                string original=Nested(lyrics.RootElement,"lrc"),translation=Nested(lyrics.RootElement,"tlyric");
                // No lyric/instrumental inference is made from an absent response.
                bool instrumental=NetEaseRecording.IsExplicitInstrumental(original);
                if(original.Length>0)found.Add(new(item.Id,item.Track,instrumental?"":original,translation,$"https://music.163.com/#/song?id={item.Id}"){Instrumental=instrumental,InstrumentalEvidence=instrumental?original:null});
            }
            return found;
        }
        catch(JsonException){throw new LyricsProviderException("中文译文来源返回格式异常，未保存内容。");}
    }
    static string Text(JsonElement e,string name)=>e.ValueKind==JsonValueKind.Object && e.TryGetProperty(name,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString()??"":"";
    static string Nested(JsonElement e,string name)=>e.TryGetProperty(name,out var child)?Text(child,"lyric"):"";
    void CheckCode(JsonElement e)
    {
        if(e.ValueKind!=JsonValueKind.Object || !e.TryGetProperty("code",out var code)||code.ValueKind!=JsonValueKind.Number||!code.TryGetInt32(out var n))throw new LyricsProviderException("中文译文来源返回格式异常，未保存内容。");
        if(n!=200){if(n is 405 or 429)next=DateTimeOffset.UtcNow.AddMinutes(1);throw new LyricsProviderException($"歌词与译文来源未返回成功结果（{n}），未保存内容。",n);}
    }
    public void Dispose(){client.Dispose();} // A cancelled request can still release its gate.
}
