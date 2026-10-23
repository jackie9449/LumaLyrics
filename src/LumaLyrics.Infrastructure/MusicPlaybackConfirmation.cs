using System.Diagnostics;
using System.Runtime.InteropServices;
using LumaLyrics.Core;
namespace LumaLyrics.Infrastructure;

public sealed record PlaybackConfirmationOptions(TimeSpan Timeout,TimeSpan ReadTimeout,TimeSpan PollInterval)
{
    public static PlaybackConfirmationOptions Default {get;}=new(TimeSpan.FromSeconds(60),TimeSpan.FromSeconds(5),TimeSpan.FromMilliseconds(250));
}

public static class MusicPlaybackConfirmation
{
    // A read timeout is a reason to keep waiting, not evidence that Play failed.
    // Retain an in-flight probe so a slow native RPC is neither discarded nor duplicated.
    public static async Task<bool> WaitAsync(Func<CancellationToken,Task<PlaybackSnapshot?>> probe,Func<Track,bool> matches,DateTimeOffset issuedUtc,CancellationToken token,PlaybackConfirmationOptions? options=null)
    {
        options??=PlaybackConfirmationOptions.Default;
        if(options.Timeout<=TimeSpan.Zero || options.ReadTimeout<=TimeSpan.Zero || options.PollInterval<=TimeSpan.Zero)throw new ArgumentOutOfRangeException(nameof(options));
        using var lifetime=CancellationTokenSource.CreateLinkedTokenSource(token);var watch=Stopwatch.StartNew();Task<PlaybackSnapshot?>? pending=null;
        try
        {
            while(watch.Elapsed<options.Timeout)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    pending??=probe(lifetime.Token);
                    var remaining=options.Timeout-watch.Elapsed;if(remaining<=TimeSpan.Zero)break;
                    var actual=await pending.WaitAsync(remaining<options.ReadTimeout?remaining:options.ReadTimeout,token);pending=null;
                    token.ThrowIfCancellationRequested();
                    if(actual is {Status:PlayState.Playing} && actual.CapturedUtc>=issuedUtc && actual.SessionId.Contains("AppleMusic",StringComparison.OrdinalIgnoreCase) && matches(actual.Track))return true;
                }
                catch(TimeoutException){if(pending?.IsCompleted==true){_=pending.Exception;pending=null;}}
                catch(Exception ex) when(ex is InvalidOperationException or ExternalException){pending=null;}
                var delay=options.Timeout-watch.Elapsed;if(delay<=TimeSpan.Zero)break;
                await Task.Delay(delay<options.PollInterval?delay:options.PollInterval,token);
            }
            token.ThrowIfCancellationRequested();return false;
        }
        finally
        {
            lifetime.Cancel();
            if(pending is not null)_=pending.ContinueWith(t=>{_=t.Exception;},CancellationToken.None,TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
        }
    }
}
