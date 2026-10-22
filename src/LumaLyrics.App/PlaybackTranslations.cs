using System.IO;
using System.Text.Json;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

public sealed partial class PlaybackCoordinator
{
    readonly TranslationRepository translations;
    CancellationTokenSource? translationCancellation;Task? translationTask;
    public ChineseTranslation? Translation {get;private set;}
    public string TranslationStatus {get;private set;}="播放有原词的歌曲后显示译文来源。";
    public TranslationProgress TranslationProgress {get;private set;}=new(0,0,0,0,0,0,0,0,0,false,"尚未准备中文译文。");
    public void RefreshTranslation()
    {
        ChineseTranslation? next=null;
        try{if(ActiveTrack is { } track && Document is { } doc)next=translations.Find(track,doc);}
        catch(Exception e) when(e is IOException or InvalidDataException or JsonException or Microsoft.Data.Sqlite.SqliteException){store.Log("Translation read: "+e.GetType().Name);}
        if(next is null || Translation is null || next.SourceHash!=Translation.SourceHash || next.Source!=Translation.Source || next.PreparedUtc!=Translation.PreparedUtc || !next.Lines.SequenceEqual(Translation.Lines))Translation=next;
        TranslationStatus=Document?.Instrumental==true?"纯音乐，无需译文。":Translation is { } translation?$"{translation.Source}\n{translation.Review}":Document is not {IsSynced:true}?"请先取得与歌曲版本一致的动态原词。":TranslationAlignment.IsChinese(string.Join("\n",Document.Groups.Where(g=>TranslationAlignment.IsLyricText(g.Text)).Select(g=>g.Text)))?"原词以中文为主。":"暂无与当前原词完整对应的中文译文，保持留空。";
    }
    void RefreshTranslationProgress()
    {
        var path=TranslationPreparation.ProgressPath(store.Root);
        if(File.Exists(path)){var recorded=JsonSerializer.Deserialize<TranslationProgress>(File.ReadAllText(path));TranslationProgress=translations.Progress(recorded?.Message??"",TranslationPreparation.IsRunning(store.Root));}
        else TranslationProgress=translations.Progress(running:TranslationPreparation.IsRunning(store.Root));
    }
    public void SetTranslationVisibility(bool main,bool floating)
    {Settings.ShowChineseTranslation=main;Settings.OverlayShowChineseTranslation=floating;SaveSettings();RefreshTranslation();Updated?.Invoke();}
    public async Task StartTranslationPreparationAsync(bool retry=false)
    {
        if(disposed)return;
        if(!Settings.OnlineLookup){TranslationProgress=TranslationProgress with{Message="请在常规设置中开启联网查词，已有译文可离线显示。"};Updated?.Invoke();return;}
        if(translationTask is {IsCompleted:false} || TranslationPreparation.IsRunning(store.Root)){RefreshTranslationProgress();Updated?.Invoke();return;}
        var path=Settings.LibraryPath??AppleMusicLibraryReader.Discover();
        if(path is null){TranslationProgress=TranslationProgress with{Message="未找到 Apple Music 资料库，请先在资料库窗口选择。"};Updated?.Invoke();return;}
        translationCancellation?.Dispose();translationCancellation=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);var token=translationCancellation.Token;
        File.Delete(TranslationPreparation.PausePath(store.Root));
        TranslationProgress=TranslationProgress with{Running=true,Message="正在读取资料库，准备逐行中文译文…"};Updated?.Invoke();
        translationTask=Task.Run(async()=>
        {
            using var provider=new NetEaseTranslationProvider();var worker=new TranslationPreparation(store.Root,provider);
            worker.Progress+=p=>{if(!disposed)_=dispatcher.BeginInvoke(()=>{TranslationProgress=p;RefreshTranslation();Updated?.Invoke();});};
            try{await worker.RunAsync(path,token,retry:retry);}
            catch(OperationCanceledException){}
            catch(Exception e){if(!disposed)_=dispatcher.BeginInvoke(()=>{TranslationProgress=TranslationProgress with{Running=false,Message="译文准备中断："+e.Message};Updated?.Invoke();});}
        },token);
        await Task.Yield();
    }
    public void PauseTranslationPreparation()
    {
        if(translationTask is not {IsCompleted:false} && !TranslationPreparation.IsRunning(store.Root))return;
        File.WriteAllText(TranslationPreparation.PausePath(store.Root),"");
        translationCancellation?.Cancel();TranslationProgress=TranslationProgress with{Message="正在暂停，已准备的译文保留在个人目录。"};Updated?.Invoke();
    }
    public void ImportChineseTranslation(string raw)
    {
        if(ActiveTrack is not { } track || Document is not {IsSynced:true,Instrumental:false} doc)throw new InvalidDataException("请先为当前歌曲准备动态原词。");
        var translated=parser.Parse(raw);var result=TranslationAlignment.Align(doc,doc,translated,"个人导入的中文译文","manual");
        if(result is null)throw new InvalidDataException("请导入与当前原词时间点逐行对应的中文 LRC。行数、对应或内容检查未通过，原有译文已保留。");
        translations.Save(track,result with{Review="个人导入 · 逐行对应已检查 · 语义由导入者确认"},true);RefreshTranslation();Updated?.Invoke();
    }
}
