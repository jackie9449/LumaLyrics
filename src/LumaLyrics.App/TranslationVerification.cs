using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

public partial class MainWindow
{
    async Task VerifyTranslationsAsync(string directory,Dictionary<string,object?> checks)
    {
        const string original="[00:00]We follow the river into morning\n[00:12]The city wakes beneath a silver sky\n[00:24]The stars will guide us home\n[00:36]\n[00:48]We carry little lights across the water\n[01:00]A song can keep the distant shore in sight\n[01:12]The wind will turn the page\n[01:24]And leave a quiet glow beside the night";
        const string chinese="[00:00]我们沿着河流走向清晨\n[00:12]城市在银色天空下醒来\n[00:24]星光会指引我们归家\n[00:48]我们携着微光渡过水面\n[01:00]歌声让远岸始终在望\n[01:12]微风轻轻翻过这一页\n[01:24]在夜色身旁留下一抹柔光";
        var track=new Track("归途的星光","浮光","原创双语测试文字",96000);
        var previousTrack=coordinator.ActiveTrack;var previousBinding=coordinator.Binding;var previousSnapshot=coordinator.Snapshot;
        bool main=settings.ShowChineseTranslation,floating=settings.OverlayShowChineseTranslation;string theme=settings.UiTheme;
        var appearance=OverlayAppearance.From(settings);bool visible=overlay.IsVisible;SettingsWindow? preferences=null;
        try
        {
            coordinator.Import(track,original,"Original bilingual verification fixture",false);coordinator.ImportChineseTranslation(chinese);
            coordinator.SetTranslationVerificationSnapshot(track,48000);Tick();
            checks["translation.loadedExactOriginal"]=coordinator.Translation?.Lines.Count==7;
            coordinator.SetTranslationVisibility(false,false);await Task.Delay(40);
            checks["translation.disabledKeepsOriginalLayout"]=lyricBlocks.Count==8 && LyricLines.Children.OfType<TextBlock>().Count()==8 && translationBlocks.All(b=>b.Visibility==Visibility.Collapsed);
            checks["translation.disabledClearsFloating"]=vm.CurrentTranslation.Length==0 && vm.NextTranslation.Length==0;
            preferences=new(coordinator,id=>SelectUiTheme(id)){Owner=this};preferences.Show();preferences.SelectTranslationTab();await Task.Delay(70);
            preferences.ChineseTranslations.SetVisibility(true,false);await Task.Delay(40);
            checks["translation.settingsTabAvailable"]=Descendants<TabItem>(preferences).Any(t=>t.Header?.ToString()=="中文译文");
            checks["translation.mainToggleIndependent"]=settings.ShowChineseTranslation && !settings.OverlayShowChineseTranslation && translationBlocks.Count(b=>b.Visibility==Visibility.Visible)==7 && vm.CurrentTranslation.Length==0;
            checks["translation.savedImmediately"]=store.Load().ShowChineseTranslation && !store.Load().OverlayShowChineseTranslation;
            var keepBrowsing=DateTime.UtcNow.AddMinutes(1);manualUntil=keepBrowsing;preferences.ChineseTranslations.SetVisibility(false,false);preferences.ChineseTranslations.SetVisibility(true,false);
            checks["translation.togglePreservesManualBrowsing"]=manualUntil==keepBrowsing;manualUntil=DateTime.MinValue;
            checks["translation.originalRowsAndBlankIntervalPreserved"]=lyricBlocks.Count==8 && translationBlocks[3].Visibility==Visibility.Collapsed && lyricBlocks[3].Text==" ";
            checks["translation.sourceAndReviewVisible"]=Descendants<TextBlock>(preferences.ChineseTranslations).Any(b=>b.Text.Contains("个人导入")&&b.Text.Contains("逐行对应"));
            VisualVerification.Save(preferences,Path.Combine(directory,"settings-translations.png"));preferences.ChineseTranslations.SetVisibility(false,true);await Task.Delay(40);
            checks["translation.floatingToggleIndependent"]=!settings.ShowChineseTranslation && translationBlocks.All(b=>b.Visibility==Visibility.Collapsed) && vm.CurrentTranslation=="我们携着微光渡过水面";
            checks["translation.nextRemainsNextOriginal"]=vm.Next=="A song can keep the distant shore in sight" && vm.NextTranslation=="歌声让远岸始终在望";
            preferences.ChineseTranslations.SetVisibility(true,true);preferences.Close();preferences=null;
            overlay.Show();(appearance with{Animations=false,TwoLines=true,Width=820}).Apply(settings);overlay.ApplyStyle();
            await Task.Delay(70);
            checks["translation.floatingBothLinesRender"]=overlay.LyricSurface.CurrentChinese.IsVisible && overlay.LyricSurface.NextChinese.IsVisible && overlay.LyricSurface.NextLine.Text==vm.Next;
            checks["translation.floatingSubordinateTypography"]=overlay.LyricSurface.CurrentChinese.FontSize<overlay.LyricSurface.CurrentLine.FontSize;
            VisualVerification.Save(overlay.LyricSurface,Path.Combine(directory,"overlay-translations.png"));
            foreach(var id in new[]{"liquid","frost","material"})
            {
                SelectUiTheme(id,false);Width=1220;Height=820;Tick();await Task.Delay(60);
                checks["translation."+id+".subordinateText"]=translationBlocks[4].IsVisible && translationBlocks[4].FontSize<lyricBlocks[4].FontSize && translationBlocks[4].Foreground==FindResource("MutedBrush");
                checks["translation."+id+".preservesOriginalHighlight"]=lyricBlocks[4].FontSize==34 && currentIndex==4;
                VisualVerification.Save((FrameworkElement)Content,Path.Combine(directory,"translations-"+id+".png"),true);
            }
            (appearance with{Animations=false,TwoLines=false}).Apply(settings);overlay.ApplyStyle();await Task.Delay(40);
            checks["translation.singleLineHidesNextPair"]=overlay.LyricSurface.NextHost.Visibility==Visibility.Collapsed && overlay.LyricSurface.NextChinese.Visibility==Visibility.Collapsed && overlay.LyricSurface.CurrentChinese.IsVisible;
            coordinator.SetTranslationVerificationSnapshot(track,36000);Tick();await Task.Delay(40);
            checks["translation.emptyIntervalStaysBlank"]=vm.Current=="" && vm.CurrentTranslation=="" && overlay.LyricSurface.CurrentChinese.Visibility==Visibility.Collapsed;
            coordinator.SetTranslationVerificationSnapshot(track,48000);coordinator.SetOffset(100);Tick();
            checks["translation.followsExistingOffset"]=vm.CurrentTranslation.Length==0;coordinator.SetOffset(0);
            coordinator.Import(track,original.Replace("little lights","gentle lights"),"Changed original verification fixture",false);
            checks["translation.changedOriginalInvalidatesOldText"]=coordinator.Translation is null && translationBlocks.All(b=>b.Visibility==Visibility.Collapsed) && vm.CurrentTranslation.Length==0;
            coordinator.SelectCandidate(track,new(900010,track,null,null,true));Tick();await Task.Delay(40);
            checks["translation.instrumentalSuppressesBothLanguages"]=vm.Current.Length==0 && vm.Next.Length==0 && vm.CurrentTranslation.Length==0 && vm.NextTranslation.Length==0 && overlay.InstrumentalSuppressed;
            coordinator.Import(track,original,"Restored original bilingual fixture",false);Tick();await Task.Delay(40);
            checks["translation.originalRestoresItsCachedTranslation"]=coordinator.Translation is not null && !overlay.InstrumentalSuppressed;
            using(var translations=new TranslationRepository(store.Root))
            {
                var cached=coordinator.Translation!;translations.Save(track,cached with{Draft=true,Source="AI verification fixture",Review="AI 译稿 · 待校对",PreparedUtc=DateTimeOffset.UtcNow},true);
            }
            coordinator.RefreshTranslation();coordinator.SetTranslationVisibility(true,true);coordinator.SetTranslationVerificationSnapshot(track,48000);Tick();await Task.Delay(40);
            checks["translation.aiMainHasNoReviewBadge"]=!vm.LyricStatus.Contains("待校对") && translationBlocks[4].IsVisible;
            checks["translation.aiFloatingHasNoReviewBadge"]=!Descendants<TextBlock>(overlay.LyricSurface).Any(b=>b.IsVisible && b.Text.Contains("待校对")) && overlay.LyricSurface.CurrentChinese.IsVisible;
            checks["translation.privateProvenanceRetained"]=coordinator.Translation is{Draft:true,Review:"AI 译稿 · 待校对"};
            preferences=new(coordinator,id=>SelectUiTheme(id)){Owner=this};preferences.Show();preferences.SelectTranslationTab();await Task.Delay(40);
            checks["translation.aiSettingsHasNoReviewBadge"]=!Descendants<TextBlock>(preferences.ChineseTranslations).Any(b=>b.Text.Contains("待校对")) && coordinator.TranslationStatus.Contains("AI 生成");
            overlay.ToolbarPopup.IsOpen=true;await Task.Delay(40);
            checks["translation.toolbarSwitchAvailableAboveLyrics"]=overlay.ChineseTranslationToggle.IsVisible && System.Windows.Automation.AutomationProperties.GetName(overlay.ChineseTranslationToggle)=="悬浮歌词中文译文开关" && overlay.Toolbar.PointToScreen(new()).Y<overlay.PointToScreen(new()).Y;
            string unchangedTheme=settings.UiTheme;string unchangedChrome=overlay.Toolbar.Background.ToString();
            var peer=new System.Windows.Automation.Peers.ToggleButtonAutomationPeer(overlay.ChineseTranslationToggle);
            var toggle=(System.Windows.Automation.Provider.IToggleProvider)peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Toggle);
            toggle.Toggle();await Task.Delay(40);
            checks["translation.toolbarToggleOnlyChangesFloating"]=settings.ShowChineseTranslation && !settings.OverlayShowChineseTranslation && translationBlocks[4].IsVisible && vm.CurrentTranslation.Length==0;
            checks["translation.toolbarTogglePersists"]=store.Load().ShowChineseTranslation && !store.Load().OverlayShowChineseTranslation;
            var floatingSetting=Descendants<CheckBox>(preferences.ChineseTranslations).Single(c=>System.Windows.Automation.AutomationProperties.GetName(c)=="悬浮歌词中文译文开关");
            checks["translation.toolbarToggleReflectsInOpenSettings"]=floatingSetting.IsChecked==false;
            preferences.ChineseTranslations.SetVisibility(false,true);await Task.Delay(40);
            checks["translation.settingsReflectsInToolbar"]=overlay.ChineseTranslationToggle.IsChecked==true && vm.CurrentTranslation.Length>0 && !settings.ShowChineseTranslation;
            toggle.Toggle();await Task.Delay(40);
            checks["translation.toolbarPreservesDisabledMainSwitch"]=!settings.ShowChineseTranslation && !settings.OverlayShowChineseTranslation;
            checks["translation.toolbarPreservesThemes"]=settings.UiTheme==unchangedTheme && overlay.Toolbar.Background.ToString()==unchangedChrome;
            toggle.Toggle();await Task.Delay(40);
            VisualVerification.Save(overlay.Toolbar,Path.Combine(directory,"overlay-translation-toolbar.png"));
            checks["translation.toolbarToggleRestoresTranslation"]=!settings.ShowChineseTranslation && vm.CurrentTranslation.Length>0;
            preferences.Close();preferences=null;overlay.ToolbarPopup.IsOpen=false;
        }
        finally
        {
            preferences?.Close();coordinator.SetTranslationVerificationSnapshot(null,0);
            coordinator.SetTranslationVisibility(main,floating);SelectUiTheme(theme,false);appearance.Apply(settings);overlay.ApplyStyle();if(!visible)overlay.Hide();
            if(previousTrack is not null && previousBinding is not null)coordinator.Import(previousTrack,previousBinding.Raw,previousBinding.Source,previousBinding.Plain);
            if(previousSnapshot is not null)coordinator.SetTranslationVerificationSnapshot(previousSnapshot.Track,previousSnapshot.PositionMs??0);
        }
    }
}

public sealed partial class PlaybackCoordinator
{
    internal void SetTranslationVerificationSnapshot(Track? track,double position)
    {
        if(!Environment.GetCommandLineArgs().Contains("--verify-ui"))throw new InvalidOperationException("Translation fixtures require explicit isolated UI verification.");
        Snapshot=track is null?null:new("original-translation-fixture",1,1,track,PlayState.Paused,1,position,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,Stopwatch.GetTimestamp(),PositionQuality.Reliable);
        clock.Update(Snapshot);RefreshTranslation();Updated?.Invoke();
    }
}
