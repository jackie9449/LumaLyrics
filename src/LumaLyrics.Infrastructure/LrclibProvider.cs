using System.Net;
using System.Text.Json;
using LumaLyrics.Core;
namespace LumaLyrics.Infrastructure;

public sealed class LyricsProviderException(string message,int? statusCode=null) : Exception(message){public int? StatusCode {get;}=statusCode;}
public sealed class LrclibProvider : ILyricsProvider, IDisposable
{
    readonly HttpClient client;
    readonly SemaphoreSlim gate = new(1);
    readonly bool searchOnly;readonly TimeSpan minimumInterval;
    long searchRetryTicks,rateRetryTicks;
    DateTimeOffset nextRequest;
    public LrclibProvider(HttpMessageHandler? handler = null,bool searchOnly=false,TimeSpan? minimumInterval=null)
    {
        this.searchOnly=searchOnly;this.minimumInterval=minimumInterval??TimeSpan.FromSeconds(1);
        client = handler is null ? new() : new(handler);
        client.BaseAddress = new("https://lrclib.net/api/"); client.Timeout = TimeSpan.FromSeconds(15);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LumaLyrics/0.15.0 (https://github.com/jackie9449/LumaLyrics)");
    }
    async Task<string?> GetAsync(string url, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            bool search=url.StartsWith("search?",StringComparison.Ordinal);
            if(DateTimeOffset.UtcNow.UtcTicks<Interlocked.Read(ref rateRetryTicks))throw new LyricsProviderException("LRCLIB 仍处于来源要求的限流等待期，继续查询其他来源。",429);
            if(search && DateTimeOffset.UtcNow.UtcTicks<Interlocked.Read(ref searchRetryTicks))throw new LyricsProviderException("LRCLIB 搜索暂时繁忙，精确匹配和其他来源仍会继续查询。",503);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                var delay = nextRequest - DateTimeOffset.UtcNow; if (delay > TimeSpan.Zero) await Task.Delay(delay, token);
                try
                {
                    using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
                    nextRequest = DateTimeOffset.UtcNow+minimumInterval;
                    if (response.StatusCode == HttpStatusCode.NotFound) return null;
                    if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        var retry = response.Headers.RetryAfter;
                        nextRequest = (retry?.Date ?? DateTimeOffset.UtcNow + (retry?.Delta ?? TimeSpan.FromSeconds(30))).AddMilliseconds(Random.Shared.Next(20,250));
                        Interlocked.Exchange(ref rateRetryTicks,nextRequest.UtcTicks);token.ThrowIfCancellationRequested();
                        throw new LyricsProviderException("歌词服务暂时限流，请稍后重试。",429);
                    }
                    if ((int)response.StatusCode >= 500)
                    {
                        var retry=response.Headers.RetryAfter;nextRequest=retry?.Date??DateTimeOffset.UtcNow+(retry?.Delta??TimeSpan.FromSeconds(2));
                        if(search){Interlocked.Exchange(ref searchRetryTicks,DateTimeOffset.UtcNow.AddSeconds(Math.Max(30,(nextRequest-DateTimeOffset.UtcNow).TotalSeconds)).UtcTicks);throw new LyricsProviderException("LRCLIB 搜索暂时繁忙，精确匹配和其他来源仍会继续查询。",(int)response.StatusCode);}
                        if(attempt==0)continue;
                    }
                    if (!response.IsSuccessStatusCode) throw new LyricsProviderException($"歌词服务错误（{(int)response.StatusCode}）。",(int)response.StatusCode);
                    if (response.Content.Headers.ContentLength > 2 * 1024 * 1024) throw new LyricsProviderException("歌词响应超过 2 MB。");
                    using var stream = await response.Content.ReadAsStreamAsync(token);
                    using var output = new MemoryStream(); var buffer = new byte[8192]; int count;
                    while ((count = await stream.ReadAsync(buffer, token)) > 0) { if (output.Length + count > 2 * 1024 * 1024) throw new LyricsProviderException("歌词响应超过 2 MB。"); await output.WriteAsync(buffer.AsMemory(0, count), token); }
                    return System.Text.Encoding.UTF8.GetString(output.ToArray());
                }
                catch (HttpRequestException) when (attempt == 0) { nextRequest = DateTimeOffset.UtcNow.AddSeconds(2); }
                catch (TaskCanceledException) when (!token.IsCancellationRequested) { throw new LyricsProviderException("歌词查询超时，可手动重试。"); }
            }
            throw new LyricsProviderException("无法连接歌词服务，请检查网络。");
        }
        catch (HttpRequestException) { throw new LyricsProviderException("无法连接歌词服务，请检查网络。"); }
        finally { gate.Release(); }
    }
    internal static LyricsCandidate Parse(JsonElement e)
    {
        string? S(string key) => e.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
        double? d = e.TryGetProperty("duration", out var dp) && dp.ValueKind==JsonValueKind.Number && dp.TryGetDouble(out var v) ? v * 1000 : null;
        long id = e.TryGetProperty("id", out var ip) && ip.ValueKind==JsonValueKind.Number && ip.TryGetInt64(out var n) ? n : 0;
        bool instrumental = e.TryGetProperty("instrumental", out var b) && b.ValueKind == JsonValueKind.True;
        return new(id, new(S("trackName") ?? "", S("artistName") ?? "", S("albumName") ?? "", d), S("syncedLyrics"), S("plainLyrics"), instrumental, e.GetRawText());
    }
    // An artist search is a ranked page, never proof that another recording is absent.
    // Batch and foreground lookup therefore use the same exact recording signature.
    public Task<IReadOnlyList<LyricsCandidate>> LookupLibraryAsync(Track input,CancellationToken token)=>LookupAsync(input,token);
    public async Task<IReadOnlyList<LyricsCandidate>> LookupAsync(Track track, CancellationToken token)
    {
        track=TrackMetadata.FromAppleSession(track);
        static string E(string s) => Uri.EscapeDataString(s);
        try
        {
            var all = new List<LyricsCandidate>();var unavailable=new List<string>();
            if (!searchOnly && track.DurationMs.HasValue)
            {
                string? raw=null;
                try{raw = await GetAsync($"get?track_name={E(track.Title)}&artist_name={E(track.Artist)}&album_name={E(track.Album)}&duration={(track.DurationMs.Value / 1000).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}", token);}
                catch(LyricsProviderException e) when(e.StatusCode>=500){unavailable.Add($"LRCLIB 精确匹配（{e.StatusCode}）");} // Search can remain available when exact lookup fails.
                if (raw is not null) { using var doc = JsonDocument.Parse(raw); all.Add(Parse(doc.RootElement)); }
                if (AutomaticLyricsMatcher.Select(track, all) is {Candidate.Instrumental:false}) return all;
            }
            string? search;
            try{search=await GetAsync($"search?track_name={E(track.Title)}&artist_name={E(track.Artist)}", token);}
            catch(LyricsProviderException ex) when(ex.StatusCode>=500 && all.Count>0){return new LyricsLookupResult(all,unavailable.Append($"LRCLIB 搜索（{ex.StatusCode}）"));}
            if (search is not null) { using var doc = JsonDocument.Parse(search); foreach (var e in doc.RootElement.EnumerateArray()) all.Add(Parse(e)); }
            // Observed Apple Music 1.1538: Artist="artist — album", AlbumTitle="".
            // Supplement search only. Scoring keeps the original metadata, requiring
            // manual confirmation rather than promoting an inferred artist to certainty.
            int separator=track.Artist.IndexOf(" — ",StringComparison.Ordinal);
            if(all.Count==0 && track.Album.Length==0 && separator>0)
            {
                var supplement=await GetAsync($"search?track_name={E(track.Title)}&artist_name={E(track.Artist[..separator])}",token);
                if(supplement is not null){using var doc=JsonDocument.Parse(supplement);foreach(var e in doc.RootElement.EnumerateArray())all.Add(Parse(e));}
            }
            var filtered=all.Where(x => x.Instrumental || !string.IsNullOrWhiteSpace(x.Content)).DistinctBy(x => x.Id).Take(100).ToArray();
            return unavailable.Count>0?new LyricsLookupResult(filtered,unavailable):filtered;
        }
        catch (JsonException) { throw new LyricsProviderException("歌词服务返回了无法解析的内容。"); }
        catch (InvalidOperationException) { throw new LyricsProviderException("歌词服务返回的数据格式无效。"); }
    }
    public void Dispose() { client.Dispose(); } // In-flight cancellation may still release the gate.
}

/// <summary>Compatibility adapter sharing the foreground limiter and exact recording lookup.</summary>
public sealed class LibraryLyricsProvider(LrclibProvider provider):ILyricsProvider
{public Task<IReadOnlyList<LyricsCandidate>> LookupAsync(Track track,CancellationToken token)=>provider.LookupLibraryAsync(track,token);}
