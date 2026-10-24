using System.Windows;
using System.Windows.Automation;
namespace LumaLyrics.App;

public partial class MainWindow
{
    bool updatingQueueToggle;
    void PlaybackQueueToggled(object sender,RoutedEventArgs e)
    {
        if(!updatingQueueToggle && playbackSidebar is not null)SetPlaybackQueueVisible(PlaybackQueueToggle.IsChecked==true);
    }
    void SetPlaybackQueueVisible(bool visible,bool persist=true)
    {
        settings.ShowPlaybackQueue=visible;updatingQueueToggle=true;
        try{PlaybackQueueToggle.IsChecked=visible;}finally{updatingQueueToggle=false;}
        PlaybackQueueHost.Visibility=visible?Visibility.Visible:Visibility.Collapsed;
        PlaybackQueueToggle.ToolTip=visible?"隐藏播放列表":"显示播放列表";
        AutomationProperties.SetName(PlaybackQueueToggle,visible?"隐藏播放列表":"显示播放列表");
        ApplyPlaybackLayout();if(persist){coordinator.SaveSettings();if(visible && PlaybackQueueHost.Child is FrameworkElement queue)AppMotion.Reveal(queue);else if(!visible)AppMotion.Reveal(LyricsScroll);}
    }
}
