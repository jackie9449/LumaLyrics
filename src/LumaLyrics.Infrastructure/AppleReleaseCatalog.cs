using System.Globalization;
using System.Net;
using System.Text.Json;
using LumaLyrics.Core;

namespace LumaLyrics.Infrastructure;

internal sealed record AppleCatalogSong(long Id,long AlbumId,int Disc,int Number,Track Track);
internal sealed record AppleCatalogAlbum(long Id,long ArtistId,string Name,string Artist,int Count,DateOnly? Released,IReadOnlyList<AppleCatalogSong> Songs);
internal sealed record AppleLocalizedRelease(AppleCatalogAlbum Published,AppleCatalogAlbum Japanese);

// Uses the public iTunes Search API. Only metadata is read, including when checking an album.
internal sealed class AppleReleaseCatalog : IDisposable
{
    const int SearchLimit=24;
    const int MaximumTracks=200;
    static readonly RequestLimiter LiveRequests=new(TimeSpan.FromSeconds(3.2));
    readonly RequestLimiter requests;
    readonly HttpClient client;

    internal AppleReleaseCatalog(HttpMessageHandler? handler)
    {
        requests=handler is null?LiveRequests:new(TimeSpan.Zero);
        client=handler is null?new HttpClient():new HttpClient(handler);
        client.BaseAddress=new("https://itunes.apple.com/");
        client.Timeout=TimeSpan.FromSeconds(12);
        client.MaxResponseContentBufferSize=2*1024*1024;
    }

    internal async Task<AppleLocalizedRelease?> FindAsync(Track track,CancellationToken token)
    {
        bool japanese=HasJapaneseText(track.Title) || HasJapaneseText(track.Album) || HasJapaneseText(track.Artist);
        string storefront=japanese?"country=jp&lang=ja_jp":"country=us";
        string songTerm=Uri.EscapeDataString(track.Title+" "+track.Artist);
        using var searchedSongs=await ReadAsync($"search?term={songTerm}&media=music&entity=song&limit={SearchLimit}&{storefront}",token);
        var anchors=Songs(searchedSongs).Where(s=>CatalogRecordingMatch.MatchesLocal(track,s.Track,s.Track))
            .DistinctBy(s=>s.Id).Take(2).ToArray();
        if(anchors.Length>1)return null;

        long albumId;
        if(anchors.Length==1)albumId=anchors[0].AlbumId;
        else
        {
            string albumTerm=Uri.EscapeDataString(track.Album+" "+track.Artist);
            using var searchedAlbums=await ReadAsync($"search?term={albumTerm}&media=music&entity=album&limit={SearchLimit}&{storefront}",token);
            var albums=Headers(searchedAlbums).Where(a=>MatchRules.ArtistCreditsCover(track.Artist,a.Artist,0))
                .DistinctBy(a=>a.Id).ToArray();
            var sameName=albums.Where(a=>MatchRules.CatalogAlbumMatches(track.Album,a.Name)).ToArray();
            var choices=sameName.Length>0?sameName:albums;
            if(choices.Length!=1)return null;
            albumId=choices[0].Id;
        }

        var published=await GetAlbumAsync(albumId,"country=us",token);
        if(published is null)return null;
        var localized=await GetAlbumAsync(albumId,"country=jp&lang=ja_jp",token);
        if(localized is null || published.ArtistId!=localized.ArtistId || published.Count!=localized.Count)
            return null;

        int matched=0;
        for(int i=0;i<published.Count;i++)
        {
            var display=published.Songs[i];
            var actual=localized.Songs[i];
            if(display.Id!=actual.Id || display.Disc!=actual.Disc || display.Number!=actual.Number
                || !CatalogRecordingMatch.PlausibleDuration(display.Track.DurationMs,actual.Track.DurationMs)
                || !MatchRules.SameRecordingVersion(display.Track,actual.Track))return null;
            if(CatalogRecordingMatch.MatchesLocal(track,display.Track,actual.Track))
            {
                if(anchors.Length==1 && display.Id!=anchors[0].Id)return null;
                matched++;
            }
        }
        return matched==1?new(published,localized):null;
    }

    internal static bool HasJapaneseText(string text)
        =>JapaneseKana.HasKana(text) || text.Any(c=>c is >= '\u3400' and <= '\u9fff');

    async Task<AppleCatalogAlbum?> GetAlbumAsync(long id,string storefront,CancellationToken token)
    {
        using var document=await ReadAsync($"lookup?id={id}&entity=song&limit={MaximumTracks}&{storefront}",token);
        var headers=Headers(document).Where(a=>a.Id==id).Take(2).ToArray();
        if(headers.Length!=1)return null;
        var album=headers[0];
        var songs=Songs(document).Where(s=>s.AlbumId==id).OrderBy(s=>s.Disc).ThenBy(s=>s.Number).ToArray();
        if(songs.Length!=album.Count || songs.Select(s=>s.Id).Distinct().Count()!=songs.Length
            || songs.Select(s=>(s.Disc,s.Number)).Distinct().Count()!=songs.Length)return null;
        return album with{Songs=songs};
    }

    static IEnumerable<AppleCatalogAlbum> Headers(JsonDocument document)
    {
        foreach(var row in Rows(document).Where(row=>Text(row,"wrapperType")=="collection"))
        {
            long id=Number(row,"collectionId"),artistId=Number(row,"artistId"),count=Number(row,"trackCount");
            string name=Text(row,"collectionName"),artist=Text(row,"artistName");
            if(id<=0 || artistId<=0 || count is <1 or >MaximumTracks || name.Length==0 || artist.Length==0)continue;
            DateOnly? released=DateTimeOffset.TryParse(Text(row,"releaseDate"),CultureInfo.InvariantCulture,DateTimeStyles.None,out var date)
                ?DateOnly.FromDateTime(date.UtcDateTime):null;
            yield return new(id,artistId,name,artist,(int)count,released,[]);
        }
    }

    static IEnumerable<AppleCatalogSong> Songs(JsonDocument document)
    {
        foreach(var row in Rows(document).Where(row=>Text(row,"kind")=="song"))
        {
            long id=Number(row,"trackId"),albumId=Number(row,"collectionId"),disc=Number(row,"discNumber"),number=Number(row,"trackNumber");
            long ms=Number(row,"trackTimeMillis");
            string title=Text(row,"trackName"),artist=Text(row,"artistName"),album=Text(row,"collectionName");
            if(id<=0 || albumId<=0 || disc is <1 or >int.MaxValue || number is <1 or >int.MaxValue || ms<=0
                || title.Length==0 || artist.Length==0 || album.Length==0)continue;
            yield return new(id,albumId,(int)disc,(int)number,new(title,artist,album,ms));
        }
    }

    static IEnumerable<JsonElement> Rows(JsonDocument document)
    {
        var root=document.RootElement;
        if(root.ValueKind!=JsonValueKind.Object || !root.TryGetProperty("results",out var rows) || rows.ValueKind!=JsonValueKind.Array)
            throw new JsonException("Invalid Apple catalogue response.");
        return rows.EnumerateArray().Where(row=>row.ValueKind==JsonValueKind.Object);
    }
    static string Text(JsonElement row,string key)
        =>row.TryGetProperty(key,out var value) && value.ValueKind==JsonValueKind.String?value.GetString()?.Trim()??"":"";
    static long Number(JsonElement row,string key)
        =>row.TryGetProperty(key,out var value) && value.ValueKind==JsonValueKind.Number && value.TryGetInt64(out var n)?n:0;

    Task<JsonDocument> ReadAsync(string path,CancellationToken token)=>requests.ReadAsync(client,path,token);
    public void Dispose()=>client.Dispose();

    sealed class RequestLimiter(TimeSpan interval)
    {
        readonly SemaphoreSlim gate=new(1,1);
        DateTimeOffset next;

        public async Task<JsonDocument> ReadAsync(HttpClient client,string path,CancellationToken token)
        {
            await gate.WaitAsync(token);
            try
            {
                var delay=next-DateTimeOffset.UtcNow;
                if(delay>TimeSpan.Zero)await Task.Delay(delay,token);
                next=DateTimeOffset.UtcNow+interval;
                using var response=await client.GetAsync(path,token);
                if(response.StatusCode==HttpStatusCode.TooManyRequests)
                    next=response.Headers.RetryAfter?.Date??DateTimeOffset.UtcNow+(response.Headers.RetryAfter?.Delta??TimeSpan.FromMinutes(1));
                response.EnsureSuccessStatusCode();
                return JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            }
            finally{gate.Release();}
        }
    }
}
