namespace LumaLyrics.Infrastructure;

/// <summary>Also cancels another process's CLI import during HTTP backoff, not only between songs.</summary>
internal sealed class LibraryPauseMonitor : IDisposable
{
    readonly CancellationTokenSource cancellation;
    readonly FileSystemWatcher watcher;
    readonly string pausePath;
    public CancellationToken Token=>cancellation.Token;
    public LibraryPauseMonitor(string databasePath,CancellationToken token)
    {
        cancellation=CancellationTokenSource.CreateLinkedTokenSource(token);pausePath=LibraryPrefetcher.PausePath(databasePath);
        watcher=new(Path.GetDirectoryName(pausePath)!,Path.GetFileName(pausePath)){NotifyFilter=NotifyFilters.FileName|NotifyFilters.LastWrite};
        watcher.Created+=Changed;watcher.Changed+=Changed;watcher.Renamed+=Changed;watcher.EnableRaisingEvents=true;
        CancelIfPaused();
    }
    void Changed(object sender,FileSystemEventArgs e)=>CancelIfPaused();
    void CancelIfPaused(){try{if(File.Exists(pausePath))cancellation.Cancel();}catch(ObjectDisposedException){}}
    public void Dispose(){watcher.Dispose();cancellation.Dispose();}
}
