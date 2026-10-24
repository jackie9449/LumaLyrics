using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
using Microsoft.Win32;
namespace LumaLyrics.App;

internal static class DialogUi
{
    public static StackPanel Panel(Window window,string heading,string hint)
    {
        window.Width=650;window.Height=650;window.MinWidth=480;window.MinHeight=400;window.WindowStartupLocation=WindowStartupLocation.CenterOwner;
        ThemeManager.Bind(window,Window.BackgroundProperty,"WindowBrush");ThemeManager.Bind(window,Window.ForegroundProperty,"TextBrush");window.FontFamily=new("Segoe UI Variable, Microsoft YaHei UI");
        var p=new StackPanel{Margin=new(26)};p.Children.Add(new TextBlock{Text=heading,FontSize=24,FontWeight=FontWeights.SemiBold,Margin=new(0,0,0,12)});
        var hintLabel=new TextBlock{Text=hint,TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,18)};ThemeManager.Bind(hintLabel,TextBlock.ForegroundProperty,"MutedBrush");p.Children.Add(hintLabel);
        ApplicationWindowFrame.SetContent(window,new ScrollViewer{Content=p,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});return p;
    }
    public static TextBox Field(Panel p,string label,string value="") {p.Children.Add(new TextBlock{Text=label,Margin=new(0,8,0,4)});var box=new TextBox{Text=value};p.Children.Add(box);return box;}
    public static Button Button(Panel p,string label,Action clicked) {var b=new Button{Content=label,HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,12,8,0)};b.Click+=(_,_)=>clicked();p.Children.Add(b);return b;}
    public static ComboBox Choice(Panel p,string label,string[] values,string selected) {p.Children.Add(new TextBlock{Text=label,Margin=new(0,8,0,0)});var b=new ComboBox{ItemsSource=values,SelectedItem=selected};p.Children.Add(b);return b;}
    public static CheckBox Check(Panel p,string label,bool value) {var b=new CheckBox{Content=label,IsChecked=value};p.Children.Add(b);return b;}
    public static Track? ManualTrack(Window owner)
    {
        var d=new Window{Title="手动选歌",Owner=owner};var p=Panel(d,"手动选歌","没有媒体会话时，可以查找或导入歌词进行静态浏览。");
        var title=Field(p,"歌名");var artist=Field(p,"歌手");var album=Field(p,"专辑（可留空）");var duration=Field(p,"时长，秒（可留空）");Track? result=null;
        Button(p,"确定",()=>{if(string.IsNullOrWhiteSpace(title.Text)){MessageBox.Show(d,"请填写歌名。");return;}double? ms=double.TryParse(duration.Text,out var v)&&v>0?v*1000:null;result=new(title.Text.Trim(),artist.Text.Trim(),album.Text.Trim(),ms);d.DialogResult=true;});
        d.ShowDialog();return result;
    }
}
public sealed class ImportWindow : Window
{
    readonly PlaybackCoordinator coordinator;readonly Track? target;
    readonly TextBox content;readonly TextBlock preview;readonly CheckBox plain;readonly ComboBox encoding;
    string? file;
    public ImportWindow(PlaybackCoordinator coordinator,string? path)
    {
        this.coordinator=coordinator;Title="浮光歌词 · 导入";
        var p=DialogUi.Panel(this,"导入歌词","预览后保存到浮光歌词本地库。原始文件不会被修改。");
        target=coordinator.ActiveTrack;var trackLabel=new TextBlock{Text=target is null?"未连接歌曲，确认时可以手动填写":$"绑定目标：{target.Title} · {target.Artist}",TextWrapping=TextWrapping.Wrap};p.Children.Add(trackLabel);
        DialogUi.Button(p,"选择 LRC / TXT 文件",()=>{var d=new OpenFileDialog{Filter="歌词文件|*.lrc;*.txt",CheckFileExists=true};if(d.ShowDialog(this)==true)Read(d.FileName);});
        encoding=DialogUi.Choice(p,"文件编码（乱码时切换）",["UTF-8 / BOM","GB18030","Big5"],"UTF-8 / BOM");
        plain=DialogUi.Check(p,"作为静态 TXT 显示",false);
        content=new TextBox{AcceptsReturn=true,AcceptsTab=true,Height=230,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,TextWrapping=TextWrapping.Wrap,MaxLength=2*1024*1024};p.Children.Add(content);
        preview=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new(0,12,0,0)};p.Children.Add(preview);
        DialogUi.Button(p,"确认导入并绑定",Confirm);
        content.TextChanged+=(_,_)=>Preview();plain.Checked+=(_,_)=>Preview();plain.Unchecked+=(_,_)=>Preview();encoding.SelectionChanged+=(_,_)=>{if(file is not null)Read(file);};
        if(path is not null)Read(path);
    }
    void Read(string path)
    {
        try
        {
            if(!new[]{".lrc",".txt"}.Contains(Path.GetExtension(path).ToLowerInvariant()))throw new InvalidDataException("请选择 .lrc 或 .txt 文件。");
            if(new FileInfo(path).Length>2*1024*1024)throw new InvalidDataException("文件不能大于 2 MB。");
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var enc=encoding.SelectedIndex switch{1=>Encoding.GetEncoding("GB18030",EncoderFallback.ExceptionFallback,DecoderFallback.ExceptionFallback),2=>Encoding.GetEncoding("big5",EncoderFallback.ExceptionFallback,DecoderFallback.ExceptionFallback),_=>new UTF8Encoding(false,true)};
            file=path; using var reader=new StreamReader(path,enc,true);content.Text=reader.ReadToEnd();plain.IsChecked=Path.GetExtension(path).Equals(".txt",StringComparison.OrdinalIgnoreCase);Preview();
        }
        catch(Exception ex) when(ex is IOException or DecoderFallbackException or InvalidDataException){content.Clear();MessageBox.Show(this,ex is DecoderFallbackException?"UTF-8 解码失败，请选择 GB18030 或 Big5 后重试。":ex.Message,"歌词导入");}
    }
    void Preview()
    {
        try {var doc=new LrcParser().Parse(content.Text,plain.IsChecked==true);preview.Text=$"{(doc.IsSynced?"同步 LRC":"静态歌词")} · {doc.Groups.Count} 个时间点 · 文件偏移 {doc.FileOffsetMs/1000:+0.0;-0.0;0.0} 秒（正值延后）\n"+string.Join("\n",doc.Warnings.Take(8));}
        catch(Exception ex){preview.Text=ex.Message;}
    }
    void Confirm()
    {
        if(string.IsNullOrWhiteSpace(content.Text)){MessageBox.Show(this,"请粘贴歌词或选择文件。");return;}
        var t=target ?? DialogUi.ManualTrack(this);if(t is null)return;
        if(coordinator.Snapshot is not null && coordinator.ActiveTrack?.Fingerprint!=t.Fingerprint) {MessageBox.Show(this,"播放歌曲已改变，请重新打开导入窗口确认目标。");return;}
        try{coordinator.Import(t,content.Text,file is null?"手动粘贴":"本地导入",plain.IsChecked==true);DialogResult=true;}catch(Exception ex){MessageBox.Show(this,ex.Message);}
    }
}
public sealed class CandidateWindow : Window
{
    readonly PlaybackCoordinator coordinator;Track? target;readonly ListBox list;readonly TextBox preview;readonly TextBlock status;readonly Button choose;
    public CandidateWindow(PlaybackCoordinator coordinator)
    {
        this.coordinator=coordinator;target=coordinator.ActiveTrack;Title="浮光歌词 · 更换歌词";
        var p=DialogUi.Panel(this,"选择适合这首歌的版本","确认后锁定绑定。自动查询不会覆盖你的选择。");
        status=new TextBlock{Text=target is null?"尚无歌曲":$"当前歌曲：{target.Title} · {target.Artist}",TextWrapping=TextWrapping.Wrap};p.Children.Add(status);
        DialogUi.Button(p,"手动填写歌名 / 歌手搜索",async()=>{if(!coordinator.Settings.OnlineLookup){MessageBox.Show(this,"请先在设置中开启联网查词。");return;}var t=DialogUi.ManualTrack(this);if(t is null)return;target=t;status.Text="正在搜索…";await coordinator.LookupManualAsync(t);Refresh();});
        list=new ListBox{Height=200};p.Children.Add(list);preview=new TextBox{IsReadOnly=true,Height=130,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};p.Children.Add(preview);
        choose=DialogUi.Button(p,"使用所选歌词",()=>{if(target is null || list.SelectedItem is not LyricsCandidate c)return;if(coordinator.Snapshot is not null && coordinator.Snapshot.Track.Fingerprint!=target.Fingerprint){MessageBox.Show(this,"当前正在播放另一首歌，请重新确认绑定目标。");return;}coordinator.SelectCandidate(target,c);DialogResult=true;});
        DialogUi.Button(p,"恢复自动匹配",async()=>{await coordinator.RestoreAutoAsync();Refresh();});
        list.SelectionChanged+=(_,_)=>{if(list.SelectedItem is LyricsCandidate c){var score=target is null?null:MatchRules.Score(target,c);preview.Text=$"{score?.Reason} · 置信度 {score?.Score:P0}\n\n"+string.Join("\n",c.Content.Split('\n').Take(10));choose.IsEnabled=true;}};
        Refresh();
    }
    void Refresh(){list.ItemsSource=coordinator.Candidates;choose.IsEnabled=false;status.Text=coordinator.LyricsStatus;}
}
public sealed class DiagnosticsWindow : Window
{
    public DiagnosticsWindow(PlaybackCoordinator c,SettingsStore store,string? hotkeyError)
    {
        Title="浮光歌词 · 诊断";var p=DialogUi.Panel(this,"连接与诊断","本机兼容性仍需按人工验收表检查。普通置顶无法覆盖独占全屏或系统安全桌面。");
        var text=new TextBox{IsReadOnly=true,AcceptsReturn=true,Height=220,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};p.Children.Add(text);
        void Refresh(){var s=c.Snapshot;text.Text=$"系统：{Environment.OSVersion}\n来源：{s?.SessionId ?? "未发现 Apple Music"}\n会话代次：{s?.Generation} / 曲目修订：{s?.Revision}\n原始位置：{s?.PositionMs} ms\n时间轴更新：{s?.TimelineUtc:O}\n质量：{c.Quality}\nSeek：{s?.CanSeek}\n快捷键：{hotkeyError ?? "已注册"}\n数据目录：{store.Root}\n歌词状态：{c.LyricsStatus}";}
        Refresh();var sources=DialogUi.Choice(p,"可用媒体来源（只在明确选择时切换）",c.Source.SessionIds.ToArray(),c.Source.SelectedId ?? "");
        DialogUi.Button(p,"使用所选来源",async()=>{if(sources.SelectedItem is string id){c.Settings.PreferredSession=id;c.SaveSettings();await c.Source.SelectAsync(id);Refresh();}});
        DialogUi.Button(p,"重新连接",async()=>{await c.ReconnectAsync();Refresh();});
        DialogUi.Button(p,"打开本地数据目录",()=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe",store.Root){UseShellExecute=true}));
        DialogUi.Button(p,"刷新诊断",Refresh);
    }
}
public sealed class LibraryWindow : Window
{
    public LibraryWindow(PlaybackCoordinator c)
    {
        Title="浮光歌词 · 资料库";
        var p=DialogUi.Panel(this,"把音乐的细节，提前备好","动态歌词按录音版本、时长、时间戳与全文覆盖筛选。封面直接对应 Apple Music 资料库，优先缓存高清图片。进度保存在本机，下次继续。");
        var status=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=17,Margin=new(0,4,0,18)};p.Children.Add(status);
        var progress=new ProgressBar{Height=8,Minimum=0,Maximum=1};p.Children.Add(progress);
        var path=DialogUi.Field(p,"Apple Music 资料库路径",c.Settings.LibraryPath??AppleMusicLibraryReader.Discover()??"");path.IsReadOnly=true;
        DialogUi.Button(p,"选择本机 Library.musicdb",()=>{var picker=new OpenFileDialog{Filter="Apple Music 资料库|Library.musicdb",CheckFileExists=true};if(picker.ShowDialog(this)==true){c.PauseLibraryPrefetch();c.Settings.LibraryPath=picker.FileName;path.Text=picker.FileName;c.Settings.AutoPrefetchLibrary=true;c.SaveSettings();_=c.ResumeLibraryPrefetchAsync();}});
        DialogUi.Button(p,"暂停后台预缓存",()=>{c.Settings.AutoPrefetchLibrary=false;c.SaveSettings();c.PauseLibraryPrefetch();});
        DialogUi.Button(p,"继续预缓存",async()=>{if(!c.Settings.OnlineLookup){MessageBox.Show(this,"请先在设置中开启联网查词。");return;}c.Settings.AutoPrefetchLibrary=true;c.SaveSettings();await c.ResumeLibraryPrefetchAsync();});
        DialogUi.Button(p,"立即重试尚未缓存的歌曲",async()=>{c.Settings.AutoPrefetchLibrary=true;c.SaveSettings();await c.ResumeLibraryPrefetchAsync(true);});
        var note=new TextBlock{Text="只自动保存通过检查的动态歌词；静态全文不计入动态歌词，手动绑定会保留。确认的纯音乐歌词区留空。待补齐与网络错误分开记录，缺少歌词不会被当作纯音乐。高清按实际图片尺寸统计。",TextWrapping=TextWrapping.Wrap,Margin=new(0,20,0,0)};ThemeManager.Bind(note,TextBlock.ForegroundProperty,"MutedBrush");p.Children.Add(note);
        void Refresh(){var s=c.LibraryStatus;var a=c.ArtworkStatus;status.Text=s+"\n\n"+a;progress.Maximum=Math.Max(1,s.Total+a.Total);progress.Value=s.Cached+s.Unavailable+s.Errors+s.Manual+a.Cached+a.Unavailable+a.Errors;}
        c.Updated+=Refresh;Closed+=(_,_)=>c.Updated-=Refresh;Refresh();
    }
}
