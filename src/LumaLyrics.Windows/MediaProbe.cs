using System.Diagnostics;
using System.Text.Json;
using Windows.Media.Control;
namespace LumaLyrics.Windows;

public static class MediaProbe
{
    public static async Task RunAsync(string path, int seconds, bool includeMetadata, CancellationToken token = default)
    {
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        using var writer = new StreamWriter(path, false) { AutoFlush = true };
        var ids = new Dictionary<string, int>();
        var gate = new SemaphoreSlim(1);
        async void Capture(string reason)
        {
            if (!await gate.WaitAsync(0)) return;
            try
            {
                var sessions = manager.GetSessions();
                if (sessions.Count == 0) writer.WriteLine(JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, reason, sessions = 0 }));
                foreach (var session in sessions)
                {
                    var media = await session.TryGetMediaPropertiesAsync();
                    var info = session.GetPlaybackInfo(); var t = session.GetTimelineProperties();
                    var identity = media.Title + "|" + media.Artist + "|" + media.AlbumTitle;
                    if (!ids.TryGetValue(identity, out int track)) ids[identity] = track = ids.Count + 1;
                    writer.WriteLine(JsonSerializer.Serialize(new {
                        utc = DateTimeOffset.UtcNow, monotonic = Stopwatch.GetTimestamp(), reason,
                        source = session.SourceAppUserModelId, track,
                        title = includeMetadata ? media.Title : null, artist = includeMetadata ? media.Artist : null,
                        album = includeMetadata ? media.AlbumTitle : null, thumbnail = media.Thumbnail is not null,
                        status = info.PlaybackStatus.ToString(), rate = info.PlaybackRate,
                        positionMs = t.Position.TotalMilliseconds, startMs = t.StartTime.TotalMilliseconds,
                        endMs = t.EndTime.TotalMilliseconds, updatedUtc = t.LastUpdatedTime,
                        minSeekMs = t.MinSeekTime.TotalMilliseconds, maxSeekMs = t.MaxSeekTime.TotalMilliseconds,
                        canPlay = info.Controls.IsPlayEnabled, canPause = info.Controls.IsPauseEnabled,
                        canSeek = info.Controls.IsPlaybackPositionEnabled,
                        canNext = info.Controls.IsNextEnabled, canPrevious = info.Controls.IsPreviousEnabled
                    }));
                }
            }
            catch (Exception e) { writer.WriteLine(JsonSerializer.Serialize(new { reason, error = e.GetType().Name, e.Message })); }
            finally { gate.Release(); }
        }
        var subscribed = new HashSet<GlobalSystemMediaTransportControlsSession>();
        void Subscribe()
        {
            foreach (var s in manager.GetSessions()) if (subscribed.Add(s))
            {
                s.MediaPropertiesChanged += Media; s.PlaybackInfoChanged += Playback; s.TimelinePropertiesChanged += Timeline;
            }
        }
        void Media(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) => Capture("metadata");
        void Playback(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) => Capture("playback");
        void Timeline(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs e) => Capture("timeline");
        void Sessions(GlobalSystemMediaTransportControlsSessionManager m, SessionsChangedEventArgs e) { Subscribe(); Capture("sessions"); }
        manager.SessionsChanged += Sessions; Subscribe(); Capture("start");
        try { for (int i = 0; i < seconds; i++) { await Task.Delay(1000, token); Subscribe(); Capture("poll"); } }
        finally
        {
            manager.SessionsChanged -= Sessions;
            foreach (var s in subscribed) { s.MediaPropertiesChanged -= Media; s.PlaybackInfoChanged -= Playback; s.TimelinePropertiesChanged -= Timeline; }
            await gate.WaitAsync(); gate.Release();
        }
    }
}
