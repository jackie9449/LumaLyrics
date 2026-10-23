using LumaLyrics.Core;
using Microsoft.Data.Sqlite;
namespace LumaLyrics.Infrastructure.Tests;

public sealed class LibraryCoverageAuditTests:IDisposable
{
    readonly string root=Path.Combine(Path.GetTempPath(),"Luma-own-coverage-"+Guid.NewGuid().ToString("N"));
    public LibraryCoverageAuditTests()=>Directory.CreateDirectory(root);
    [Fact] public void CountsFreshNativeOccurrencesAndDistinguishesCompleteDraftFromSemanticReview()
    {
        var song=new Track("Own song","Own artist","Own album",10000);var instrumental=song with{Title="Own instrumental"};var missing=song with{Title="Own missing"};
        var document=new LrcParser().Parse("[00:01]We sail home\n[00:04]Across the bay\n[00:08]Under the stars");
        using(var lyrics=new LyricsRepository(Path.Combine(root,"lyrics.db"))){lyrics.Save(song,document.Raw,"own",true);lyrics.Save(instrumental,"","own",true,true);}
        using(var translations=new TranslationRepository(root))translations.Save(song,new(TranslationAlignment.Hash(document.Raw),[new(0,"我们驶向家乡"),new(1,"穿过海湾"),new(2,"在星光之下")],"Own AI draft","local:ai","AI draft; no semantic review",DateTimeOffset.UtcNow,true));
        var snapshot=new LibrarySnapshot("own-current-library",4,[new("1",song),new("2",song),new("3",instrumental),new("4",missing)],"own-hash");var report=LibraryCoverageAudit.Create(root,snapshot);
        Assert.Equal(4,report.Total);Assert.Equal(2,report.Timed);Assert.Equal(1,report.Instrumental);Assert.Equal(1,report.MissingOrUnconfirmed);Assert.Equal(2,report.CompleteTranslations);Assert.Equal(2,report.AiDrafts);Assert.All(report.Tracks.Where(t=>t.RequiredGroups>0),t=>Assert.Equal("CompleteDraft",t.TranslationState));
    }
    public void Dispose(){SqliteConnection.ClearAllPools();Directory.Delete(root,true);}
}
