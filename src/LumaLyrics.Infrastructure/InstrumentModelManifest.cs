using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using LumaLyrics.Core;

namespace LumaLyrics.Infrastructure;

/// <summary>All changes affecting cached scores or activity produce a new profile identity.</summary>
public record InstrumentInferenceProfile
{
    public required string Id { get; init; }
    public required string Sha256 { get; init; }
    public required int SampleRate { get; init; }
    public required int WindowSamples { get; init; }
    public required int HopSamples { get; init; }
    public required string InputName { get; init; }
    public required string OutputName { get; init; }
    public required int OutputClassCount { get; init; }
    public required string PreprocessingVersion { get; init; }
    public Dictionary<int, InstrumentKind> LabelMap { get; init; } = [];
    public Dictionary<InstrumentKind, ActivityParameters> Thresholds { get; init; } = [];
    public Dictionary<InstrumentKind, ActivityParameters> RosterThresholds { get; init; } = [];
    public int RecentSilenceSamples { get; init; }

    [JsonIgnore]
    public string ProfileId
    {
        get
        {
            Validate();
            return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            {
                Id, Sha256 = Sha256.ToLowerInvariant(), SampleRate, WindowSamples, HopSamples, InputName, OutputName,
                OutputClassCount, PreprocessingVersion, RecentSilenceSamples,
                LabelMap = LabelMap.OrderBy(item => item.Key).ToArray(),
                Thresholds = Thresholds.OrderBy(item => item.Key).ToArray(),
                RosterThresholds = RosterThresholds.OrderBy(item => item.Key).ToArray()
            }))).ToLowerInvariant();
        }
    }

    public virtual void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 80 || Id.Any(c => !(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-')))
            throw new ArgumentException("Invalid model identifier.");
        if (!IsHex(Sha256, 64)) throw new ArgumentException("Invalid model content hash.");
        if (SampleRate is < 8000 or > 192000 || WindowSamples < 1 || WindowSamples > SampleRate * 30
            || HopSamples < 1 || HopSamples > WindowSamples || OutputClassCount is < 1 or > 10000
            || RecentSilenceSamples < 0 || RecentSilenceSamples > WindowSamples
            || string.IsNullOrWhiteSpace(InputName) || string.IsNullOrWhiteSpace(OutputName)
            || string.IsNullOrWhiteSpace(PreprocessingVersion) || LabelMap is null || Thresholds is null || RosterThresholds is null || LabelMap.Count == 0)
            throw new ArgumentException("Invalid model inference configuration.");
        foreach (var label in LabelMap)
            if (label.Key < 0 || label.Key >= OutputClassCount || !Enum.IsDefined(label.Value))
                throw new ArgumentException("Invalid model label map.");
        if (!LabelMap.Values.Distinct().Order().SequenceEqual(Thresholds.Keys.Order()))
            throw new ArgumentException("Every mapped instrument requires activity parameters.");
        foreach (var parameters in Thresholds.Values) _ = new InstrumentActivityFilter(parameters);
        if (RosterThresholds.Count > 0 && !RosterThresholds.Keys.Order().SequenceEqual(Thresholds.Keys.Order()))
            throw new ArgumentException("Roster admission must cover every mapped instrument.");
        foreach (var parameters in RosterThresholds.Values) _ = new InstrumentActivityFilter(parameters);
    }

    internal static bool IsHttps(Uri? uri) => uri is { IsAbsoluteUri: true, Scheme: "https", Port: 443 }
        && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Fragment);
    internal static bool IsHex(string? value, int length) => value?.Length == length && value.All(Uri.IsHexDigit);
}

/// <summary>Download provenance is separate from an already verified local inference profile.</summary>
public sealed record InstrumentModelManifest : InstrumentInferenceProfile
{
    public required Uri DownloadUri { get; init; }
    public required long Bytes { get; init; }
    public required string SourceWeightsSha256 { get; init; }
    public required string SourceRevision { get; init; }
    public required Uri LicenseUri { get; init; }

    public override void Validate()
    {
        base.Validate();
        if (!IsHttps(DownloadUri) || !IsHttps(LicenseUri) || Bytes is <= 0 or > InstrumentModelStore.MaximumModelBytes
            || !IsHex(SourceWeightsSha256, 64) || !IsHex(SourceRevision, 40))
            throw new ArgumentException("Incomplete model provenance.");
    }
}
