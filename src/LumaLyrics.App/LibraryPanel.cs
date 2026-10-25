using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using LumaLyrics.Infrastructure;
using Microsoft.Win32;
namespace LumaLyrics.App;

public sealed class LibraryPanel : UserControl,IDisposable
{
    readonly PlaybackCoordinator coordinator;
    readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,FontSize=15,LineHeight=24};
    readonly ProgressBar progress=new(){Height=6,Minimum=0,Maximum=1,Margin=new(0,18,0,0)};
    bool subscribed,disposed;
    internal bool IsSubscribed=>subscribed;
    internal TextBox SourcePath {get;}

    public LibraryPanel(PlaybackCoordinator coordinator)
    {
        this.coordinator=coordinator;
        var body=new StackPanel();Content=body;ThemeManager.Bind(this,ForegroundProperty,"TextBrush");
        body.Children.Add(new TextBlock{Text="把音乐的细节，提前备好",FontSize=21,FontWeight=FontWeights.SemiBold,Margin=new(0,0,0,10)});
        body.Children.Add(Note("动态歌词按录音版本、时长、时间戳与全文覆盖筛选。封面直接对应 Apple Music 资料库，优先缓存高清图片。进度保存在本机，下次继续。"));
        var summary=new StackPanel();summary.Children.Add(status);summary.Children.Add(progress);
        var card=new Border{Child=summary,Padding=new(20),BorderThickness=new(1),Margin=new(0,16,0,22)};
        ThemeManager.Bind(card,Border.BackgroundProperty,"ControlBrush");ThemeManager.Bind(card,Border.BorderBrushProperty,"BorderBrush");card.SetResourceReference(Border.CornerRadiusProperty,"CardRadius");body.Children.Add(card);
        SourcePath=DialogUi.Field(body,"Apple Music 资料库路径",coordinator.Settings.LibraryPath??AppleMusicLibraryReader.Discover()??"");SourcePath.IsReadOnly=true;AutomationProperties.SetName(SourcePath,"Apple Music 资料库路径");
        DialogUi.Button(body,"选择本机 Library.musicdb",()=>{
            var picker=new OpenFileDialog{Filter="Apple Music 资料库|Library.musicdb",CheckFileExists=true};
            if(picker.ShowDialog(Window.GetWindow(this))!=true)return;
            coordinator.PauseLibraryPrefetch();coordinator.Settings.LibraryPath=picker.FileName;SourcePath.Text=picker.FileName;
            coordinator.Settings.AutoPrefetchLibrary=true;coordinator.SaveSettings();_=coordinator.ResumeLibraryPrefetchAsync();
        });
        var actions=new WrapPanel{Margin=new(0,12,0,0)};body.Children.Add(actions);
        DialogUi.Button(actions,"暂停后台预缓存",()=>{coordinator.Settings.AutoPrefetchLibrary=false;coordinator.SaveSettings();coordinator.PauseLibraryPrefetch();});
        DialogUi.Button(actions,"继续预缓存",async()=>{
            if(!coordinator.Settings.OnlineLookup){MessageBox.Show(Window.GetWindow(this),"请先在设置中开启联网查词。");return;}
            coordinator.Settings.AutoPrefetchLibrary=true;coordinator.SaveSettings();await coordinator.ResumeLibraryPrefetchAsync();
        });
        DialogUi.Button(actions,"立即重试尚未缓存的歌曲",async()=>{coordinator.Settings.AutoPrefetchLibrary=true;coordinator.SaveSettings();await coordinator.ResumeLibraryPrefetchAsync(true);});
        var note=Note("只自动保存通过检查的动态歌词；静态全文不计入动态歌词，手动绑定会保留。确认的纯音乐歌词区留空。待补齐与网络错误分开记录，缺少歌词不会被当作纯音乐。高清按实际图片尺寸统计。");note.Margin=new(0,24,0,0);body.Children.Add(note);
        Loaded+=(_,_)=>{if(disposed)return;if(!subscribed){coordinator.Updated+=Refresh;subscribed=true;}Refresh();};
        Unloaded+=(_,_)=>Unsubscribe();Refresh();
    }
    static TextBlock Note(string text)
    {
        var note=new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,FontSize=12,LineHeight=21};ThemeManager.Bind(note,TextBlock.ForegroundProperty,"MutedBrush");return note;
    }
    void Refresh()
    {
        var lyrics=coordinator.LibraryStatus;var artwork=coordinator.ArtworkStatus;status.Text=lyrics+"\n\n"+artwork;
        progress.Maximum=Math.Max(1,lyrics.Total+artwork.Total);progress.Value=lyrics.Cached+lyrics.Unavailable+lyrics.Errors+lyrics.Manual+artwork.Cached+artwork.Unavailable+artwork.Errors;
    }
    void Unsubscribe(){if(!subscribed)return;coordinator.Updated-=Refresh;subscribed=false;}
    public void Dispose(){disposed=true;Unsubscribe();}
}
