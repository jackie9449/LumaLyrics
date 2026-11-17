using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using LumaLyrics.Core;

namespace LumaLyrics.App;

public partial class MainWindow
{
    async Task VerifyPlaybackClockAsync(string directory,Dictionary<string,object?> checks)
    {
        settings.OnlineLookup=false;settings.AutoPrefetchLibrary=false;settings.AppleMusicOriginalLyrics=false;
        var track=new Track("时钟航线","浮光","原创同步验证",180000);
        const string raw="[00:00]We start beside the water\n[00:10]The morning opens slowly\n[00:12]We cross the quiet bridge\n[00:30]The evening brings us home";
        coordinator.Import(track,raw,"Original clock fixture",false);
        coordinator.ImportChineseTranslation("[00:00]我们从水边出发\n[00:10]清晨缓缓展开\n[00:12]我们走过安静的桥\n[00:30]暮色带我们回家");
        coordinator.SetTranslationVisibility(true);overlay.ApplyStyle();overlay.Show();
        PlaybackSnapshot Capture(double position,PlayState state,DateTimeOffset? stamp=null)=>new("clock-fixture",1,1,track,state,1,position,stamp??DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,Stopwatch.GetTimestamp(),PositionQuality.Estimated);
        async Task Deliver(PlaybackSnapshot snapshot,int delay=0)
        {
            if(delay>0)await Task.Delay(delay);
            coordinator.QueueClockVerificationSnapshot(snapshot);
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.Background);Tick();
        }
        double Expected(PlaybackSnapshot s)=>s.PositionMs!.Value+(s.Status==PlayState.Playing?Stopwatch.GetElapsedTime(s.MonotonicTicks).TotalMilliseconds:0);
        void Near(string name,double expected)=>checks["clock."+name]=Math.Abs(vm.Position-expected)<120;

        var initial=Capture(10000,PlayState.Paused);await Deliver(initial);
        Near("initialPause",10000);
        var resume=Capture(10000,PlayState.Playing,initial.TimelineUtc);await Deliver(resume,2200);
        Near("resumeIncludesDelivery",Expected(resume));
        checks["clock.originalAndTranslationAdvanceTogether"]=vm.Current=="We cross the quiet bridge" && vm.CurrentTranslation=="我们走过安静的桥";
        checks["clock.floatingUsesSamePair"]=overlay.LyricSurface.CurrentLine.Text==vm.Current && overlay.LyricSurface.CurrentChinese.Text==vm.CurrentTranslation;
        checks["clock.sliderMatchesViewModel"]=Math.Abs(SeekSlider.Value-vm.Position)<1;

        var fresh=Capture(16000,PlayState.Playing);await Deliver(fresh);
        var pause=Capture(16000,PlayState.Paused,fresh.TimelineUtc);double pauseExpected=16000+Stopwatch.GetElapsedTime(fresh.MonotonicTicks,pause.MonotonicTicks).TotalMilliseconds;
        await Deliver(pause,1200);Near("delayedPauseStopsAtCapture",pauseExpected);
        double frozen=vm.Position;await Task.Delay(180);Tick();Near("pauseStaysFrozen",frozen);

        var paused=Capture(10500,PlayState.Paused);await Deliver(paused);
        await Task.Delay(100);var duringPause=DateTimeOffset.UtcNow;await Task.Delay(350);
        var shortResume=Capture(10500,PlayState.Playing,duringPause);await Deliver(shortResume);
        Near("shortPauseNotCounted",Expected(shortResume));
        await Task.Delay(100);var repeat=Capture(10500,PlayState.Playing,duringPause.AddMilliseconds(1));await Deliver(repeat);
        Near("laterStalePollNotCounted",Expected(shortResume));

        var seek=Capture(30000,PlayState.Playing);await Deliver(seek);Near("freshSeek",Expected(seek));
        checks["clock.seekUsesCorrectPair"]=vm.Current=="The evening brings us home" && vm.CurrentTranslation=="暮色带我们回家";
        coordinator.SetOffset(1000);Tick();checks["clock.songOffsetPreserved"]=vm.Current=="We cross the quiet bridge";
        settings.GlobalOffsetMs=-1000;Tick();checks["clock.globalOffsetPreserved"]=vm.Current=="The evening brings us home";
        checks["clock.rawAndTranslationHashUnchanged"]=coordinator.Binding?.Raw==raw && coordinator.Translation?.SourceHash==TranslationAlignment.Hash(raw);
        coordinator.SetOffset(0);settings.GlobalOffsetMs=0;
        await Deliver(Capture(12000,PlayState.Paused));await Task.Delay(300);UpdateLayout();
        VisualVerification.Save((FrameworkElement)Content,Path.Combine(directory,"playback-clock.png"),true);
        coordinator.QueueClockVerificationSnapshot(null);await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.Background);
        checks["clock.disconnectClearsPosition"]=coordinator.Position is null;overlay.Hide();
    }

    // Explicit opt-in, isolated data, no controls/seek/library/native-UI commands.
    async Task VerifyLiveClockAsync(string directory,Dictionary<string,object?> checks)
    {
        settings.OnlineLookup=false;settings.AutoPrefetchLibrary=false;settings.AppleMusicOriginalLyrics=false;
        var samples=new List<object>();var freshErrors=new List<double>();var displayLags=new List<double>();
        PlaybackSnapshot? previous=null;int playing=0;
        try
        {
            await coordinator.Source.StartAsync();timer.Start();
            for(int i=0;i<200;i++)
            {
                await Task.Delay(100);
                var s=coordinator.Snapshot;var latest=coordinator.Source.Latest;
                if(s is not {Status:PlayState.Playing,PositionMs:not null,TimelineUtc:not null} || latest is null
                    || s.SessionId!=latest.SessionId || s.Generation!=latest.Generation || s.Revision!=latest.Revision || s.MonotonicTicks!=latest.MonotonicTicks)continue;
                double? position=coordinator.Position;if(position is null)continue;
                playing++;
                double elapsed=Stopwatch.GetElapsedTime(s.MonotonicTicks).TotalMilliseconds;
                double source=s.PositionMs.Value+(Math.Max(0,(s.CapturedUtc-s.TimelineUtc.Value).TotalMilliseconds)+elapsed)*(s.Rate is >0 and <=4?s.Rate.Value:1);
                double lag=position.Value-vm.Position;displayLags.Add(lag);
                bool fresh=previous is not null && previous.Generation==s.Generation && previous.Revision==s.Revision && previous.PositionMs!=s.PositionMs;
                if(fresh)freshErrors.Add(position.Value-source);
                samples.Add(new{status="Playing",rawMs=s.PositionMs,sourceProjectedMs=source,clockMs=position,displayedMs=vm.Position,displayLagMs=lag,fresh});
                previous=s;
            }
        }
        finally{timer.Stop();}
        File.WriteAllLines(Path.Combine(directory,"live-clock-private.jsonl"),samples.Select(sample=>JsonSerializer.Serialize(sample)));
        checks["liveClock.playingSamples"]=playing;checks["liveClock.freshSamples"]=freshErrors.Count;
        checks["liveClock.hasPlayingCoverage"]=playing>=100 && freshErrors.Count>=10;
        checks["liveClock.maximumFreshErrorMs"]=freshErrors.Count>0?freshErrors.Max(Math.Abs):(double?)null;
        checks["liveClock.maximumDisplayLagMs"]=displayLags.Count>0?displayLags.Max(Math.Abs):(double?)null;
        checks["liveClock.matchesFreshSourceWithin20ms"]=freshErrors.Count>=10 && freshErrors.All(x=>Math.Abs(x)<20);
        checks["liveClock.displayWithin150ms"]=displayLags.Count>=100 && displayLags.All(x=>Math.Abs(x)<150);
    }
}

public sealed partial class PlaybackCoordinator
{
    internal void QueueClockVerificationSnapshot(PlaybackSnapshot? snapshot)
    {
        if(!Environment.GetCommandLineArgs().Contains("--verify-ui"))throw new InvalidOperationException("Clock fixtures require isolated UI verification.");
        QueueSnapshot(snapshot);
    }
}
