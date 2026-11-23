using System.Text;
using System.Text.RegularExpressions;

namespace LumaLyrics.Core;

public static partial class MatchRules
{
    enum CreditMatch { None, Partial, Equivalent }
    const string OriginalCastLabel = @"Original\s+(?:(?<year>[0-9]{4})\s+)?(?<location>[\p{L}\p{M}.'’ -]+?)\s+Cast(?:\s+Recording)?";

    // Use explicit separators for partial credits and preserve names such as AC/DC.
    [GeneratedRegex(@"\b(?:feat(?:uring)?|ft)\.?\s+|\s+/\s+|[,;&、]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExplicitCreditSeparator();
    [GeneratedRegex(@"\s+and\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CreditConjunction();
    [GeneratedRegex(@"^(?<show>.+)\s+[\(\[]?" + OriginalCastLabel + @"[\)\]]?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OriginalCastCredit();
    [GeneratedRegex(@"^(?:The\s+)?" + OriginalCastLabel + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OriginalCastEnsemble();

    static HashSet<string> CreditKeys(IEnumerable<string> credits)
        => credits.Select(Normalize).Where(name => name.Length > 0).ToHashSet(StringComparer.Ordinal);

    static (CreditMatch Match, bool SameAlbum) CompareCollaborativeCredits(Track local, Track source)
    {
        string localArtist = local.Artist.Normalize(NormalizationForm.FormKC);
        string sourceArtist = source.Artist.Normalize(NormalizationForm.FormKC);
        var localCredits = ExplicitCreditSeparator().Split(localArtist);
        var sourceCredits = ExplicitCreditSeparator().Split(sourceArtist);
        var expected = CreditKeys(localCredits);
        var actual = CreditKeys(sourceCredits);
        if (expected.Count == 0 || actual.Count == 0 || Math.Max(expected.Count, actual.Count) < 2)
            return default;

        bool hasAllPerformers = expected.IsSubsetOf(actual);
        // Treat “and” as a conjunction only when the other credit list verifies all of the same names.
        bool equivalent = (hasAllPerformers && expected.Count == actual.Count)
            || (expected.Count > 1 && expected.SetEquals(CreditKeys(sourceCredits.SelectMany(credit => CreditConjunction().Split(credit)))))
            || (actual.Count > 1 && actual.SetEquals(CreditKeys(localCredits.SelectMany(credit => CreditConjunction().Split(credit)))));
        var match = equivalent ? CreditMatch.Equivalent
            : hasAllPerformers || actual.IsProperSubsetOf(expected) ? CreditMatch.Partial : CreditMatch.None;
        if (match == CreditMatch.None)
            return default;

        bool sameAlbum = CatalogAlbumMatches(local.Album, source.Album)
            || SameOriginalCastRelease(local.Album, source.Album, sourceCredits, hasAllPerformers || equivalent);
        return (match, sameAlbum);
    }

    static bool SameOriginalCastRelease(string localAlbum, string sourceAlbum, string[] sourceCredits, bool hasAllPerformers)
    {
        var expected = OriginalCastCredit().Match(localAlbum.Normalize(NormalizationForm.FormKC).Trim());
        if (!expected.Success)
            return false;

        string show = expected.Groups["show"].Value;
        var released = OriginalCastCredit().Match(sourceAlbum.Normalize(NormalizationForm.FormKC).Trim());
        if (released.Success)
            return CatalogRecordingMatch.SameName(show, released.Groups["show"].Value) && SameCastVersion(expected, released);

        // A source can shorten a musical's album title. Require the named cast and every credited performer in that case.
        if (!hasAllPerformers || !CatalogRecordingMatch.SameName(show, sourceAlbum))
            return false;
        var showCredits = sourceCredits.Select(credit => OriginalCastCredit().Match(credit.Trim()))
            .Where(credit => credit.Success && CatalogRecordingMatch.SameName(show, credit.Groups["show"].Value)).ToArray();
        return showCredits.Length > 0 && showCredits.All(credit => SameCastVersion(expected, credit));
    }

    static bool SameCastVersion(Match expected, Match actual)
        => CatalogRecordingMatch.SameName(expected.Groups["location"].Value, actual.Groups["location"].Value)
            && (!expected.Groups["year"].Success || !actual.Groups["year"].Success
                || expected.Groups["year"].Value == actual.Groups["year"].Value);

    // The caller requires both sources to name the same full release before using the cast as a collective credit.
    static bool MatchesOriginalCastEnsemble(Track local, string sourceArtist)
    {
        if (string.IsNullOrWhiteSpace(local.Artist))
            return false;
        var release = OriginalCastCredit().Match(local.Album.Normalize(NormalizationForm.FormKC).Trim());
        if (!release.Success)
            return false;

        var ensemble = ReadCastEnsemble(sourceArtist.Normalize(NormalizationForm.FormKC).Trim());
        if (!ensemble.Success || !MatchesCastLabel(release, ensemble))
            return false;

        var localCredits = ExplicitCreditSeparator().Split(local.Artist.Normalize(NormalizationForm.FormKC));
        return localCredits.Select(credit => ReadCastEnsemble(credit.Trim()))
            .All(credit => !credit.Success || MatchesCastLabel(release, credit));
    }

    static Match ReadCastEnsemble(string artist)
    {
        var ensemble = OriginalCastEnsemble().Match(artist);
        return ensemble.Success ? ensemble : OriginalCastCredit().Match(artist);
    }

    static bool MatchesCastLabel(Match release, Match credit)
        => SameCastVersion(release, credit) && (!credit.Groups["show"].Success
            || CatalogRecordingMatch.SameName(release.Groups["show"].Value, credit.Groups["show"].Value));
}
