using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using LumaLyrics.Core;

namespace LumaLyrics.Infrastructure;

public interface IOfflineLyricTiming
{
    bool Ready {get;}
    long DownloadBytes {get;}
    Task PrepareAsync(IProgress<(long Done,long Total)>? progress,CancellationToken token);
    Task<IReadOnlyList<LyricTimingSpan?>> AlignAsync(LyricTimingAudio audio,string[] lines,CancellationToken token);
}

/// <summary>Optional, pinned inference files outside the software and personal library.</summary>
public sealed class OfflineLyricTiming : IOfflineLyricTiming
{
    const string Worker="lyric-timing-worker.py";
    const string RuntimeError="离线打轴组件未能运行。可以继续手动打轴。";
    static readonly ArgosLock manifest=JsonSerializer.Deserialize<ArgosLock>(ArgosInstallation.Resource("lyric-timing-lock.json"))!;
    static readonly SemaphoreSlim inference=new(1,1);
    readonly string root,installed;
    public OfflineLyricTiming(string? toolsDirectory=null)
    {
        root=Path.GetFullPath(toolsDirectory??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LumaLyricsTools","lyric-timing"));
        installed=Path.Combine(root,manifest.Version);
    }
    public long DownloadBytes=>manifest.Components.Sum(p=>p.Size);
    public bool Ready
    {
        get
        {
            try
            {
                return File.Exists(Path.Combine(installed,"installed")) && File.Exists(Path.Combine(installed,"python.exe")) && File.Exists(Path.Combine(installed,Worker))
                    && manifest.Components.Where(c=>c.Target=="model").All(c=>new FileInfo(Path.Combine(installed,"model",c.Name)) is {Exists:true} f && f.Length==c.Size);
            }
            catch(IOException){return false;}
            catch(UnauthorizedAccessException){return false;}
        }
    }

    public async Task PrepareAsync(IProgress<(long Done,long Total)>? progress,CancellationToken token)
    {
        if(!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture!=Architecture.X64)throw new InvalidDataException("离线打轴组件仅支持 Windows x64。");
        if(Ready)return;
        Directory.CreateDirectory(root);
        using var lease=new FileStream(Path.Combine(root,"prepare.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        if(Ready)return;
        var drive=new DriveInfo(Path.GetPathRoot(root)!);
        if(drive.AvailableFreeSpace<6_000_000_000L)throw new InvalidDataException("请为离线打轴组件预留至少 6 GB 可用空间。");
        using var client=new HttpClient{Timeout=Timeout.InfiniteTimeSpan};
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LumaLyrics/1.2.0");
        long progressDone=0,total=DownloadBytes;
        // A cancelled download can be resumed; completed assets are extracted only after SHA-256 verification.
        string downloads=Path.Combine(root,"downloads"), staging=ArgosInstallation.SafePath(root,"setup-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(downloads);Directory.CreateDirectory(staging);
        try
        {
            foreach(var item in manifest.Components)
            {
                token.ThrowIfCancellationRequested();
                string bytes=ArgosInstallation.SafePath(downloads,item.Name+".part");
                if(!await ArgosInstallation.VerifyComponentAsync(bytes,item,token).ConfigureAwait(false))
                {
                    long have=File.Exists(bytes)?new FileInfo(bytes).Length:0;
                    if(have>=item.Size){File.Delete(bytes);have=0;}
                    var attempt=new Uri(item.Url);
                    if(attempt.Scheme!="https")throw new InvalidDataException(RuntimeError);
                    Action<long> moved=n=>progress?.Report((progressDone+n,total));
                    if(item.Size>=512L*1024*1024 && attempt.Host=="huggingface.co")
                        await FetchRangesAsync(client,attempt,bytes,item.Size,have,moved,token).ConfigureAwait(false);
                    else
                        await FetchSequentialAsync(client,attempt,bytes,item.Size,have,moved,token).ConfigureAwait(false);
                    if(!await ArgosInstallation.VerifyComponentAsync(bytes,item,token).ConfigureAwait(false))
                    {
                        if(File.Exists(bytes)&&new FileInfo(bytes).Length>=item.Size)File.Delete(bytes);
                        throw new InvalidDataException("离线组件的下载校验失败。请重试。");
                    }
                }
                if(item.Target=="model")
                {
                    string model=Path.Combine(staging,"model");Directory.CreateDirectory(model);
                    File.Copy(bytes,ArgosInstallation.SafePath(model,item.Name));
                }
                else
                {
                    await Task.Run(()=>ArgosInstallation.Extract(bytes,item.Target=="site"?Path.Combine(staging,"Lib","site-packages"):staging,item.Target=="site",
                        item.Name.StartsWith("torch-",StringComparison.Ordinal)?path=>path.EndsWith(".lib",StringComparison.OrdinalIgnoreCase) || path.StartsWith("torch/include/",StringComparison.Ordinal):null,token),token).ConfigureAwait(false);
                }
                progressDone+=item.Size;
                progress?.Report((progressDone,total));
            }
            await File.WriteAllTextAsync(Path.Combine(staging,"python312._pth"),"python312.zip\n.\nLib/site-packages\nimport site\n",token).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(staging,Worker),ArgosInstallation.Resource(Worker),token).ConfigureAwait(false);
            // The NumPy wheel carries the local MSVC runtime needed by the Python extension loaders.
            string? cpp=Directory.EnumerateFiles(Path.Combine(staging,"Lib","site-packages","numpy.libs"),"msvcp140-*.dll").FirstOrDefault();
            if(cpp is not null && !File.Exists(Path.Combine(staging,"msvcp140.dll")))File.Copy(cpp,Path.Combine(staging,"msvcp140.dll"));
            await File.WriteAllTextAsync(Path.Combine(staging,"installed"),manifest.Version,token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if(Directory.Exists(installed))Directory.Move(installed,ArgosInstallation.SafePath(root,"incomplete-"+Guid.NewGuid().ToString("N")));
            Directory.Move(staging,installed);
            foreach(var item in manifest.Components)
            {
                try{File.Delete(ArgosInstallation.SafePath(downloads,item.Name+".part"));}
                catch(IOException){}
                catch(UnauthorizedAccessException){}
            }
        }
        finally
        {
            if(Directory.Exists(staging) && (File.GetAttributes(staging)&FileAttributes.ReparsePoint)==0)
                Directory.Delete(staging,true);
        }
    }

    static async Task FetchSequentialAsync(HttpClient client,Uri address,string partialFile,long size,long have,Action<long> progress,CancellationToken token)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,address);
        if(have>0)request.Headers.Range=new RangeHeaderValue(have,null);
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(25));
        try
        {
            using var reply=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token).ConfigureAwait(false);
            CheckResponse(reply);
            bool partial=reply.StatusCode==HttpStatusCode.PartialContent;
            if(partial&&(reply.Content.Headers.ContentRange?.From!=have || reply.Content.Headers.ContentRange.Length!=size))
                throw new InvalidDataException("离线组件的下载内容不完整。请重试。");
            if(!partial)have=0;
            await using var output=new FileStream(partialFile,partial?FileMode.Append:FileMode.Create,FileAccess.Write,FileShare.None,65536,true);
            await ReadDownloadAsync(reply,output,size-have,n=>progress(have+n),deadline.Token).ConfigureAwait(false);
        }
        catch(OperationCanceledException) when(!token.IsCancellationRequested){throw new IOException("Download timed out.");}
    }

    static async Task FetchRangesAsync(HttpClient client,Uri address,string partialFile,long size,long have,Action<long> progress,CancellationToken token)
    {
        const int block=12*1024*1024,workers=6;
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(60));
        await using var output=new FileStream(partialFile,have>0?FileMode.Append:FileMode.Create,FileAccess.Write,FileShare.None,65536,true);
        try
        {
            for(long next=have;next<size;)
            {
                long baseline=next,batchReceived=0;
                var pending=new List<Task<MemoryStream>>(workers);
                for(int i=0;i<workers && next<size;i++)
                {
                    long start=next,end=Math.Min(size-1,next+block-1);
                    pending.Add(ReadPart(start,end));
                    next=end+1;
                }
                try
                {
                    foreach(var part in pending)
                    {
                        var data=await part.ConfigureAwait(false);
                        await data.CopyToAsync(output,65536,deadline.Token).ConfigureAwait(false);
                    }
                    await output.FlushAsync(deadline.Token).ConfigureAwait(false);
                }
                finally
                {
                    try{await Task.WhenAll(pending).ConfigureAwait(false);}catch(Exception e) when(e is IOException or HttpRequestException or OperationCanceledException or InvalidDataException){}
                    foreach(var part in pending)if(part.IsCompletedSuccessfully)part.Result.Dispose();
                }

                async Task<MemoryStream> ReadPart(long start,long end)
                {
                    for(int retry=0;;retry++)
                    {
                        using var request=new HttpRequestMessage(HttpMethod.Get,address);
                        request.Headers.Range=new RangeHeaderValue(start,end);
                        using var limit=CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
                        limit.CancelAfter(TimeSpan.FromMinutes(4));
                        var buffer=new MemoryStream(checked((int)(end-start+1)));
                        long previous=0;
                        try
                        {
                            using var reply=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,limit.Token).ConfigureAwait(false);
                            CheckResponse(reply);
                            var range=reply.Content.Headers.ContentRange;
                            if(reply.StatusCode!=HttpStatusCode.PartialContent || range?.From!=start || range.To!=end || range.Length!=size)
                                throw new InvalidDataException("离线组件的下载内容不完整。请重试。");
                            await ReadDownloadAsync(reply,buffer,end-start+1,n=>
                            {progress(baseline+Interlocked.Add(ref batchReceived,n-previous));previous=n;},limit.Token).ConfigureAwait(false);
                            buffer.Position=0;return buffer;
                        }
                        catch(Exception e) when(e is IOException or HttpRequestException or OperationCanceledException)
                        {
                            Interlocked.Add(ref batchReceived,-previous);
                            buffer.Dispose();if(retry>=1 || deadline.IsCancellationRequested)throw;
                        }
                        catch{buffer.Dispose();throw;}
                    }
                }
            }
        }
        catch(OperationCanceledException) when(!token.IsCancellationRequested){throw new IOException("Download timed out.");}
    }

    static void CheckResponse(HttpResponseMessage reply)
    {
        reply.EnsureSuccessStatusCode();
        if(reply.RequestMessage?.RequestUri?.Scheme!="https")throw new InvalidDataException(RuntimeError);
    }

    static async Task ReadDownloadAsync(HttpResponseMessage reply,Stream output,long total,Action<long> progress,CancellationToken token)
    {
        await using var input=await reply.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        byte[] buffer=new byte[65536];long length=0,last=0;
        using var readTimeout=CancellationTokenSource.CreateLinkedTokenSource(token);
        while(true)
        {
            readTimeout.CancelAfter(TimeSpan.FromSeconds(45));
            int n=await input.ReadAsync(buffer,readTimeout.Token).ConfigureAwait(false);
            if(n==0)break;
            length+=n;
            if(length>total)throw new InvalidDataException("离线组件的下载校验失败。请重试。");
            await output.WriteAsync(buffer.AsMemory(0,n),token).ConfigureAwait(false);
            if(length-last>=1024*1024){last=length;progress(length);}
        }
        if(length!=total)throw new IOException("Incomplete download.");
        progress(total);
    }

    public async Task<IReadOnlyList<LyricTimingSpan?>> AlignAsync(LyricTimingAudio audio,string[] lines,CancellationToken token)
    {
        if(!Ready || !double.IsFinite(audio.StartMs) || audio.StartMs<0 || audio.Count>audio.Samples.Length
            || audio.Count is <16000 or >LyricTimingAudio.SampleRate*LyricTimingAudio.MaximumSeconds || lines.Length is <1 or >1000)
            throw new InvalidDataException(RuntimeError);
        await inference.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var lease=new FileStream(Path.Combine(root,"analysis.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            return await RunAsync(audio,lines,token).ConfigureAwait(false);
        }
        finally{inference.Release();}
    }

    async Task<IReadOnlyList<LyricTimingSpan?>> RunAsync(LyricTimingAudio audio,string[] lines,CancellationToken token)
    {
        var info=new ProcessStartInfo(Path.Combine(installed,"python.exe"))
        {WorkingDirectory=installed,UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,
            StandardInputEncoding=new UTF8Encoding(false),StandardOutputEncoding=Encoding.UTF8};
        info.ArgumentList.Add("-I");info.ArgumentList.Add(Path.Combine(installed,Worker));info.ArgumentList.Add(Path.Combine(installed,"model"));
        info.Environment["HF_HUB_OFFLINE"]="1";info.Environment["TRANSFORMERS_OFFLINE"]="1";
        info.Environment["USE_TF"]="0";info.Environment["USE_FLAX"]="0";
        using var process=Process.Start(info)??throw new InvalidDataException(RuntimeError);
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(12));
        try
        {
            var output=ReadOutputAsync(process.StandardOutput,deadline.Token);
            var errors=process.StandardError.BaseStream.CopyToAsync(Stream.Null,deadline.Token);
            var header=JsonSerializer.SerializeToUtf8Bytes(new{Count=audio.Count,Lines=lines});
            if(header.Length>512*1024)throw new InvalidDataException("打轴原词超过 128 KB。");
            var size=new byte[4];BinaryPrimitives.WriteInt32LittleEndian(size,header.Length);
            var input=process.StandardInput.BaseStream;
            await input.WriteAsync(size,deadline.Token).ConfigureAwait(false);
            await input.WriteAsync(header,deadline.Token).ConfigureAwait(false);
            byte[] chunk=new byte[65536];
            try
            {
                for(int i=0;i<audio.Count;)
                {
                    int frames=Math.Min(audio.Count-i,chunk.Length/2);
                    Buffer.BlockCopy(audio.Samples,i*2,chunk,0,frames*2);
                    await input.WriteAsync(chunk.AsMemory(0,frames*2),deadline.Token).ConfigureAwait(false);
                    i+=frames;
                }
            }
            finally{Array.Clear(chunk);}
            process.StandardInput.Close();
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            await errors.ConfigureAwait(false);
            var reply=JsonSerializer.Deserialize<WorkerReply>(await output.ConfigureAwait(false));
            if(reply?.Error=="size")throw new InvalidDataException("歌词和录音过长，无法自动打轴。请手动打轴。");
            if(process.ExitCode!=0 || reply?.Spans?.Length!=lines.Length)throw new InvalidDataException(RuntimeError);
            int At(int ms)=>checked((int)Math.Round(audio.StartMs+ms));
            return reply.Spans.Select(s=>s is {} span && span.StartMs>=0 && span.StartMs<audio.LengthMs
                && (span.EndMs is null || span.EndMs>span.StartMs && span.EndMs<=audio.LengthMs+1)
                ?new LyricTimingSpan(At(span.StartMs),span.EndMs is {} end?At(end):null):(LyricTimingSpan?)null).ToArray();
        }
        catch(JsonException){throw new InvalidDataException(RuntimeError);}
        catch(OperationCanceledException) when(!token.IsCancellationRequested){throw new InvalidDataException("离线分析超时。可继续手动打轴。");}
        finally
        {
            if(!process.HasExited)
            {
                try{process.Kill(entireProcessTree:true);}catch(InvalidOperationException){}
                using var cleanup=new CancellationTokenSource(TimeSpan.FromSeconds(3));
                try{await process.WaitForExitAsync(cleanup.Token).ConfigureAwait(false);}catch(OperationCanceledException){}
            }
        }
    }

    static async Task<string> ReadOutputAsync(StreamReader reader,CancellationToken token)
    {
        var output=new StringBuilder();char[] buffer=new char[2048];
        while(true)
        {
            int n=await reader.ReadAsync(buffer,token).ConfigureAwait(false);
            if(n==0)return output.ToString();
            if(output.Length+n>256*1024)throw new InvalidDataException(RuntimeError);
            output.Append(buffer,0,n);
        }
    }
    sealed record WorkerReply(LyricTimingSpan?[]? Spans,string? Error);
}
