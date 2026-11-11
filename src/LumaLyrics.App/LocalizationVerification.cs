using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

public partial class MainWindow
{
    async Task VerifyLocalizationAsync(string directory,Dictionary<string,object?> checks)
    {
        string language=UiText.Language,theme=settings.UiTheme;
        var appearance=OverlayAppearance.From(settings);bool visible=overlay.IsVisible;
        var track=new Track("设置","播放","Original locale fixture",60000);
        const string lyrics="[00:00]播放\n[00:12]设置\n[00:24]Original lyric · 歌詞 · 日本語 🌙\n[00:36]\n[00:48]A quiet light";
        async Task Settle(){await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(75);UpdateLayout();}
        Rect Bounds(FrameworkElement item,Visual relative)=>item.TransformToAncestor(relative).TransformBounds(new Rect(item.RenderSize));
        bool LabelsFit(DependencyObject root)=>Descendants<Button>(root).Where(b=>b.IsVisible && b.Content is string && b.ActualWidth>0).All(b=>
        {
            var text=new FormattedText((string)b.Content,UiText.Culture,FlowDirection.LeftToRight,new Typeface(b.FontFamily,b.FontStyle,b.FontWeight,b.FontStretch),b.FontSize,Brushes.Black,VisualTreeHelper.GetDpi(b).PixelsPerDip);
            return text.WidthIncludingTrailingWhitespace<=b.ActualWidth+1;
        });
        try
        {
            settings.OnlineLookup=false;settings.AutoPrefetchLibrary=false;
            Width=1220;Height=820;WindowState=WindowState.Normal;
            coordinator.Import(track,lyrics,"Original localization verification",false);coordinator.SetTranslationVerificationSnapshot(track,1000);
            coordinator.SetChineseScript("original");coordinator.SetTranslationVisibility(false,false);
            checks["locale.loadedFromSettings"]=language==UiText.Normalize(store.Load().UiLanguage);
            checks["locale.mainPageDecorationRemoved"]=FindName("PlaybackHeadline") is null && FindName("PlaybackEyebrow") is null;
            checks["locale.originalMetadataPreserved"]=vm.Title=="设置" && vm.Artist.Contains("播放") && vm.Artist.Contains("Original locale fixture");
            checks["locale.originalLyricsPreserved"]=lyricBlocks[0].Text=="播放" && lyricBlocks[1].Text=="设置" && vm.Current=="播放" && vm.Next=="设置" && coordinator.Document!.Raw==lyrics;
            checks["locale.navigationLocalized"]=AutomationProperties.GetName(SettingsNav)==UiText.T("设置") && AutomationProperties.GetName(DiagnosticsNav)==UiText.T("诊断");
            (appearance with{Animations=false,TwoLines=true}).Apply(settings);overlay.ApplyStyle();overlay.Show();await Settle();
            overlay.ShowHoverControls();await Settle();
            checks["locale.overlayControlsLocalized"]=AutomationProperties.GetName(overlay.ChineseTranslationToggle)==UiText.T("悬浮歌词中文译文开关") && overlay.LyricSurface.CurrentLine.Text=="播放";
            VisualVerification.Save(overlay.Toolbar,Path.Combine(directory,"locale-overlay-toolbar.png"));overlay.HideHoverControls();
            foreach(var appTheme in AppThemes.All)
            {
                SelectUiTheme(appTheme.Id,false);
                foreach(double width in new[]{1220d,980d,820d})
                {
                    Width=width;await Settle();
                    string key=$"locale.main.{appTheme.Id}.{width:0}";
                    var controls=Bounds(PlaybackActions,this);var offsets=Bounds(OffsetActions,this);
                    var selector=(ComboBox)ChineseScriptHost.Content;var selectorBounds=Bounds(selector,LyricContent);
                    checks[key+".footerFits"]=controls.Right<=ActualWidth-3 && controls.Left>=0 && !controls.IntersectsWith(offsets);
                    checks[key+".selectorFits"]=selectorBounds.Left>=0 && selectorBounds.Right<=LyricContent.ActualWidth+.5;
                    checks[key+".buttonLabelsFit"]=LabelsFit(LyricsPage);
                    if(width==820 || appTheme.Id=="liquid" && width==1220)VisualVerification.Save((FrameworkElement)Content,Path.Combine(directory,$"locale-main-{appTheme.Id}-{width:0}.png"),true);
                }
            }
            Width=1220;SelectUiTheme(theme,false);await Settle();
            SettingsClicked(this,new());await Settle();
            checks["locale.settingsPageDecorationRemoved"]=!Descendants<TextBlock>(embeddedPageHost).Any(t=>t.Text is "P R E F E R E N C E S" or "让浮光，更合你心意。");
            var languagePanel=Descendants<InterfaceLanguagePanel>(embeddedPageHost).Single();
            checks["locale.languageChoicesStable"]=languagePanel.Selector.Items.Cast<ComboBoxItem>().Select(i=>i.Tag).SequenceEqual(new[]{"zh-Hans","zh-Hant","en","ja"});
            checks["locale.languageChoiceRestored"]=languagePanel.Selector.SelectedValue?.ToString()==language;
            languagePanel.Selector.SelectedValue=language=="en"?"ja":"en";await Settle();
            checks["locale.preferenceSavedForRestart"]=store.Load().UiLanguage==(language=="en"?"ja":"en") && UiText.Language==language;
            languagePanel.Selector.SelectedValue=language;await Settle();
            foreach(var (name,select) in new (string,Action)[]{("application",()=>settingsWindow!.SelectApplicationTab()),("general",()=>settingsWindow!.SelectGeneralTab()),("appearance",()=>settingsWindow!.SelectOverlayTab()),("personal",()=>settingsWindow!.SelectPersonalTab()),("library",()=>settingsWindow!.SelectLibraryTab()),("songs",()=>settingsWindow!.SelectLibrarySongs()),("tasks",()=>settingsWindow!.SelectLibraryTasks()),("display",()=>settingsWindow!.SelectTranslationTab())})
            {
                select();await Settle();checks["locale.settings."+name+".labelsFit"]=LabelsFit(embeddedPageHost);
                VisualVerification.Save((FrameworkElement)Content,Path.Combine(directory,"locale-settings-"+name+".png"),true);
                Width=820;await Settle();checks["locale.settings."+name+".narrowLabelsFit"]=LabelsFit(embeddedPageHost);
                if(name is "application" or "general" or "appearance")VisualVerification.Save((FrameworkElement)Content,Path.Combine(directory,"locale-settings-"+name+"-820.png"),true);
                Width=1220;await Settle();
            }
            ShowLyricsPage();await Settle();
            checks["locale.appearanceIdentityPreserved"]=settings.OverlayPreset==appearance.Preset;
            var confirmation=LocalizedMessageBox.Create(this,UiText.T("采用部分稿？旧译文保留在历史中。"),UiText.T("部分稿"),MessageBoxButton.OKCancel,MessageBoxImage.None,out var confirmationResult);
            _=Dispatcher.BeginInvoke(async ()=>{
                await Task.Delay(350);confirmation.UpdateLayout();
                checks["locale.confirmationButtonsLocalized"]=Descendants<Button>(confirmation).Any(b=>b.Content?.ToString()==UiText.T("确定")) && Descendants<Button>(confirmation).Any(b=>b.Content?.ToString()==UiText.T("取消"));
                VisualVerification.Save(confirmation,Path.Combine(directory,"locale-confirmation.png"));
                Descendants<Button>(confirmation).Single(b=>b.Content?.ToString()==UiText.T("取消")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            },DispatcherPriority.ApplicationIdle);
            confirmation.ShowDialog();checks["locale.confirmationCancelPreserved"]=confirmationResult()==MessageBoxResult.Cancel;
            var unknownAlbum=MusicLibraryAlbums.Group([new MusicLibrarySong("fixture","empty",new("Original title","","",10000))]).Single();
            var namedAlbum=MusicLibraryAlbums.Group([new MusicLibrarySong("fixture","named",new("Original title","未知艺人","未命名专辑",10000))]).Single();
            checks["locale.onlyGeneratedAlbumFallbacksLocalized"]=LocalizedAlbumLabels.Title(unknownAlbum)==UiText.T("未命名专辑") && LocalizedAlbumLabels.Artist(unknownAlbum)==UiText.T("未知艺人") && LocalizedAlbumLabels.Title(namedAlbum)=="未命名专辑" && LocalizedAlbumLabels.Artist(namedAlbum)=="未知艺人";
            var songs=new[]{new MusicLibrarySong("fixture","one",track,null,"Synced"),new MusicLibrarySong("fixture","two",new("Refresh","設定","Original locale fixture",80000)),new MusicLibrarySong("fixture","three",new("Music · 音樂","Artist","Another original album",100000))};
            var browser=new MusicLibraryView(_=>Task.FromResult(new MusicLibraryResult(songs,"")),(song,_)=>Task.FromResult(new LibraryPlayResult(true,song.Title)),()=>{},ShowLyricsPage,()=>{},vm,()=>{},()=>{},()=>{},(_,_)=>Task.FromResult(new LibraryPlayResult(true,"")),_=>Task.FromResult(new MusicQueueSnapshot(track,[new(songs[1].Track),new(songs[0].Track)],true,true,DateTimeOffset.UtcNow,"与 Apple Music 待播清单同步")),(song,_)=>Task.FromResult(new LibraryPlayResult(true,song.Title)));
            musicLibrary=browser;MainPageHost.Children.Add(browser);
            try
            {
                await ShowMusicLibraryAsync();await Settle();
                checks["locale.libraryAlbumMetadataPreserved"]=browser.AlbumGroups.Any(a=>a.Title=="Original locale fixture");
                VisualVerification.Save((FrameworkElement)Content,Path.Combine(directory,"locale-library-albums.png"),true);
                browser.ShowAllSongs();await Settle();
                checks["locale.librarySongMetadataPreserved"]=browser.SongList.Items.Cast<MusicLibraryView.MusicRow>().Any(r=>r.Song.Track.Title=="设置" && r.Song.Track.Artist=="播放");
                checks["locale.libraryActionsLocalized"]=Descendants<Button>(browser).Any(b=>b.Content?.ToString()==UiText.T("播放所选"));
                foreach(double width in new[]{1220d,980d,820d}){Width=width;await Settle();checks[$"locale.library.{width:0}.labelsFit"]=LabelsFit(browser);VisualVerification.Save((FrameworkElement)Content,Path.Combine(directory,$"locale-library-songs-{width:0}.png"),true);}
                browser.ShowQueue();await browser.ActiveQueueRefresh!;await Settle();
                checks["locale.queueIdentityPreserved"]=browser.QueuePanel.Snapshot!.Upcoming[0].Track.Title=="Refresh" && browser.QueuePanel.Snapshot.Current==track;
                checks["locale.queueActionsLocalized"]=browser.QueuePanel.RefreshButton.Content?.ToString()==UiText.T("刷新列表");
                VisualVerification.Save((FrameworkElement)Content,Path.Combine(directory,"locale-queue-820.png"),true);
            }
            finally{browser.Dispose();MainPageHost.Children.Remove(browser);musicLibrary=null;ShowLyricsPage();}
            await VerifyNativeLocalesAsync(checks);
            checks["locale.language"]=language;checks["locale.missingKeys"]=UiText.MissingKeys.Order(StringComparer.Ordinal).ToArray();
            checks["locale.catalogCompleteForVisitedPages"]=UiText.MissingKeys.Count==0;
        }
        finally
        {
            CloseEmbeddedPage();coordinator.SetTranslationVerificationSnapshot(null,0);SelectUiTheme(theme,false);appearance.Apply(settings);overlay.ApplyStyle();if(!visible)overlay.Hide();
        }
    }
}
