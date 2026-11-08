using System.IO;
using System.Windows;
using System.Windows.Controls;
using LayoutInformation=System.Windows.Controls.Primitives.LayoutInformation;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;
public partial class MainWindow
{
    async Task VerifyLibraryLayoutAsync(string directory,Dictionary<string,object?> checks)
    {
        string? source=settings.LibraryPath;string theme=settings.UiTheme;bool reduced=settings.ReduceMotion;SettingsWindow? window=null;
        try
        {
            settings.LibraryPath="own-layout";
            var tracks=Enumerable.Range(0,3003).Select(i=>new LibraryTrack(i.ToString(),new Track(i<50?"Own song "+i:"Own recording with a deliberately longer original fixture title "+i,"Own artist","Own album",10000))).ToArray();
            using(var lyrics=new LyricsRepository(Path.Combine(coordinator.DataRoot,"lyrics.db")))lyrics.SyncLibrary(new("own-layout",tracks.Length,tracks,"own"));
            await coordinator.ReadWorkspaceAsync(default,true);
            window=new(coordinator,id=>SelectUiTheme(id,false)){Owner=this};window.Show();window.SelectLibraryTab();var tabs=window.Library.Navigation;
            foreach(var appTheme in AppThemes.All)
            {
                SelectUiTheme(appTheme.Id,false);
                foreach(double width in new[]{1220d,980d,820d,640d})
                {
                    window.Width=width;window.Height=width==640?520:820;
                    for(int page=0;page<4;page++)
                    {
                        tabs.SelectedIndex=page;if(page==1)await window.Library.Songs.Reload();if(page==2)await window.Library.Tasks.Reload();await Task.Delay(75);window.UpdateLayout();
                        string key=$"layout.{appTheme.Id}.{width}.{page}";
                        var content=(FrameworkElement)((TabItem)tabs.Items[page]).Content;
                        var host=(ContentPresenter)tabs.Template.FindName("PART_SelectedContentHost",tabs);var slot=LayoutInformation.GetLayoutSlot(host);
                        var origin=content.TranslatePoint(new Point(),tabs);
                        checks[key+".fillsWidth"]=slot.Width>200&&Math.Abs(content.ActualWidth-slot.Width)<1&&Math.Abs(origin.X-slot.X)<1;
                        checks[key+".fillsHeight"]=Math.Abs(content.ActualHeight-slot.Height)<1;
                        var scroll=page==1?Descendants<ScrollViewer>(window.Library.Songs.SongList).First():page is 0 or 3?(ScrollViewer)content:null;
                        if(scroll is not null)
                        {
                            scroll.ScrollToTop();window.UpdateLayout();await Task.Delay(30);
                            double before=content.ActualWidth,viewport=scroll.ViewportWidth,offset=scroll.VerticalOffset;
                            scroll.ScrollToVerticalOffset(scroll.ScrollableHeight/2);scroll.LineDown();window.UpdateLayout();await Task.Delay(45);window.UpdateLayout();
                            checks[key+".scrollWidthStable"]=Math.Abs(content.ActualWidth-before)<1&&Math.Abs(scroll.ViewportWidth-viewport)<1;
                            if(page==1)checks[key+".scrollActuallyMoved"]=scroll.VerticalOffset>offset;
                            checks[key+".geometry"]=new{Available=slot.Width,Before=before,After=content.ActualWidth,ViewportBefore=viewport,ViewportAfter=scroll.ViewportWidth};
                            scroll.ScrollToTop();window.UpdateLayout();
                        }
                        if(width is 1220 or 640)VisualVerification.Save(window,Path.Combine(directory,$"layout-{appTheme.Id}-{width}-{page}.png"));
                    }
                    // Returning to an earlier page must not restore a cached desired-width layout.
                    tabs.SelectedIndex=1;await Task.Delay(35);window.UpdateLayout();
                    checks[$"layout.{appTheme.Id}.{width}.songReentry"]=Math.Abs(window.Library.Songs.ActualWidth-tabs.ActualWidth)<1;
                    checks[$"layout.{appTheme.Id}.{width}.virtualized"]=Descendants<ListBoxItem>(window.Library.Songs.SongList).Count()<100&&window.Library.Songs.SongList.Items.Count==3003;
                }
            }
            AppMotion.Configure(true);window.Width=1220;tabs.SelectedIndex=0;window.UpdateLayout();tabs.SelectedIndex=1;await Task.Delay(40);window.UpdateLayout();
            checks["layout.reducedMotion.fillsWidth"]=Math.Abs(window.Library.Songs.ActualWidth-tabs.ActualWidth)<1;
        }
        finally{window?.Close();settings.LibraryPath=source;SelectUiTheme(theme,false);AppMotion.Configure(reduced);}
    }
}
