using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using LumaLyrics.Infrastructure;
using Microsoft.Win32;
namespace LumaLyrics.App;

public sealed class LibraryPanel:UserControl,IDisposable
{
    readonly PlaybackCoordinator coordinator;
    readonly TextBlock status=LibraryWorkspaceUi.Text("正在读取资料库…"),coverage=LibraryWorkspaceUi.Text("覆盖量按可靠原词和实际译文统计。"),total,timed,chinese;
    readonly ProgressBar progress=new(){Height=5,Minimum=0,Maximum=1,Margin=new(0,12,0,12)};
    readonly CancellationTokenSource stop=new();CancellationTokenSource? reading;bool subscribed,disposed;string? stamp;long readVersion;
    internal bool IsSubscribed=>subscribed;
    internal TextBox SourcePath {get;}
    public LibraryPanel(PlaybackCoordinator coordinator,Action? chooseSongs=null)
    {
        this.coordinator=coordinator;var body=new StackPanel();Content=body;
        body.Children.Add(LibraryWorkspaceUi.Heading("资料库概况","同步歌曲、准备原词，让中文译文跟上你的音乐。"));
        var metrics=new UniformGrid{Columns=3,Margin=new(0,0,-10,0)};metrics.Children.Add(LibraryWorkspaceUi.Metric("资料库歌曲",out total));metrics.Children.Add(LibraryWorkspaceUi.Metric("动态原词",out timed));metrics.Children.Add(LibraryWorkspaceUi.Metric("中文完整",out chinese));body.Children.Add(metrics);
        var prepare=new StackPanel();prepare.Children.Add(LibraryWorkspaceUi.Text("从选中的歌曲开始",15,false));var hint=LibraryWorkspaceUi.Text("先补原词，再找现有中文；剩余歌曲交给本地 agent。无需先播放。 ");hint.Margin=new(0,5,0,12);prepare.Children.Add(hint);var start=new WrapPanel();prepare.Children.Add(start);if(chooseSongs is not null)LibraryWorkspaceUi.Button(start,"选择歌曲开始准备",chooseSongs,true);prepare.Children.Add(coverage);body.Children.Add(LibraryWorkspaceUi.Card(prepare));
        var source=new StackPanel();source.Children.Add(LibraryWorkspaceUi.Text("资料库来源",15,false));var note=LibraryWorkspaceUi.Text("刷新成员清单无需开启联网查词或自动预缓存。");note.Margin=new(0,4,0,10);source.Children.Add(note);
        SourcePath=new(){Text=coordinator.Settings.LibraryPath??AppleMusicLibraryReader.Discover()??"",IsReadOnly=true,Margin=new(0,0,0,12),FontSize=11};AutomationProperties.SetName(SourcePath,"Apple Music 资料库路径");source.Children.Add(SourcePath);var sourceActions=new WrapPanel();source.Children.Add(sourceActions);
        LibraryWorkspaceUi.Button(sourceActions,"刷新资料库清单",async()=>{try{await coordinator.SynchronizeWorkspaceAsync(stop.Token);stamp=null;Refresh();}catch(OperationCanceledException){}catch(Exception e){if(!disposed)status.Text="刷新未完成："+e.Message;}},true);
        LibraryWorkspaceUi.Button(sourceActions,"选择本机 Library.musicdb",()=>{var picker=new OpenFileDialog{Filter="Apple Music 资料库|Library.musicdb",CheckFileExists=true};if(picker.ShowDialog(Window.GetWindow(this))!=true)return;coordinator.PauseWorkspace();coordinator.PauseLibraryPrefetch();coordinator.Settings.LibraryPath=picker.FileName;SourcePath.Text=picker.FileName;coordinator.Settings.AutoPrefetchLibrary=true;coordinator.SaveSettings();_=coordinator.ResumeLibraryPrefetchAsync();});body.Children.Add(LibraryWorkspaceUi.Card(source));
        var background=new StackPanel();background.Children.Add(LibraryWorkspaceUi.Text("后台准备",15,false));var progressHint=LibraryWorkspaceUi.Text("进度表示已处理数量；可用覆盖见上方统计。",11);progressHint.Margin=new(0,5,0,0);background.Children.Add(progressHint);background.Children.Add(progress);background.Children.Add(status);var actions=new WrapPanel{Margin=new(0,12,0,0)};background.Children.Add(actions);
        LibraryWorkspaceUi.Button(actions,"继续预缓存",async()=>{if(!coordinator.Settings.OnlineLookup){status.Text="请先在常规设置开启联网查词。";return;}coordinator.Settings.AutoPrefetchLibrary=true;coordinator.SaveSettings();await coordinator.ResumeLibraryPrefetchAsync();},true);
        LibraryWorkspaceUi.Button(actions,"暂停后台预缓存",()=>{coordinator.Settings.AutoPrefetchLibrary=false;coordinator.SaveSettings();coordinator.PauseLibraryPrefetch();});
        LibraryWorkspaceUi.Button(actions,"立即重试尚未缓存的歌曲",async()=>{coordinator.Settings.AutoPrefetchLibrary=true;coordinator.SaveSettings();await coordinator.ResumeLibraryPrefetchAsync(true);});body.Children.Add(LibraryWorkspaceUi.Card(background));
        body.Children.Add(LibraryWorkspaceUi.Text("自动保存通过录音版本与时间轴检查的动态原词；手动绑定保留，缺词不会被标为纯音乐。",11));
        Loaded+=(_,_)=>{if(disposed)return;if(!subscribed){coordinator.Updated+=Refresh;subscribed=true;}stamp=null;Refresh();};Unloaded+=(_,_)=>Unsubscribe();Refresh();
    }
    void Refresh()
    {
        if(disposed)return;var lyrics=coordinator.LibraryStatus;var artwork=coordinator.ArtworkStatus;status.Text=lyrics+"\n\n"+artwork;
        string next=$"{lyrics.Cached}:{lyrics.Total}:{artwork.Cached}:{coordinator.TranslationJobMessage}:{coordinator.WorkspaceMessage}:{coordinator.Settings.LibraryPath}";if(subscribed&&stamp!=next){bool refresh=stamp is null;stamp=next;_=ReadCoverage(refresh);}
        progress.Maximum=Math.Max(1,lyrics.Total+artwork.Total);progress.Value=lyrics.Cached+lyrics.Unavailable+lyrics.Errors+lyrics.Manual+artwork.Cached+artwork.Unavailable+artwork.Errors;
    }
    async Task ReadCoverage(bool refresh=false)
    {
        reading?.Cancel();reading?.Dispose();reading=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);var token=reading.Token;long version=++readVersion;
        try{var snapshot=await coordinator.ReadWorkspaceAsync(token,refresh);if(disposed||!subscribed||token.IsCancellationRequested||version!=readVersion)return;total.Text=snapshot.Songs.Count.ToString("N0");timed.Text=snapshot.Songs.Count(s=>s.Status.Original==LibraryOriginalState.Ready).ToString("N0");chinese.Text=snapshot.Songs.Count(s=>s.Status.Chinese==LibraryChineseState.Complete).ToString("N0");coverage.Text=$"中文部分 {snapshot.Songs.Count(s=>s.Status.Chinese==LibraryChineseState.Partial):N0} 首 · 尚缺中文 {snapshot.Songs.Count(s=>s.Status.Chinese==LibraryChineseState.Missing):N0} 首\n"+(snapshot.Message??"");}
        catch(OperationCanceledException){}catch(Exception e){if(!disposed&&subscribed)coverage.Text="覆盖状态暂不可读："+e.Message;}
    }
    void Unsubscribe(){coordinator.Updated-=Refresh;subscribed=false;if(!disposed)reading?.Cancel();}
    public void Dispose(){if(disposed)return;Unsubscribe();disposed=true;stop.Cancel();reading?.Dispose();reading=null;stop.Dispose();}
}
