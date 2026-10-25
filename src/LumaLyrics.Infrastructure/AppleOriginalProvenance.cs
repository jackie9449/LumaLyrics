using System.Text.Json;
using LumaLyrics.Core;

namespace LumaLyrics.Infrastructure;

public sealed record AppleOriginalRecord(int Schema,string Provider,string Method,Track Track,DateTimeOffset CapturedUtc,
    string OriginalHash,string TtmlHash,string Ttml,string Evidence,bool SemanticReview)
{
    public string TimingModel {get;init;}="ttml-par";
}
public static class AppleOriginalProvenance
{
    static readonly JsonSerializerOptions Json=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase,WriteIndented=true};
    public static string Key(AppleOriginalRecord record)=>TranslationAlignment.Hash(record.Track.Fingerprint+"\0"+record.Track.DurationMs?.ToString(System.Globalization.CultureInfo.InvariantCulture)+"\0"+record.OriginalHash);
    public static string Create(Track track,AppleTimedLyrics lyrics)=>JsonSerializer.Serialize(new AppleOriginalRecord(1,"Apple Music","local-read-only-ttml",track,DateTimeOffset.UtcNow,
        TranslationAlignment.Hash(lyrics.Lrc),TranslationAlignment.Hash(lyrics.Ttml),lyrics.Ttml,
        "Native recording metadata and ordered distinctive original lines, before and after bounded scan",false){TimingModel=lyrics.TimingModel},Json);
    public static AppleOriginalRecord Validate(string raw,string? original=null)
    {
        if(raw.Length>16*1024*1024)throw new InvalidDataException("原词来源记录过大。");
        var record=JsonSerializer.Deserialize<AppleOriginalRecord>(raw,Json)??throw new InvalidDataException("原词来源记录为空。");
        if(record.Schema!=1 || record.Provider!="Apple Music" || record.Method!="local-read-only-ttml" || record.Track?.DurationMs is not >0 || record.Ttml is null
            || record.SemanticReview || record.TtmlHash!=TranslationAlignment.Hash(record.Ttml))throw new InvalidDataException("原词来源记录无效。");
        var lyrics=AppleTimedLyricsParser.Parse(record.Ttml,record.Track.DurationMs.Value);
        if(record.TimingModel!=lyrics.TimingModel)throw new InvalidDataException("原词时间模型记录不一致。");
        if(record.OriginalHash!=TranslationAlignment.Hash(lyrics.Lrc) || original is not null && record.OriginalHash!=TranslationAlignment.Hash(original))throw new InvalidDataException("原词与 TTML 来源哈希不一致。");
        return record;
    }
}
