using System.Diagnostics;
using System.Text.Json;
using LumaLyrics.Core;
using LumaLyrics.Windows;

namespace LumaLyrics.Probe;

internal static class TimingProbe
{
    internal static async Task RunAsync(string output,bool play)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        await using var source=new GsmtcPlaybackSource();
        var clock=new PlaybackClock();
        source.Changed+=snapshot=>{lock(clock)clock.Update(snapshot);};
        await source.StartAsync();
        var original=source.Latest;
        string database=Path.Combine(LumaLyrics.Infrastructure.DataDirectoryConfiguration.Resolve(),"lyrics.db");
        if(original is not null && File.Exists(database))
        {
            using var repository=new LumaLyrics.Infrastructure.LyricsRepository(database,readOnly:true);
            var binding=repository.FindReadOnly(original.Track);
            Console.WriteLine(JsonSerializer.Serialize(new{bindingFound=binding is not null,songOffsetMs=binding?.OffsetMs,
                fileOffsetMs=binding is null?(double?)null:new LrcParser().Parse(binding.Raw).FileOffsetMs}));
        }
        bool started=false;
        try
        {
            if(play && original?.Status==PlayState.Paused)started=await source.ControlAsync("play");
            using var writer=new StreamWriter(output);
            for(int i=0;i<100;i++)
            {
                await Task.Delay(200);
                var snapshot=source.Latest;
                double? position;lock(clock)position=clock.PositionMs;
                writer.WriteLine(JsonSerializer.Serialize(new{utc=DateTimeOffset.UtcNow,status=snapshot?.Status.ToString(),rawMs=snapshot?.PositionMs,
                    timelineUtc=snapshot?.TimelineUtc,capturedUtc=snapshot?.CapturedUtc,clockMs=position,
                    snapshotAgeMs=snapshot is null?0:Stopwatch.GetElapsedTime(snapshot.MonotonicTicks).TotalMilliseconds}));
            }
        }
        finally
        {
            if(started && source.Latest is { } current && original is not null
                && current.SessionId==original.SessionId && current.Generation==original.Generation && current.Revision==original.Revision)
                await source.ControlAsync("pause");
        }
    }
}
