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
        PlaybackSnapshot? observed=null;
        source.Changed+=snapshot=>{lock(clock){clock.Update(snapshot);observed=snapshot;}};
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
            PlaybackSnapshot? previous=null;
            for(int i=0;i<100;i++)
            {
                await Task.Delay(200);
                PlaybackSnapshot? snapshot;double? position,projected;double snapshotAge;bool fresh;
                lock(clock)
                {
                    snapshot=observed;position=clock.PositionMs;
                    snapshotAge=snapshot is null?0:Stopwatch.GetElapsedTime(snapshot.MonotonicTicks).TotalMilliseconds;
                    projected=snapshot?.PositionMs;
                    if(snapshot is {Status:PlayState.Playing,TimelineUtc:not null})projected+=(Math.Max(0,(snapshot.CapturedUtc-snapshot.TimelineUtc.Value).TotalMilliseconds)+snapshotAge)*(snapshot.Rate is >0 and <=4?snapshot.Rate.Value:1);
                    fresh=snapshot is not null && previous is not null && snapshot.Generation==previous.Generation && snapshot.Revision==previous.Revision && snapshot.PositionMs!=previous.PositionMs;
                }
                writer.WriteLine(JsonSerializer.Serialize(new{utc=DateTimeOffset.UtcNow,status=snapshot?.Status.ToString(),rawMs=snapshot?.PositionMs,
                    timelineUtc=snapshot?.TimelineUtc,capturedUtc=snapshot?.CapturedUtc,clockMs=position,
                    snapshotAgeMs=snapshotAge,sourceProjectedMs=projected,clockMinusSourceMs=position-projected,freshPosition=fresh}));
                previous=snapshot;
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
