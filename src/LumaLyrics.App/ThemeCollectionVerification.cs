using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;

namespace LumaLyrics.App;

public partial class MainWindow
{
    async Task VerifyThemeCollectionAsync(string directory,Dictionary<string,object?> checks)
    {
        string original=settings.UiTheme;double width=Width,height=Height;var oldSource=Artwork.Source;var oldPalette=artworkPalette;
        overlay.ApplyStyle();var floating=OverlayAppearance.From(settings);var toolbar=overlay.Toolbar.Background.ToString();
        var images=new List<(string Name,BitmapSource Image)>();
        var structures=new HashSet<string>();var typefaces=new HashSet<string>();
        var paths=new List<string>();
        for(int i=0;i<6;i++)
        {
            var visual=new DrawingVisual();using(var dc=visual.RenderOpen())
            {
                var color=new[]{"#294E54","#A1684F","#596581","#5A6F58","#AD8B50","#835D6B"}[i];
                var paint=new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
                dc.DrawRectangle(paint,null,new Rect(0,0,900,900));dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(236,217,175)),null,new(600,315),255,255);
                dc.DrawEllipse(paint,null,new(455,220),245,245);dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(75,15,27,32)),null,new Rect(0,610,900,290));
                dc.DrawLine(new Pen(Brushes.White,1),new(70,690),new(830,690));
                var text=new FormattedText("L U M A  /  O R I G I N S",CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),24,Brushes.White,1);dc.DrawText(text,new(70,74));
                dc.DrawText(new FormattedText("微光之间",CultureInfo.GetCultureInfo("zh-CN"),FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),54,Brushes.White,1),new(70,730));
            }
            var bitmap=new RenderTargetBitmap(900,900,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
            string path=Path.Combine(store.Root,"theme-original-"+i+".img");var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(path))png.Save(file);paths.Add(path);
        }
        string[] titles=["微光之间","静静的海面","晚风来信","夜航","慢慢靠近","漫游日记","晴日","片刻留白","海岸线","遥远的灯火","流动的时间","归途"];
        var songs=Enumerable.Range(0,24).Select(i=>new MusicLibrarySong("original-theme-fixture",i.ToString(),new Track(titles[i%titles.Length],"浮光工作室","创作集 · "+(i/3+1),180000),paths[i%6])).ToArray();
        coordinator.Import(songs[0].Track,"[00:00]微风轻轻推开了窗\n[00:12]城市在远处慢慢发亮\n[00:24]把此刻，轻轻唱成一束光。\n[00:36]让旋律停留在你身旁\n[00:48]那些未曾说出口的向往\n[01:00]都随着音乐，去往远方", "原创主题预览",false);
        await coordinator.RefreshArtworkAsync();
        currentIndex=2;SetArtwork(File.ReadAllBytes(paths[0]));vm.Time="00:24 / 03:00";vm.Position=24000;vm.Duration=180000;vm.Status="浮光 · 原创界面预览";vm.Quality="音乐与文字，都在此刻相遇。";
        playbackSidebar.View.SetLibrary(songs);playbackSidebar.View.Apply(new(songs[0].Track,songs.Skip(1).Take(8).Select(s=>new MusicQueueEntry(s.Track)).ToArray(),true,true,DateTimeOffset.UtcNow,"原创界面示例"));
        var browser=new MusicLibraryView(_=>Task.FromResult(new MusicLibraryResult(songs,"原创主题预览")),(_,_)=>Task.FromResult(new LibraryPlayResult(true,"原创示例")),()=>{},()=>{},()=>{},vm,()=>{},()=>{},()=>{});
        MainPageHost.Children.Add(browser);browser.Visibility=Visibility.Collapsed;
        async Task Settle(){await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(340);UpdateLayout();}
        try
        {
            Width=1220;Height=820;SetPlaybackQueueVisible(true,false);
            foreach(var theme in AppThemes.All)
            {
                browser.Visibility=Visibility.Collapsed;ShowLyricsPage();SelectUiTheme(theme.Id,false);StyleLyricLines(-1,2);await Settle();
                string key="collection."+theme.Id;
                structures.Add(NavigationColumn.ActualWidth+"/"+ArtworkMat.Margin+"/"+LyricsPanel.CornerRadius+"/"+LyricsPanel.BorderThickness+"/"+Grid.GetRow(PlaybackHeadline));typefaces.Add(PlaybackHeadline.FontFamily.Source);
                var picture=VisualVerification.Render((FrameworkElement)Content,true);images.Add((theme.Name,picture));
                VisualVerification.Save((FrameworkElement)Content,Path.Combine(directory,"collection-playback-"+theme.Id+".png"),true);
                checks[key+".originalAndQueueRetained"]=displayed?.Groups.Count==6 && playbackSidebar.View.Snapshot?.Upcoming.Count==8 && currentIndex==2;
                checks[key+".squareArtworkAndReadableLyrics"]=Math.Abs(ArtworkBorder.ActualWidth-ArtworkBorder.ActualHeight)<.5 && lyricBlocks[2].ActualWidth>120 && lyricBlocks[2].FontSize>=27;
                checks[key+".actualOriginalCoverRendered"]=Artwork.Source is BitmapImage{PixelWidth:900} && CoverPlaceholder.Visibility==Visibility.Collapsed;
                checks[key+".floatingPaletteAndSettingsIndependent"]=floating==OverlayAppearance.From(settings) && toolbar==overlay.Toolbar.Background.ToString();
                LyricsPage.Visibility=Visibility.Collapsed;browser.Visibility=Visibility.Visible;await browser.EnsureLoadedAsync();SelectNavigation(true);await Settle();
                VisualVerification.Save((FrameworkElement)Content,Path.Combine(directory,"collection-library-"+theme.Id+".png"),true);
                checks[key+".libraryKeepsOriginalFixture"]=browser.AlbumShelf.Albums.Sum(a=>a.Songs.Count)==songs.Length;
                browser.Visibility=Visibility.Collapsed;ShowLyricsPage();
                var preferences=new SettingsWindow(coordinator,id=>SelectUiTheme(id,false)){Owner=this,Width=1120,Height=940};preferences.Show();await Settle();
                try
                {
                    checks[key+".sixAccessibleThemeChoices"]=Descendants<RadioButton>(preferences).Count(b=>b.GroupName=="ApplicationTheme")==AppThemes.All.Count && Descendants<ThemePreview>(preferences).Count()==AppThemes.All.Count;
                    VisualVerification.Save((FrameworkElement)preferences.Content,Path.Combine(directory,"collection-settings-"+theme.Id+".png"),true);
                    preferences.Width=640;await Settle();
                    var cards=Descendants<RadioButton>(preferences).Where(b=>b.GroupName=="ApplicationTheme").ToArray();
                    checks[key+".narrowGalleryDoesNotOverflow"]=cards.All(c=>c.ActualWidth>250 && c.ActualWidth<preferences.ActualWidth-146);
                }
                finally{preferences.Close();}
            }
            checks["collection.themesDifferInRenderedStructureNotOnlyColor"]=structures.Count==AppThemes.All.Count && typefaces.Count>=3;
            var sheet=new DrawingVisual();using(var dc=sheet.RenderOpen())
            {
                dc.DrawRectangle(Brushes.WhiteSmoke,null,new Rect(0,0,1500,790));
                for(int i=0;i<images.Count;i++)
                {
                    double x=i%3*500,y=i/3*395;dc.DrawImage(images[i].Image,new Rect(x+12,y+48,476,320));
                    dc.DrawText(new FormattedText(images[i].Name,CultureInfo.GetCultureInfo("zh-CN"),FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),19,Brushes.Black,1),new(x+16,y+16));
                }
            }
            var overview=new RenderTargetBitmap(1500,790,96,96,PixelFormats.Pbgra32);overview.Render(sheet);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(overview));using var output=File.Create(Path.Combine(directory,"theme-collection.png"));encoder.Save(output);
        }
        finally
        {
            browser.Dispose();MainPageHost.Children.Remove(browser);ShowLyricsPage();Artwork.Source=oldSource;artworkPalette=oldPalette;SelectUiTheme(original,false);Width=width;Height=height;
        }
    }
}
