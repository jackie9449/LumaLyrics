using System.Globalization;
using System.Text;
using System.Windows.Documents;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using LumaLyrics.Infrastructure;
using LumaLyrics.Windows;

namespace LumaLyrics.App;

internal static class LyricFontRenderer
{
    // Only display strings, in bounded process memory; no disk/network cache.
    static readonly Dictionary<(string Font,string Default,LanguageFontPreferences Choices,string Language),FontFamily> Families = new();
    static readonly Dictionary<string,string?> Languages = new(StringComparer.Ordinal);
    internal static string Language(string text, string defaultLanguage)
    {
        if (text.Any(c => c is >= '\u3040' and <= '\u30ff' or >= '\uff66' and <= '\uff9f')) return "ja";
        if (text.Any(c => c is >= '\uac00' and <= '\ud7af' or >= '\u1100' and <= '\u11ff')) return "ko";
        if (!Languages.TryGetValue(text, out string? detected))
        {
            detected = LyricFontLanguages.NormalizeTag(TextLanguageDetector.Detect(text));
            if (Languages.Count >= 512) Languages.Clear();
            Languages[text] = detected;
        }
        if (detected is not null) return detected;
        if (text.Any(c => c is >= '\u4e00' and <= '\u9fff'))
            return ChineseLanguage(text,defaultLanguage);
        return defaultLanguage;
    }
    internal static string ChineseLanguage(string text,string defaultLanguage)
    {
        if(ChineseScriptConverter.Convert(text,"simplified")!=text)return "zh-Hant";
        if(ChineseScriptConverter.Convert(text,"traditional")!=text)return "zh-Hans";
        return defaultLanguage=="zh-Hant"?"zh-Hant":"zh-Hans";
    }
    static string LatinLanguage(OverlayAppearance a,string language)=>LyricFontLanguages.Get(language).Script=="Latn"?language:LyricFontLanguages.Get(a.FontLanguage).Script=="Latn"?a.FontLanguage:"en";
    static string HanLanguage(OverlayAppearance a,string language)=>language is "ja" or "zh-Hans" or "zh-Hant"?language:a.FontLanguage=="zh-Hant"?"zh-Hant":"zh-Hans";
    static string ScriptLanguage(Rune rune,OverlayAppearance a,string language,string previous)
    {
        int c=rune.Value;
        if(c is >=0x3040 and <=0x30ff or >=0x31f0 and <=0x31ff or >=0xff66 and <=0xff9f)return "ja";
        if(c is >=0x1100 and <=0x11ff or >=0x3130 and <=0x318f or >=0xa960 and <=0xa97f or >=0xac00 and <=0xd7ff)return "ko";
        if(c is >=0x2e80 and <=0x303f or >=0x3100 and <=0x312f or >=0x31a0 and <=0x31bf or >=0x3400 and <=0x4dbf or >=0x4e00 and <=0x9fff or >=0xf900 and <=0xfaff or >=0x20000 and <=0x323af)return HanLanguage(a,language);
        if(c is >=0x400 and <=0x52f or >=0x2de0 and <=0x2dff or >=0xa640 and <=0xa69f)return language=="uk"?"uk":"ru";
        if(c is >=0x370 and <=0x3ff or >=0x1f00 and <=0x1fff)return "el";
        if(c is >=0x600 and <=0x6ff or >=0x750 and <=0x77f or >=0x8a0 and <=0x8ff or >=0xfb50 and <=0xfdff or >=0xfe70 and <=0xfeff)return "ar";
        if(c is >=0x590 and <=0x5ff or >=0xfb1d and <=0xfb4f)return "he";
        if(c is >=0x900 and <=0x97f or >=0xa8e0 and <=0xa8ff)return "hi";
        if(c is >=0xe00 and <=0xe7f)return "th";
        if(c is >=0x980 and <=0x9ff)return "bn";
        if(c is >=0xb80 and <=0xbff)return "ta";
        if(Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)return previous;
        return Rune.IsLetter(rune) && c<=0x24f || c is >=0x1e00 and <=0x1eff ? LatinLanguage(a,language) : previous;
    }
    internal static string Apply(TextBlock line, OverlayAppearance appearance, string? languageOverride=null)
    {
        string text=line.Text;
        string language = languageOverride ?? Language(text, appearance.FontLanguage);
        var font = Create(appearance, language);
        line.FontFamily = font;
        line.Language = XmlLanguage.GetLanguage(language);
        line.FlowDirection = LyricFontLanguages.Get(language).Script is "Arab" or "Hebr" ? System.Windows.FlowDirection.RightToLeft : System.Windows.FlowDirection.LeftToRight;
        // Explicit runs are necessary: WPF can otherwise reuse one CJK face across adjacent
        // scripts when that face also contains kana/Hangul, bypassing the regional choice.
        var segments=new List<(string Language,string Text)>();var buffer=new StringBuilder();string previous=language;
        foreach(var rune in text.EnumerateRunes())
        {
            string tag=ScriptLanguage(rune,appearance,language,previous);
            if(tag!=previous && buffer.Length>0){segments.Add((previous,buffer.ToString()));buffer.Clear();}
            previous=tag;buffer.Append(rune.ToString());
        }
        if(buffer.Length>0)segments.Add((previous,buffer.ToString()));
        line.Inlines.Clear();
        foreach(var segment in segments)
        {
            string family=LanguageFontCatalog.Effective(appearance,segment.Language);
            string fallback=LyricFontLanguages.Get(segment.Language).Fallback;
            line.Inlines.Add(new Run(segment.Text){Language=XmlLanguage.GetLanguage(segment.Language),FontFamily=new FontFamily(LanguageFontCatalog.Target(family)+", "+LanguageFontCatalog.Target(fallback)+", Global User Interface")});
        }
        return LanguageFontCatalog.Effective(appearance, language);
    }
    internal static FontFamily Create(OverlayAppearance appearance, string language)
    {
        var key=(appearance.Font,appearance.FontLanguage,appearance.LanguageFonts,language);
        if(Families.TryGetValue(key,out var cached))return cached;
        var font = new FontFamily();
        string latin = LatinLanguage(appearance,language);
        string han = HanLanguage(appearance,language);
        void Map(string ranges, string tag)
        {
            string family = LanguageFontCatalog.Effective(appearance, tag);
            string fallback = LyricFontLanguages.Get(tag).Fallback;
            font.FamilyMaps.Add(new FontFamilyMap { Unicode = ranges, Target = LanguageFontCatalog.Target(family) + ", " + LanguageFontCatalog.Target(fallback) + ", Global User Interface" });
        }
        Map("0000-036F,1AB0-1AFF,1D00-1D7F,1D80-1DBF,1DC0-1DFF,1E00-1EFF,2000-206F,FE20-FE2F", latin);
        Map("2E80-303F,3100-312F,31A0-31BF,3400-4DBF,4E00-9FFF,F900-FAFF,20000-323AF", han);
        Map("3040-30FF,31F0-31FF,FF66-FF9F", "ja");
        Map("1100-11FF,3130-318F,A960-A97F,AC00-D7FF", "ko");
        Map("0400-052F,2DE0-2DFF,A640-A69F", language == "uk" ? "uk" : "ru");
        Map("0370-03FF,1F00-1FFF", "el");
        Map("0600-06FF,0750-077F,08A0-08FF,FB50-FDFF,FE70-FEFF", "ar");
        Map("0590-05FF,FB1D-FB4F", "he");
        Map("0900-097F,A8E0-A8FF", "hi");
        Map("0E00-0E7F", "th");
        Map("0980-09FF", "bn");
        Map("0B80-0BFF", "ta");
        font.FamilyMaps.Add(new FontFamilyMap { Target = "Global User Interface" });
        if(Families.Count>=128)Families.Clear();Families[key]=font;return font;
    }
}
