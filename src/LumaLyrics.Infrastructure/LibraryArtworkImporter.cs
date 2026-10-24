using System.Net;
using System.Text.Json;
using LumaLyrics.Core;
namespace LumaLyrics.Infrastructure;

public sealed class LibraryArtworkImporter : IDisposable
{
    readonly string root;readonly HttpClient client;readonly TimeSpan pacing;
    public LibraryArtworkImporter(string root,HttpMessageHandler? handler=null,TimeSpan? pacing=null)
    {
        this.root=root;this.pacing=pacing??TimeSpan.FromMilliseconds(100);
        client=new(handler??new HttpClientHandler{AllowAutoRedirect=false,AutomaticDecompression=DecompressionMethods.All}){Timeout=TimeSpan.FromSeconds(20)};
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LumaLyrics/0.5.0");
    }
    public static string ProgressPath(string root)=>Path.Combine(root,"artwork-prefetch-progress.json");
    public static bool IsRunning(string root)=>LibraryPrefetcher.LeaseHeld(Path.Combine(root,"artwork-prefetch.lock"));
    public event Action<ArtworkProgress>? Progress;
    public event Action<Track>? Cached;
    public async Task RunAsync(string libraryPath,CancellationToken token,int? maximum=null)
    {
        var snapshot=await Task.Run(()=>new AppleMusicLibraryReader().Read(libraryPath),token);
        await RunAsync(snapshot,token,maximum);
    }
    public async Task RunAsync(LibrarySnapshot snapshot,CancellationToken token,int? maximum=null,IReadOnlyDictionary<string,AppleArtworkSource>? sources=null)
    {
        Directory.CreateDirectory(root);using var cache=new ArtworkCache(root);FileStream lease;
        try{lease=new(Path.Combine(root,"artwork-prefetch.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
        catch(IOException){Progress?.Invoke(cache.GetProgress("其他实例正在导入封面",true));return;}
        using var held=lease;
        using var pause=new LibraryPauseMonitor(Path.Combine(root,"lyrics.db"),token);token=pause.Token;
        void Report(string message,bool running)
        {
            var progress=cache.GetProgress(message,running);
            PreparationProgressFile.Write(ProgressPath(root),JsonSerializer.Serialize(progress));Progress?.Invoke(progress);
        }
        bool Paused()=>File.Exists(LibraryPrefetcher.PausePath(Path.Combine(root,"lyrics.db")));
        try
        {
            token.ThrowIfCancellationRequested();sources??=new AppleMusicArtworkReader().Read(snapshot.Path);cache.Sync(snapshot,sources);
            Report("已按 Apple Music 持久 ID 读取封面索引",true);int processed=0;var attempted=new HashSet<string>();
            foreach(var item in cache.GetQueue())
            {
                token.ThrowIfCancellationRequested();if(Paused() || maximum.HasValue&&processed>=maximum)break;
                if(!attempted.Add(item.ImageKey))continue;
                var existing=cache.Read(item.ImageKey);
                if(existing?.HighResolution==true){cache.MarkCached(item.ImageKey,true);Report("复用已验证的专辑封面",true);continue;}
                try
                {
                    byte[]? bytes=null;
                    if(item.RemoteUri is not null && AppleMusicArtworkReader.IsAppleImage(item.RemoteUri))
                    {
                        var requestUri=existing is not null && !existing.HighResolution?AppleMusicArtworkReader.HighResolutionUri(item.RemoteUri.AbsoluteUri,2,2400)??item.RemoteUri:item.RemoteUri;
                        using var requestTimeout=CancellationTokenSource.CreateLinkedTokenSource(token);requestTimeout.CancelAfter(TimeSpan.FromSeconds(20));
                        using var response=await client.GetAsync(requestUri,HttpCompletionOption.ResponseHeadersRead,requestTimeout.Token);
                        if(response.StatusCode==HttpStatusCode.TooManyRequests)
                        {
                            var retry=response.Headers.RetryAfter;var delay=retry?.Delta??(retry?.Date-DateTimeOffset.UtcNow)??TimeSpan.FromSeconds(30);
                            Report("图片服务限流，等待后继续",true);
                            if(delay>TimeSpan.Zero)await Task.Delay(delay,token);
                            throw new HttpRequestException("图片服务限流，稍后重试");
                        }
                        if(response.IsSuccessStatusCode)
                        {
                            if(response.Content.Headers.ContentLength>ImageDimensions.MaximumBytes)throw new InvalidDataException("封面超过 15 MB。");
                            using var stream=await response.Content.ReadAsStreamAsync(requestTimeout.Token);using var memory=new MemoryStream();var buffer=new byte[16384];int n;
                            while((n=await stream.ReadAsync(buffer,requestTimeout.Token))>0){if(memory.Length+n>ImageDimensions.MaximumBytes)throw new InvalidDataException("封面超过 15 MB。");memory.Write(buffer,0,n);}bytes=memory.ToArray();
                            var size=ImageDimensions.Read(bytes);if(existing is not null && (long)existing.Width*existing.Height>(long)size.Width*size.Height)bytes=existing.Bytes;
                        }
                        else if(response.StatusCode!=HttpStatusCode.NotFound)throw new HttpRequestException($"封面服务错误（{(int)response.StatusCode}）");
                    }
                    if(bytes is null && item.LocalPath is not null && File.Exists(item.LocalPath) && new FileInfo(item.LocalPath).Length<=ImageDimensions.MaximumBytes)bytes=await File.ReadAllBytesAsync(item.LocalPath,token);
                    token.ThrowIfCancellationRequested();if(Paused())break;
                    if(bytes is not null){cache.Save(item,bytes);Cached?.Invoke(item.Track);}
                    else cache.SetFailure(item,"Missing","Apple Music 暂无可用封面来源");
                }
                catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}
                catch(Exception e) when(e is HttpRequestException or IOException or InvalidDataException or TaskCanceledException)
                {
                    if(existing is not null)cache.MarkCached(item.ImageKey,existing.HighResolution);
                    else if(item.LocalPath is not null && File.Exists(item.LocalPath) && new FileInfo(item.LocalPath).Length<=ImageDimensions.MaximumBytes)
                    {
                        try{cache.Save(item,await File.ReadAllBytesAsync(item.LocalPath,token));Cached?.Invoke(item.Track);}catch(Exception local) when(local is IOException or InvalidDataException){cache.SetFailure(item,"Error","下载及本地封面验证失败");}
                    }
                    else cache.SetFailure(item,"Error",e is TaskCanceledException?"封面查询超时":e.Message);
                }
                processed++;Report($"本轮已处理 {processed} 首；相同专辑共享图片缓存",true);await Task.Delay(pacing,token);
            }
            Report(Paused()?"已暂停，可继续导入":"本轮封面导入完成；未完成项目保留并稍后重试",false);
        }
        catch(OperationCanceledException){Report("已暂停，封面进度已保存",false);}
    }
    public void Dispose()=>client.Dispose();
}
