using System.Security.Cryptography;
using System.Text.Json;
using LumaLyrics.Core;

namespace LumaLyrics.Infrastructure;

/// <summary>Call only with independently confirmed source IDs, never name fingerprints.</summary>
public static class InstrumentRecordingIdentity
{
    public static string KeyId(InstrumentRecordingKey recording)
    {
        Validate(recording);
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(recording with
        { AudioSha256 = recording.AudioSha256?.ToLowerInvariant() }))).ToLowerInvariant();
    }

    internal static void Validate(InstrumentRecordingKey recording)
    {
        ArgumentNullException.ThrowIfNull(recording);
        if (string.IsNullOrWhiteSpace(recording.Source) || recording.Source.Length > 80
            || recording.Source.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or ':' or '.'))
            || string.IsNullOrWhiteSpace(recording.PersistentId) || recording.PersistentId.Length > 4096
            || recording.PersistentId.Any(char.IsControl) || recording.DurationMs <= 0
            || recording.AudioSha256 is { } hash && !InstrumentInferenceProfile.IsHex(hash, 64))
            throw new InvalidDataException("Incomplete or invalid confirmed recording identity.");
    }

    public static InstrumentRecordingKey? Resolve(IReadOnlyList<InstrumentRecordingKey> confirmedCandidates,
        double reportedDurationMs, string? currentAudioSha256, double observedRoundingToleranceMs = 0)
    {
        if (!double.IsFinite(reportedDurationMs) || reportedDurationMs <= 0 || !double.IsFinite(observedRoundingToleranceMs)
            || observedRoundingToleranceMs is < 0 or > 1000
            || currentAudioSha256 is { } hash && !InstrumentInferenceProfile.IsHex(hash, 64))
            throw new ArgumentException("Invalid current recording evidence.");
        foreach (var candidate in confirmedCandidates) Validate(candidate);
        if (confirmedCandidates.Select(key => (key.Source, key.PersistentId)).Distinct().Count() > 1)
            throw new ArgumentException("Candidates must belong to one confirmed persistent recording reference.");
        // A missing current hash cannot select between different known file versions.
        if (currentAudioSha256 is null && confirmedCandidates.Select(key => key.AudioSha256?.ToLowerInvariant()).Distinct().Count() > 1)
            return null;
        var matches = confirmedCandidates.Where(key => Math.Abs(key.DurationMs - reportedDurationMs) <= observedRoundingToleranceMs
            && (currentAudioSha256 is null || string.Equals(key.AudioSha256, currentAudioSha256, StringComparison.OrdinalIgnoreCase)))
            .DistinctBy(KeyId).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
}

internal static class InstrumentStoragePaths
{
    internal static string CheckRoot(string path)
    {
        string root = Path.GetFullPath(path);
        PersonalLibraryArchive.EnsureOutsideSoftware(root);
        for (var directory = new DirectoryInfo(root); directory is not null; directory = directory.Parent)
        {
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Instrument storage cannot contain directory redirections.");
            if (File.Exists(Path.Combine(directory.FullName, "LumaLyrics.slnx")))
                throw new InvalidDataException("Instrument personal data must stay outside the software checkout.");
        }
        return root;
    }
}
