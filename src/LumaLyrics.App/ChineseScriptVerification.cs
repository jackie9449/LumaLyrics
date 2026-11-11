using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Popup=System.Windows.Controls.Primitives.Popup;
using System.Windows.Threading;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
using LumaLyrics.Windows;

namespace LumaLyrics.App;

public partial class MainWindow
{
    async Task VerifyChineseScriptAsync(string directory,Dictionary<string,object?> checks)
    {
        const string original="[offset:125]\n[00:00]風中的音樂，照亮夢中的遠方\n[00:12]让微光陪我们走过长夜\n[00:24]\n[00:36]一起望见云端的星光 🌙\n[00:48]把温暖留在每扇窗前";
        const string firstSimplified="风中的音乐，照亮梦中的远方",firstTraditional="風中的音樂，照亮夢中的遠方";
        const string nextSimplified="让微光陪我们走过长夜",nextTraditional="讓微光陪我們走過長夜";
        const string english="[00:00]We keep a small light by the water\n[00:12]The wind carries our song across the sky\n[00:24]\n[00:36]The open road brings us home";
        const string chinese="[00:00]風中的音樂照亮遠方\n[00:12]让微光陪我们走过长夜\n[00:36]星光陪我们回家";
        var track=new Track("微光与远方","浮光工作室","原创简繁显示样例",60000);
        var previousTrack=coordinator.ActiveTrack;var previousBinding=coordinator.Binding;var previousSnapshot=coordinator.Snapshot;
        string previousScript=settings.ChineseScript,theme=settings.UiTheme;
        bool main=settings.ShowChineseTranslation,floating=settings.OverlayShowChineseTranslation,visible=overlay.IsVisible,queue=settings.ShowPlaybackQueue;
        double width=Width,height=Height,globalOffset=settings.GlobalOffsetMs;var state=WindowState;var browsing=manualUntil;
        var appearance=OverlayAppearance.From(settings);
        var choice=(ComboBox)ChineseScriptHost.Content;
        async Task Settle(){await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(50);UpdateLayout();}
        async Task Choose(string mode){choice.SelectedValue=mode;await Settle();Tick();}
        void Load(Track fixture,string raw,bool plain=false)
        {
            coordinator.SetTranslationVerificationSnapshot(null,0);
            coordinator.Import(fixture,raw,"原创简繁显示验证",plain);
            coordinator.SetTranslationVerificationSnapshot(fixture,1000);
        }
        try
        {
            WindowState=WindowState.Normal;Width=1220;Height=820;ShowLyricsPage();SetPlaybackQueueVisible(true);
            settings.GlobalOffsetMs=0;coordinator.SetTranslationVisibility(false,false);
            (appearance with{Animations=false,TwoLines=true,Width=820}).Apply(settings);overlay.ApplyStyle();overlay.Show();
            Load(track,original);coordinator.SetOffset(275);await Settle();
            var document=coordinator.Document!;var binding=coordinator.Binding!;
            var timings=document.Groups.Select(g=>g.TimeMs).ToArray();string hash=TranslationAlignment.Hash(document.Raw);
            var retainedBrowsing=DateTime.UtcNow.AddMinutes(1);manualUntil=retainedBrowsing;

            await Choose("simplified");
            checks["chineseScript.simplifiedOriginals"]=lyricBlocks[0].Text==firstSimplified && lyricBlocks[1].Text==nextSimplified;
            checks["chineseScript.simplifiedOverlayCurrentAndNext"]=vm.Current==firstSimplified && vm.Next==nextSimplified && overlay.LyricSurface.CurrentLine.Text==firstSimplified && overlay.LyricSurface.NextLine.Text==nextSimplified;
            checks["chineseScript.scriptIndependentOfTranslationVisibility"]=!settings.ShowChineseTranslation && !settings.OverlayShowChineseTranslation && vm.CurrentTranslation.Length==0;
            checks["chineseScript.selectionSaved"]=store.Load().ChineseScript=="simplified";
            await Choose("traditional");
            checks["chineseScript.traditionalOriginalsAndOverlay"]=lyricBlocks[0].Text==firstTraditional && lyricBlocks[1].Text==nextTraditional && vm.Current==firstTraditional && vm.Next==nextTraditional;
            checks["chineseScript.conversionPreservesSourceBindingAndTimeline"]=ReferenceEquals(document,coordinator.Document) && coordinator.Document.Raw==original && TranslationAlignment.Hash(coordinator.Document.Raw)==hash && coordinator.Document.Groups.Select(g=>g.TimeMs).SequenceEqual(timings) && coordinator.Document.FileOffsetMs==-125 && coordinator.Binding==binding && binding.Manual && binding.OffsetMs==275;
            checks["chineseScript.switchPreservesBrowsing"]=manualUntil==retainedBrowsing;
            using(var repository=new LyricsRepository(Path.Combine(store.Root,"lyrics.db")))
                checks["chineseScript.persistedOriginalUnchanged"]=repository.Find(track)==binding;
            await Choose("original");
            checks["chineseScript.originalRestoresMixedScriptsExactly"]=lyricBlocks[0].Text==firstTraditional && lyricBlocks[1].Text==nextSimplified && vm.Current==firstTraditional && vm.Next==nextSimplified;
            coordinator.SetTranslationVerificationSnapshot(track,25000);await Choose("traditional");
            checks["chineseScript.blankIntervalPreserved"]=currentIndex==2 && vm.Current.Length==0 && lyricBlocks[2].Text==" " && vm.Next=="一起望見雲端的星光 🌙";
            coordinator.SetTranslationVerificationSnapshot(track,1000);Tick();
            checks["chineseScript.emojiAndWhitespacePreserved"]=ChineseScriptConverter.Convert("\t風與夢 🌙\r\n\n", "simplified")=="\t风与梦 🌙\r\n\n" && ChineseScriptConverter.Convert("", "traditional")=="";
            checks["chineseScript.nonBmpAndEmbeddedNullPreserved"]=ChineseScriptConverter.Convert("風\0𠀀🌙夢", "simplified")=="风\0𠀀🌙梦";
            checks["chineseScript.phraseAwareTraditional"]=ChineseScriptConverter.Convert("头发 发展 后来 皇后", "traditional")=="頭髮 發展 後來 皇后";
            checks["chineseScript.mixedChineseAndMiddleDotConvert"]=ChineseScriptDisplay.ConvertOriginals(["音乐・梦想","Love 与远方"],"traditional").SequenceEqual(["音樂・夢想","Love 與遠方"]);
            checks["chineseScript.unknownModePreservesText"]=ChineseScriptConverter.Convert("風与梦", "future-mode")=="風与梦";

            foreach(var app in AppThemes.All)
            {
                SelectUiTheme(app.Id,false);
                foreach(double targetWidth in new[]{1220d,980d,820d})
                {
                    Width=targetWidth;await Settle();Activate();
                    var bounds=choice.TransformToAncestor(LyricContent).TransformBounds(new Rect(choice.RenderSize));
                    string key=$"chineseScript.selector.{app.Id}.{targetWidth:0}";
                    checks[key+".fitsLyricHeader"]=choice.IsVisible && choice.ActualWidth>=120 && bounds.Left>=0 && bounds.Top>=0 && bounds.Right<=LyricContent.ActualWidth+.5 && bounds.Bottom<=LyricContent.ActualHeight;
                    checks[key+".keyboardFocus"]=choice.Focus() && choice.IsKeyboardFocusWithin;
                    choice.IsDropDownOpen=true;await Settle();
                    var popup=choice.Template.FindName("PART_Popup",choice) as Popup;
                    checks[key+".choicesVisible"]=popup?.IsOpen==true && choice.Items.Cast<ComboBoxItem>().All(item=>item.IsVisible && item.ActualHeight>0) && choice.Items.Count==3;
                    choice.IsDropDownOpen=false;
                }
            }
            Width=1220;SelectUiTheme(theme,false);await Settle();
            VisualVerification.Save((FrameworkElement)Content,Path.Combine(directory,"chinese-script-main-traditional.png"),true);
            VisualVerification.Save(overlay.LyricSurface,Path.Combine(directory,"chinese-script-overlay-traditional.png"));

            SettingsClicked(this,new());settingsWindow!.SelectTranslationTab();await Settle();
            var settingsChoice=Descendants<ComboBox>(settingsWindow.ChineseTranslations).Single(c=>AutomationProperties.GetName(c)=="歌词简繁显示");
            checks["chineseScript.settingsReflectsMainChoice"]=settingsChoice.SelectedValue?.ToString()=="traditional" && settingsChoice.IsVisible && embeddedPageHost.IsVisible;
            settingsChoice.SelectedValue="simplified";await Settle();
            checks["chineseScript.settingsUpdatesOverlayAndSaves"]=vm.Current==firstSimplified && store.Load().ChineseScript=="simplified";
            VisualVerification.Save((FrameworkElement)Content,Path.Combine(directory,"chinese-script-settings.png"),true);
            ShowLyricsPage();await Settle();
            checks["chineseScript.mainReflectsSettingsOnReturn"]=choice.SelectedValue?.ToString()=="simplified" && lyricBlocks[0].Text==firstSimplified;

            var translatedTrack=new Track("河边的灯","浮光工作室","原创双语简繁样例",48000);
            Load(translatedTrack,english);coordinator.ImportChineseTranslation(chinese);coordinator.SetTranslationVisibility(true,true);await Choose("simplified");
            var rawTranslation=coordinator.Translation!;
            checks["chineseScript.chineseTranslationSimplified"]=translationBlocks[0].Text=="风中的音乐照亮远方" && vm.CurrentTranslation=="风中的音乐照亮远方" && vm.NextTranslation==nextSimplified;
            await Choose("traditional");
            checks["chineseScript.chineseTranslationTraditional"]=translationBlocks[1].Text==nextTraditional && vm.CurrentTranslation=="風中的音樂照亮遠方" && vm.NextTranslation==nextTraditional;
            checks["chineseScript.foreignOriginalAndTranslationSourceUnchanged"]=lyricBlocks[0].Text=="We keep a small light by the water" && vm.Next=="The wind carries our song across the sky" && ReferenceEquals(rawTranslation,coordinator.Translation) && coordinator.Translation.TextAt(1)==nextSimplified && coordinator.Translation.SourceHash==TranslationAlignment.Hash(english);
            coordinator.SetTranslationVisibility(false,true);await Choose("simplified");
            checks["chineseScript.hiddenMainStillConvertsFloatingTranslation"]=translationBlocks.All(b=>b.Visibility==Visibility.Collapsed) && vm.CurrentTranslation=="风中的音乐照亮远方";
            coordinator.SetTranslationVisibility(true,false);await Choose("traditional");
            checks["chineseScript.hiddenFloatingStillConvertsMainTranslation"]=translationBlocks[1].IsVisible && translationBlocks[1].Text==nextTraditional && vm.CurrentTranslation.Length==0 && vm.NextTranslation.Length==0;
            coordinator.SetTranslationVisibility(true,true);await Choose("original");
            checks["chineseScript.originalRestoresTranslationExactly"]=translationBlocks[0].Text=="風中的音樂照亮遠方" && translationBlocks[1].Text==nextSimplified && vm.CurrentTranslation=="風中的音樂照亮遠方" && vm.NextTranslation==nextSimplified;
            coordinator.SetTranslationVerificationSnapshot(translatedTrack,25000);await Choose("traditional");
            checks["chineseScript.translatedBlankIntervalPreserved"]=vm.Current.Length==0 && vm.CurrentTranslation.Length==0 && translationBlocks[2].Visibility==Visibility.Collapsed;

            foreach(var (language,foreign) in new[]{("japanese","[00:00]風の歌を聞く\n[00:12]音楽と夢\n[00:24]遠い空の光"),("korean","[00:00]바람의 노래\n[00:12]音樂與夢\n[00:24]먼 하늘의 빛")})
            {
                var foreignTrack=new Track(language+"原创语言样例","浮光工作室","语言保留测试",36000);
                Load(foreignTrack,foreign);coordinator.ImportChineseTranslation("[00:00]風中的音樂照亮遠方\n[00:12]让微光陪我们走过长夜\n[00:24]星光陪我们回家");
                var sourceLines=coordinator.Document!.Groups.Select(g=>g.Text).ToArray();
                foreach(string mode in new[]{"simplified","traditional"})
                {
                    await Choose(mode);
                    checks[$"chineseScript.{language}.{mode}.originalUntouched"]=lyricBlocks.Select(b=>b.Text).SequenceEqual(sourceLines) && vm.Current==sourceLines[0] && vm.Next==sourceLines[1];
                    checks[$"chineseScript.{language}.{mode}.translationConverted"]=vm.CurrentTranslation==(mode=="simplified"?"风中的音乐照亮远方":"風中的音樂照亮遠方") && vm.NextTranslation==(mode=="simplified"?nextSimplified:nextTraditional);
                }
            }

            const string plain="風中的音樂\r\n\r\nA quiet night 🌙\n";
            Load(track,plain,true);await Choose("simplified");
            checks["chineseScript.staticLyricsConvertAndKeepBlankLines"]=lyricBlocks.Count==4 && lyricBlocks[0].Text=="风中的音乐\r" && lyricBlocks[1].Text==" " && lyricBlocks[2].Text=="A quiet night 🌙" && lyricBlocks[3].Text==" " && coordinator.Document?.Raw==plain && coordinator.Binding?.Plain==true;
            coordinator.SelectCandidate(track,new(900021,track,null,null,true));await Choose("traditional");
            checks["chineseScript.instrumentalStaysEmpty"]=lyricBlocks.Count==0 && vm.Current.Length==0 && vm.Next.Length==0 && vm.CurrentTranslation.Length==0 && vm.NextTranslation.Length==0 && overlay.InstrumentalSuppressed;
            Load(track,original);await Choose("simplified");
            checks["chineseScript.vocalLyricsRestoreAfterInstrumental"]=!overlay.InstrumentalSuppressed && vm.Current==firstSimplified && lyricBlocks.Count==5;
        }
        finally
        {
            choice.IsDropDownOpen=false;ShowLyricsPage();coordinator.SetTranslationVerificationSnapshot(null,0);
            settings.GlobalOffsetMs=globalOffset;coordinator.SetChineseScript(previousScript);coordinator.SetTranslationVisibility(main,floating);
            SelectUiTheme(theme,false);appearance.Apply(settings);overlay.ApplyStyle();if(!visible)overlay.Hide();
            SetPlaybackQueueVisible(queue);WindowState=WindowState.Normal;Width=width;Height=height;WindowState=state;
            if(previousTrack is not null && previousBinding is not null){coordinator.Import(previousTrack,previousBinding.Raw,previousBinding.Source,previousBinding.Plain);coordinator.SetOffset(previousBinding.OffsetMs);}
            if(previousSnapshot is not null)coordinator.SetTranslationVerificationSnapshot(previousSnapshot.Track,previousSnapshot.PositionMs??0);
            manualUntil=browsing;
        }
    }
}
