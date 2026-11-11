namespace LumaLyrics.App;

public partial class MainWindow
{
    string DisplayLyricsStatus()
    {
        if(coordinator.Binding is { } binding && coordinator.Document is { } document)
        {
            string kind=document.IsSynced?"逐行歌词":"静态歌词";
            string canonical=binding.Instrumental?"纯音乐":$"{kind} · {binding.Source}{(binding.Manual?" · 已锁定绑定":"")}";
            if(coordinator.LyricsStatus==canonical)
                return binding.Instrumental?UiText.T("纯音乐"):$"{UiText.T(kind)} · {UiText.Status(binding.Source)}{(binding.Manual?UiText.T(" · 已锁定绑定"):"")}";
        }
        return UiText.Status(coordinator.LyricsStatus);
    }
}
