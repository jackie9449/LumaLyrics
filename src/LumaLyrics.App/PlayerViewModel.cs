using System.ComponentModel;
using System.Runtime.CompilerServices;
using LumaLyrics.Core;
namespace LumaLyrics.App;
public sealed class PlayerViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    void Set<T>(ref T field, T value, [CallerMemberName] string name = "") { if (EqualityComparer<T>.Default.Equals(field,value)) return; field = value; PropertyChanged?.Invoke(this,new(name)); }
    string title = "让歌词，浮在音乐之上。", artist = "打开 Apple Music，播放你喜欢的歌", status = "等待 Apple Music", lyricStatus = "播放后会自动查找歌词", time = "--:-- / --:--", offset = "歌词偏移 0.0 秒", current = "浮光歌词", next = "等待播放", quality = "尚无播放时间轴";
    public string Title { get => title; set => Set(ref title,value); }
    public string Artist { get => artist; set => Set(ref artist,value); }
    public string Status { get => status; set => Set(ref status,value); }
    public string LyricStatus { get => lyricStatus; set => Set(ref lyricStatus,value); }
    public string Time { get => time; set => Set(ref time,value); }
    public string Offset { get => offset; set => Set(ref offset,value); }
    public string Current { get => current; set => Set(ref current,value); }
    public string Next { get => next; set => Set(ref next,value); }
    string currentTranslation="",nextTranslation="";
    public string CurrentTranslation {get=>currentTranslation;set=>Set(ref currentTranslation,value);}
    public string NextTranslation {get=>nextTranslation;set=>Set(ref nextTranslation,value);}
    public string Quality { get => quality; set => Set(ref quality,value); }
    string library="资料库：等待扫描";
    public string Library {get=>library;set=>Set(ref library,value);}
    string libraryLabel="资料库";
    public string LibraryLabel {get=>libraryLabel;set=>Set(ref libraryLabel,value);}
    double position, duration = 1;
    public double Position { get => position; set => Set(ref position,value); }
    public double Duration { get => duration; set => Set(ref duration,value); }
    bool canPlay, canNext, canPrevious, canSeek;
    public bool CanPlay { get => canPlay; set => Set(ref canPlay,value); }
    public bool CanNext { get => canNext; set => Set(ref canNext,value); }
    public bool CanPrevious { get => canPrevious; set => Set(ref canPrevious,value); }
    public bool CanSeek { get => canSeek; set => Set(ref canSeek,value); }
    bool isPlaying;
    public bool IsPlaying {get=>isPlaying;set{if(isPlaying==value)return;Set(ref isPlaying,value);PropertyChanged?.Invoke(this,new(nameof(PlayGlyph)));PropertyChanged?.Invoke(this,new(nameof(PlayLabel)));}}
    public string PlayGlyph=>IsPlaying?"\uE769":"\uE768";
    public string PlayLabel=>IsPlaying?"暂停":"播放";
}
