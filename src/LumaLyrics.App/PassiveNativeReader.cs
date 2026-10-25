using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.UIA3;
using LumaLyrics.Infrastructure;

namespace LumaLyrics.App;

internal static partial class AppleMusicLibraryHandoff
{
    static readonly PassiveReadCircuit passiveReads=new();
    static readonly Lazy<NativeReadThread> passiveThread=new(()=>new());

    // One windowless MTA owns the automation object and every passive element.
    // Keep its connection alive through outstanding RPCs, including when a caller
    // has timed out. No automation element crosses this thread's boundary.
    sealed class NativeReadThread
    {
        readonly BlockingCollection<Action> work=new(1);
        UIA3Automation? automation;
        internal NativeReadThread()
        {
            var thread=new Thread(()=>
            {
                try{foreach(var action in work.GetConsumingEnumerable())action();}
                finally
                {
                    try{automation?.Dispose();}
                    catch(Exception error) when(error is COMException or FlaUI.Core.Exceptions.FlaUIException){}
                }
            }){IsBackground=true,Name="Apple Music passive UIA"};
            thread.SetApartmentState(ApartmentState.MTA);thread.Start();
        }
        internal Task<T> Run<T>(Func<UIA3Automation,T> read)
        {
            var result=new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            try{if(!work.TryAdd(()=>
            {
                try
                {
                    automation??=new UIA3Automation{ConnectionTimeout=TimeSpan.FromSeconds(1),TransactionTimeout=TimeSpan.FromSeconds(1)};
                    result.SetResult(read(automation));
                }
                catch(Exception error){result.SetException(error);}
            }))
                result.SetException(new InvalidOperationException("A native read is already pending."));}
            catch(InvalidOperationException error){result.SetException(error);}
            return result.Task;
        }
        internal void Stop()=>work.CompleteAdding();
    }

    internal static void StopPassiveReads(){if(passiveThread.IsValueCreated)passiveThread.Value.Stop();}

    static async Task<T> ReadPassiveAsync<T>(Func<Process,UIA3Automation,CancellationToken,T> read,T unavailable,TimeSpan budget,CancellationToken token)
    {
        try{return await ReadPassiveCoreAsync(read,unavailable,budget,token);}
        catch(Exception error) when(error is InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception){return unavailable;}
    }

    static async Task<T> ReadPassiveCoreAsync<T>(Func<Process,UIA3Automation,CancellationToken,T> read,T unavailable,TimeSpan budget,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var selected=Process.GetProcessesByName("AppleMusic").FirstOrDefault(p=>p.MainWindowHandle!=0);
        if(selected is null || IsIconic(selected.MainWindowHandle))return unavailable;
        int id=selected.Id;long started=selected.StartTime.ToUniversalTime().Ticks;
        var lease=passiveReads.TryEnter(id+":"+started);if(lease is null)return unavailable;
        var bounded=CancellationTokenSource.CreateLinkedTokenSource(token);bounded.CancelAfter(budget);
        var worker=passiveThread.Value.Run(automation=>
        {
            using(lease)
            {
                using var process=Process.GetProcessById(id);
                if(process.StartTime.ToUniversalTime().Ticks!=started || IsIconic(process.MainWindowHandle))return unavailable;
                if(!process.Responding){lease.Quarantine();return unavailable;}
                bool owned=false;
                try
                {
                    owned=gate.Wait(TimeSpan.FromSeconds(1),bounded.Token);if(!owned)return unavailable;
                    bounded.Token.ThrowIfCancellationRequested();
                    var value=read(process,automation,bounded.Token);
                    bounded.Token.ThrowIfCancellationRequested();process.Refresh();
                    if(!process.Responding){lease.Quarantine();return unavailable;}
                    return value;
                }
                catch(Exception error) when(error is TimeoutException or COMException or FlaUI.Core.Exceptions.FlaUIException)
                {lease.Quarantine();throw;}
                catch(OperationCanceledException) when(!token.IsCancellationRequested){lease.Quarantine();throw;}
                finally{if(owned)gate.Release();}
            }
        });
        try{return await worker.WaitAsync(budget+TimeSpan.FromSeconds(1),token);}
        catch(OperationCanceledException) when(!token.IsCancellationRequested){return unavailable;}
        catch(TimeoutException){lease.Quarantine();return unavailable;}
        catch(Exception error) when(error is InvalidOperationException or ArgumentException or COMException or System.ComponentModel.Win32Exception or FlaUI.Core.Exceptions.FlaUIException){return unavailable;}
        finally
        {
            bounded.Cancel();
            if(worker.IsCompleted){lease.Dispose();bounded.Dispose();}
            else _=worker.ContinueWith(t=>{_=t.Exception;lease.Dispose();bounded.Dispose();},TaskScheduler.Default);
        }
    }
}
