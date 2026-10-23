using System.Diagnostics;
using LumaLyrics.Core;
using Windows.Media.Control;
namespace LumaLyrics.Windows;

public sealed class GsmtcPlaybackSource : IPlaybackSource
{
    GlobalSystemMediaTransportControlsSessionManager? manager;
    GlobalSystemMediaTransportControlsSession? selected;
    readonly SemaphoreSlim gate = new(1);
    readonly CancellationTokenSource lifetime = new();
    Task? poll; long generation, revision; string? fingerprint;
    PlaybackSnapshot? latest;
    public PlaybackSnapshot? Latest=>Volatile.Read(ref latest);
    void Publish(PlaybackSnapshot? snapshot){Volatile.Write(ref latest,snapshot);Changed?.Invoke(snapshot);}
    string? preferredId; DateTimeOffset? minimumTimeline;
    public event Action<PlaybackSnapshot?>? Changed;
    public event Action<string>? Error;
    public event Action? SessionsUpdated;
    public IReadOnlyList<string> SessionIds => manager?.GetSessions().Select(s => s.SourceAppUserModelId).Distinct().ToArray() ?? [];
    public string? SelectedId => selected?.SourceAppUserModelId;
    public GsmtcPlaybackSource(string? preferred = null) { preferredId = preferred; }
    public async Task StartAsync(CancellationToken token = default)
    {
        manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        manager.SessionsChanged += OnSessions;
        await RefreshAsync();
        poll = Task.Run(async () => {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            try { while (await timer.WaitForNextTickAsync(lifetime.Token)) await RefreshAsync(); }
            catch (OperationCanceledException) { }
        }, lifetime.Token);
    }
    public async Task SelectAsync(string id) { preferredId = id; Detach(); await RefreshAsync(); }
    public async Task ReconnectAsync()
    {
        if(manager is null){await StartAsync();return;}
        await gate.WaitAsync(lifetime.Token);
        try{Detach();}finally{gate.Release();}
        await RefreshAsync();
    }
    async void OnSessions(GlobalSystemMediaTransportControlsSessionManager s, SessionsChangedEventArgs e) => await RefreshAsync();
    async void OnMedia(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) => await RefreshAsync();
    async void OnPlayback(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) => await RefreshAsync();
    async void OnTimeline(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs e) { try { await Task.Delay(120,lifetime.Token); await RefreshAsync(); } catch(OperationCanceledException) { } }
    void Detach()
    {
        if (selected is not null) { selected.MediaPropertiesChanged -= OnMedia; selected.PlaybackInfoChanged -= OnPlayback; selected.TimelinePropertiesChanged -= OnTimeline; }
        selected = null; fingerprint = null; minimumTimeline=null; generation++;
    }
    public async Task RefreshAsync()
    {
        if (lifetime.IsCancellationRequested || !await gate.WaitAsync(0)) return;
        try
        {
            var sessions = manager?.GetSessions();
            if (sessions is null) return;
            if (selected is not null && !sessions.Any(s => s.Equals(selected))) Detach();
            if (selected is null)
            {
                selected = sessions.FirstOrDefault(s => preferredId is not null && s.SourceAppUserModelId == preferredId)
                    ?? sessions.FirstOrDefault(s => s.SourceAppUserModelId.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase));
                if (selected is not null) { generation++; selected.MediaPropertiesChanged += OnMedia; selected.PlaybackInfoChanged += OnPlayback; selected.TimelinePropertiesChanged += OnTimeline; }
                SessionsUpdated?.Invoke();
            }
            if (selected is null) { Publish(null); return; }
            var session = selected; var gen = generation;
            var m = await session.TryGetMediaPropertiesAsync();
            if (session != selected || gen != generation || lifetime.IsCancellationRequested) return;
            var p = session.GetPlaybackInfo(); var t = session.GetTimelineProperties();
            double duration = (t.EndTime - t.StartTime).TotalMilliseconds;
            var track = new Track(m.Title, m.Artist, m.AlbumTitle, duration > 0 ? duration : null);
            if(session.SourceAppUserModelId.Contains("AppleMusic",StringComparison.OrdinalIgnoreCase))track=TrackMetadata.FromAppleSession(track);
            if (track.Fingerprint != fingerprint) { if(fingerprint is not null)minimumTimeline=DateTimeOffset.UtcNow; fingerprint = track.Fingerprint; revision++; }
            bool staleForTrack=minimumTimeline.HasValue && t.LastUpdatedTime<minimumTimeline.Value;
            if(staleForTrack)track=track with{DurationMs=null};
            var state = p.PlaybackStatus switch { GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => PlayState.Playing, GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => PlayState.Paused, GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed => PlayState.Closed, _ => PlayState.Stopped };
            var quality = !staleForTrack && t.LastUpdatedTime.Year > 2000 && duration > 0 ? PositionQuality.Estimated : PositionQuality.Unavailable;
            var c = p.Controls;
            Publish(new(session.SourceAppUserModelId, generation, revision, track, state, p.PlaybackRate,
                quality != PositionQuality.Unavailable ? (t.Position - t.StartTime).TotalMilliseconds : null,
                t.LastUpdatedTime.Year > 2000 ? t.LastUpdatedTime : null, DateTimeOffset.UtcNow, Stopwatch.GetTimestamp(), quality,
                c.IsPlayEnabled, c.IsPauseEnabled, c.IsNextEnabled, c.IsPreviousEnabled, c.IsPlaybackPositionEnabled));
        }
        catch (Exception e) { Error?.Invoke($"媒体会话读取失败：{e.Message}"); Detach(); Publish(null); }
        finally { gate.Release(); }
    }
    public async Task<bool> ControlAsync(string action, double? ms = null)
    {
        var s = selected; if (s is null) return false;
        try
        {
            var c = s.GetPlaybackInfo().Controls;
            return action switch {
                "play" when c.IsPlayEnabled => await s.TryPlayAsync(),
                "pause" when c.IsPauseEnabled => await s.TryPauseAsync(),
                "next" when c.IsNextEnabled => await s.TrySkipNextAsync(),
                "previous" when c.IsPreviousEnabled => await s.TrySkipPreviousAsync(),
                "seek" when c.IsPlaybackPositionEnabled && ms.HasValue => await s.TryChangePlaybackPositionAsync(s.GetTimelineProperties().StartTime.Ticks + TimeSpan.FromMilliseconds(ms.Value).Ticks),
                _ => false
            };
        }
        catch (Exception e) { Error?.Invoke(e.Message); return false; }
    }
    public async Task<byte[]?> GetArtworkAsync()
    {
        var s = selected; if (s is null) return null;
        var m = await s.TryGetMediaPropertiesAsync(); if (m.Thumbnail is null) return null;
        using var stream = await m.Thumbnail.OpenReadAsync();
        if (stream.Size > 8 * 1024 * 1024) return null;
        using var reader = new global::Windows.Storage.Streams.DataReader(stream);
        await reader.LoadAsync((uint)stream.Size); var data = new byte[(int)stream.Size]; reader.ReadBytes(data); return data;
    }
    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel(); if (manager is not null) manager.SessionsChanged -= OnSessions;
        if (poll is not null) await poll;
        await gate.WaitAsync(); try { Detach(); } finally { gate.Release(); }
        lifetime.Dispose();
    }
}
