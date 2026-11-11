namespace LumaLyrics.Core;

public enum ShortcutAction { ToggleOverlay=1,UnlockOverlay,PlayPause,Previous,Next,LyricsEarlier,LyricsLater,ResetOffset }
public sealed record ShortcutDefinition(ShortcutAction Action,string Label,string Default);
public static class KeyboardShortcuts
{
    public static IReadOnlyList<ShortcutDefinition> All {get;}=[
        new(ShortcutAction.PlayPause,"播放 / 暂停","Ctrl+Alt+Space"),
        new(ShortcutAction.Previous,"上一曲","Ctrl+Alt+Left"),
        new(ShortcutAction.Next,"下一曲","Ctrl+Alt+Right"),
        new(ShortcutAction.LyricsEarlier,"歌词提前 0.1 秒","Ctrl+Alt+Up"),
        new(ShortcutAction.LyricsLater,"歌词延后 0.1 秒","Ctrl+Alt+Down"),
        new(ShortcutAction.ResetOffset,"重置当前歌曲偏移","Ctrl+Alt+R"),
        new(ShortcutAction.ToggleOverlay,"显示 / 隐藏歌词","Ctrl+Alt+L"),
        new(ShortcutAction.UnlockOverlay,"解锁歌词","Ctrl+Alt+U")];
}

/// <summary>Portable, explicit syntax for Windows global shortcuts. Empty bindings disable an action.</summary>
public readonly record struct ShortcutGesture(uint Modifiers,uint Key,string Text)
{
    static readonly Dictionary<string,uint> keys=new(StringComparer.OrdinalIgnoreCase){["Space"]=0x20,["Left"]=0x25,["Up"]=0x26,["Right"]=0x27,["Down"]=0x28,["Home"]=0x24,["End"]=0x23,["PageUp"]=0x21,["PageDown"]=0x22,["Insert"]=0x2D,["Delete"]=0x2E,["Enter"]=0x0D,["Tab"]=0x09,["Escape"]=0x1B,["Backspace"]=0x08};
    public static bool TryParse(string? text,out ShortcutGesture gesture)
    {
        gesture=default;if(string.IsNullOrWhiteSpace(text))return false;
        var parts=text.Split('+',StringSplitOptions.TrimEntries);if(parts.Length<2)return false;
        uint modifiers=0;
        foreach(string part in parts[..^1])
        {
            uint flag=part.ToUpperInvariant() switch{"CTRL" or "CONTROL"=>2,"ALT"=>1,"SHIFT"=>4,_=>0};
            if(flag==0 || (modifiers&flag)!=0)return false;modifiers|=flag;
        }
        if((modifiers&3)==0)return false;
        string name=parts[^1];uint key;
        if(name.Length==1 && char.IsAsciiLetterOrDigit(name[0])){name=name.ToUpperInvariant();key=name[0];}
        else if(name.StartsWith("F",StringComparison.OrdinalIgnoreCase) && int.TryParse(name[1..],out int number) && number is >=1 and <=24){key=(uint)(0x70+number-1);name="F"+number;}
        else if(keys.TryGetValue(name,out key))name=keys.Single(p=>p.Value==key).Key;
        else return false;
        // Windows reserves F12 for debugging. Avoid security/task-switching shortcuts.
        if(key==0x7B || (modifiers&1)!=0 && key is 0x09 or 0x1B or 0x73 || (modifiers&2)!=0 && key==0x1B || (modifiers&3)==3 && key==0x2E)return false;
        string prefix=((modifiers&2)!=0?"Ctrl+":"")+((modifiers&1)!=0?"Alt+":"")+((modifiers&4)!=0?"Shift+":"");
        gesture=new(modifiers,key,prefix+name);return true;
    }
    public static IEnumerable<ShortcutGesture> Alternatives(string preferred)
    {
        if(TryParse(preferred,out var parsed))
        {
            if((parsed.Modifiers&4)==0 && TryParse("Ctrl+Alt+Shift+"+parsed.Text.Split('+')[^1],out var shifted))yield return shifted;
        }
        for(int number=1;number<=11;number++)if(TryParse("Ctrl+Alt+Shift+F"+number,out var fallback))yield return fallback;
    }
}
