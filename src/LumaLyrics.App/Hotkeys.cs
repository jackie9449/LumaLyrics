using System.Windows;
using System.Windows.Interop;
using LumaLyrics.Core;
namespace LumaLyrics.App;

internal sealed class Hotkeys : IDisposable
{
    readonly HwndSource source;
    readonly Action<ShortcutAction> action;
    internal ShortcutRegistry Registry {get;}
    internal Hotkeys(Window window,Action<ShortcutAction> action)
    {
        var handle=new WindowInteropHelper(window).EnsureHandle();source=HwndSource.FromHwnd(handle);this.action=action;
        Registry=new(new Backend(handle));source.AddHook(Hook);
    }
    sealed class Backend(nint handle) : IShortcutBackend
    {
        public bool Register(int id,ShortcutGesture gesture)=>NativeWindow.RegisterHotKey(handle,id,gesture.Modifiers|0x4000,gesture.Key);
        public void Unregister(int id)=>NativeWindow.UnregisterHotKey(handle,id);
    }
    nint Hook(nint h,int msg,nint w,nint l,ref bool handled)
    {
        var id=(ShortcutAction)(int)w;
        if(msg==0x312 && Registry.Active.TryGetValue(id,out var gesture)
            && ((long)l&0xFFFF)==gesture.Modifiers && (((long)l>>16)&0xFFFF)==gesture.Key)
        {if(HotkeySettingsPanel.IsRecording)HotkeySettingsPanel.CaptureRegisteredGesture(gesture.Text);else action(id);handled=true;}
        return 0;
    }
    public void Dispose(){Registry.Dispose();source.RemoveHook(Hook);}
}
