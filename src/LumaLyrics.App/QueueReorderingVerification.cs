using System.IO;
using System.Windows;
using System.Windows.Controls;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

public partial class MainWindow
{
    async Task VerifyQueueReorderingAsync(string directory,Dictionary<string,object?> checks)
    {
        var current=new Track("微光之间","浮光工作室","原创测试",180000);var repeated=new Track("夜航","浮光工作室","原创测试",200000);
        var snapshot=new MusicQueueSnapshot(current,[new(repeated,Inserted:true),new(new("海面","浮光工作室","原创测试",220000)),new(repeated)],true,true,DateTimeOffset.UtcNow,"原创队列");
        var requests=new List<(MusicQueueMove Move,CancellationToken Token,TaskCompletionSource<LibraryPlayResult> Completion)>();int activated=0,completed=0;
        Task<LibraryPlayResult> Reorder(MusicQueueMove move,CancellationToken token){var completion=new TaskCompletionSource<LibraryPlayResult>(TaskCreationOptions.RunContinuationsAsynchronously);requests.Add((move,token,completion));return completion.Task;}
        var view=new PlaybackQueueView(()=>{},()=>{},true,reorder:Reorder,activate:()=>activated++){Visibility=Visibility.Visible,Margin=new(22)};view.PlaybackCompleted+=()=>completed++;
        var window=new Window{Owner=this,Title="浮光歌词 · 原创拖动排序回归",Width=380,Height=650,Content=new System.Windows.Documents.AdornerDecorator{Child=view},ShowInTaskbar=false};window.Show();view.Apply(snapshot);window.UpdateLayout();
        ListBoxItem Row(int index){view.UpcomingList.ScrollIntoView(view.UpcomingList.Items[index]);window.UpdateLayout();return (ListBoxItem)view.UpcomingList.ItemContainerGenerator.ContainerFromIndex(index);}
        try
        {
            var row=(PlaybackQueueView.QueueRow)view.UpcomingList.Items[2];var data=new DataObject(typeof(PlaybackQueueView.QueueDrag),new PlaybackQueueView.QueueDrag(view,snapshot,2,row));
            var target=Row(0);var point=target.TranslatePoint(new Point(15,2),view.UpcomingList);
            bool accepted=view.TryDrop(data,point,out var move,out var y);checks["queueMove.dropPinsRepeatedOccurrence"]=accepted && move is {SourceIndex:2,InsertionIndex:0} && ReferenceEquals(move.Snapshot,snapshot);
            checks["queueMove.foreignDragRejected"]=!view.TryDrop(new DataObject("Text","arbitrary external content"),point,out _,out _);
            var other=new PlaybackQueueView(()=>{},()=>{},reorder:Reorder);var foreign=new DataObject(typeof(PlaybackQueueView.QueueDrag),new PlaybackQueueView.QueueDrag(other,snapshot,2,row));
            checks["queueMove.otherViewPayloadRejected"]=!view.TryDrop(foreign,point,out _,out _);other.Dispose();
            view.ShowDropHint(y);await Task.Delay(50);VisualVerification.Save(window,Path.Combine(directory,"queue-reorder-insertion.png"));
            view.BeginReorder(move!);checks["queueMove.pendingRetainsActualRows"]=view.IsSelecting && view.Snapshot!.Upcoming.SequenceEqual(snapshot.Upcoming) && activated==0;
            view.BeginReorder(new(snapshot,0,3));checks["queueMove.pendingRejectsSecondDrop"]=requests.Count==1;
            requests[0].Completion.SetResult(new(false,"原创过期顺序，请刷新"));await view.ActivePlayback!;view.Apply(snapshot);
            checks["queueMove.failurePreservesRowsAndMessage"]=completed==1 && activated==0 && view.Snapshot!.Upcoming.SequenceEqual(snapshot.Upcoming) && view.Message=="原创过期顺序，请刷新";
            view.BeginReorder(move!);requests[1].Completion.SetResult(new(true,"原创顺序已确认"));await view.ActivePlayback!;
            checks["queueMove.confirmedCompletionActivatesOnce"]=activated==1 && completed==2 && !view.IsSelecting;
            var reordered=snapshot with{Upcoming=MusicPlaybackQueue.MovedEntries(move!)};view.Apply(reordered);view.BeginReorder(move!);
            checks["queueMove.staleSnapshotRejected"]=requests.Count==2;
            checks["queueMove.staleDragRowRejected"]=!view.TryDrop(data,point,out _,out _);
            view.Apply(snapshot with{IsComplete=false});view.BeginReorder(new(view.Snapshot!,0,3));checks["queueMove.partialQueueRejected"]=requests.Count==2;
            view.Apply(snapshot);view.BeginReorder(new(snapshot,1,2));checks["queueMove.noOpDropRejected"]=requests.Count==2;
            view.BeginReorder(new(snapshot,1,3));var pending=view.ActivePlayback!;view.Dispose();requests[2].Completion.SetResult(new(true,"关闭后回调"));await pending;
            checks["queueMove.disposalCancelsLateSuccess"]=requests[2].Token.IsCancellationRequested && activated==1;
        }
        finally{view.Dispose();window.Close();}

        var preferences=new SettingsWindow(coordinator,id=>SelectUiTheme(id)){Owner=this};preferences.Show();preferences.SelectTranslationTab();preferences.UpdateLayout();
        checks["review.entryExistsInTranslationSettings"]=Descendants<Button>(preferences).Any(b=>b.Content?.ToString()=="以 Apple Music 原词校对…");preferences.Close();
        var observation=new LyricsOcrObservation(new("原创航线","浮光工作室","原创测试",10000),TranslationAlignment.Hash("[00:01]We sail home\n[00:04]Across the quiet bay\n[00:08]The stars guide us"),DateTimeOffset.UtcNow,1000,"en-US",new(0,0,400,500),["We sail home","Across the qulet bay","Another original fixture"]);
        var original=new LrcParser().Parse("[00:01]We sail home\n[00:04]Across the quiet bay\n[00:08]The stars guide us");
        coordinator.Import(observation.Track,original.Raw,"原创 OCR 回归",false);
        var review=new LyricsReviewWindow(coordinator,(_,_,_,_)=>throw new InvalidOperationException("UI fixtures must not capture native media")){Owner=this};review.Show();
        try
        {
            var report=LyricsOcrComparison.Compare(observation.Track,original,observation);review.ShowReport(report);await Task.Delay(100);review.UpdateLayout();
            checks["review.fixtureShowsExactSimilarAndUnmatched"]=review.ComparedLines.Select(line=>line.Match).SequenceEqual(new[]{LyricsOcrMatch.Matched,LyricsOcrMatch.Similar,LyricsOcrMatch.Unmatched});
            checks["review.neverMarksWholeSongReviewed"]=!report.FullSongReviewed;
            VisualVerification.Save(review,Path.Combine(directory,"lyrics-review.png"));
            string originalTheme=ThemeManager.Current.Id;var table=Descendants<DataGrid>(review).Single();
            foreach(var theme in AppThemes.All)
            {
                SelectUiTheme(theme.Id,false);await Task.Delay(70);review.UpdateLayout();var first=(DataGridRow)table.ItemContainerGenerator.ContainerFromIndex(0);
                checks[$"review.{theme.Id}.openWindowFollowsTheme"]=first.Background==review.FindResource("PanelBrush") && first.Foreground==review.FindResource("TextBrush");
                checks[$"review.{theme.Id}.columnsRemainReadable"]=table.Columns.All(column=>column.ActualWidth>=90) && table.Columns.Sum(column=>column.ActualWidth)<=table.ActualWidth+2;
                VisualVerification.Save(review,Path.Combine(directory,"proofreading-"+theme.Id+".png"));
            }
            SelectUiTheme(originalTheme,false);
        }
        finally{review.Close();}
    }
}
