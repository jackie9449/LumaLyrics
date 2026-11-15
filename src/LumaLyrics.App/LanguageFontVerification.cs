using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using LumaLyrics.Infrastructure;
using LumaLyrics.Windows;

namespace LumaLyrics.App;

public partial class MainWindow
{
    async Task VerifyLanguageFontsAsync(string directory,Dictionary<string,object?> checks)
    {
        var original=OverlayAppearance.From(settings);var profiles=settings.OverlayThemeProfiles;
        double oldWidth=Width,oldHeight=Height;var oldState=WindowState;
        async Task Settle(){await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(80);}
        static string[] DrawnFamilies(TextBlock text)
        {
            text.Measure(new Size(750,300));text.Arrange(new Rect(0,0,750,300));text.UpdateLayout();
            var bitmap=new RenderTargetBitmap(750,300,96,96,PixelFormats.Pbgra32);bitmap.Render(text);
            IEnumerable<string> Read(Drawing? drawing)
            {
                if(drawing is GlyphRunDrawing glyph)foreach(string family in glyph.GlyphRun.GlyphTypeface.FamilyNames.Values)yield return family;
                if(drawing is DrawingGroup group)foreach(var child in group.Children)foreach(var family in Read(child))yield return family;
            }
            return Read(VisualTreeHelper.GetDrawing(text)).Distinct().ToArray();
        }
        try
        {
            var watch=Stopwatch.StartNew();
            foreach(var language in LyricFontLanguages.All)
            {
                var choices=LanguageFontCatalog.For(language.Tag);
                checks["coverage."+language.Tag]=LanguageFontCatalog.Covers(LanguageFontCatalog.Family(language.Fallback),language.Alphabet+language.Sample.Replace(" ",""));
                checks["physicalBundledFont."+language.Tag]=LanguageFontCatalog.Family(language.Fallback).GetTypefaces().Any(t=>t.TryGetGlyphTypeface(out var g) && g.FontUri.LocalPath.Contains("Assets",StringComparison.OrdinalIgnoreCase) && g.FamilyNames.Values.Contains(language.Fallback));
                checks["fallbackListed."+language.Tag]=choices.Contains(language.Fallback);
                checks["classification."+language.Tag]=language.Script!="Latn" || choices.All(f=>!LanguageFontCatalog.Covers(LanguageFontCatalog.Family(f),"中文"));
                var sample=new TextBlock{Text=language.Sample,FontSize=25,TextWrapping=TextWrapping.Wrap,MaxWidth=540,Margin=new(10)};
                LyricFontRenderer.Apply(sample,new OverlayAppearance{FontLanguage=language.Tag,Font=language.Fallback}.Normalize());
                sample.Measure(new Size(540,200));sample.Arrange(new Rect(sample.DesiredSize));sample.UpdateLayout();
                checks["layout."+language.Tag]=sample.ActualWidth>20 && sample.ActualHeight>10;
            }
            checks["catalogMilliseconds"]=watch.ElapsedMilliseconds;
            checks["englishExcludesKaiTi"]=!LanguageFontCatalog.For("en").Any(f=>f.Contains("KaiTi",StringComparison.OrdinalIgnoreCase)||f.Contains("楷"));
            foreach(var (tag,text) in new[]{("en","The music stays in my heart and I will always remember you."),("fr","La musique reste dans mon cœur et je me souviens toujours de toi."),("es","La música permanece en mi corazón y siempre te recordaré."),("de","Die Musik bleibt in meinem Herzen und ich werde mich immer an dich erinnern."),("ja","音楽が心に響く あなたの歌を忘れない"),("ko","음악이 마음에 머물러요")})
                checks["detect."+tag]=LyricFontRenderer.Language(text,"en")==tag;
            checks["detect.invalidUtf16Safe"]=TextLanguageDetector.Detect("\ud800") is null;
            checks["detect.emptySafe"]=TextLanguageDetector.Detect("") is null;
            var configured=new OverlayAppearance{FontLanguage="zh-Hans",Font="Noto Sans CJK SC",LanguageFonts=new LanguageFontPreferences().Set("en","Arial").Set("fr","Georgia").Set("ja","Noto Sans CJK JP")}.Normalize();
            var mixed=LyricFontRenderer.Create(configured,"ja");
            checks["mixedUsesJapaneseHan"]=mixed.FamilyMaps[1].Target.Contains("Noto Sans CJK JP");
            checks["mixedUsesIndependentLatin"]=mixed.FamilyMaps[0].Target.StartsWith("Arial,");
            var actualMixed=new TextBlock{Text="Hello 中文 あいう 한글",FontSize=24};LyricFontRenderer.Apply(actualMixed,configured,"en");
            string[] actualFamilies=DrawnFamilies(actualMixed);
            checks["actualMixedFamilies"]=actualFamilies;
            checks["mixedGlyphsUseFourFamilies"]=new[]{"Arial","Noto Sans CJK SC","Noto Sans CJK JP","Noto Sans CJK KR"}.All(actualFamilies.Contains);
            var french=new TextBlock{Text="La musique reste dans mon cœur.",FontSize=24};LyricFontRenderer.Apply(french,configured,"fr");
            checks["frenchUsesOwnGlyphs"]=DrawnFamilies(french).Contains("Georgia");
            checks["translationScriptTraditional"]=LyricFontRenderer.ChineseLanguage("愛與音樂","zh-Hans")=="zh-Hant";
            checks["translationScriptSimplified"]=LyricFontRenderer.ChineseLanguage("爱与音乐","zh-Hant")=="zh-Hans";
            checks["missingFontFallsBack"]=LanguageFontCatalog.Effective(configured with{LanguageFonts=configured.LanguageFonts.Set("fr","Missing Font 123")},"fr")=="Noto Sans";
            checks["cjkCannotOverrideEnglish"]=LanguageFontCatalog.Effective(configured with{LanguageFonts=configured.LanguageFonts.Set("en","KaiTi")},"en")=="Noto Sans";
            original.Apply(settings);
            SettingsClicked(this,new());ShowAppearance(settingsWindow);await Settle();
            var editor=appearanceWindow!;var fontEditor=editor.LanguageFonts;
            fontEditor.OpenLanguages();await Settle();
            checks["addMenuOpens"]=fontEditor.AddButton.ContextMenu is {IsOpen:true};
            checks["addMenuHasUniqueRemainingLanguages"]=fontEditor.AddButton.ContextMenu!.Items.Count==LyricFontLanguages.All.Count-fontEditor.Pickers.Count;
            var englishMenu=fontEditor.AddButton.ContextMenu.Items.OfType<MenuItem>().FirstOrDefault(m=>m.Tag as string=="en");
            if(englishMenu is not null)englishMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            fontEditor.AddButton.ContextMenu.IsOpen=false;await Settle();
            if(fontEditor.Pickers.TryGetValue("en",out var englishPicker) && original.FontLanguage!="en")
            {
                englishPicker.BringIntoView();await Settle();englishPicker.SelectedIndex=0;englishPicker.IsDropDownOpen=true;editor.UpdateLayout();await Settle();
                englishPicker.SelectedItem="Arial";await Settle();
                var container=(ComboBoxItem)englishPicker.ItemContainerGenerator.ContainerFromIndex(0);
                container.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=MouseEnterEvent});await Settle();
                checks["extraHoverPreviewsLanguage"]=editor.PreviewSurface.RenderedFont.StartsWith("Noto Sans,");
                checks["extraHoverDoesNotPersist"]=OverlayAppearance.From(settings).LanguageFonts.Fonts["en"]=="Arial";
                var size=Descendants<Slider>(editor).First();double previous=size.Value;size.Value=previous+size.SmallChange;await Settle();
                checks["hoverRetainsOtherEdits"]=fontEditor.HoverAppearance?.FontSize==OverlayAppearance.From(settings).FontSize;
                englishPicker.IsDropDownOpen=false;await Settle();
                checks["extraHoverEnds"]=fontEditor.HoverAppearance is null && editor.PreviewSurface.RenderedFont.StartsWith("Arial,");
            }
            checks["systemLanguageRow"]=fontEditor.Pickers.ContainsKey(LyricFontLanguages.SystemLanguage);
            foreach(var language in LyricFontLanguages.All)fontEditor.AddLanguage(language.Tag);
            checks["allLanguagesStack"]=fontEditor.Pickers.Count==LyricFontLanguages.All.Count;
            checks["addedRowsStayAtEnd"]=fontEditor.Pickers.Keys.Last()==LyricFontLanguages.All.Last().Tag;
            checks["noDuplicateRows"]=!fontEditor.AddLanguage("en") && fontEditor.Pickers.Count==LyricFontLanguages.All.Count;
            checks["addDisabledOnlyWhenExhausted"]=!fontEditor.AddButton.IsEnabled;
            string extra=LyricFontLanguages.All.First(l=>l.Tag!=original.FontLanguage).Tag;
            fontEditor.RemoveLanguage(extra);checks["removeReenablesAdd"]=fontEditor.AddButton.IsEnabled && !fontEditor.Pickers.ContainsKey(extra);
            fontEditor.AddLanguage(extra);var saved=OverlayAppearance.From(settings);editor.Save();await Settle();
            checks["savedRoundTrip"]=OverlayAppearance.From(store.Load())==saved;
            ShowAppearance(settingsWindow);await Settle();editor=appearanceWindow!;fontEditor=editor.LanguageFonts;
            fontEditor.RemoveLanguage(extra);editor.Close();await Settle();
            checks["cancelRestoresRows"]=OverlayAppearance.From(settings)==saved;
            ShowAppearance(settingsWindow);await Settle();editor=appearanceWindow!;
            WindowState=WindowState.Normal;Width=820;Height=900;await Settle();
            checks["narrowEditorInsideViewport"]=editor.ActualWidth>200 && editor.ActualWidth<ActualWidth;
            VisualVerification.Save((FrameworkElement)Content,Path.Combine(directory,"fonts-narrow.png"),true);
            Width=1220;await Settle();VisualVerification.Save((FrameworkElement)Content,Path.Combine(directory,"fonts-wide.png"),true);
            var specimenPanel=new StackPanel{Background=Brushes.White};
            foreach(var tag in new[]{"zh-Hans","zh-Hant","en","ja","fr","es","de","ko","ru","ar","he","hi","th","bn","ta"})
            {
                var spec=LyricFontLanguages.Get(tag);var sample=new TextBlock{Text=spec.Sample,Foreground=Brushes.Black,FontSize=26,Margin=new(16,8,16,8)};
                LyricFontRenderer.Apply(sample,new OverlayAppearance{FontLanguage=tag,Font=spec.Fallback}.Normalize());specimenPanel.Children.Add(sample);
            }
            specimenPanel.Measure(new Size(800,1600));specimenPanel.Arrange(new Rect(0,0,800,specimenPanel.DesiredSize.Height));specimenPanel.UpdateLayout();
            VisualVerification.Save(specimenPanel,Path.Combine(directory,"font-specimens.png"));
        }
        finally
        {
            appearanceWindow?.Close();settingsWindow?.Close();settings.OverlayThemeProfiles=profiles;original.Apply(settings);overlay.ApplyStyle();coordinator.SaveSettings();
            WindowState=WindowState.Normal;Width=oldWidth;Height=oldHeight;WindowState=oldState;
        }
    }
}
