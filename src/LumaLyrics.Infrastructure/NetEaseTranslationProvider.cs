using System.Net;
using System.Text.Json;
using LumaLyrics.Core;
namespace LumaLyrics.Infrastructure;

public sealed record TranslationCandidate(long Id,Track Track,string Original,string Translated,string SourceUrl)
{
    public bool Instrumental {get;init;}
    public string? InstrumentalEvidence {get;init;}
    public long? CatalogAlbumId {get;init;}
    public NetEaseReleaseEvidence? ReleaseEvidence {get;init;}
    internal LyricsCandidate CatalogCandidate=>NetEaseTranslationProvider.CatalogCandidate(Id,Track,CatalogAlbumId,ReleaseEvidence);
    public LyricsCandidate OriginalCandidate=>CatalogCandidate with
    {
        SyncedLyrics=Instrumental?null:Original,
        Instrumental=Instrumental,
        RawJson=JsonSerializer.Serialize(new NetEaseOriginal("NetEase",Id,Track,Original,SourceUrl,Instrumental,InstrumentalEvidence,CatalogAlbumId,ReleaseEvidence))
    };
    public LyricsCandidate OriginalCandidateFor(Track local)=>NetEaseTranslationProvider.ApplyCreditCompatibility(local,OriginalCandidate);
}
public sealed record NetEaseOriginal(string Provider,long Id,Track Track,string Original,string SourceUrl,bool Instrumental=false,
    string? InstrumentalEvidence=null,long? CatalogAlbumId=null,NetEaseReleaseEvidence? ReleaseEvidence=null);
public interface ITranslationProvider
{
    Task<IReadOnlyList<TranslationCandidate>> LookupAsync(Track track,CancellationToken token);
}

public sealed partial class NetEaseTranslationProvider : ITranslationProvider,IManualLyricsSearch,IAutomaticLyricsSearch,IDisposable
{
    const int AlbumHintCapacity=128;
    const int AlbumDirectoryCapacity=64;
    sealed record CatalogSong(LyricsCandidate Candidate,long? AlbumId,int Disc,int Number,NetEaseReleaseEvidence? ReleaseEvidence=null);
    internal sealed record RecordingLookup(IReadOnlyList<TranslationCandidate> Candidates,LyricsCatalogStatus CatalogStatus,bool CatalogUnavailable=false);
    sealed class SessionCache
    {
        public readonly Dictionary<(string Artist,string Album),(long AlbumId,DateTimeOffset Expires)> AlbumHints=new();
        public readonly Dictionary<(string Artist,string Album),(IReadOnlyList<CatalogSong> Songs,DateTimeOffset Expires)> AlbumDirectory=new();
        public readonly Dictionary<AppleLocalizedRelease,CachedRelease> CheckedReleases=new();
    }
    SessionCache sessionCache=new();
    readonly SemaphoreSlim albumGate=new(1,1);
    readonly HttpClient client;readonly SemaphoreSlim gate=new(1,1);readonly TimeSpan interval;DateTimeOffset next;
    long rateRetryTicks;
    internal bool IsRateLimited=>DateTimeOffset.UtcNow.UtcTicks<Interlocked.Read(ref rateRetryTicks);
    public NetEaseTranslationProvider(HttpMessageHandler? handler=null,TimeSpan? minimumInterval=null,HttpMessageHandler? appleCatalogHandler=null)
    {
        interval=minimumInterval??TimeSpan.FromSeconds(1);
        client=handler is null?new():new(handler);client.BaseAddress=new("https://music.163.com/");client.Timeout=TimeSpan.FromSeconds(20);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LumaLyrics/0.15.0 (https://github.com/jackie9449/LumaLyrics)");
        client.DefaultRequestHeaders.Referrer=client.BaseAddress;
        if(handler is null || appleCatalogHandler is not null)appleCatalog=new(appleCatalogHandler);
    }
    async Task<JsonDocument> GetAsync(string uri,CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            token.ThrowIfCancellationRequested();
            if(IsRateLimited)throw new LyricsProviderException("中文译文来源仍处于限流等待期，继续查询其他来源。",429);
            var delay=next-DateTimeOffset.UtcNow;if(delay>TimeSpan.Zero)await Task.Delay(delay,token);
            var started=DateTimeOffset.UtcNow;
            next=started+interval;
            using var response=await client.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,token);
            if(response.StatusCode==HttpStatusCode.TooManyRequests)
            {
                next=response.Headers.RetryAfter?.Date??DateTimeOffset.UtcNow+(response.Headers.RetryAfter?.Delta??TimeSpan.FromMinutes(1));
                Interlocked.Exchange(ref rateRetryTicks,next.UtcTicks);
                throw new LyricsProviderException("中文译文来源暂时限流，稍后重试。",429);
            }
            if(!response.IsSuccessStatusCode)throw new LyricsProviderException($"中文译文来源暂不可用（{(int)response.StatusCode}）。",(int)response.StatusCode);
            if(response.Content.Headers.ContentLength>2*1024*1024)throw new LyricsProviderException("译文响应超过 2 MB。");
            using var input=await response.Content.ReadAsStreamAsync(token);using var output=new MemoryStream();var buffer=new byte[8192];int count;
            while((count=await input.ReadAsync(buffer,token))>0){if(output.Length+count>2*1024*1024)throw new LyricsProviderException("译文响应超过 2 MB。");await output.WriteAsync(buffer.AsMemory(0,count),token);}
            var document=JsonDocument.Parse(output.GetBuffer().AsMemory(0,(int)output.Length));
            // Apply JSON rate limits before the next request can enter the gate.
            try{CheckCode(document.RootElement);return document;}
            catch{document.Dispose();throw;}
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
    internal static LyricsCandidate CatalogCandidate(long id,Track track,long? albumId,NetEaseReleaseEvidence? evidence)
    {
        var candidate=new LyricsCandidate(id,track,null,null,false){ProviderName="NetEase"};
        return evidence?.Confirm(candidate,albumId) is {} match?candidate with{CatalogMatch=match}:candidate;
    }
    // Existing extra-credit compatibility applies only when independently corroborated.
    internal static LyricsCandidate ApplyCreditCompatibility(Track local,LyricsCandidate candidate)
        =>!MatchRules.Score(local,candidate).Eligible && NetEaseRecording.AdditionalCreditsMatch(local,candidate.Track)
            ?candidate with{Track=candidate.Track with{Artist=local.Artist}}:candidate;
    public async Task<IReadOnlyList<TranslationCandidate>> LookupAsync(Track input,CancellationToken token)
    {
        var lookup=await LookupRecordingsAsync(input,token);
        if(lookup.CatalogUnavailable && lookup.Candidates.Count==0)throw new LyricsProviderException("发行目录暂不可用，稍后重试。");
        return lookup.Candidates;
    }

    internal async Task<RecordingLookup> LookupRecordingsAsync(Track input,CancellationToken token,bool preferTranslation=true)
    {
        var track=TrackMetadata.FromAppleSession(input);
        token.ThrowIfCancellationRequested();
        if(track.DurationMs is not >0 || string.IsNullOrWhiteSpace(track.Title) || string.IsNullOrWhiteSpace(track.Artist))
            return new([],LyricsCatalogStatus.None);
        var cache=Volatile.Read(ref sessionCache);
        bool hasAlbum=!string.IsNullOrWhiteSpace(track.Album);
        var albumKey=hasAlbum?AppleReleaseCatalog.AlbumKey(track.Artist,track.Album):default;
        try
        {
            if(hasAlbum && await ReadAlbumDirectoryAsync(cache,albumKey,track,token) is {} directory)
            {
                var albumMatches=directory.Where(song=>RecordingMatches(track,song.Candidate)).ToArray();
                if(albumMatches.Length>0)return await ReadLyricsAsync(track,albumMatches,token,preferTranslation);
            }
            var catalog=await SearchSongsAsync(track.Title+" "+track.Artist,token);
            var matches=catalog.Where(song=>RecordingMatches(track,song.Candidate)).ToArray();
            if(matches.Length==0 && ManualLyricsSearch.KanaQuery(track) is {} kana)
            {
                var kanaCatalog=await SearchSongsAsync(kana.Text,token);
                matches=kanaCatalog.Where(song=>RecordingMatches(track,song.Candidate)).ToArray();
                if(matches.Length==0)catalog=catalog.Concat(kanaCatalog).ToArray();
            }
            if(matches.Length>0)
            {
                if(hasAlbum)await RememberAlbumHintAsync(cache,albumKey,track,matches,token);
                return await ReadLyricsAsync(track,matches,token,preferTranslation);
            }
            var release=await FindLocalizedSongAsync(track,catalog,token);
            if(release.Song is {} song)return await ReadLyricsAsync(track,[song],token,preferTranslation);
            return new([],catalog.Count>0?LyricsCatalogStatus.Unconfirmed:LyricsCatalogStatus.None,release.Unavailable);
        }
        catch(JsonException){throw new LyricsProviderException("中文译文来源返回格式异常，未保存内容。");}
    }

    async Task<IReadOnlyList<CatalogSong>?> ReadAlbumDirectoryAsync(SessionCache cache,(string Artist,string Album) key,Track track,CancellationToken token)
    {
        await albumGate.WaitAsync(token);
        try
        {
            token.ThrowIfCancellationRequested();
            var now=DateTimeOffset.UtcNow;
            if(cache.AlbumDirectory.TryGetValue(key,out var directory) && directory.Expires>now)return directory.Songs;
            cache.AlbumDirectory.Remove(key);
            if(!cache.AlbumHints.TryGetValue(key,out var hint))return null;

            // Hold the gate through promotion so concurrent tracks share this request.
            var songs=hint.Expires>now?await FetchAlbumDirectoryAsync(hint.AlbumId,track,token):null;
            token.ThrowIfCancellationRequested();
            cache.AlbumHints.Remove(key);
            if(songs is null)return null;
            if(cache.AlbumDirectory.Count>=AlbumDirectoryCapacity)
                cache.AlbumDirectory.Remove(cache.AlbumDirectory.MinBy(pair=>pair.Value.Expires).Key);
            cache.AlbumDirectory[key]=(songs,DateTimeOffset.UtcNow.AddHours(1));
            return songs;
        }
        finally{albumGate.Release();}
    }

    async Task<IReadOnlyList<CatalogSong>?> FetchAlbumDirectoryAsync(long albumId,Track track,CancellationToken token)
    {
        try
        {
            using var detail=await GetAsync($"api/album/{albumId}",token);
            if(!detail.RootElement.TryGetProperty("album",out var node) || ReadAlbum(node) is not {Count:>=2} album
                || album.Id!=albumId || !MatchRules.CatalogAlbumMatches(track.Album,album.Name))return null;
            var songs=ParseSongs(AlbumSongsParent(detail.RootElement,node),Math.Max(30,album.Count))
                .Where(song=>song.AlbumId is null || song.AlbumId==album.Id).ToArray();
            return songs.Length>=2?songs:null;
        }
        catch(LyricsProviderException ex) when(ex.StatusCode is not (429 or 405)){return null;}
        catch(OperationCanceledException) when(!token.IsCancellationRequested){return null;}
        catch(Exception ex) when(ex is JsonException or IOException){return null;}
    }

    async Task RememberAlbumHintAsync(SessionCache cache,(string Artist,string Album) key,Track track,IReadOnlyList<CatalogSong> matches,CancellationToken token)
    {
        var hint=RankSongs(track,matches).FirstOrDefault(song=>song.AlbumId is >0
            && MatchRules.AlbumMatches(song.Candidate.Track.Album,track.Album));
        if(hint?.AlbumId is not {} albumId)return;
        await albumGate.WaitAsync(token);
        try
        {
            token.ThrowIfCancellationRequested();
            var now=DateTimeOffset.UtcNow;
            if(cache.AlbumDirectory.TryGetValue(key,out var directory) && directory.Expires>now
                || cache.AlbumHints.TryGetValue(key,out var existing) && existing.Expires>now)return;
            if(cache.AlbumHints.Count>=AlbumHintCapacity && !cache.AlbumHints.ContainsKey(key))
                cache.AlbumHints.Remove(cache.AlbumHints.MinBy(pair=>pair.Value.Expires).Key);
            // The first verified keyword hit only remembers the ID for a later lookup.
            cache.AlbumHints[key]=(albumId,now.AddHours(1));
        }
        finally{albumGate.Release();}
    }

    static IOrderedEnumerable<CatalogSong> RankSongs(Track track,IReadOnlyList<CatalogSong> songs)
        =>songs.OrderByDescending(song=>song.Candidate.CatalogMatch?.Confirms(track,song.Candidate)==true)
            .ThenByDescending(song=>MatchRules.AlbumMatches(song.Candidate.Track.Album,track.Album))
            .ThenBy(song=>MatchRules.Score(track,song.Candidate).UsesJapaneseReading)
            .ThenBy(song=>Math.Abs(song.Candidate.Track.DurationMs!.Value-track.DurationMs!.Value));

    async Task<RecordingLookup> ReadLyricsAsync(Track track,IReadOnlyList<CatalogSong> matches,CancellationToken token,bool preferTranslation=true)
    {
        var found=new List<TranslationCandidate>();
        foreach(var song in RankSongs(track,matches).DistinctBy(song=>song.Candidate.Id).Take(3))
        {
            if(await FetchLyricsAsync(song,token) is not {} lyrics)continue;
            found.Add(lyrics);
            var originalCandidate=lyrics.OriginalCandidateFor(track);
            if(AutomaticLyricsMatcher.Select(track,[originalCandidate]) is {Candidate.Instrumental:false}
                && (!preferTranslation || !string.IsNullOrWhiteSpace(lyrics.Translated) || IsChineseOriginal(lyrics.Original)))break;
        }
        return new(found,found.Count==0?LyricsCatalogStatus.MatchedWithoutLyrics:LyricsCatalogStatus.None);
    }

    static bool IsChineseOriginal(string raw)
    {
        var doc=new LrcParser().Parse(raw);
        return TranslationAlignment.IsChinese(string.Join("\n",doc.Groups.Where(g=>TranslationAlignment.IsLyricText(g.Text)).Select(g=>g.Text)));
    }

    async Task<TranslationCandidate?> FetchLyricsAsync(CatalogSong song,CancellationToken token)
    {
        var item=song.Candidate;
        try
        {
            using var json=await GetAsync($"api/song/lyric?id={item.Id}&lv=-1&kv=-1&tv=-1",token);
            string original=Nested(json.RootElement,"lrc");
            if(string.IsNullOrWhiteSpace(original))return null;
            bool instrumental=NetEaseRecording.IsExplicitInstrumental(original);
            return new(item.Id,item.Track,instrumental?"":original,Nested(json.RootElement,"tlyric"),$"https://music.163.com/#/song?id={item.Id}")
            {
                Instrumental=instrumental,
                InstrumentalEvidence=instrumental?original:null,
                CatalogAlbumId=song.AlbumId,
                ReleaseEvidence=song.ReleaseEvidence
            };
        }
        catch(LyricsProviderException ex) when(ex.StatusCode==404){return null;}
    }

    async Task<IReadOnlyList<CatalogSong>> SearchSongsAsync(string query,CancellationToken token)
    {
        using var json=await GetAsync("api/search/get?s="+Uri.EscapeDataString(query)+"&type=1&limit=30",token);
        return json.RootElement.TryGetProperty("result",out var result)?ParseSongs(result):[];
    }

    static IReadOnlyList<CatalogSong> ParseSongs(JsonElement result,int limit=30)
    {
        if(result.ValueKind!=JsonValueKind.Object || !result.TryGetProperty("songs",out var songs) || songs.ValueKind!=JsonValueKind.Array)return [];
        var candidates=new List<CatalogSong>();
        foreach(var song in songs.EnumerateArray().Take(limit))
        {
            if(Id(song) is not {} id)continue;
            string title=Text(song,"name");
            if(title.Length==0)continue;
            string artist=song.TryGetProperty("artists",out var ar)&&ar.ValueKind==JsonValueKind.Array?string.Join(" & ",ar.EnumerateArray().Select(a=>Text(a,"name"))):"";
            song.TryGetProperty("album",out var album);
            double? duration=song.TryGetProperty("duration",out var d)&&d.ValueKind==JsonValueKind.Number&&d.TryGetDouble(out var ms)?ms:null;
            var albumId=Id(album);
            var candidate=new LyricsCandidate(id,new(title,artist,Text(album,"name"),duration),null,null,false){ProviderName="NetEase"};
            candidates.Add(new(candidate,albumId,Integer(song,"disc"),Integer(song,"position")));
        }
        return candidates;
    }
    static long? Id(JsonElement entry)=>entry.ValueKind==JsonValueKind.Object && entry.TryGetProperty("id",out var id)
        && id.ValueKind==JsonValueKind.Number && id.TryGetInt64(out var number) && number>0?number:null;
    static int Integer(JsonElement entry,string key)
    {
        if(!entry.TryGetProperty(key,out var value))return 0;
        if(value.ValueKind==JsonValueKind.Number && value.TryGetInt32(out int number))return number;
        return value.ValueKind==JsonValueKind.String && int.TryParse(value.GetString(),out number)?number:0;
    }
    static string Text(JsonElement e,string name)=>e.ValueKind==JsonValueKind.Object && e.TryGetProperty(name,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString()??"":"";
    static string Nested(JsonElement e,string name)=>e.TryGetProperty(name,out var child)?Text(child,"lyric"):"";
    void CheckCode(JsonElement e)
    {
        if(e.ValueKind!=JsonValueKind.Object || !e.TryGetProperty("code",out var code)||code.ValueKind!=JsonValueKind.Number||!code.TryGetInt32(out var n))throw new LyricsProviderException("中文译文来源返回格式异常，未保存内容。");
        if(n is 405 or 429)
        {
            next=DateTimeOffset.UtcNow.AddMinutes(1);
            Interlocked.Exchange(ref rateRetryTicks,next.UtcTicks);
        }
        if(n!=200)throw new LyricsProviderException($"歌词与译文来源未返回成功结果（{n}），未保存内容。",n);
    }
    internal void ClearSessionCache()
    {
        // Swap the state so a UI clear can finish while a request owns a gate.
        Interlocked.Exchange(ref sessionCache,new());
        appleCatalog?.ClearCache();
    }
    public void Dispose(){client.Dispose();appleCatalog?.Dispose();} // A cancelled request can still release its gate.
}
