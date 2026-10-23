using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

internal sealed partial class PlaybackQueueView
{
    internal sealed record QueueDrag(PlaybackQueueView Owner,MusicQueueSnapshot Snapshot,int Index,QueueRow Row);
    Func<MusicQueueMove,CancellationToken,Task<LibraryPlayResult>>? reorder;
    Point dragOrigin,dragPoint;
    QueueDrag? armedDrag;
    DropHint? dropHint;
    readonly DispatcherTimer dragScroll=new(){Interval=TimeSpan.FromMilliseconds(65)};
    internal bool IsDragging {get;private set;}
    void SetupReordering(Func<MusicQueueMove,CancellationToken,Task<LibraryPlayResult>>? move)
    {
        reorder=move;if(move is null)return;
        upcoming.AllowDrop=true;upcoming.ToolTip="拖动曲目调整实际播放顺序；双击或按 Enter 播放";
        upcoming.PreviewMouseLeftButtonDown+=(sender,e)=>
        {
            armedDrag=null;
            if(IsSelecting || e.ClickCount!=1 || Snapshot is not {Available:true,IsComplete:true} snapshot || e.OriginalSource is not DependencyObject source)return;
            if(ItemsControl.ContainerFromElement(upcoming,source) is ListBoxItem{DataContext:QueueRow row})
            {dragOrigin=e.GetPosition(upcoming);armedDrag=new(this,snapshot,row.Number-1,row);}
        };
        upcoming.PreviewMouseLeftButtonUp+=(_,_)=>armedDrag=null;
        upcoming.PreviewMouseMove+=(sender,e)=>
        {
            if(armedDrag is not { } payload || IsSelecting || e.LeftButton!=MouseButtonState.Pressed)return;
            var point=e.GetPosition(upcoming);
            if(Math.Abs(point.X-dragOrigin.X)<SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y-dragOrigin.Y)<SystemParameters.MinimumVerticalDragDistance)return;
            armedDrag=null;IsDragging=true;PlaybackStarting?.Invoke();
            try{DragDrop.DoDragDrop(upcoming,new DataObject(typeof(QueueDrag),payload),DragDropEffects.Move);}
            finally{IsDragging=false;ClearDropHint();if(!selecting)PlaybackCompleted?.Invoke();}
            e.Handled=true;
        };
        upcoming.DragOver+=(sender,e)=>
        {
            e.Handled=true;e.Effects=DragDropEffects.None;
            if(!TryDrop(e.Data,e.GetPosition(upcoming),out _,out double y,true)){ClearDropHint();return;}
            dragPoint=e.GetPosition(upcoming);e.Effects=DragDropEffects.Move;ShowDropHint(y);dragScroll.Start();
        };
        upcoming.DragLeave+=(_,_)=>ClearDropHint();
        upcoming.Drop+=(sender,e)=>
        {
            e.Handled=true;e.Effects=DragDropEffects.None;ClearDropHint();
            if(!TryDrop(e.Data,e.GetPosition(upcoming),out var request,out _))return;
            e.Effects=DragDropEffects.Move;BeginReorder(request!);
        };
        dragScroll.Tick+=(_,_)=>
        {
            if(disposed || !IsDragging || !upcoming.IsVisible){ClearDropHint();return;}
            var scroll=FindScroll(upcoming);if(scroll is null)return;
            double delta=dragPoint.Y<32?-18:dragPoint.Y>upcoming.ActualHeight-32?18:0;
            if(delta!=0){scroll.ScrollToVerticalOffset(scroll.VerticalOffset+delta);upcoming.UpdateLayout();ShowDropHint(Math.Clamp(dragPoint.Y,2,Math.Max(2,upcoming.ActualHeight-2)));}
        };
    }
    internal bool TryDrop(IDataObject data,Point point,out MusicQueueMove? request,out double y,bool allowUnchanged=false)
    {
        request=null;y=0;
        if(disposed || reorder is null || selecting || !data.GetDataPresent(typeof(QueueDrag)) || data.GetData(typeof(QueueDrag)) is not QueueDrag drag || drag.Owner!=this || Snapshot is not {Available:true,IsComplete:true} actual
            || drag.Index<0 || drag.Index>=upcoming.Items.Count || !ReferenceEquals(upcoming.Items[drag.Index],drag.Row))return false;
        int insertion=upcoming.Items.Count;bool found=false;
        for(int i=0;i<upcoming.Items.Count;i++)
        {
            if(upcoming.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem container || !container.IsVisible)continue;
            var top=container.TranslatePoint(new Point(0,0),upcoming);double bottom=top.Y+container.ActualHeight;
            if(bottom<0)continue;if(top.Y>upcoming.ActualHeight)break;
            if(point.Y<top.Y+container.ActualHeight/2){insertion=i;y=top.Y;found=true;break;}
            insertion=i+1;y=bottom;found=true;
        }
        if(!found)return false;
        request=new(drag.Snapshot,drag.Index,insertion);
        return (allowUnchanged || MusicPlaybackQueue.CanMove(request)) && MusicPlaybackQueue.MatchesSelection(new(drag.Snapshot,drag.Index),actual);
    }
    internal void BeginReorder(MusicQueueMove move)
    {
        if(disposed || reorder is null || selecting || Snapshot is not { } actual || !MusicPlaybackQueue.CanMove(move) || !MusicPlaybackQueue.MatchesMove(move,actual))return;
        PlaybackStarting?.Invoke();ActivePlayback=ReorderAsync(move);PlaybackStarted?.Invoke(ActivePlayback);
    }
    async Task ReorderAsync(MusicQueueMove move)
    {
        playing?.Cancel();playing?.Dispose();playing=new();var token=playing.Token;long version=playVersions.Next();selecting=true;
        message.Text=playMessage="正在同步播放顺序…";RefreshButton.IsEnabled=false;
        try
        {
            var result=await reorder!(move,token);token.ThrowIfCancellationRequested();
            if(disposed || !playVersions.IsCurrent(version))return;
            message.Text=playMessage=result.Message;if(result.Confirmed && result.ActivateWindow)activate?.Invoke();
        }
        catch(OperationCanceledException){}
        catch(Exception ex) when(ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException or System.IO.IOException or TimeoutException)
        {if(!disposed && playVersions.IsCurrent(version))message.Text=playMessage="排序暂未确认，请刷新播放列表查看。";}
        finally{if(!disposed && playVersions.IsCurrent(version)){selecting=false;RefreshButton.IsEnabled=true;PlaybackCompleted?.Invoke();}}
    }
    internal void ShowDropHint(double y)
    {
        if(dropHint is null && AdornerLayer.GetAdornerLayer(upcoming) is { } layer){dropHint=new(upcoming);layer.Add(dropHint);}
        if(dropHint is not null){dropHint.Y=Math.Clamp(y,2,Math.Max(2,upcoming.ActualHeight-2));dropHint.InvalidateVisual();}
    }
    void ClearDropHint(){dragScroll.Stop();if(dropHint is not null){AdornerLayer.GetAdornerLayer(upcoming)?.Remove(dropHint);dropHint=null;}}
    static ScrollViewer? FindScroll(DependencyObject root)
    {if(root is ScrollViewer scroll)return scroll;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)if(FindScroll(VisualTreeHelper.GetChild(root,i)) is { } found)return found;return null;}
    sealed class DropHint:Adorner
    {
        internal DropHint(UIElement element):base(element){IsHitTestVisible=false;}
        internal double Y;
        protected override void OnRender(DrawingContext context)
        {
            var brush=(AdornedElement as FrameworkElement)?.TryFindResource("AccentBrush") as Brush??Brushes.CornflowerBlue;
            context.DrawLine(new(brush,2.5),new(4,Y),new(Math.Max(4,AdornedElement.RenderSize.Width-5),Y));context.DrawEllipse(brush,null,new(5,Y),3,3);
        }
    }
}
