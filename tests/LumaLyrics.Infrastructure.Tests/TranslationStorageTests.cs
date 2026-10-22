using LumaLyrics.Core;
using Microsoft.Data.Sqlite;
namespace LumaLyrics.Infrastructure.Tests;
public class TranslationStorageTests : IDisposable
{
    readonly string root=Path.Combine(Path.GetTempPath(),"Luma-translation-tests-"+Guid.NewGuid().ToString("N"));
    static readonly Track Track=new("Own song","Own artist","Own album",10000);
    const string Original="[00:01]We sail home\n[00:04]Across the quiet bay\n[00:08]The stars will guide us";
    static LyricsDocument Document=>new LrcParser().Parse(Original);
    static ChineseTranslation Translation=>new(TranslationAlignment.Hash(Original),[new(0,"我们驶向家乡"),new(1,"穿过宁静海湾"),new(2,"星光为我们引路")],"Own test","https://example.org/own","原创测试译文",DateTimeOffset.UtcNow);
    [Fact] public void PersistsHashBoundTranslationAndProtectsManualEdits()
    {
        using(var repo=new TranslationRepository(root)){repo.Save(Track,Translation,true);repo.Save(Track,Translation with{Source="Automatic replacement"});}
        using var reopened=new TranslationRepository(root,true);Assert.Equal("Own test",reopened.Find(Track,Document)!.Source);Assert.True(Assert.Single(reopened.ReadAll()).Manual);
        Assert.Null(reopened.Find(Track,new LrcParser().Parse("[offset:100]\n"+Original)));Assert.Null(reopened.Find(Track with{Artist="Someone else"},Document));Assert.Null(reopened.Find(Track with{DurationMs=30000},Document));
    }
    [Fact] public void RefusesCorruptOrDuplicateMapping()
    {
        using var repo=new TranslationRepository(root);Assert.Throws<InvalidDataException>(()=>repo.Save(Track,Translation with{SourceHash="wrong"}));Assert.Throws<InvalidDataException>(()=>repo.Save(Track,Translation with{Lines=[new(0,"家"),new(0,"乡")]}));Assert.Throws<InvalidDataException>(()=>repo.Save(Track,Translation with{Lines=[new(-1,"家")]}));
    }
    [Fact] public void QueueResumesInvalidatesChangedOriginalAndPreservesOtherStates()
    {
        using var repo=new TranslationRepository(root);repo.Sync([("one",Track,"hash-one",1)]);var item=Assert.Single(repo.Queue());repo.SetState(item,"Prepared","own", "hash-one");Assert.Empty(repo.Queue());
        repo.Sync([("one",Track,"hash-one",1)]);Assert.Equal(1,repo.Progress().Prepared);repo.Sync([("one",Track,"hash-two",1)]);Assert.Single(repo.Queue());
        repo.SetState(item,"Mismatch","own");Assert.Empty(repo.Queue());repo.ResetRetries();Assert.Single(repo.Queue());Assert.Equal(1,repo.Progress().Mismatch);
        repo.Sync([]);Assert.Equal(0,repo.Progress().Total);
    }
    [Fact] public void RejectsFutureSchemaWithoutModifyingIt()
    {
        Directory.CreateDirectory(root);using(var db=new SqliteConnection($"Data Source={Path.Combine(root,"translations.db")}")){db.Open();using var c=db.CreateCommand();c.CommandText="PRAGMA user_version=20";c.ExecuteNonQuery();}
        Assert.Throws<InvalidDataException>(()=>new TranslationRepository(root));
    }
    [Fact] public void IndependentTranslationStoreNeverChangesTheOriginalDatabase()
    {
        Directory.CreateDirectory(root);var path=Path.Combine(root,"lyrics.db");File.WriteAllText(path,"Original independent fixture");using var repo=new TranslationRepository(root);repo.Save(Track,Translation);Assert.Equal("Original independent fixture",File.ReadAllText(path));TranslationRepository.CheckDatabase(Path.Combine(root,"translations.db"));
    }
    public void Dispose(){SqliteConnection.ClearAllPools();if(Directory.Exists(root))Directory.Delete(root,true);}
}
