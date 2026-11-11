using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using FlaUI.UIA3;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

public partial class MainWindow
{
    async Task VerifyNativeLocalesAsync(Dictionary<string,object?> checks)
    {
        var title=new TextBlock{Text="Original locale fixture"};AutomationProperties.SetAutomationId(title,"ScrollingText");
        var artist=new TextBlock{Text="Luma fixture artist"};AutomationProperties.SetAutomationId(artist,"ScrollingText");
        var queue=new ToggleButton{Content="Queue",IsChecked=true};AutomationProperties.SetAutomationId(queue,"PlayQueueToggleButton");
        var history=new ToggleButton{IsChecked=false};var next=new ToggleButton{IsChecked=true};var empty=new TextBlock();
        var paneContents=new StackPanel();paneContents.Children.Add(history);paneContents.Children.Add(next);paneContents.Children.Add(empty);
        var pane=new GroupBox{Header="Original native locale fixture",Content=paneContents};AutomationProperties.SetAutomationId(pane,"PaneRoot");
        var content=new StackPanel();content.Children.Add(title);content.Children.Add(artist);content.Children.Add(queue);content.Children.Add(pane);
        var window=new Window{Owner=this,Title="LumaLyrics native locale verification",Width=520,SizeToContent=SizeToContent.Height,Content=content,ShowInTaskbar=false};
        window.Show();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);nint handle=NativeWindow.Handle(window);
            Task<MusicQueueSnapshot?> Capture()=>Task.Run(()=>{using var automation=new UIA3Automation();return AppleMusicLibraryHandoff.CaptureExplicitEmptyQueue(automation.FromHandle(handle));},timeout.Token);
            foreach(var (locale,historyLabel,nextLabel,emptyLabel) in new[]{
                ("zh-Hans","历史记录","待播清单","无待播歌曲。"),
                ("zh-Hant","記錄","待播清單","沒有即將播放的歌曲。"),
                ("en","History","Playing Next","No upcoming songs."),
                ("ja","履歴","次に再生","次に再生される曲はありません。")})
            {
                history.Content=historyLabel;next.Content=nextLabel;empty.Text=emptyLabel;history.IsChecked=false;next.IsChecked=true;
                await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);window.UpdateLayout();
                checks[$"nativeLocale.{locale}.exactEmptyQueue"]=await Capture() is{Available:true,IsComplete:true,Upcoming.Count:0,Current.Title:"Original locale fixture",Current.Artist:"Luma fixture artist"};
                empty.Text=emptyLabel+" unknown";window.UpdateLayout();
                checks[$"nativeLocale.{locale}.unknownEmptyLabelRejected"]=await Capture() is null;
                empty.Text=emptyLabel;next.IsChecked=false;history.IsChecked=true;window.UpdateLayout();
                checks[$"nativeLocale.{locale}.historyIsNotEmptyQueue"]=await Capture() is null;
                var unavailable=await Task.Run(()=>{using var automation=new UIA3Automation();return AppleMusicLibraryHandoff.ReadQueueSurface(automation,automation.FromHandle(handle),timeout.Token);},timeout.Token);
                checks[$"nativeLocale.{locale}.passiveReadPreservesHistory"]=!unavailable.Available && history.IsChecked==true && next.IsChecked==false && queue.IsChecked==true;
            }
        }
        finally{window.Close();}
    }
}
