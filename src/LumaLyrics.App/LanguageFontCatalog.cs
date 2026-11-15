using System.IO;
using System.Windows.Media;
using LumaLyrics.Infrastructure;

namespace LumaLyrics.App;

internal static class LanguageFontCatalog
{
    static readonly Uri FontDirectory = new(Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts") + Path.DirectorySeparatorChar);
    static readonly HashSet<string> Bundled = LyricFontLanguages.All.Select(l => l.Fallback).ToHashSet(StringComparer.OrdinalIgnoreCase);
    static readonly Lazy<Dictionary<string,FontFamily>> Installed = new(()=>Fonts.SystemFontFamilies.Where(f=>!f.Source.StartsWith('@')).GroupBy(f=>f.Source,StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.First(),StringComparer.OrdinalIgnoreCase));
    static readonly Dictionary<(string Language,string Family),bool> Availability = new();
    static readonly Lazy<IReadOnlyDictionary<string,string[]>> Choices = new(BuildChoices);
    internal static string Target(string family) => Bundled.Contains(family) ? FontDirectory.AbsoluteUri + "#" + family : family;
    internal static FontFamily Family(string family) => new(Target(family));
    internal static IReadOnlyList<string> For(string language) => Choices.Value[LyricFontLanguages.Get(language).Tag];
    internal static bool Available(string language, string family)
    {
        if (!LanguageFontPreferences.ValidFamily(family) || !Bundled.Contains(family) && !Installed.Value.ContainsKey(family)) return false;
        var spec=LyricFontLanguages.Get(language);var key=(spec.Tag,family);
        if(Availability.TryGetValue(key,out bool available))return available;
        if(Choices.IsValueCreated)available=For(spec.Tag).Contains(family,StringComparer.OrdinalIgnoreCase);
        else available=Eligible(family,Glyphs(Family(family)),spec);
        if(Availability.Count>=2048)Availability.Clear();Availability[key]=available;return available;
    }
    internal static string Effective(OverlayAppearance appearance, string language)
    {
        var spec = LyricFontLanguages.Get(language);
        string? requested = spec.Tag == appearance.FontLanguage ? appearance.Font : appearance.LanguageFonts.Fonts.GetValueOrDefault(spec.Tag);
        return requested is not null && Available(spec.Tag, requested) ? requested : spec.Fallback;
    }
    static IReadOnlyList<GlyphTypeface> Glyphs(FontFamily family)
    {
        try { return family.GetTypefaces().Select(t=>t.TryGetGlyphTypeface(out var glyph)?glyph:null).OfType<GlyphTypeface>().ToArray(); }
        catch (Exception e) when (e is ArgumentException or IOException or NotSupportedException or InvalidOperationException) { return []; }
    }
    static bool Covers(IReadOnlyList<GlyphTypeface> glyphs,string alphabet) => glyphs.Any(g=>alphabet.EnumerateRunes().All(r=>g.CharacterToGlyphMap.TryGetValue(r.Value,out ushort id) && id!=0));
    internal static bool Covers(FontFamily family, string alphabet) => Covers(Glyphs(family),alphabet);
    static bool Eligible(string name,IReadOnlyList<GlyphTypeface> glyphs,LyricFontLanguage spec)
    {
        if(spec.Script is "Latn" or "Cyrl" or "Grek")
        {
            bool eastAsian=Covers(glyphs,"中文") || Covers(glyphs,"あいう") || Covers(glyphs,"가나다");
            if(eastAsian || Bundled.Contains(name) && !name.Equals("Noto Sans",StringComparison.OrdinalIgnoreCase))return false;
        }
        // CJK families share glyphs; restrict the bundled collection to its intended regional design.
        if(name.StartsWith("Noto Sans CJK ",StringComparison.OrdinalIgnoreCase) && !name.Equals(spec.Fallback,StringComparison.OrdinalIgnoreCase))return false;
        return Covers(glyphs,spec.Alphabet);
    }
    static IReadOnlyDictionary<string,string[]> BuildChoices()
    {
        var fonts = new Dictionary<string,FontFamily>(Installed.Value,StringComparer.OrdinalIgnoreCase);
        foreach (var family in Bundled) fonts[family] = Family(family);
        var lists = LyricFontLanguages.All.ToDictionary(l => l.Tag, _ => new List<string>());
        foreach (var (name, family) in fonts)
        {
            // Enumerate each physical family once; never count WPF fallback as glyph coverage.
            var glyphs=Glyphs(family);
            foreach (var spec in LyricFontLanguages.All)
                if(Eligible(name,glyphs,spec))lists[spec.Tag].Add(name);
        }
        return lists.ToDictionary(p => p.Key, p => p.Value.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n == LyricFontLanguages.Get(p.Key).Fallback ? 0 : 1).ThenBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToArray());
    }
}
