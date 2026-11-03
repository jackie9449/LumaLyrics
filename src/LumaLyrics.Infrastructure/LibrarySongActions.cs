using System.Text.Json;
using LumaLyrics.Core;
using static LumaLyrics.Infrastructure.TranslationJobRepository;
namespace LumaLyrics.Infrastructure;
public static class LibrarySongActions
{
    public static LyricsDocument Check(string root,LibraryPreparationTarget target)
    {
        using var lyrics=new LyricsRepository(Path.Combine(root,"lyrics.db"),true);var matches=lyrics.GetLibraryQueue(true).Where(m=>m.Track.Fingerprint==target.Track.Fingerprint).ToArray();
        if(!matches.Any(m=>m.Source.Equals(target.Key.Source,StringComparison.OrdinalIgnoreCase)&&m.PersistentId==target.Key.PersistentId&&m.Track==target.Track))throw new InvalidDataException("所选记录已变化。");
        if(matches.Length>1)throw new InvalidDataException("多条记录共享译文，未执行定向替换。");
        var b=lyrics.FindReadOnly(target.Track);if(b is null||b.Id!=target.BindingId||b.Plain||b.Instrumental||TranslationAlignment.Hash(b.Raw)!=target.SourceHash)throw new InvalidDataException("所选动态原词已变化。");
        var doc=new LrcParser().Parse(b.Raw);using var tr=new TranslationRepository(root,true);if(tr.FindActive(target.Track,doc)?.Revision!=target.ExpectedTranslationRevision)throw new InvalidDataException("所选译文已变化。");return doc;
    }
    internal static void CheckAtWrite(Microsoft.Data.Sqlite.SqliteConnection db,LibraryPreparationTarget target)
    {
        var t=target.Track;
        string? raw=Scalar(db,"SELECT d.Raw FROM originals.TrackLyricsBindings b JOIN originals.LyricsDocuments d ON d.Id=b.DocumentId JOIN originals.Tracks t ON t.Id=b.TrackId WHERE b.Id=$id AND t.Fingerprint=$f AND d.Plain=0 AND d.Instrumental=0 AND ($d IS NULL OR t.Duration IS NULL OR abs(t.Duration-$d)<=5000)",("$id",target.BindingId),("$f",t.Fingerprint),("$d",t.DurationMs));
        string? member=Scalar(db,"SELECT l.PersistentId FROM originals.LibraryTracks l JOIN originals.LibraryMembership m ON m.Source=l.Source AND m.PersistentId=l.PersistentId WHERE l.Active=1 AND l.Source=$s COLLATE NOCASE AND l.PersistentId=$p AND l.Fingerprint=$f",("$s",target.Key.Source),("$p",target.Key.PersistentId),("$f",t.Fingerprint));
        string? count=Scalar(db,"SELECT CAST(COUNT(*) AS TEXT) FROM originals.LibraryTracks l JOIN originals.LibraryMembership m ON m.Source=l.Source AND m.PersistentId=l.PersistentId WHERE l.Active=1 AND l.Fingerprint=$f",("$f",t.Fingerprint));
        string? revision=Scalar(db,"SELECT Revision FROM Translations WHERE Fingerprint=$f AND SourceHash=$h",("$f",t.Fingerprint),("$h",target.SourceHash));
        if(raw is null||TranslationAlignment.Hash(raw)!=target.SourceHash||member is null||count!="1"||revision!=target.ExpectedTranslationRevision)throw new InvalidDataException("写入前所选成员、原词或译文已变化，旧译文保留。");
    }
    public static void Import(string root,LibraryPreparationTarget target,string raw)
    {
        var doc=Check(root,target);var translation=TranslationAlignment.Align(doc,doc,new LrcParser().Parse(raw),"个人导入的中文译文","manual")??throw new InvalidDataException("中文歌词与所选原词不完整对应。");
        using var db=Open(root);Execute(db,"ATTACH DATABASE $p AS originals",("$p",Path.Combine(root,"lyrics.db")));using var tx=db.BeginTransaction(deferred:false);
        CheckAtWrite(db,target);
        string? current=Scalar(db,"SELECT d.Raw FROM originals.TrackLyricsBindings b JOIN originals.LyricsDocuments d ON d.Id=b.DocumentId WHERE b.Id=$id",("$id",target.BindingId));
        string? revision=Scalar(db,"SELECT Revision FROM Translations WHERE Fingerprint=$f AND SourceHash=$h",("$f",target.Track.Fingerprint),("$h",target.SourceHash));
        if(current!=doc.Raw||revision!=target.ExpectedTranslationRevision)throw new InvalidDataException("导入前资料已更新，旧译文保留。");
        var t=target.Track;Execute(db,"INSERT INTO Translations(Fingerprint,SourceHash,Duration,Title,Artist,Album,Content,Manual,Revision) VALUES($f,$h,$d,$t,$a,$al,$c,1,$r) ON CONFLICT(Fingerprint,SourceHash) DO UPDATE SET Content=excluded.Content,Manual=1,Revision=excluded.Revision",("$f",t.Fingerprint),("$h",target.SourceHash),("$d",t.DurationMs),("$t",t.Title),("$a",t.Artist),("$al",t.Album),("$c",JsonSerializer.Serialize(translation with{Review="个人导入 · 完整逐组对应已检查"})),("$r",Guid.NewGuid().ToString("N")));tx.Commit();
    }
}
