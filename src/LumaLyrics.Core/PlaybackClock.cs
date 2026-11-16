namespace LumaLyrics.Core;

public interface IPlaybackClock { double? PositionMs { get; } PositionQuality Quality { get; } void Update(PlaybackSnapshot? snapshot); void Invalidate(); }

/// <summary>Source positions anchor at capture time, independently of dispatcher delivery.</summary>
public sealed class PlaybackClock(TimeProvider? provider = null) : IPlaybackClock
{
    readonly TimeProvider time = provider ?? TimeProvider.System;
    PlaybackSnapshot? previous;
    double anchor; long anchorTicks; long evidenceTicks; double evidenceAgeMs; bool valid; bool advancing;
    double rate = 1; DateTimeOffset? acceptedUtc, playingSinceUtc; double? acceptedRaw;
    public PositionQuality Quality { get; private set; }
    public double? PositionMs
    {
        get
        {
            var position=At(time.GetTimestamp());
            if(position is null)Quality=PositionQuality.Unavailable;
            return position;
        }
    }
    double? At(long ticks)
    {
        if(!valid || advancing && evidenceAgeMs+time.GetElapsedTime(evidenceTicks,ticks).TotalMilliseconds>90000)return null;
        double position=anchor+(advancing?time.GetElapsedTime(anchorTicks,ticks).TotalMilliseconds*rate:0);
        double maximum=previous?.Track.DurationMs is double duration && double.IsFinite(duration) && duration>=0?duration:double.MaxValue;
        return Math.Clamp(position,0,maximum);
    }
    public void Invalidate() { valid = false; advancing = false; acceptedUtc = playingSinceUtc = null; acceptedRaw = null; Quality = PositionQuality.Unavailable; }
    public void Update(PlaybackSnapshot? s)
    {
        if (s is null) { Invalidate(); previous = null; return; }
        // Capture order, not callback arrival order, owns both state and recording changes.
        if(previous is not null && s.MonotonicTicks<previous.MonotonicTicks)return;
        bool changed = previous is null || s.Generation != previous.Generation || s.Revision != previous.Revision || s.SessionId != previous.SessionId;
        double? predicted = At(s.MonotonicTicks);
        if (changed) { Invalidate(); predicted = null; }
        bool statusChanged = !changed && s.Status != previous!.Status;
        bool playing=s.Status==PlayState.Playing;
        bool resumed=statusChanged && playing;
        double nextRate=s.Rate is >0 and <=4?s.Rate.Value:1;
        bool rateChanged=!changed && nextRate!=rate;
        // Evidence from within a pause is never projected across that pause, even on later polls.
        if(resumed)playingSinceUtc=s.CapturedUtc;
        bool newEvidence = s.PositionMs.HasValue && (acceptedUtc != s.TimelineUtc || acceptedRaw != s.PositionMs);
        // Apple Music on this machine publishes integral seconds with intervening
        // timestamps for the same value. Do not reset interpolation on those repeats.
        bool quantizedRepeat = !changed && !statusChanged && acceptedRaw==s.PositionMs && playing && s.Quality!=PositionQuality.Reliable;
        double ageMs = s.TimelineUtc.HasValue ? (s.CapturedUtc - s.TimelineUtc.Value).TotalMilliseconds : double.NaN;
        bool sane = s.Quality != PositionQuality.Unavailable && s.PositionMs is double raw && double.IsFinite(raw)
            && ageMs >= -1000 && (!playing || resumed || ageMs < 90000);
        bool ordered=acceptedUtc is null || s.TimelineUtc>=acceptedUtc;
        bool withinPlayback=!playing || resumed || playingSinceUtc is null || s.TimelineUtc>=playingSinceUtc;
        if (newEvidence && sane && ordered && withinPlayback && !quantizedRepeat)
        {
            // Do not average authoritative positions with history: independent consumers
            // must converge to the same source timeline, including small seeks/corrections.
            evidenceAgeMs=playing && !resumed && !rateChanged?Math.Max(0,ageMs):0;
            anchor=s.PositionMs!.Value+evidenceAgeMs*nextRate;
            anchorTicks = s.MonotonicTicks; evidenceTicks = anchorTicks; valid = true;
            acceptedUtc = s.TimelineUtc; acceptedRaw = s.PositionMs;
            Quality = s.Quality==PositionQuality.Reliable && s.Rate is >0 and <=4 ? PositionQuality.Reliable : PositionQuality.Estimated;
        }
        else
        {
            if(quantizedRepeat && sane && ordered && withinPlayback && newEvidence)acceptedUtc=s.TimelineUtc;
            if ((statusChanged || rateChanged) && predicted.HasValue)
            {
                anchor=predicted.Value;anchorTicks=s.MonotonicTicks;
                if(resumed){evidenceTicks=anchorTicks;evidenceAgeMs=0;}
                Quality=PositionQuality.Estimated;
            }
        }
        rate=nextRate;
        advancing = valid && playing;
        previous = s;
    }
}
