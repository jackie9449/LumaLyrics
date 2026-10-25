using System.Diagnostics;
using System.IO;
using System.Text.Json;
using LumaLyrics.Windows;

namespace LumaLyrics.App;

public partial class MainWindow
{
    // Opt-in private diagnostic. Baseline/after sampling uses process responsiveness,
    // not UI Automation, so the check does not introduce another tree traversal.
    async Task VerifyNativeReadSafetyAsync(string directory,string mode,CancellationToken token)
    {
        await using var source=new GsmtcPlaybackSource();await source.StartAsync(token);await source.RefreshAsync().WaitAsync(token);
        using var process=Process.GetProcessesByName("AppleMusic").FirstOrDefault()??throw new InvalidOperationException("Apple Music is not running.");
        var samples=new List<object>();var events=new List<object>();
        async Task Sample(string phase)
        {
            process.Refresh();double cpu=process.TotalProcessorTime.TotalMilliseconds;await Task.Delay(1000,token);process.Refresh();
            samples.Add(new{phase,responding=process.Responding,cpuMs=process.TotalProcessorTime.TotalMilliseconds-cpu,utc=DateTimeOffset.UtcNow});
        }
        for(int i=0;i<3;i++)await Sample("baseline");
        var expected=source.Latest??throw new InvalidOperationException("No live recording; probe was not issued.");
        if(!process.Responding)throw new InvalidOperationException("Client was already unresponsive; probe was not issued.");
        int anchors=0,documents=0,queueReads=0;bool accepted=false;string? error=null;var watch=Stopwatch.StartNew();
        events.Add(new{stage="start",utc=DateTimeOffset.UtcNow});
        try
        {
            if(mode is "anchors" or "anchors-repeat")
            {
                for(int i=0;i<(mode=="anchors-repeat"?12:1);i++)
                {
                    anchors=(await AppleMusicLibraryHandoff.ReadOriginalAnchorsAsync(source.Latest?.Track??expected.Track,token))?.Lines.Count??0;
                    await Sample("read");if(!process.Responding)break;
                }
            }
            else if(mode=="queue")_ = await AppleMusicLibraryHandoff.ReadQueueAsync(token);
            else if(mode=="memory")documents=(await Task.Run(()=>new AppleMusicMemoryLyrics().Read(process,expected.Track.DurationMs??600000,token),token)).Count;
            else if(mode is "original" or "combined-repeat")
            {
                void Trace(string stage,object? value)
                {
                    if(stage=="originalTtmlCandidate")return;
                    if(value is AppleOriginalAnchors text)anchors=text.Lines.Count;
                    File.AppendAllText(Path.Combine(directory,"native-lyrics-diagnostic.jsonl"),JsonSerializer.Serialize(new{stage,value})+Environment.NewLine);
                }
                var reader=new AppleOriginalLyricsReader(trace:Trace);
                for(int i=0;i<(mode=="combined-repeat"?12:1);i++)
                {
                    var original=await reader.ReadAsync(source.Latest??expected,()=>source.Latest,token);accepted|=original is not null;documents+=original is null?0:1;
                    if(original is not null)File.WriteAllText(Path.Combine(directory,"native-ttml.json"),original.Provenance);
                    if(mode=="combined-repeat"){_=await AppleMusicLibraryHandoff.ReadQueueAsync(token);queueReads++;await Sample("combined");if(!process.Responding)break;}
                }
            }
            else throw new ArgumentException("Unknown safety probe mode.");
        }
        catch(Exception ex) when(ex is TimeoutException or InvalidOperationException or System.Runtime.InteropServices.COMException){error=ex.GetType().Name;}
        events.Add(new{stage="completed",utc=DateTimeOffset.UtcNow,elapsed=watch.Elapsed.TotalSeconds});
        for(int i=0;i<15;i++)await Sample("after");
        process.Refresh();var result=new{mode,anchors,documents,accepted,queueReads,error,responding=process.Responding,expected,actual=source.Latest,samples,events};
        File.WriteAllText(Path.Combine(directory,"native-read-safety.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
    }
}
