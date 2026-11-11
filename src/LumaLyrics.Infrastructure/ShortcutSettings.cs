using LumaLyrics.Core;
namespace LumaLyrics.Infrastructure;

public static class ShortcutSettings
{
    public static Dictionary<ShortcutAction,string> Read(AppSettings settings)=>KeyboardShortcuts.All.ToDictionary(d=>d.Action,d=>
        settings.Shortcuts?.TryGetValue(d.Action.ToString(),out var saved)==true?saved??"":d.Action switch{ShortcutAction.ToggleOverlay=>settings.ToggleHotkey,ShortcutAction.UnlockOverlay=>settings.UnlockHotkey,_=>d.Default});
    public static void Write(AppSettings settings,IReadOnlyDictionary<ShortcutAction,string> bindings)
    {
        settings.Shortcuts=bindings.ToDictionary(p=>p.Key.ToString(),p=>p.Value);
        settings.ToggleHotkey=bindings.GetValueOrDefault(ShortcutAction.ToggleOverlay,"");settings.UnlockHotkey=bindings.GetValueOrDefault(ShortcutAction.UnlockOverlay,"");
    }
}
