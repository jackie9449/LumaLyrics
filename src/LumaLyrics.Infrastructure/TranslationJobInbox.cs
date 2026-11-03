using System.Threading.Channels;
namespace LumaLyrics.Infrastructure;

public sealed class TranslationJobInbox(string root):IAsyncDisposable
{
    readonly Channel<bool> requests=Channel.CreateBounded<bool>(new BoundedChannelOptions(1){FullMode=BoundedChannelFullMode.DropWrite});
    readonly CancellationTokenSource stop=new();FileSystemWatcher? watcher;
    public event Action<TranslationJobReceipt>? Received;
    public void RequestScan()=>requests.Writer.TryWrite(true);
    public async Task RunAsync(CancellationToken token)
    {
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(token,stop.Token);token=linked.Token;
        string folder=Path.Combine(root,"translation-jobs");Directory.CreateDirectory(folder);
        using var watch=new FileSystemWatcher(folder,"translation-result.json"){IncludeSubdirectories=true,NotifyFilter=NotifyFilters.FileName|NotifyFilters.LastWrite};watcher=watch;
        watch.Created+=(_,_)=>RequestScan();watch.Changed+=(_,_)=>RequestScan();watch.Renamed+=(_,_)=>RequestScan();watch.Error+=(_,_)=>RequestScan();watch.EnableRaisingEvents=true;RequestScan();
        try{await foreach(var _ in requests.Reader.ReadAllAsync(token))
        {
            foreach(var job in TranslationJobExchange.ReadJobs(root).Where(j=>j.Receipt.State==TranslationJobState.AwaitingResult))
            {
                token.ThrowIfCancellationRequested();if(!File.Exists(job.Ticket.ResultPath))continue;
                for(int attempt=0;attempt<5;attempt++)
                {
                    try{Received?.Invoke(await TranslationJobExchange.ImportAsync(root,job.Ticket.ResultPath,token));break;}
                    catch(IOException) when(attempt<4){await Task.Delay(new[]{100,250,500,1000}[attempt],token);}
                    catch(Exception e) when(e is IOException or InvalidDataException or System.Text.Json.JsonException or Microsoft.Data.Sqlite.SqliteException){var receipt=new TranslationJobReceipt(job.Ticket.Request.JobId,TranslationJobState.Rejected,"InvalidResult",e.Message,0,0,null,DateTimeOffset.UtcNow);
                        if(e is InvalidDataException or System.Text.Json.JsonException){using var db=TranslationJobRepository.Open(root);TranslationJobRepository.Execute(db,"UPDATE TranslationJobs SET State='Rejected',Receipt=$r WHERE Id=$id AND State='AwaitingResult'",("$r",System.Text.Json.JsonSerializer.Serialize(receipt,TranslationJobFiles.Json)),("$id",job.Ticket.Request.JobId));}
                        Received?.Invoke(receipt);break;}
                }
            }
        }}finally{watcher=null;}
    }
    public ValueTask DisposeAsync(){stop.Cancel();watcher?.Dispose();return ValueTask.CompletedTask;}
}
