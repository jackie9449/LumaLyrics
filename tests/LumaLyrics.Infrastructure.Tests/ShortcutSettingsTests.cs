using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.Infrastructure.Tests;
public sealed class ShortcutSettingsTests
{
    [Fact] public void OldCustomBindingsMigrateWithoutChangingThem()
    {
        var settings=new AppSettings{ToggleHotkey="Ctrl+Shift+L",UnlockHotkey="Alt+U"};var bindings=ShortcutSettings.Read(settings);
        Assert.Equal("Ctrl+Shift+L",bindings[ShortcutAction.ToggleOverlay]);Assert.Equal("Alt+U",bindings[ShortcutAction.UnlockOverlay]);Assert.Equal("Ctrl+Alt+Space",bindings[ShortcutAction.PlayPause]);
    }
    [Fact] public void ExplicitlyDisabledBindingsSurviveSerialization()
    {
        var settings=new AppSettings();var bindings=ShortcutSettings.Read(settings);bindings[ShortcutAction.PlayPause]="";bindings[ShortcutAction.Next]="Ctrl+Shift+N";ShortcutSettings.Write(settings,bindings);
        var copy=System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(settings))!;
        Assert.Equal("",ShortcutSettings.Read(copy)[ShortcutAction.PlayPause]);Assert.Equal("Ctrl+Shift+N",ShortcutSettings.Read(copy)[ShortcutAction.Next]);
    }
    [Fact] public void NativeQueueParsingKeepsAlbumForSeeking()
    {
        var track=new Track("Original recording","Original artist","Original album",180000);
        var row=MusicPlaybackQueue.ParseNativeRow([track.Title,track.Artist+" — "+track.Album]);
        Assert.NotNull(row);Assert.True(PlaybackSeeking.MatchesNativeRecording(track,row.Track));Assert.False(PlaybackSeeking.MatchesNativeRecording(track,row.Track with{Album="Another album"}));
    }
}
