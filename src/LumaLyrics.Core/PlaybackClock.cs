namespace LumaLyrics.Core;

public interface IPlaybackClock { double? PositionMs { get; } PositionQuality Quality { get; } void Update(PlaybackSnapshot? snapshot); void Invalidate(); }

/// <summary>Only new source evidence may move the anchor. A repeated timeline is not a heartbeat.</summary>
public sealed class PlaybackClock(TimeProvider? provider = null) : IPlaybackClock
{
    readonly TimeProvider time = provider ?? TimeProvider.System;
    PlaybackSnapshot? previous;
    double anchor; long anchorTicks; long evidenceTicks; bool valid; bool advancing;
    double rate = 1; DateTimeOffset? acceptedUtc; double? acceptedRaw;
    public PositionQuality Quality { get; private set; }
    public double? PositionMs
    {
        get
        {
            if (!valid) return null;
            double age = time.GetElapsedTime(evidenceTicks).TotalSeconds;
            if (advancing && age > 90) { Quality = PositionQuality.Unavailable; return null; }
            double p = anchor + (advancing ? time.GetElapsedTime(anchorTicks).TotalMilliseconds * rate : 0);
            return Math.Clamp(p, 0, previous?.Track.DurationMs ?? double.MaxValue);
        }
    }
    public void Invalidate() { valid = false; advancing = false; acceptedUtc = null; acceptedRaw = null; Quality = PositionQuality.Unavailable; }
    public void Update(PlaybackSnapshot? s)
    {
        if (s is null) { Invalidate(); previous = null; return; }
        bool changed = previous is null || s.Generation != previous.Generation || s.Revision != previous.Revision || s.SessionId != previous.SessionId;
        double? predicted = PositionMs;
        if (changed) { Invalidate(); predicted = null; }
        bool statusChanged = previous is not null && s.Status != previous.Status;
        bool newEvidence = s.PositionMs.HasValue && (acceptedUtc != s.TimelineUtc || acceptedRaw != s.PositionMs);
        // Apple Music on this machine publishes integral seconds with intervening
        // timestamps for the same value. Do not reset interpolation on those repeats.
        bool quantizedRepeat = !changed && !statusChanged && acceptedRaw==s.PositionMs && s.Status==PlayState.Playing;
        double ageMs = s.TimelineUtc.HasValue ? (s.CapturedUtc - s.TimelineUtc.Value).TotalMilliseconds : double.NaN;
        bool sane = s.Quality != PositionQuality.Unavailable && ageMs >= -1000 && (s.Status == PlayState.Paused || ageMs < 90000);
        // A state transition accompanied by an old timeline cannot project across a pause.
        bool transitionStale = statusChanged && ageMs > 2000;
        if (newEvidence && sane && !transitionStale && !quantizedRepeat)
        {
            rate = s.Rate is > 0 and <= 4 ? s.Rate.Value : 1;
            var target = s.PositionMs!.Value + (s.Status == PlayState.Playing ? Math.Max(0, ageMs) * rate : 0);
            double error = predicted.HasValue ? target-predicted.Value : double.MaxValue;
            anchor = !changed && !statusChanged && Math.Abs(error)<500 ? predicted!.Value+error*.35 : target;
            anchorTicks = time.GetTimestamp(); evidenceTicks = anchorTicks; valid = true;
            acceptedUtc = s.TimelineUtc; acceptedRaw = s.PositionMs;
            Quality = s.Quality==PositionQuality.Reliable && s.Rate is >0 and <=4 ? PositionQuality.Reliable : PositionQuality.Estimated;
        }
        else if(quantizedRepeat && sane && newEvidence) { acceptedUtc=s.TimelineUtc; }
        else if (statusChanged && predicted.HasValue)
        {
            anchor = predicted.Value; anchorTicks = time.GetTimestamp();
            Quality = PositionQuality.Estimated;
        }
        advancing = valid && s.Status == PlayState.Playing;
        previous = s;
    }
}
