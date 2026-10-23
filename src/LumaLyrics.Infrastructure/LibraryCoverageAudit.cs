using System.Text.Json;
using LumaLyrics.Core;
namespace LumaLyrics.Infrastructure;

public sealed record LibraryCoverageTrack(string PersistentId,Track Track,string OriginalState,string? Source,string? SourceHash,int OriginalGroups,string TranslationState,int RequiredGroups,int TranslatedGroups,string? TranslationSource,string? Review);
public sealed record LibraryCoverageReport(DateTimeOffset CreatedUtc,string NativeLibraryHash,int DeclaredCount,int Total,int Timed,int Instrumental,int MissingOrUnconfirmed,int ForeignTimed,int CompleteTranslations,int AiDrafts,int MissingOrPartialTranslations,IReadOnlyList<LibraryCoverageTrack> Tracks);
public static class LibraryCoverageAudit
{
    public static LibraryCoverageReport Create(string root,LibrarySnapshot library,CancellationToken token=default)
    {
        using var database=PersonalLibrarySnapshot.Open(Path.Combine(root,"lyrics.db"));using var command=database.CreateCommand();
        command.CommandText="SELECT t.Title,t.Artist,t.Album,t.Duration,b.Id,b.TrackId,d.Raw,d.Source,b.Manual,COALESCE(o.Offset,0),d.Instrumental,d.Plain,d.ProviderJson FROM Tracks t JOIN TrackLyricsBindings b ON b.TrackId=t.Id JOIN LyricsDocuments d ON d.Id=b.DocumentId LEFT JOIN SyncOffsets o ON o.BindingId=b.Id ORDER BY b.Manual DESC";
        var bindings=new List<(Track Track,LyricsBinding Binding)>();using(var reader=command.ExecuteReader())while(reader.Read())bindings.Add((new(reader.GetString(0),reader.GetString(1),reader.GetString(2),reader.IsDBNull(3)?null:reader.GetDouble(3)),new(reader.GetString(4),reader.GetString(5),reader.GetString(6),reader.GetString(7),reader.GetBoolean(8),reader.GetDouble(9),reader.GetBoolean(10),reader.GetBoolean(11),reader.IsDBNull(12)?null:reader.GetString(12))));
        var byTrack=bindings.GroupBy(x=>x.Track.Fingerprint,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.ToArray(),StringComparer.Ordinal);var parser=new LrcParser();var rows=new List<LibraryCoverageTrack>();
        using var translations=File.Exists(Path.Combine(root,"translations.db"))?new TranslationRepository(root,true):null;
        foreach(var item in library.Tracks)
        {
            token.ThrowIfCancellationRequested();var bound=byTrack.GetValueOrDefault(item.Track.Fingerprint)?.FirstOrDefault(x=>item.Track.DurationMs is null || x.Track.DurationMs is null || Math.Abs(item.Track.DurationMs.Value-x.Track.DurationMs.Value)<=5000).Binding;
            bool valid=bound is not null && (bound.Manual || LibraryPrefetcher.QualityOf(bound,item.Track));var document=valid?parser.Parse(bound!.Raw,bound.Plain) with{Instrumental=bound.Instrumental}:null;
            string state=document?.Instrumental==true?"Instrumental":document?.IsSynced==true?bound!.Manual?"ManualTimed":"CheckedTimed":bound is null?"Missing":"Unconfirmed";
            var required=document is {IsSynced:true,Instrumental:false}?document.Groups.Select((g,i)=>(g,i)).Where(x=>TranslationAlignment.NeedsChineseTranslation(document,x.g.Text)).Select(x=>x.i).ToArray():[];
            var translation=document is null?null:translations?.Find(item.Track,document);
            int translated=required.Count(index=>!string.IsNullOrWhiteSpace(translation?.TextAt(index)));
            string translationState=document is null || !document.IsSynced && !document.Instrumental?"MissingOriginal":required.Length==0?"NotRequired":translation is null?"Missing":(translated==required.Length?"Complete":"Partial")+(translation.Draft?"Draft":"Translation");
            rows.Add(new(item.PersistentId,item.Track,state,bound?.Source,document is null?null:TranslationAlignment.Hash(document.Raw),document?.Groups.Count??0,translationState,required.Length,translated,translation?.Source,translation?.Review));
        }
        int foreign=rows.Count(x=>x.RequiredGroups>0);
        return new(DateTimeOffset.UtcNow,library.SourceHash,library.DeclaredCount,rows.Count,rows.Count(x=>x.OriginalState is "ManualTimed" or "CheckedTimed"),rows.Count(x=>x.OriginalState=="Instrumental"),rows.Count(x=>x.OriginalState is "Missing" or "Unconfirmed"),foreign,rows.Count(x=>x.TranslationState is "CompleteDraft" or "CompleteTranslation"),rows.Count(x=>x.TranslationState.EndsWith("Draft",StringComparison.Ordinal)),rows.Count(x=>x.RequiredGroups>0 && x.TranslatedGroups<x.RequiredGroups),rows);
    }
    public static void Write(LibraryCoverageReport report,string destination)
    {
        PersonalLibraryArchive.EnsureOutsideSoftware(destination);string folder=Path.GetDirectoryName(Path.GetFullPath(destination))!;Directory.CreateDirectory(folder);DataDirectoryConfiguration.ProtectFromGit(folder);
        string temp=destination+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temp,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));File.Move(temp,destination,true);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
