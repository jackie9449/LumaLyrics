using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;
public sealed partial class PlaybackCoordinator
{
    TranslationJobInbox? translationInbox;Task? translationInboxTask;EventHandler? translationActivated;
    public string TranslationJobMessage {get;private set;}="只需交接一份批量 MD；agent逐曲处理，可中断续做。";
    void StartTranslationInbox()
    {
        translationInbox=new(store.Root);
        translationActivated=(_,_)=>translationInbox.RequestScan();System.Windows.Application.Current.Activated+=translationActivated;
        translationInbox.Received+=receipt=>{if(!disposed)_=dispatcher.BeginInvoke(()=>{if(disposed)return;InvalidateWorkspace();TranslationJobMessage=receipt.Message;RefreshTranslation();Updated?.Invoke();});};
        translationInboxTask=Task.Run(async()=>{try{await translationInbox.RunAsync(lifetime.Token);}catch(OperationCanceledException){}catch(Exception e){store.Log("Translation inbox: "+e.GetType().Name);}});
    }
    public IReadOnlyList<LibraryQueueItem> TranslationLibraryTracks(){using var repo=new LyricsRepository(System.IO.Path.Combine(store.Root,"lyrics.db"),true);return repo.GetLibraryQueue(true);}
    public async Task<TranslationJobBatchResult> CreateTranslationBatchAsync(IReadOnlyList<TranslationBatchSelection> selections,bool preferExisting,CancellationToken token)
    {
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(token,lifetime.Token);token=linked.Token;
        return await Task.Run(async()=>
        {
            if(preferExisting&&Settings.OnlineLookup)
            {
                using var provider=new NetEaseTranslationProvider();var workflow=new TranslationWorkflow(store.Root,provider);int errors=0;
                foreach(var item in selections.Where(s=>s.Mode==TranslationJobMode.FillMissing))
                {
                    token.ThrowIfCancellationRequested();var result=await workflow.PrepareAsync(item.Track,true,false,token);
                    errors=result.Lookup.State==TranslationLookupState.TransientError?errors+1:0;if(errors>=4)break;
                }
            }
            token.ThrowIfCancellationRequested();var batch=TranslationBatchExchange.Create(store.Root,selections,token:token);translationInbox?.RequestScan();return batch;
        },token);
    }
    public void RefreshTranslationJobs(){InvalidateWorkspace();translationInbox?.RequestScan();RefreshTranslation();Updated?.Invoke();}
}
