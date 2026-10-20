namespace LumaLyrics.Core;

public enum PlayState { Closed, Stopped, Paused, Playing }
public enum PositionQuality { Unavailable, Estimated, Reliable }
public sealed record Track(string Title, string Artist, string Album, double? DurationMs)
{
    public string Fingerprint => string.Join("|", MatchRules.Normalize(Title), MatchRules.Normalize(Artist), MatchRules.Normalize(Album));
}
public sealed record PlaybackSnapshot(string SessionId, long Generation, long Revision, Track Track,
    PlayState Status, double? Rate, double? PositionMs, DateTimeOffset? TimelineUtc,
    DateTimeOffset CapturedUtc, long MonotonicTicks, PositionQuality Quality,
    bool CanPlay = false, bool CanPause = false, bool CanNext = false, bool CanPrevious = false, bool CanSeek = false);
public sealed record TimedLineGroup(double TimeMs, string[] Lines)
{
    public string Text => string.Join(Environment.NewLine, Lines);
}
public sealed record LyricsDocument(string Raw, IReadOnlyList<TimedLineGroup> Groups, double FileOffsetMs,
    IReadOnlyList<string> Warnings, bool Instrumental = false)
{
    public bool IsSynced => Groups.Count > 0;
    public int FindIndex(double positionMs, double songOffsetMs = 0, double globalOffsetMs = 0)
    {
        var target = positionMs - FileOffsetMs - songOffsetMs - globalOffsetMs;
        int lo = 0, hi = Groups.Count - 1, answer = -1;
        while (lo <= hi) { int m = (lo + hi) / 2; if (Groups[m].TimeMs <= target) { answer = m; lo = m + 1; } else hi = m - 1; }
        return answer;
    }
}
public sealed record LyricsCandidate(long Id, Track Track, string? SyncedLyrics, string? PlainLyrics, bool Instrumental, string RawJson = "")
{
    public string Content => !string.IsNullOrWhiteSpace(SyncedLyrics)?SyncedLyrics:PlainLyrics??"";
    public override string ToString() => $"{Track.Title} · {Track.Artist}\n{Track.Album} · {Track.DurationMs / 1000:0}s · {(Instrumental ? "纯音乐" : SyncedLyrics is not null ? "同步歌词" : "静态歌词")} · LRCLIB #{Id}";
}
public sealed record ScoredCandidate(LyricsCandidate Candidate, double Score, bool Eligible, string Reason);
public interface IPlaybackSource : IAsyncDisposable
{
    event Action<PlaybackSnapshot?>? Changed;
    Task StartAsync(CancellationToken token = default);
}
public interface ILyricsParser { LyricsDocument Parse(string raw, bool forcePlain = false); }
public interface ILyricsProvider { Task<IReadOnlyList<LyricsCandidate>> LookupAsync(Track track, CancellationToken token); }
public sealed record LyricsBinding(string Id, string TrackId, string Raw, string Source, bool Manual, double OffsetMs, bool Instrumental, bool Plain = false,string? ProviderJson=null);
public interface ILyricsRepository
{
    LyricsBinding? Find(Track track);
    LyricsBinding Save(Track track, string raw, string source, bool manual, bool instrumental = false, bool plain = false, string? providerJson = null);
    void SetOffset(string bindingId, double offsetMs);
    void RestoreAutomatic(Track track);
    void ClearNetworkCache();
}
