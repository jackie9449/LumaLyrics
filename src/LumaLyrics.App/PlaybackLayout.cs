using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace LumaLyrics.App;

public partial class MainWindow
{
    double playbackTextScale=1;
    DispatcherOperation? resizeRefresh;

    void ApplyPlaybackLayout()
    {
        if(InfoPanel is null || ActualWidth<=0)return;
        double inset=Math.Clamp(ActualWidth*.022,22,60),gap=Math.Clamp(ActualWidth*.023,20,40);
        LyricsPage.Margin=new(inset,14,inset,18);
        double bodyWidth=Math.Min(2080,Math.Max(1,ActualWidth-82-inset*2));
        double queueWidth=settings.ShowPlaybackQueue?Math.Clamp(ActualWidth*.212,244,320):0;
        double available=bodyWidth-queueWidth;
        double height=Body.ActualHeight>0?Body.ActualHeight:Math.Max(240,ActualHeight-230);
        bool compact=ActualWidth<(settings.ShowPlaybackQueue?1180:940) || height<500;
        double cover=compact?Math.Clamp(available*.25,96,144):Math.Min(Math.Clamp(available*.34,200,640),Math.Max(160,height-235));
        double dpi=VisualTreeHelper.GetDpi(this).DpiScaleX;cover=Math.Round(cover*dpi)/dpi;

        Grid.SetColumn(InfoPanel,0);Grid.SetRow(InfoPanel,0);Grid.SetColumnSpan(InfoPanel,compact?2:1);
        Grid.SetColumn(LyricsPanel,compact?0:1);Grid.SetRow(LyricsPanel,compact?1:0);Grid.SetColumnSpan(LyricsPanel,compact?2:1);
        InfoColumn.Width=compact?new(1,GridUnitType.Star):new(cover+gap);
        PlaybackQueueColumn.Width=new(queueWidth);
        InfoRow.Height=compact?new(Math.Clamp(height*.29,154,178)):new(1,GridUnitType.Star);
        LyricsRow.Height=compact?new(1,GridUnitType.Star):new(0);
        InfoPanel.Margin=compact?new(0,0,0,12):new(0,0,gap,0);
        foreach(var row in InfoPanel.RowDefinitions)row.Height=GridLength.Auto;
        InfoPanel.ColumnDefinitions[0].Width=compact?new(cover):new(1,GridUnitType.Star);
        InfoPanel.ColumnDefinitions[1].Width=new(compact?18:0);
        InfoPanel.ColumnDefinitions[2].Width=compact?new(1,GridUnitType.Star):new(0);
        Grid.SetColumn(ArtworkBorder,0);Grid.SetRow(ArtworkBorder,0);Grid.SetRowSpan(ArtworkBorder,1);
        ArtworkBorder.Visibility=Visibility.Visible;ArtworkBorder.Width=ArtworkBorder.Height=cover;ArtworkBorder.VerticalAlignment=VerticalAlignment.Top;ArtworkBorder.HorizontalAlignment=HorizontalAlignment.Left;
        Grid.SetColumn(TrackMetadata,compact?2:0);Grid.SetRow(TrackMetadata,compact?0:1);
        double titleSize=compact?Math.Clamp(ActualWidth/49,17,21):Math.Clamp(cover*.09,24,34);
        TrackTitle.FontSize=titleSize;TrackTitle.MaxHeight=compact?titleSize*1.4:titleSize*2.6;
        TrackTitle.Margin=compact?new(0,0,0,4):new(0,Math.Clamp(cover*.065,18,30),0,7);
        TrackArtist.FontSize=compact?11:Math.Clamp(cover*.04,12,15);TrackArtist.MaxHeight=compact?18:42;
        TrackArtist.LineHeight=TrackArtist.FontSize*1.5;
        TrackTime.Margin=compact?new(0,5,0,0):new(0,18,0,2);
        PlaybackControls.Margin=compact?new(0,2,0,0):new(0,8,0,12);
        PlayButton.MinWidth=compact?96:Math.Clamp(cover*.42,100,150);
        ConnectionStatus.Visibility=QualityStatus.Visibility=compact?Visibility.Collapsed:Visibility.Visible;
        playbackTextScale=Math.Clamp(Math.Min(ActualWidth/1220,ActualHeight/820),.8,1.12);
        double lyricInset=Math.Clamp(ActualWidth*.024,18,44);
        LyricContent.Margin=new(lyricInset,22,lyricInset,20);
        EmptyState.MaxWidth=compact?460:390;
        EmptyState.LayoutTransform=new ScaleTransform(compact?.68:1,compact?.68:1);
        // One refresh per render frame during native sizing; never queue a scroll for every mouse event.
        if(resizeRefresh is not {Status:DispatcherOperationStatus.Pending})
            resizeRefresh=Dispatcher.BeginInvoke(DispatcherPriority.Render,()=>{
                resizeRefresh=null;if(quitting || !IsLoaded)return;
                StyleLyricLines(-1,currentIndex);
                if(currentIndex>=0 && DateTime.UtcNow>=manualUntil)CenterLine(true);
            });
    }
}
