using System.Net;
using System.Text;
using System.Text.Json;
using LumaLyrics.Core;
namespace LumaLyrics.Infrastructure;

public sealed record TranslationCandidate(long Id,Track Track,string Original,string Translated,string SourceUrl)
{
    public bool Instrumental {get;init;}
    public string? InstrumentalEvidence {get;init;}
    public long? CatalogAlbumId {get;init;}
    internal LyricsCandidate CatalogCandidate=>NetEaseTranslationProvider.CatalogCandidate(Id,Track,CatalogAlbumId);
    public LyricsCandidate OriginalCandidate=>CatalogCandidate with
    {
        SyncedLyrics=Instrumental?null:Original,
        Instrumental=Instrumental,
        RawJson=JsonSerializer.Serialize(new NetEaseOriginal("NetEase",Id,Track,Original,SourceUrl,Instrumental,InstrumentalEvidence,CatalogAlbumId))
    };
    public LyricsCandidate OriginalCandidateFor(Track local)=>NetEaseTranslationProvider.ApplyCreditCompatibility(local,OriginalCandidate);
}
public sealed record NetEaseOriginal(string Provider,long Id,Track Track,string Original,string SourceUrl,bool Instrumental=false,string? InstrumentalEvidence=null,long? CatalogAlbumId=null);
public interface ITranslationProvider
{
    Task<IReadOnlyList<TranslationCandidate>> LookupAsync(Track track,CancellationToken token);
}

public sealed partial class NetEaseTranslationProvider : ITranslationProvider,IManualLyricsSearch,IAutomaticLyricsSearch,IDisposable
{
    sealed record CatalogSong(LyricsCandidate Candidate,long? AlbumId);
    internal sealed record RecordingLookup(IReadOnlyList<TranslationCandidate> Candidates,LyricsCatalogStatus CatalogStatus);
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
        =>RecordingMatches(local,new LyricsCandidate(0,candidate,null,null,false));

    internal static bool RecordingMatches(Track local,TranslationCandidate candidate)
        =>RecordingMatches(local,candidate.CatalogCandidate);

    internal static bool RecordingMatches(Track local,LyricsCandidate candidate)
    {
        local=TrackMetadata.FromAppleSession(local);
        var score=MatchRules.Score(local,candidate);
        return local.DurationMs is >0 && candidate.Track.DurationMs is >0
            && (score.Eligible && score.Score>=.89 || NetEaseRecording.AdditionalCreditsMatch(local,candidate.Track));
    }
    internal static LyricsCandidate CatalogCandidate(long id,Track track,long? albumId)
        =>NetEaseVerifiedRelease.Attach(new(id,track,null,null,false){ProviderName="NetEase"},albumId);
    // Existing extra-credit compatibility applies only when independently corroborated.
    internal static LyricsCandidate ApplyCreditCompatibility(Track local,LyricsCandidate candidate)
        =>!MatchRules.Score(local,candidate).Eligible && NetEaseRecording.AdditionalCreditsMatch(local,candidate.Track)
            ?candidate with{Track=candidate.Track with{Artist=local.Artist}}:candidate;
    public async Task<IReadOnlyList<TranslationCandidate>> LookupAsync(Track input,CancellationToken token)
        =>(await LookupRecordingsAsync(input,token)).Candidates;

    internal async Task<RecordingLookup> LookupRecordingsAsync(Track input,CancellationToken token)
    {
        var track=TrackMetadata.FromAppleSession(input);
        if(track.DurationMs is not >0 || string.IsNullOrWhiteSpace(track.Title) || string.IsNullOrWhiteSpace(track.Artist))
            return new([],LyricsCatalogStatus.None);
        try
        {
            if(await FindVerifiedSongAsync(track,token) is {} verified)
                return await ReadLyricsAsync(track,[verified],true,token);

            var catalog=await SearchSongsAsync(track.Title+" "+track.Artist,token);
            bool discovered=catalog.Count>0;
            var matches=catalog.Where(song=>RecordingMatches(track,song.Candidate)).ToArray();
            if(matches.Length==0 && ManualLyricsSearch.KanaQuery(track) is {} kana)
            {
                catalog=await SearchSongsAsync(kana.Text,token);
                discovered|=catalog.Count>0;
                matches=catalog.Where(song=>RecordingMatches(track,song.Candidate)).ToArray();
            }
            return await ReadLyricsAsync(track,matches,discovered,token);
        }
        catch(JsonException){throw new LyricsProviderException("中文译文来源返回格式异常，未保存内容。");}
    }

    async Task<CatalogSong?> FindVerifiedSongAsync(Track track,CancellationToken token)
    {
        if(NetEaseVerifiedRelease.FindId(track) is not {} id)return null;
        try
        {
            using var json=JsonDocument.Parse(await GetAsync($"api/song/detail?ids=%5B{id}%5D",token));
            CheckCode(json.RootElement);
            return ParseSongs(json.RootElement).FirstOrDefault(song=>song.Candidate.Id==id
                && song.Candidate.CatalogMatch?.Confirms(track,song.Candidate)==true);
        }
        catch(LyricsProviderException ex) when(ex.StatusCode==404){return null;}
    }

    async Task<RecordingLookup> ReadLyricsAsync(Track track,IReadOnlyList<CatalogSong> matches,bool discovered,CancellationToken token)
    {
        var found=new List<TranslationCandidate>();
        foreach(var song in matches.OrderByDescending(song=>song.Candidate.CatalogMatch?.Confirms(track,song.Candidate)==true)
            .ThenByDescending(song=>MatchRules.AlbumMatches(song.Candidate.Track.Album,track.Album))
            .ThenBy(song=>MatchRules.Score(track,song.Candidate).UsesJapaneseReading)
            .ThenBy(song=>Math.Abs(song.Candidate.Track.DurationMs!.Value-track.DurationMs!.Value))
            .DistinctBy(song=>song.Candidate.Id).Take(3))
        {
            if(await FetchLyricsAsync(song,token) is {} lyrics)found.Add(lyrics);
        }
        var missing=found.Count>0?LyricsCatalogStatus.None:matches.Count>0?LyricsCatalogStatus.MatchedWithoutLyrics
            :discovered?LyricsCatalogStatus.Unconfirmed:LyricsCatalogStatus.None;
        return new(found,missing);
    }

    async Task<TranslationCandidate?> FetchLyricsAsync(CatalogSong song,CancellationToken token)
    {
        var item=song.Candidate;
        try
        {
            using var json=JsonDocument.Parse(await GetAsync($"api/song/lyric?id={item.Id}&lv=-1&kv=-1&tv=-1",token));
            CheckCode(json.RootElement);
            string original=Nested(json.RootElement,"lrc");
            if(string.IsNullOrWhiteSpace(original))return null;
            bool instrumental=NetEaseRecording.IsExplicitInstrumental(original);
            return new(item.Id,item.Track,instrumental?"":original,Nested(json.RootElement,"tlyric"),$"https://music.163.com/#/song?id={item.Id}")
            {
                Instrumental=instrumental,
                InstrumentalEvidence=instrumental?original:null,
                CatalogAlbumId=song.AlbumId
            };
        }
        catch(LyricsProviderException ex) when(ex.StatusCode==404){return null;}
    }

    async Task<IReadOnlyList<CatalogSong>> SearchSongsAsync(string query,CancellationToken token)
    {
        var raw=await GetAsync("api/search/get?s="+Uri.EscapeDataString(query)+"&type=1&limit=30",token);
        using var json=JsonDocument.Parse(raw);
        CheckCode(json.RootElement);
        return json.RootElement.TryGetProperty("result",out var result)?ParseSongs(result):[];
    }

    static IReadOnlyList<CatalogSong> ParseSongs(JsonElement result)
    {
        if(result.ValueKind!=JsonValueKind.Object || !result.TryGetProperty("songs",out var songs) || songs.ValueKind!=JsonValueKind.Array)return [];
        var candidates=new List<CatalogSong>();
        foreach(var song in songs.EnumerateArray().Take(30))
        {
            if(Id(song) is not {} id)continue;
            string title=Text(song,"name");
            if(title.Length==0)continue;
            string artist=song.TryGetProperty("artists",out var ar)&&ar.ValueKind==JsonValueKind.Array?string.Join(" & ",ar.EnumerateArray().Select(a=>Text(a,"name"))):"";
            song.TryGetProperty("album",out var album);
            double? duration=song.TryGetProperty("duration",out var d)&&d.ValueKind==JsonValueKind.Number&&d.TryGetDouble(out var ms)?ms:null;
            var albumId=Id(album);
            candidates.Add(new(CatalogCandidate(id,new(title,artist,Text(album,"name"),duration),albumId),albumId));
        }
        return candidates;
    }
    static long? Id(JsonElement entry)=>entry.ValueKind==JsonValueKind.Object && entry.TryGetProperty("id",out var id)
        && id.ValueKind==JsonValueKind.Number && id.TryGetInt64(out var number) && number>0?number:null;
    static string Text(JsonElement e,string name)=>e.ValueKind==JsonValueKind.Object && e.TryGetProperty(name,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString()??"":"";
    static string Nested(JsonElement e,string name)=>e.TryGetProperty(name,out var child)?Text(child,"lyric"):"";
    void CheckCode(JsonElement e)
    {
        if(e.ValueKind!=JsonValueKind.Object || !e.TryGetProperty("code",out var code)||code.ValueKind!=JsonValueKind.Number||!code.TryGetInt32(out var n))throw new LyricsProviderException("中文译文来源返回格式异常，未保存内容。");
        if(n!=200){if(n is 405 or 429)next=DateTimeOffset.UtcNow.AddMinutes(1);throw new LyricsProviderException($"歌词与译文来源未返回成功结果（{n}），未保存内容。",n);}
    }
    public void Dispose(){client.Dispose();} // A cancelled request can still release its gate.
}
