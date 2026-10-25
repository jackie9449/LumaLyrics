using System.Diagnostics;
using System.Text.Json;
using LumaLyrics.Windows;
using LumaLyrics.Infrastructure;

namespace LumaLyrics.Probe;

internal static class NativeMemorySafetyProbe
{
    internal static async Task RunAsync(string directory)
    {
        PersonalLibraryArchive.EnsureOutsideSoftware(directory);Directory.CreateDirectory(directory);
        using var token=new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await using var source=new GsmtcPlaybackSource();await source.StartAsync(token.Token);await source.RefreshAsync().WaitAsync(token.Token);
        using var process=Process.GetProcessesByName("AppleMusic").FirstOrDefault()??throw new InvalidOperationException("Apple Music is not running.");
        var samples=new List<object>();
        async Task Sample(string phase)
        {
            process.Refresh();double before=process.TotalProcessorTime.TotalMilliseconds;await Task.Delay(1000,token.Token);process.Refresh();
            samples.Add(new{phase,responding=process.Responding,cpuMs=process.TotalProcessorTime.TotalMilliseconds-before,utc=DateTimeOffset.UtcNow});
        }
        for(int i=0;i<5;i++)await Sample("baseline");
        if(!process.Responding)throw new InvalidOperationException("Native client is already unresponsive; no memory scan issued.");
        var before=source.Latest;double duration=before?.Track.DurationMs??600000;
        var read=Task.Run(()=>new AppleMusicMemoryLyrics().Read(process,duration,token.Token,(stage,value)=>
        {
            if(stage=="originalTtmlCandidate")return;
            File.AppendAllText(Path.Combine(directory,"native-memory-diagnostic.jsonl"),JsonSerializer.Serialize(new{stage,value})+Environment.NewLine);
        }),token.Token);
        while(!read.IsCompleted)await Sample("scan");var documents=await read;
        for(int i=0;i<15;i++)await Sample("after");
        process.Refresh();var after=source.Latest;
        var result=new{documents=documents.Count,responding=process.Responding,before,after,samples};
        File.WriteAllText(Path.Combine(directory,"native-memory-safety.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(JsonSerializer.Serialize(new{documents=documents.Count,responding=process.Responding,recordingRetained=before is not null && after is not null && before.Track==after.Track}));
    }
}
