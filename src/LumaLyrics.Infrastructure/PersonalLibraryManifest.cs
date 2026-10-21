using LumaLyrics.Core;
namespace LumaLyrics.Infrastructure;

public sealed class PersonalLibraryManifest
{
    public const string FileName="personal-library.json";
    public string Format {get;init;}="LumaLyrics.PersonalLibrary";
    public int Version {get;init;}=1;
    public DateTimeOffset CreatedUtc {get;init;}=DateTimeOffset.UtcNow;
    public List<PersonalLibraryFile> Files {get;init;}=[];
    public List<PersonalLibraryImage> Images {get;init;}=[];
    public List<PersonalLibraryTrack> Tracks {get;init;}=[];
    public List<string> Warnings {get;init;}=[];
    public PersonalLibrarySummary Summary=>new(Tracks.Count,Tracks.Count(t=>t.LyricPath is not null),
        Tracks.Count(t=>t.LyricState=="Synced"),Tracks.Count(t=>t.Manual),Tracks.Count(t=>t.LyricState=="Instrumental"),
        Tracks.Count(t=>t.LyricState is "Missing" or "NeedsReview"),Tracks.Count(t=>t.CoverPath is not null),
        Tracks.Count(t=>t.CoverPath is not null && Images.Any(i=>i.Path==t.CoverPath && Math.Min(i.Width,i.Height)>=1000)),
        Images.Select(i=>i.Path).Distinct().Count());
}
public sealed record PersonalLibraryFile(string Path,string Sha256,long Bytes,string Kind);
public sealed record PersonalLibraryImage(string ImageKey,string Path,int Width,int Height);
public sealed record PersonalLibraryTrack(string Id,Track Track,string LyricState,string? LyricPath,string? CoverPath,
    bool Manual,double OffsetMs,string? LyricSource,string? PersistentId);
public sealed record PersonalLibrarySummary(int Tracks,int LyricFiles,int AutomaticSynced,int Manual,int Instrumental,
    int MissingOrNeedsReview,int CoverTracks,int HighResolutionCoverTracks,int UniqueCovers);
