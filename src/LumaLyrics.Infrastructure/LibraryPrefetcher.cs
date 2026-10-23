using LumaLyrics.Core;
namespace LumaLyrics.Infrastructure;
public sealed record LibraryQueueItem(string Source,string PersistentId,Track Track);
public sealed record LibraryProgress(int Total,int Cached,int Unavailable,int Errors,bool Running,string Message)
{
    public int Synced {get;init;}
    public int Instrumental {get;init;}
    public int Manual {get;init;}
    public int Pending=>Math.Max(0,Total-Cached-Unavailable-Errors-Manual);
    public override string ToString()=>$"资料库 {Total} 首 · 动态歌词 {Synced} · 纯音乐 {Instrumental} · 手动保留 {Manual}\n待处理 {Pending} · 待补齐 {Unavailable} · 网络待重试 {Errors}\n{Message}";
}
public sealed class LibraryPrefetcher(string databasePath,ILyricsProvider provider,Func<bool>? foregroundBusy=null,TimeSpan? pacing=null)
{
    public static string PausePath(string databasePath)=>Path.Combine(Path.GetDirectoryName(Path.GetFullPath(databasePath))!,"library-prefetch.pause");
    public static string ProgressPath(string databasePath)=>Path.Combine(Path.GetDirectoryName(Path.GetFullPath(databasePath))!,"library-prefetch-progress.json");
    public static bool IsRunning(string databasePath)=>LeaseHeld(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(databasePath))!,"library-prefetch.lock"));
    internal static bool LeaseHeld(string path)
    {
        if(!File.Exists(path))return false;
        try{using var lease=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.None);return false;}
        catch(IOException){return true;}
    }
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
        using var pause=new LibraryPauseMonitor(databasePath,token);token=pause.Token;
        try
        {
            var snapshot=await Task.Run(()=>new AppleMusicLibraryReader().Read(libraryPath),token);
            repository.SyncLibrary(snapshot);
            foreach(var item in repository.GetLibraryQueue(cachedOnly:true))
            {
                token.ThrowIfCancellationRequested();var binding=repository.Find(item.Track);
                if(binding?.Manual==true)repository.SetLibraryState(item,"Manual","已保留手动锁定的歌词");
                else if(binding is null || !QualityOf(binding,item.Track))repository.SetLibraryState(item,"Pending","本地自动缓存需重新验证");
            }
            Report(repository.GetLibraryProgress("已读取完整资料库并复查自动缓存，后台开始预缓存",true));
            int processed=0,consecutiveErrors=0;
            foreach(var item in repository.GetLibraryQueue())
            {
                token.ThrowIfCancellationRequested();if(maximum.HasValue && processed>=maximum)break;
                if(File.Exists(PausePath(databasePath))){Report(repository.GetLibraryProgress("已暂停，下次从本地记录继续",false));return;}
                var cached=repository.Find(item.Track);
                if(cached?.Manual==true){repository.SetLibraryState(item,"Manual","已保留手动锁定的歌词");continue;}
                if(cached is not null && QualityOf(cached,item.Track))
                {repository.SetLibraryState(item,"Cached","已有通过检查的本地缓存",cached.Instrumental?"Instrumental":"Synced");continue;}
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
                        repository.Save(item.Track,selected.Candidate.Content,selected.Candidate.SourceLabel+" · 自动检查",false,selected.Candidate.Instrumental,providerJson:selected.Candidate.RawJson);
                        repository.SetLibraryState(item,"Cached",selected.Assessment.Reason,selected.Candidate.Instrumental?"Instrumental":"Synced");Cached?.Invoke(item.Track);
                    }
                    else if(candidates is LyricsLookupResult{UnavailableSources.Count:>0} partial)repository.SetLibraryState(item,"Error","部分来源暂不可用："+string.Join("、",partial.UnavailableSources)+"；可用来源没有提供通过检查的动态歌词。");
                    else repository.SetLibraryState(item,candidates.Count==0?"Missing":"Incomplete",candidates.Count==0?"来源无结果":"没有同时通过录音版本、动态时间轴及全文检查的候选");
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
    public static bool QualityOf(LyricsBinding binding,Track track)
    {
        if(binding.Plain && !binding.Instrumental)return false;
        if(!string.IsNullOrWhiteSpace(binding.ProviderJson))
        {
            try
            {
                using var json=System.Text.Json.JsonDocument.Parse(binding.ProviderJson);var original=LrclibProvider.Parse(json.RootElement);
                if(json.RootElement.TryGetProperty("Provider",out var source) && source.GetString()=="NetEase")
                {
                    var saved=System.Text.Json.JsonSerializer.Deserialize<NetEaseOriginal>(binding.ProviderJson);
                    if(saved is null || saved.Track is null || saved.Original!=binding.Raw || saved.Instrumental!=binding.Instrumental || binding.Plain)return false;
                    if(saved.Instrumental && !NetEaseRecording.IsExplicitInstrumental(saved.InstrumentalEvidence??""))return false;
                    return NetEaseTranslationProvider.RecordingMatches(track,saved.Track) && AutomaticLyricsMatcher.Assess(new(saved.Id,saved.Track,saved.Instrumental?null:saved.Original,null,saved.Instrumental)).Accepted;
                }
                if(original.Instrumental!=binding.Instrumental)return false;
                if(!AutomaticLyricsMatcher.Assess(original).Accepted)return false;
                var candidate=original with{SyncedLyrics=binding.Plain?null:binding.Raw};
                return AutomaticLyricsMatcher.Select(track,[candidate]) is not null;
            }
            catch(Exception e) when(e is System.Text.Json.JsonException or InvalidOperationException){return false;}
        }
        return AutomaticLyricsMatcher.Assess(new(0,track,binding.Plain?null:binding.Raw,binding.Plain?binding.Raw:null,binding.Instrumental)).Accepted;
    }
}
