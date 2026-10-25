using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using System.Windows.Controls.Primitives;
using ControlType=FlaUI.Core.Definitions.ControlType;
using FlaUI.UIA3;
namespace LumaLyrics.App;

public partial class MainWindow
{
    // A queue occurrence has its own identity, even when it repeats the same recording.
    // WPF value-equal data objects share an item peer and cannot model two occurrences.
    sealed class NativeQueueFixtureRow(string title,string metadata,string duration)
    {
        public string Title=>title;
        public string Metadata=>metadata;
        public string Duration=>duration;
    }
    async Task VerifyNativeQueueStructureAsync(Dictionary<string,object?> checks)
    {
        var content=new StackPanel();
        var queueToggle=new ToggleButton{Content="队列",IsChecked=false};AutomationProperties.SetAutomationId(queueToggle,"PlayQueueToggleButton");content.Children.Add(queueToggle);
        var lyricsToggle=new ToggleButton{Content="歌词",IsChecked=true};AutomationProperties.SetAutomationId(lyricsToggle,"LyricsToggleButton");content.Children.Add(lyricsToggle);
        var historyToggle=new ToggleButton{Content="历史记录",IsChecked=false};content.Children.Add(historyToggle);
        int toggleChanges=0;foreach(var toggle in new[]{queueToggle,lyricsToggle,historyToggle}){toggle.Checked+=(_,_)=>toggleChanges++;toggle.Unchecked+=(_,_)=>toggleChanges++;}
        foreach(string name in new[]{"微光之间","浮光工作室 — 原创测试"}){var text=new TextBlock{Text=name};AutomationProperties.SetAutomationId(text,"ScrollingText");content.Children.Add(text);}
        ListBox List(string id,NativeQueueFixtureRow[] rows)
        {
            var list=new ListBox{Height=125,ItemsSource=rows};AutomationProperties.SetAutomationId(list,id);
            var panel=new FrameworkElementFactory(typeof(StackPanel));
            foreach(string property in new[]{"Title","Metadata","Duration"}){var label=new FrameworkElementFactory(typeof(TextBlock));label.SetBinding(TextBlock.TextProperty,new Binding(property));panel.AppendChild(label);}
            list.ItemTemplate=new DataTemplate{VisualTree=panel};VirtualizingPanel.SetIsVirtualizing(list,true);VirtualizingPanel.SetVirtualizationMode(list,VirtualizationMode.Recycling);ScrollViewer.SetCanContentScroll(list,true);content.Children.Add(list);return list;
        }
        List("PlayQueueListViewManual",[new("夜航","浮光工作室 — 原创测试","3:00"),new("夜航","浮光工作室 — 原创测试","3:00")]);
        var automatic=List("PlayQueueListView",Enumerable.Range(0,70).Select(i=>new NativeQueueFixtureRow("原创片段 "+i,"浮光工作室 — 创作集","2:00")).ToArray());
        var window=new Window{Owner=this,Title="浮光歌词 · 原创队列结构回归",Width=450,Height=560,Content=content,ShowInTaskbar=false};window.Show();
        try
        {
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);nint handle=NativeWindow.Handle(window);
            checks["nativeQueue.fixtureActuallyVirtualizes"]=Descendants<ListBoxItem>(automatic).Count()<70;
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(18));
            Task<LumaLyrics.Infrastructure.MusicQueueSnapshot> Passive()=>Task.Run(()=>
            {
                using var automation=new UIA3Automation{ConnectionTimeout=TimeSpan.FromSeconds(1),TransactionTimeout=TimeSpan.FromSeconds(3)};
                return AppleMusicLibraryHandoff.ReadQueueSurface(automation,automation.FromHandle(handle),timeout.Token);
            },timeout.Token);
            int beforeChanges=toggleChanges;var closed=await Passive();
            checks["nativeQueue.passiveReadDoesNotOpenQueueOrCloseLyrics"]=!closed.Available && queueToggle.IsChecked==false && lyricsToggle.IsChecked==true && toggleChanges==beforeChanges;
            queueToggle.IsChecked=true;historyToggle.IsChecked=true;window.UpdateLayout();beforeChanges=toggleChanges;var history=await Passive();
            checks["nativeQueue.passiveReadPreservesHistorySelection"]=!history.Available && historyToggle.IsChecked==true && toggleChanges==beforeChanges;
            historyToggle.IsChecked=false;automatic.ScrollIntoView(automatic.Items[45]);window.UpdateLayout();
            var scroll=Descendants<ScrollViewer>(automatic).First();double offset=scroll.VerticalOffset;beforeChanges=toggleChanges;var passive=await Passive();
            checks["nativeQueue.passiveReadNeverRealizesOrScrollsRows"]=Math.Abs(scroll.VerticalOffset-offset)<.01 && toggleChanges==beforeChanges && passive.Available;
            var snapshot=await Task.Run(()=>
            {
                using var automation=new UIA3Automation{ConnectionTimeout=TimeSpan.FromSeconds(1),TransactionTimeout=TimeSpan.FromSeconds(3)};var root=automation.FromHandle(handle);
                var lists=root.FindAllDescendants(cf=>cf.ByControlType(ControlType.List)).Where(l=>(l.Properties.AutomationId.ValueOrDefault??"").StartsWith("PlayQueueListView",StringComparison.Ordinal)).ToArray();
                return AppleMusicLibraryHandoff.CaptureQueue(automation,root,lists,timeout.Token,null);
            },timeout.Token);
            checks["nativeQueue.exposedEntries"]=snapshot.Upcoming.Count;
            checks["nativeQueue.manualEntries"]=snapshot.Upcoming.Count(e=>e.Inserted);
            checks["nativeQueue.incompleteMetadataEntries"]=snapshot.Upcoming.Count(e=>!e.MetadataComplete);
            checks["nativeQueue.lastCapturedTitle"]=snapshot.Upcoming.LastOrDefault()?.Track.Title;
            checks["nativeQueue.bothNativeSectionsIncluded"]=snapshot.Available && snapshot.Upcoming.Count==72;
            checks["nativeQueue.manualBeforeOriginalQueue"]=snapshot.Upcoming.Take(2).All(e=>e.Inserted && e.Track.Title=="夜航") && snapshot.Upcoming.Skip(2).All(e=>!e.Inserted);
            checks["nativeQueue.repeatedInsertionsRetained"]=snapshot.Upcoming.Take(2).Select(e=>e.Track.Title).SequenceEqual(new[]{"夜航","夜航"});
            checks["nativeQueue.virtualizedTailRead"]=snapshot.Upcoming.LastOrDefault()?.Track.Title=="原创片段 69" && snapshot.IsComplete;
            checks["nativeQueue.currentSeparatedFromUpcoming"]=snapshot.Current?.Title=="微光之间" && snapshot.Current.Artist=="浮光工作室" && snapshot.Current.Album=="原创测试";
            var occurrences=await Task.Run(()=>
            {
                using var automation=new UIA3Automation{ConnectionTimeout=TimeSpan.FromSeconds(1),TransactionTimeout=TimeSpan.FromSeconds(3)};var root=automation.FromHandle(handle);
                var lists=root.FindAllDescendants(cf=>cf.ByControlType(ControlType.List)).Where(l=>(l.Properties.AutomationId.ValueOrDefault??"").StartsWith("PlayQueueListView",StringComparison.Ordinal)).ToArray();
                var items=new List<FlaUI.Core.AutomationElements.AutomationElement>();var captured=AppleMusicLibraryHandoff.CaptureQueue(automation,root,lists,timeout.Token,null,items);
                var context=new AppleMusicLibraryHandoff.HandoffContext();var stale=AppleMusicLibraryHandoff.InvokeQueueOccurrence(automation,root,lists,new(captured with{Upcoming=captured.Upcoming.Skip(1).ToArray()},0),timeout.Token,context);
                return new{allMapped=items.Count==captured.Upcoming.Count,duplicateOccurrencesDistinct=!items[0].Equals(items[1]),staleRejected=stale?.Contains("已经变化",StringComparison.Ordinal)==true && !context.CommandAttempted};
            },timeout.Token);
            checks["nativeQueue.allOccurrencesMappedToNativeItems"]=occurrences.allMapped;
            checks["nativeQueue.equalRecordingsHaveDistinctNativeTargets"]=occurrences.duplicateOccurrencesDistinct;
            checks["nativeQueue.staleSnapshotDoesNotAttemptPlayback"]=occurrences.staleRejected;
            content.Children.Remove(automatic);foreach(var list in content.Children.OfType<ListBox>().ToArray())content.Children.Remove(list);
            var upcomingToggle=new ToggleButton{Content="待播清单",IsChecked=true};var emptyLabel=new TextBlock{Text="无待播歌曲。"};
            var queueContent=new StackPanel();queueContent.Children.Add(upcomingToggle);var pane=new GroupBox{Header="原创空队列",Content=queueContent};AutomationProperties.SetAutomationId(pane,"PaneRoot");content.Children.Add(pane);content.Children.Add(emptyLabel);window.UpdateLayout();
            Task<LumaLyrics.Infrastructure.MusicQueueSnapshot?> Empty()=>Task.Run(()=>{using var automation=new UIA3Automation();return AppleMusicLibraryHandoff.CaptureExplicitEmptyQueue(automation.FromHandle(handle));},timeout.Token);
            checks["nativeQueue.emptyLabelOutsideNativePaneIsRejected"]=await Empty() is null;
            content.Children.Remove(emptyLabel);queueContent.Children.Add(emptyLabel);window.UpdateLayout();var explicitEmpty=await Empty();
            checks["nativeQueue.explicitEmptyStateRetainsCurrentRecording"]=explicitEmpty is {Available:true,IsComplete:true,Upcoming.Count:0,Current.Title:"微光之间"};
            upcomingToggle.IsChecked=false;window.UpdateLayout();checks["nativeQueue.emptyHistoryIsNotAnEmptyUpcomingQueue"]=await Empty() is null;
        }
        finally{window.Close();}
    }
}
