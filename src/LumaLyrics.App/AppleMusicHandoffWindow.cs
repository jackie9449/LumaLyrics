using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core.Exceptions;
using LumaLyrics.Core;
namespace LumaLyrics.App;

internal static partial class AppleMusicLibraryHandoff
{
    static readonly RequestVersion windowOwners=new();
    static readonly object windowStateGate=new();
    [DllImport("user32.dll")] static extern bool ShowWindowAsync(nint handle,int command);
    internal sealed record HandoffWindow(nint Handle,int ProcessId,bool WasMinimized);
    internal sealed class HandoffContext
    {
        internal HandoffWindow? Window;
        internal long Version;
        internal volatile bool CommandAttempted;
        internal void Capture(Process process,CancellationToken token=default)
        {
            lock(windowStateGate)
            {
                token.ThrowIfCancellationRequested();
                if(Window is not null && !windowOwners.IsCurrent(Version))throw new OperationCanceledException("A newer native operation owns this window.",token);
                if(Window is null || Window.ProcessId!=process.Id)
                {
                    Window=new(process.MainWindowHandle,process.Id,IsIconic(process.MainWindowHandle));Version=windowOwners.Next();
                }
                if(IsIconic(Window.Handle))ShowWindowAsync(Window.Handle,4);
            }
        }
    }
    internal static bool HandoffWindowMinimized(HandoffContext context)=>context.Window is { } window && IsIconic(window.Handle);
    internal static async Task<bool> CompleteHandoffWindowAsync(HandoffContext context,bool confirmed,Action<string,object?>? trace=null)
    {
        try
        {
            HandoffWindow window;
            // Queue reads use the UIA gate and never restore a window. A short, separate
            // lock makes window ownership atomic without letting a slow queue RPC delay
            // minimization after playback has already been confirmed.
            lock(windowStateGate)
            {
                if(context.Window is not { } owned || !windowOwners.IsCurrent(context.Version))return false;window=owned;
                using var process=Process.GetProcessById(window.ProcessId);
                if(process.HasExited || process.MainWindowHandle!=window.Handle)return false;
                if(confirmed || window.WasMinimized)ShowWindowAsync(window.Handle,6);
            }
            if(confirmed || window.WasMinimized)for(int i=0;i<8 && !IsIconic(window.Handle) && windowOwners.IsCurrent(context.Version);i++)await Task.Delay(100);
            bool minimized=IsIconic(window.Handle),current=windowOwners.IsCurrent(context.Version);trace?.Invoke("handoffWindowCompleted",new{confirmed,minimized,currentOwner=current,window.WasMinimized});
            return current && confirmed && minimized;
        }
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception){return false;}
    }
    internal static T? WaitForNative<T>(Func<T?> query,TimeSpan timeout,CancellationToken token) where T:class
    {
        var watch=Stopwatch.StartNew();
        while(watch.Elapsed<timeout)
        {
            token.ThrowIfCancellationRequested();
            try{if(query() is { } result)return result;}
            catch(Exception ex) when(ex is InvalidOperationException or COMException or FlaUIException){}
            token.WaitHandle.WaitOne(150);
        }
        token.ThrowIfCancellationRequested();return null;
    }
}
