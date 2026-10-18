using LumaLyrics.Core;
namespace LumaLyrics.Infrastructure;
public sealed record LibraryQueueItem(string Source,string PersistentId,Track Track);
public sealed record LibraryProgress(int Total,int Cached,int Unavailable,int Errors,bool Running,string Message)
{
    public override string ToString()=>$"资料库 {Total} 首 · 已缓存 {Cached} · 暂无可靠歌词 {Unavailable} · 待重试 {Errors}\n{Message}";
}
public sealed class LibraryPrefetcher(string databasePath,ILyricsProvider provider,Func<bool>? foregroundBusy=null,TimeSpan? pacing=null)
{
    public static string PausePath(string databasePath)=>Path.Combine(Path.GetDirectoryName(Path.GetFullPath(databasePath))!,"library-prefetch.pause");
    public static string ProgressPath(string databasePath)=>Path.Combine(Path.GetDirectoryName(Path.GetFullPath(databasePath))!,"library-prefetch-progress.json");
    void Report(LibraryProgress progress)
    {
        var path=ProgressPath(databasePath);var temp=path+".tmp";
        File.WriteAllText(temp,System.Text.Json.JsonSerializer.Serialize(progress));File.Move(temp,path,true);Progress?.Invoke(progress);
    }
    public event Action<LibraryProgress>? Progress;
    public event Action<Track>? Cached;
    public async Task RunAsync(string libraryPath,CancellationToken token,int? maximum=null)
    {
        using var repository=new LyricsRepository(databasePath);
        FileStream lease;
        try{lease=new FileStream(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(databasePath))!,"library-prefetch.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
        catch(IOException){Progress?.Invoke(repository.GetLibraryProgress("其他实例正在后台预缓存，本实例避免重复请求",true));return;}
        using var heldLease=lease;
        try
        {
            var snapshot=await Task.Run(()=>new AppleMusicLibraryReader().Read(libraryPath),token);
            repository.SyncLibrary(snapshot);Report(repository.GetLibraryProgress("已读取完整资料库，后台开始预缓存",true));
            int processed=0,consecutiveErrors=0;
            foreach(var item in repository.GetLibraryQueue())
            {
                token.ThrowIfCancellationRequested();if(maximum.HasValue && processed>=maximum)break;
                if(File.Exists(PausePath(databasePath))){Report(repository.GetLibraryProgress("已暂停，下次从本地记录继续",false));return;}
                var cached=repository.Find(item.Track);
                if(cached is not null && (cached.Manual || QualityOf(cached,item.Track)))
                {repository.SetLibraryState(item,"Cached","已有本地歌词");continue;}
                while(foregroundBusy?.Invoke()==true){token.ThrowIfCancellationRequested();await Task.Delay(250,token);}
                try
                {
                    var candidates=await provider.LookupAsync(item.Track,token);
                    consecutiveErrors=0;
                    var selected=AutomaticLyricsMatcher.Select(item.Track,candidates);
                    token.ThrowIfCancellationRequested();
                    if(File.Exists(PausePath(databasePath))){Report(repository.GetLibraryProgress("已暂停，下次从本地记录继续",false));return;}
                    if(selected is not null)
                    {
                        repository.Save(item.Track,selected.Candidate.Content,$"LRCLIB #{selected.Candidate.Id} · 自动检查",false,selected.Candidate.Instrumental,providerJson:selected.Candidate.RawJson);
                        repository.SetLibraryState(item,"Cached",selected.Assessment.Reason);Cached?.Invoke(item.Track);
                    }
                    else repository.SetLibraryState(item,candidates.Count==0?"Missing":"Incomplete",candidates.Count==0?"来源无结果":"没有同时通过版本和内容检查的候选");
                }
                catch(OperationCanceledException){throw;}
                catch(Exception e) when(e is LyricsProviderException or HttpRequestException){repository.SetLibraryState(item,"Error",e.Message);consecutiveErrors++;}
                processed++;Report(repository.GetLibraryProgress($"本轮已处理 {processed} 首；播放查词优先",true));
                if(consecutiveErrors>=4){Report(repository.GetLibraryProgress("歌词服务连续失败，暂停本轮并稍后自动重试",false));return;}
                await Task.Delay(pacing??TimeSpan.FromMilliseconds(200),token);
            }
            Report(repository.GetLibraryProgress("本轮完成；未找到及网络失败项目稍后自动重试",false));
        }
        catch(OperationCanceledException){Report(repository.GetLibraryProgress("已暂停，下次从本地记录继续",false));}
    }
    public static bool QualityOf(LyricsBinding binding,Track track)=>AutomaticLyricsMatcher.Assess(new(0,track,binding.Plain?null:binding.Raw,binding.Plain?binding.Raw:null,binding.Instrumental)).Accepted;
}
