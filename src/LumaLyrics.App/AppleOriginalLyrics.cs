using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
using LumaLyrics.Windows;

namespace LumaLyrics.App;

internal sealed record AppleOriginalCapture(AppleTimedLyrics Lyrics,string Provenance);
internal sealed record AppleOriginalAnchors(int ProcessId,long ProcessStartUtc,IReadOnlyList<string> Lines);
internal static partial class AppleMusicLibraryHandoff
{
    // Unlike the explicit OCR review command, this path never changes native UI state.
    internal static async Task<AppleOriginalAnchors?> ReadOriginalAnchorsAsync(Track expected,CancellationToken token)
    {
        var bounded=CancellationTokenSource.CreateLinkedTokenSource(token);bounded.CancelAfter(TimeSpan.FromSeconds(5));var nativeToken=bounded.Token;
        var worker=Task.Run<AppleOriginalAnchors?>(()=>
            {
                if(!gate.Wait(TimeSpan.FromSeconds(1),nativeToken))return null;
                try
                {
                    nativeToken.ThrowIfCancellationRequested();
                    using var process=Process.GetProcessesByName("AppleMusic").FirstOrDefault(p=>p.MainWindowHandle!=0);
                    if(process is null || IsIconic(process.MainWindowHandle))return null;
                    using var automation=new UIA3Automation{ConnectionTimeout=TimeSpan.FromSeconds(1),TransactionTimeout=TimeSpan.FromSeconds(1)};
                    var root=automation.FromHandle(process.MainWindowHandle);
                    if(QueueCurrent(root) is not { } actual || !MusicLibraryPlayback.IsRequestedRecording(expected,actual))return null;
                    var toggle=root.FindFirstDescendant(cf=>cf.ByAutomationId("LyricsToggleButton"));
                    if(toggle?.Patterns.Toggle.IsSupported!=true || toggle.Patterns.Toggle.Pattern.ToggleState.Value!=ToggleState.On)return null;
                    var panes=root.FindAllDescendants(cf=>cf.ByAutomationId("TimeBasedLyrics"));if(panes.Length!=1)return null;
                    var lines=panes[0].FindAllChildren(cf=>cf.ByControlType(ControlType.Group)).Select(Name).Where(s=>s.Length>0).Take(500).ToArray();
                    nativeToken.ThrowIfCancellationRequested();
                    if(IsIconic(process.MainWindowHandle) || QueueCurrent(root) is not { } after || !MusicLibraryPlayback.IsRequestedRecording(expected,after))return null;
                    return new AppleOriginalAnchors(process.Id,process.StartTime.ToUniversalTime().Ticks,lines);
                }
                finally{gate.Release();}
            },nativeToken);
        try{return await worker.WaitAsync(TimeSpan.FromSeconds(6),token);}
        catch(OperationCanceledException) when(!token.IsCancellationRequested){return null;}
        catch(Exception ex) when(ex is TimeoutException or InvalidOperationException or COMException or System.ComponentModel.Win32Exception or FlaUI.Core.Exceptions.FlaUIException){return null;}
        finally
        {
            bounded.Cancel();if(worker.IsCompleted)bounded.Dispose();
            else _=worker.ContinueWith(t=>{_=t.Exception;bounded.Dispose();},TaskScheduler.Default);
        }
    }
}

internal sealed class AppleOriginalLyricsReader
{
    readonly AppleMusicMemoryLyrics memory=new();
    readonly Action<string,object?>? trace;
    readonly Func<Track,CancellationToken,Task<AppleOriginalAnchors?>> anchors;
    readonly Func<AppleOriginalAnchors,double,CancellationToken,Task<IReadOnlyList<AppleTimedLyrics>>> scan;
    internal AppleOriginalLyricsReader(Func<Track,CancellationToken,Task<AppleOriginalAnchors?>>? anchors=null,Func<AppleOriginalAnchors,double,CancellationToken,Task<IReadOnlyList<AppleTimedLyrics>>>? scan=null,Action<string,object?>? trace=null)
    {this.anchors=anchors??AppleMusicLibraryHandoff.ReadOriginalAnchorsAsync;this.scan=scan??ScanAsync;this.trace=trace;}
    Task<IReadOnlyList<AppleTimedLyrics>> ScanAsync(AppleOriginalAnchors identity,double duration,CancellationToken token)=>Task.Run(()=>
    {
        using var process=Process.GetProcessById(identity.ProcessId);
        if(process.ProcessName!="AppleMusic" || process.StartTime.ToUniversalTime().Ticks!=identity.ProcessStartUtc)return (IReadOnlyList<AppleTimedLyrics>)[];
        return memory.Read(process,duration,token,trace);
    },token);
    internal async Task<AppleOriginalCapture?> ReadAsync(PlaybackSnapshot expected,Func<PlaybackSnapshot?> latest,CancellationToken token)
    {
        bool Current()=>!token.IsCancellationRequested && latest() is { } current && current.SessionId==expected.SessionId && current.Generation==expected.Generation && current.Revision==expected.Revision && current.Track==expected.Track;
        var track=TrackMetadata.FromAppleSession(expected.Track);
        trace?.Invoke("originalStart",new{current=Current(),track,expected.SessionId});
        if(!Current() || !expected.SessionId.Contains("AppleMusic",StringComparison.OrdinalIgnoreCase) || track.DurationMs is not >0 || string.IsNullOrWhiteSpace(track.Album) || string.IsNullOrWhiteSpace(track.Artist))return null;
        var before=await anchors(track,token);
        trace?.Invoke("originalAnchorsBefore",before);
        if(before is null || !Current())return null;
        // Avoid scanning at all when the accessible text cannot establish identity.
        var distinctive=before.Lines.Select(MatchRules.Normalize).Where(s=>s.Length>=8).Distinct().ToArray();
        if(distinctive.Length<4 || distinctive.Sum(s=>s.Length)<60)return null;
        IReadOnlyList<AppleTimedLyrics> candidates;
        try
        {
            candidates=await scan(before,track.DurationMs.Value,token);
            trace?.Invoke("originalParsedCandidates",candidates.Select(c=>new{hash=TranslationAlignment.Hash(c.Lrc),lines=c.Lines.Count,first=c.Lines.FirstOrDefault()?.BeginMs,last=c.Lines.LastOrDefault()?.EndMs}));
        }
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception){return null;}
        if(!Current())return null;
        var after=await anchors(track,token);
        trace?.Invoke("originalAnchorsAfter",after);
        if(after is null || after.ProcessId!=before.ProcessId || after.ProcessStartUtc!=before.ProcessStartUtc || !Current())return null;
        var first=AppleOriginalLyricsMatch.Select(candidates,before.Lines);
        var selected=AppleOriginalLyricsMatch.Select(candidates,after.Lines);
        trace?.Invoke("originalMatching",new{beforeAccepted=first is not null,afterAccepted=selected is not null});
        if(first is null || selected is null || first.Lrc!=selected.Lrc)return null;
        var candidate=new LyricsCandidate(0,track,selected.Lrc,null,false){ProviderName="Apple Music"};
        if(!AutomaticLyricsMatcher.Assess(candidate).Accepted)return null;
        return new(selected,AppleOriginalProvenance.Create(track,selected));
    }
}
