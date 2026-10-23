using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using FlaUI.UIA3;
namespace LumaLyrics.App;

public partial class MainWindow
{
    async Task VerifyNativeSelectorIsolationAsync(Dictionary<string,object?> checks)
    {
        var global=new TextBox{Text="保留全局搜索",Width=170};AutomationProperties.SetAutomationId(global,"TextBox");
        var local=new TextBox{Text="本地原筛选",Width=180,Visibility=Visibility.Collapsed};AutomationProperties.SetAutomationId(local,"FilterTextBox");
        var toggle=new ToggleButton{Content="过滤",Width=80};AutomationProperties.SetAutomationId(toggle,"FilterButton");
        toggle.Checked+=(_,_)=>local.Visibility=Visibility.Visible;toggle.Unchecked+=(_,_)=>local.Visibility=Visibility.Collapsed;
        var toolbar=new StackPanel();toolbar.Children.Add(toggle);toolbar.Children.Add(local);
        var group=new GroupBox{Header="原创筛选工具栏",Content=toolbar};var content=new GroupBox{Header="原创歌曲页",Content=group};
        var root=new StackPanel();root.Children.Add(global);root.Children.Add(content);
        var window=new Window{Owner=this,Title="浮光歌词 · 本地筛选回归",Width=420,SizeToContent=SizeToContent.Height,Content=new GroupBox{Header="原创窗口内容",Content=root},ShowInTaskbar=false};window.Show();
        try
        {
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);nint handle=NativeWindow.Handle(window);
            var result=await Task.Run(()=>
            {
                using var automation=new UIA3Automation{ConnectionTimeout=TimeSpan.FromSeconds(1),TransactionTimeout=TimeSpan.FromSeconds(3)};var native=automation.FromHandle(handle);
                var button=native.FindFirstDescendant(cf=>cf.ByAutomationId("FilterButton"))??throw new InvalidOperationException("Original filter fixture was not exposed through UI Automation.");
                bool closed=AppleMusicLibraryHandoff.FindFilterText(automation,button) is null;
                button.Patterns.Toggle.Pattern.Toggle();FlaUI.Core.AutomationElements.AutomationElement? edit=null;
                for(int i=0;i<8 && edit is null;i++){Thread.Sleep(75);edit=AppleMusicLibraryHandoff.FindFilterText(automation,button);}
                bool scoped=edit?.Properties.AutomationId.ValueOrDefault=="FilterTextBox";
                if(scoped)edit!.Patterns.Value.Pattern.SetValue("仅修改本地筛选");
                return(closed,scoped);
            });
            checks["nativeSelector.closedFilterNeverFallsBackToGlobalSearch"]=result.closed;
            checks["nativeSelector.openFilterUsesExactLocalIdentity"]=result.scoped;
            checks["nativeSelector.globalSearchValuePreserved"]=global.Text=="保留全局搜索";
            checks["nativeSelector.localValueActuallyChanges"]=local.Text=="仅修改本地筛选";
        }
        finally{window.Close();}
    }
}
