using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using FlaUI.UIA3;
namespace LumaLyrics.App;

public partial class MainWindow
{
    async Task VerifyNativeHandoffReadinessAsync(Dictionary<string,object?> checks)
    {
        var global=new TextBox{Text="保留全局搜索"};AutomationProperties.SetAutomationId(global,"TextBox");
        var local=new TextBox{Text="本地原筛选",Visibility=Visibility.Collapsed};AutomationProperties.SetAutomationId(local,"FilterTextBox");
        var button=new ToggleButton{Content="过滤"};AutomationProperties.SetAutomationId(button,"FilterButton");
        var more=new Button{Content="更多",Visibility=Visibility.Collapsed};AutomationProperties.SetAutomationId(more,"MoreButton");var row=new GroupBox{Header="原创延迟歌曲行",Content=more};AutomationProperties.SetAutomationId(row,"OriginalSongRow");
        var actionDelay=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(1200)};actionDelay.Tick+=(_,_)=>{actionDelay.Stop();more.Visibility=Visibility.Visible;};
        var delay=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(1800)};delay.Tick+=(_,_)=>{delay.Stop();local.Visibility=Visibility.Visible;actionDelay.Start();};button.Checked+=(_,_)=>delay.Start();
        var toolbar=new StackPanel();toolbar.Children.Add(button);toolbar.Children.Add(local);var panel=new StackPanel();panel.Children.Add(global);panel.Children.Add(new GroupBox{Header="原创新版歌曲工具栏",Content=toolbar});panel.Children.Add(row);
        var fixture=new Window{Owner=this,Title="浮光歌词 · 慢响应筛选回归",Width=400,SizeToContent=SizeToContent.Height,Content=panel,ShowInTaskbar=false};fixture.Show();
        try
        {
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);nint handle=NativeWindow.Handle(fixture);
            var elapsed=await Task.Run(()=>
            {
                using var automation=new UIA3Automation{ConnectionTimeout=TimeSpan.FromSeconds(1),TransactionTimeout=TimeSpan.FromSeconds(3)};var root=automation.FromHandle(handle);var toggle=root.FindFirstDescendant(cf=>cf.ByAutomationId("FilterButton"))??throw new InvalidOperationException("Missing original delayed filter fixture.");toggle.Patterns.Toggle.Pattern.Toggle();var watch=Stopwatch.StartNew();
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));var edit=AppleMusicLibraryHandoff.WaitForNative(()=>AppleMusicLibraryHandoff.FindFilterText(automation,toggle),TimeSpan.FromSeconds(4),timeout.Token);
                if(edit?.Properties.AutomationId.ValueOrDefault!="FilterTextBox")return(filter:-1d,action:-1d);edit.Patterns.Value.Pattern.SetValue("延迟后仍准确筛选");double filterElapsed=watch.Elapsed.TotalMilliseconds;
                var nativeRow=root.FindFirstDescendant(cf=>cf.ByAutomationId("OriginalSongRow"))??throw new InvalidOperationException("Missing original song row fixture.");watch.Restart();
                var ready=AppleMusicLibraryHandoff.WaitForNative(()=>AppleMusicLibraryHandoff.FindReadySongAction(nativeRow),TimeSpan.FromSeconds(3),timeout.Token);
                return(filter:filterElapsed,action:ready is null?-1:watch.Elapsed.TotalMilliseconds);
            });
            checks["handoff.slowLocalFilterWaitsBeyondOldLimit"]=elapsed.filter>=1200 && local.Text=="延迟后仍准确筛选";
            checks["handoff.songMetadataDoesNotImplyActionIsReady"]=elapsed.action>=800;
            checks["handoff.slowFilterPreservesGlobalSearch"]=global.Text=="保留全局搜索";
            var watch=Stopwatch.StartNew();using var cancelled=new CancellationTokenSource(50);
            bool observed=false;try{await Task.Run(()=>AppleMusicLibraryHandoff.WaitForNative<object>(()=>null,TimeSpan.FromSeconds(5),cancelled.Token));}catch(OperationCanceledException){observed=true;}
            checks["handoff.nativeReadinessCancellationIsPrompt"]=observed && watch.Elapsed<TimeSpan.FromSeconds(1);
        }
        finally{delay.Stop();actionDelay.Stop();fixture.Close();}
        var state=WindowState;
        try
        {
            WindowState=WindowState.Normal;await Task.Delay(80);using var process=Process.GetCurrentProcess();
            var old=new AppleMusicLibraryHandoff.HandoffContext();old.Capture(process);var current=new AppleMusicLibraryHandoff.HandoffContext();current.Capture(process);
            bool stale=await AppleMusicLibraryHandoff.CompleteHandoffWindowAsync(old,true);
            checks["handoff.staleCompletionCannotMinimizeWindow"]=!stale && WindowState==WindowState.Normal;
            bool failed=await AppleMusicLibraryHandoff.CompleteHandoffWindowAsync(current,false);
            checks["handoff.unconfirmedSelectionKeepsOriginallyVisibleWindow"]=!failed && WindowState==WindowState.Normal;
            var confirmed=new AppleMusicLibraryHandoff.HandoffContext();confirmed.Capture(process);bool minimized=await AppleMusicLibraryHandoff.CompleteHandoffWindowAsync(confirmed,true);await Task.Delay(80);
            checks["handoff.confirmedSelectionMinimizesVisibleWindow"]=minimized && AppleMusicLibraryHandoff.HandoffWindowMinimized(confirmed);
        }
        finally{WindowState=state;Activate();await Task.Delay(80);}
    }
}
