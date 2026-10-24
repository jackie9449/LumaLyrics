using System.Text.Json;
using LumaLyrics.Core;
namespace LumaLyrics.Infrastructure;

public sealed class TranslationPreparation(string root,ITranslationProvider provider)
{
    public static string ProgressPath(string root)=>Path.Combine(root,"translation-preparation-progress.json");
    public static string PausePath(string root)=>Path.Combine(root,"translation-preparation.pause");
    public static bool IsRunning(string root)=>LibraryPrefetcher.LeaseHeld(Path.Combine(root,"translation-preparation.lock"));
    public event Action<TranslationProgress>? Progress;
    public event Action<Track>? Prepared;
    void Report(TranslationProgress progress)
    {
        PreparationProgressFile.Write(ProgressPath(root),JsonSerializer.Serialize(progress));
        Progress?.Invoke(progress);
    }
    public async Task RunAsync(string libraryPath,CancellationToken token,int? maximum=null,bool retry=false)
    {
        Directory.CreateDirectory(root);FileStream lease;
        try{lease=new(Path.Combine(root,"translation-preparation.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
        catch(IOException){return;}
        using var held=lease;using var translations=new TranslationRepository(root);using var lyrics=new LyricsRepository(Path.Combine(root,"lyrics.db"));
        try
        {
            var snapshot=await Task.Run(()=>new AppleMusicLibraryReader().Read(libraryPath),token);lyrics.SyncLibrary(snapshot);
            await PrepareAsync(snapshot,lyrics,translations,token,maximum,retry);
        }
        catch(OperationCanceledException){Report(translations.Progress("已暂停，已准备的中文译文保留在个人目录。"));}
        catch(Exception e) when(e is IOException or InvalidDataException){Report(translations.Progress("准备中止："+e.Message));}
    }
    // The same path is exercised with self-authored snapshots in tests.
    public async Task PrepareAsync(LibrarySnapshot snapshot,LyricsRepository lyrics,TranslationRepository translations,CancellationToken token,int? maximum=null,bool retry=false)
    {
        var parser=new LrcParser();var rows=new List<(string,Track,string,int)>();
        foreach(var item in snapshot.Tracks)
        {
            token.ThrowIfCancellationRequested();var binding=lyrics.Find(item.Track);var valid=binding is not null && (binding.Manual || LibraryPrefetcher.QualityOf(binding,item.Track));
            var doc=valid?parser.Parse(binding!.Raw,binding.Plain) with{Instrumental=binding.Instrumental}:null;
            string text=doc is null?"":string.Join("\n",doc.Groups.Where(g=>TranslationAlignment.IsLyricText(g.Text)).Select(g=>g.Text));
            rows.Add((TranslationAlignment.Hash(snapshot.Path+"\0"+item.PersistentId),item.Track,doc is null?"":TranslationAlignment.Hash(doc.Raw),doc?.Instrumental==true?0:doc is null?2:TranslationAlignment.IsChinese(text)?0:1));
        }
        translations.Sync(rows);if(retry)translations.ResetRetries();int processed=0,errors=0;
        Report(translations.Progress("已读取完整资料库；优先准备已有外文原词的译文。",true));
        try
        {
            foreach(var item in translations.Queue())
            {
                token.ThrowIfCancellationRequested();if(File.Exists(PausePath(root)) || maximum.HasValue&&processed>=maximum)break;
                var binding=lyrics.Find(item.Track);var usable=binding is not null && (binding.Manual || LibraryPrefetcher.QualityOf(binding,item.Track));
                var document=usable?parser.Parse(binding!.Raw,binding.Plain) with{Instrumental=binding.Instrumental}:null;
                string hash=document is null?"":TranslationAlignment.Hash(document.Raw);
                if(document?.Instrumental==true)translations.SetState(item,"Instrumental","已有明确纯音乐标记，无需译文。",hash);
                else if(document is not null && TranslationAlignment.IsChinese(string.Join("\n",document.Groups.Where(g=>TranslationAlignment.IsLyricText(g.Text)).Select(g=>g.Text))))translations.SetState(item,"Chinese","原词以中文为主，无需中文翻译。",hash);
                else if(document is not null && translations.Find(item.Track,document) is { } cached)translations.SetState(item,cached.Draft?"Draft":"Prepared",cached.Draft?"已有 AI 译文。":"已有与当前原词一致的中文译文。",hash);
                else
                {
                    try
                    {
                        var candidates=await provider.LookupAsync(item.Track,token);errors=0;bool saved=false;bool chinese=false;
                        foreach(var candidate in candidates)
                        {
                            token.ThrowIfCancellationRequested();if(File.Exists(PausePath(root)))break;
                            var cache=Path.Combine(root,"cache","translations");Directory.CreateDirectory(cache);
                            string candidatePath=Path.Combine(cache,candidate.Id+".json"),temp=candidatePath+".tmp";
                            File.WriteAllText(temp,JsonSerializer.Serialize(candidate));File.Move(temp,candidatePath,true);
                            if(!NetEaseTranslationProvider.RecordingMatches(item.Track,candidate.Track))continue;
                            var original=parser.Parse(candidate.Original);
                            if(!AutomaticLyricsMatcher.Assess(candidate.OriginalCandidate).Accepted)continue;
                            if(TranslationAlignment.IsChinese(string.Join("\n",original.Groups.Where(g=>TranslationAlignment.IsLyricText(g.Text)).Select(g=>g.Text)))){chinese=true;continue;}
                            var local=document??original;
                            var aligned=TranslationAlignment.Align(local,original,parser.Parse(candidate.Translated),$"网易云音乐社区译文 #{candidate.Id}",candidate.SourceUrl);
                            if(aligned is null)continue;
                            // A usable original and every manual original retain their exact text/offset.
                            if(document is null)
                            {
                                if(binding?.Manual==true)continue;
                                binding=lyrics.Save(item.Track,candidate.Original,$"网易云音乐 #{candidate.Id} · 录音与时间轴检查",false,providerJson:candidate.OriginalCandidate.RawJson);
                                document=parser.Parse(binding.Raw,binding.Plain) with{Instrumental=binding.Instrumental};
                                if(TranslationAlignment.Hash(document.Raw)!=aligned.SourceHash)continue;
                            }
                            translations.Save(item.Track,aligned);translations.SetState(item,"Prepared",aligned.Review,aligned.SourceHash);Prepared?.Invoke(item.Track);saved=true;break;
                        }
                        if(!saved)
                        {
                            string state=chinese&&document is null?"Chinese":document is null?"MissingOriginal":candidates.Count==0 || candidates.All(c=>string.IsNullOrWhiteSpace(c.Translated))?"MissingTranslation":"Mismatch";
                            translations.SetState(item,state,state switch{"Chinese"=>"匹配录音的原词以中文为主。","MissingOriginal"=>"没有可确认的完整外文原词及逐行中文译文。","MissingTranslation"=>"匹配来源未提供完整中文动态译文。",_=>"译文与当前原词的完整逐行对应未通过；保持留空。"},hash);
                        }
                    }
                    catch(LyricsProviderException e){translations.SetState(item,"Error",e.Message,hash);errors++;}
                }
                processed++;Report(translations.Progress($"本轮已处理 {processed} 首。社区译文尚未人工逐首核对语义。",true));
                if(errors>=4){Report(translations.Progress("来源连续失败，已暂停请求；已保存译文不受影响。"));return;}
            }
            Report(translations.Progress(File.Exists(PausePath(root))?"已暂停，可继续准备剩余项目。":"本轮完成；未确认译文留空，可稍后重试待补齐项目。"));
        }
        catch(OperationCanceledException){Report(translations.Progress("已暂停，已准备的中文译文保留在个人目录。"));throw;}
    }
}
