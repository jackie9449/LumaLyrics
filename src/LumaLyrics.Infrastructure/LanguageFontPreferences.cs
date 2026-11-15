using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace LumaLyrics.Infrastructure;

// Value equality is necessary: appearance snapshots are compared for changes and rollback.
// Immutable maps prevent a hover/draft from mutating a different theme or the saved settings.
public sealed record LanguageFontPreferences
{
    public ImmutableSortedDictionary<string,string> Fonts { get; init; } = ImmutableSortedDictionary<string,string>.Empty;
    public ImmutableList<string> Order { get; init; } = ImmutableList<string>.Empty;
    [JsonIgnore] public IEnumerable<string> Languages => (Order??ImmutableList<string>.Empty).Concat((Fonts??ImmutableSortedDictionary<string,string>.Empty).Keys).Distinct(StringComparer.Ordinal).Where(tag=>Fonts?.ContainsKey(tag)==true);
    public LanguageFontPreferences Set(string language, string family) => this with { Fonts = Fonts.SetItem(language, family), Order = Fonts.ContainsKey(language)?Order:Order.Add(language) };
    public LanguageFontPreferences Remove(string language) => this with { Fonts = Fonts.Remove(language), Order = Order.Remove(language) };
    public LanguageFontPreferences Normalize(string defaultLanguage)
    {
        var result = ImmutableSortedDictionary.CreateBuilder<string,string>(StringComparer.Ordinal);
        if (Fonts is not null)
            foreach (var (tag, family) in Fonts)
                if (LyricFontLanguages.NormalizeTag(tag) is { } language && language != defaultLanguage && ValidFamily(family)) result[language] = family.Trim();
        var order=Languages.Select(LyricFontLanguages.NormalizeTag).OfType<string>().Where(result.ContainsKey).Distinct(StringComparer.Ordinal).ToImmutableList();
        return new() { Fonts = result.ToImmutable(), Order = order };
    }
    public static bool ValidFamily(string? family) => !string.IsNullOrWhiteSpace(family) && family.Length <= 200 && !family.Any(c => char.IsControl(c) || c is '/' or '\\' or ':' or '#' or ',');
    public bool Equals(LanguageFontPreferences? other) => ReferenceEquals(this,other) || other is not null && (Fonts ?? ImmutableSortedDictionary<string,string>.Empty).SequenceEqual(other.Fonts ?? ImmutableSortedDictionary<string,string>.Empty) && Languages.SequenceEqual(other.Languages);
    public override int GetHashCode() { var hash = new HashCode(); if (Fonts is not null) foreach(var pair in Fonts) { hash.Add(pair.Key); hash.Add(pair.Value); } foreach(var tag in Languages)hash.Add(tag);return hash.ToHashCode(); }
}
