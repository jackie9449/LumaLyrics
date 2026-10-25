using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Threading;

namespace LumaLyrics.App;

internal static partial class AppleMusicLibraryHandoff
{
    internal static Task<(int Thread,bool Mta,string[] Ids)> ReadSurfaceFixtureAsync(nint handle,CancellationToken token)
        =>passiveThread.Value.Run(automation=>(Environment.CurrentManagedThreadId,Thread.CurrentThread.GetApartmentState()==ApartmentState.MTA,
            NativeSurfaceTree.Read(automation.FromHandle(handle),token).Select(e=>e.Properties.AutomationId.ValueOrDefault??"").ToArray()));
}

public partial class MainWindow
{
    sealed class SurfaceLibraryList : ListBox
    {
        internal int DescendantReads;
        protected override AutomationPeer OnCreateAutomationPeer()=>new SurfaceLibraryPeer(this);
        sealed class SurfaceLibraryPeer(SurfaceLibraryList owner) : ListBoxAutomationPeer(owner)
        {
            protected override List<AutomationPeer>? GetChildrenCore(){owner.DescendantReads++;return base.GetChildrenCore();}
        }
    }

    async Task VerifyNativeSurfaceAsync(Dictionary<string,object?> checks)
    {
        var panel=new StackPanel();var library=new SurfaceLibraryList{Height=80,ItemsSource=Enumerable.Range(0,1000).Select(i=>"原创资料行 "+i)};
        AutomationProperties.SetAutomationId(library,"DataGridListView");panel.Children.Add(library);
        var target=new Button{Content="原创播放栏"};AutomationProperties.SetAutomationId(target,"LyricsToggleButton");panel.Children.Add(target);
        var window=new Window{Owner=this,Content=panel,Width=360,Height=220,ShowInTaskbar=false,Title="浮光 · 原生表层读取验证"};window.Show();
        try
        {
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);nint handle=NativeWindow.Handle(window);
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));int before=library.DescendantReads;
            var first=await AppleMusicLibraryHandoff.ReadSurfaceFixtureAsync(handle,timeout.Token);
            var second=await AppleMusicLibraryHandoff.ReadSurfaceFixtureAsync(handle,timeout.Token);
            checks["nativeSurface.windowlessMtaReused"]=first.Mta && second.Mta && first.Thread==second.Thread && first.Thread!=Environment.CurrentManagedThreadId;
            checks["nativeSurface.libraryChildrenNeverEnumerated"]=library.DescendantReads==before;
            checks["nativeSurface.playerStillFoundPastLibrary"]=first.Ids.Contains("LyricsToggleButton") && second.Ids.Contains("LyricsToggleButton");
            panel.Children.Clear();for(int i=0;i<540;i++)panel.Children.Add(new Button{Content="原创边界 "+i});window.UpdateLayout();
            bool rejected=false;
            try{await AppleMusicLibraryHandoff.ReadSurfaceFixtureAsync(handle,timeout.Token);}catch(TimeoutException){rejected=true;}
            checks["nativeSurface.excessiveSurfaceFailsClosed"]=rejected;
        }
        finally{window.Close();}
    }
}
