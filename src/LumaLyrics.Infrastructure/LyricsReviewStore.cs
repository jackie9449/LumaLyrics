using System.Security.Cryptography;
using System.Text.Json;
using LumaLyrics.Core;
namespace LumaLyrics.Infrastructure;

public sealed record LyricsReviewRecord(string Format,int Version,string Id,string Method,LyricsOcrObservation Observation,LyricsOcrReport Comparison,string OriginalRaw,string? ImageSha256);
public sealed class LyricsReviewStore
{
    static readonly JsonSerializerOptions Json=new(){WriteIndented=true};
    public string DirectoryPath {get;}
    public LyricsReviewStore(string root){PersonalLibraryArchive.EnsureOutsideSoftware(root);DirectoryPath=Path.Combine(root,"lyrics-review");}
    public string Save(Track expected,LyricsDocument original,LyricsOcrObservation observation,string method,byte[]? png=null)
    {
        var report=LyricsOcrComparison.Compare(expected,original,observation);
        if(!report.Accepted || method is not ("windows-ocr" or "native-text") || observation.Lines.Count is <1 or >500 || observation.Lines.Any(line=>line is null || line.Length>4000) || observation.Bounds.Width is <1 or >10000 || observation.Bounds.Height is <1 or >10000)throw new InvalidDataException("校对证据无效或对应的录音与原词已经变化。");
        if(method=="windows-ocr" && png is null)throw new InvalidDataException("屏幕 OCR 需要保留对应图像。");
        if(png is not null){if(png.Length>20*1024*1024 || ImageDimensions.Read(png)!=(observation.Bounds.Width,observation.Bounds.Height))throw new InvalidDataException("校对图像尺寸与记录不一致。");}
        Directory.CreateDirectory(DirectoryPath);DataDirectoryConfiguration.ProtectFromGit(DirectoryPath);
        string id=observation.CapturedUtc.ToString("yyyyMMdd-HHmmssfff")+"-"+Guid.NewGuid().ToString("N");string target=Path.Combine(DirectoryPath,id);Directory.CreateDirectory(target);
        var record=new LyricsReviewRecord("LumaLyrics.LyricsReview",1,id,method,observation,report,original.Raw,png is null?null:Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant());
        if(png is not null)File.WriteAllBytes(Path.Combine(target,"capture.png"),png);
        File.WriteAllText(Path.Combine(target,"review.json.tmp"),JsonSerializer.Serialize(record,Json));File.Move(Path.Combine(target,"review.json.tmp"),Path.Combine(target,"review.json"));return Path.Combine(target,"review.json");
    }
    internal static LyricsReviewRecord Validate(string path)
    {
        if(new FileInfo(path).Length>8*1024*1024)throw new InvalidDataException("校对记录过大。");
        var record=JsonSerializer.Deserialize<LyricsReviewRecord>(File.ReadAllText(path),Json)??throw new InvalidDataException("校对记录为空。");
        if(record.Format!="LumaLyrics.LyricsReview" || record.Version!=1 || record.Observation is null || record.Observation.Track is null || record.Observation.Bounds is not {Width:>0 and <=10000,Height:>0 and <=10000} || record.Observation.Lines is null || record.Observation.Lines.Count is <1 or >500 || record.Observation.Lines.Any(line=>line is null || line.Length>4000) || record.OriginalRaw is null || record.Method is not ("windows-ocr" or "native-text") || record.Comparison is null)throw new InvalidDataException("校对记录无效。");
        if(!LyricsOcrComparison.Compare(record.Observation.Track,new LrcParser().Parse(record.OriginalRaw),record.Observation).Accepted)throw new InvalidDataException("校对记录与原词哈希不符。");
        if(record.Method=="windows-ocr" && record.ImageSha256 is null)throw new InvalidDataException("校对图像缺失。");
        if(record.ImageSha256 is not null)
        {
            string image=Path.Combine(Path.GetDirectoryName(path)!,"capture.png");
            if(!PersonalLibraryArchive.IsHash(record.ImageSha256) || !File.Exists(image) || new FileInfo(image).Length>20*1024*1024)throw new InvalidDataException("校对图像校验不一致。");
            byte[] png=File.ReadAllBytes(image);
            if(Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant()!=record.ImageSha256 || ImageDimensions.Read(png)!=(record.Observation.Bounds.Width,record.Observation.Bounds.Height))throw new InvalidDataException("校对图像校验不一致。");
        }
        return record;
    }
}
