using System.Diagnostics;
using System.IO;
using System.Text.Json;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using LumaLyrics.Infrastructure;
using LumaLyrics.Windows;

namespace LumaLyrics.App;

internal sealed record PassiveNativeState(bool Running,bool Minimized,long Window,long Foreground,string? Queue,string? Lyrics);
internal static partial class AppleMusicLibraryHandoff
{
    internal static Task<PassiveNativeState> ObservePassiveStateAsync(CancellationToken token)=>Task.Run(()=>
    {
        gate.Wait(token);
        try
        {
            using var process=Process.GetProcessesByName("AppleMusic").FirstOrDefault(p=>p.MainWindowHandle!=0);
            if(process is null)return new PassiveNativeState(false,false,0,QueueForegroundWindow(),null,null);
            nint handle=process.MainWindowHandle;bool minimized=IsIconic(handle);string? queue=null,lyrics=null;
            if(!minimized)
            {
                using var automation=new UIA3Automation{ConnectionTimeout=TimeSpan.FromSeconds(1),TransactionTimeout=TimeSpan.FromSeconds(1)};var root=automation.FromHandle(handle);
                string? State(string id){var element=root.FindFirstDescendant(cf=>cf.ByAutomationId(id));return element?.Patterns.Toggle.IsSupported==true?element.Patterns.Toggle.Pattern.ToggleState.Value.ToString():null;}
                queue=State("PlayQueueToggleButton");lyrics=State("LyricsToggleButton");
            }
            return new PassiveNativeState(true,minimized,handle,QueueForegroundWindow(),queue,lyrics);
        }
        finally{gate.Release();}
    },token);
}

public partial class MainWindow
{
    // Opt-in integration diagnostic; current native recording and raw lyrics stay in
    // the external private directory. It never selects a song or changes native panels.
    async Task VerifyPassiveNativeAsync(string directory,bool acquire,CancellationToken token)
    {
        await using var source=new GsmtcPlaybackSource();await source.StartAsync(token);await source.RefreshAsync().WaitAsync(token);
        var initial=await AppleMusicLibraryHandoff.ObservePassiveStateAsync(token);var before=source.Latest;
        var observations=new List<object>();bool retained=true,didNotActivate=true;int reads=0;
        for(int i=0;i<12;i++)
        {
            var queue=await AppleMusicLibraryHandoff.ReadQueueAsync(token);var state=await AppleMusicLibraryHandoff.ObservePassiveStateAsync(token);reads++;
            bool unchanged=state.Running==initial.Running && state.Window==initial.Window && state.Minimized==initial.Minimized && state.Queue==initial.Queue && state.Lyrics==initial.Lyrics;
            retained&=unchanged;didNotActivate&=initial.Foreground==initial.Window || state.Foreground!=state.Window;observations.Add(new{state,unchanged,queue.Available,queue.Message});await Task.Delay(1000,token);
        }
        AppleOriginalCapture? original=null;
        LumaLyrics.Core.Track? capturedTrack=null;
        if(acquire && source.Latest is { } expected)
        {
            void Trace(string stage,object? value)
            {
                if(stage=="originalTtmlCandidate" && value is string xml){File.WriteAllText(Path.Combine(directory,"native-lyrics-"+LumaLyrics.Core.TranslationAlignment.Hash(xml)+".ttml"),xml);return;}
                File.AppendAllText(Path.Combine(directory,"native-lyrics-diagnostic.jsonl"),JsonSerializer.Serialize(new{stage,value})+Environment.NewLine);
            }
            original=await new AppleOriginalLyricsReader(trace:Trace).ReadAsync(expected,()=>source.Latest,token);capturedTrack=expected.Track;
        }
        if(original is not null)
        {
            File.WriteAllText(Path.Combine(directory,"native-ttml.json"),original.Provenance);
            using var personal=new LyricsRepository(Path.Combine(store.Root,"lyrics.db"));personal.Save(capturedTrack!,original.Lyrics.Lrc,"Apple Music · 本机 TTML · 自动检查",false,providerJson:original.Provenance);
        }
        var final=await AppleMusicLibraryHandoff.ObservePassiveStateAsync(token);await source.RefreshAsync().WaitAsync(token);
        retained&=final.Window==initial.Window && final.Minimized==initial.Minimized && final.Queue==initial.Queue && final.Lyrics==initial.Lyrics;
        didNotActivate&=initial.Foreground==initial.Window || final.Foreground!=final.Window;
        File.WriteAllText(Path.Combine(directory,"native-queue.json"),JsonSerializer.Serialize(new{initial,final,reads,nativePreferencesRetained=retained,didNotActivate,
            originalAcquired=original is not null,originalGroups=original?.Lyrics.Lines.Count??0,recordingBefore=before,recordingAfter=source.Latest,observations},new JsonSerializerOptions{WriteIndented=true}));
    }
}
