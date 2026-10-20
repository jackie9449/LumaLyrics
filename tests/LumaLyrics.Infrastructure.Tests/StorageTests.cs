using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.Infrastructure.Tests;
public class StorageTests : IDisposable
{
    readonly string dir=Path.Combine(Path.GetTempPath(),"LumaLyrics-tests-"+Guid.NewGuid().ToString("N"));
    public StorageTests(){Directory.CreateDirectory(dir);}
    [Fact] public void ManualBindingSurvivesNetworkRefreshCacheCleanupAndRestart()
    {
        var t=new Track("Title","Singer","Album",null);var path=Path.Combine(dir,"lyrics.db");string id;
        using(var db=new LyricsRepository(path)){var b=db.Save(t,"[00:01]Own lyrics","本地导入",true);id=b.Id;db.SetOffset(b.Id,350);Assert.Equal(id,db.Save(t with{DurationMs=120000},"network","LRCLIB #1",false).Id);db.ClearNetworkCache();}
        using(var db=new LyricsRepository(path)){var b=db.Find(t with{DurationMs=120000});Assert.NotNull(b);Assert.Equal(id,b.Id);Assert.Equal(350,b.OffsetMs);Assert.True(b.Manual);}
    }
    [Fact] public void NewLyricVersionGetsNewOffsetAndNoResultsCacheIsSeparate()
    {
        var t=new Track("Title","Singer","Album",120000);using var db=new LyricsRepository(Path.Combine(dir,"lyrics.db"));var b=db.Save(t,"A","LRCLIB #1",false);db.SetOffset(b.Id,200);db.RememberFailure(t);Assert.True(db.HasFailure(t));var replacement=db.Save(t,"B","本地导入",true);Assert.Equal(0,replacement.OffsetMs);Assert.False(db.HasFailure(t));
    }
    [Fact] public void DifferentRecordingDurationDoesNotCollide(){var t=new Track("Title","Singer","Album",120000);using var db=new LyricsRepository(Path.Combine(dir,"lyrics.db"));db.Save(t,"A","本地导入",true);Assert.Null(db.Find(t with{DurationMs=160000}));}
    [Fact] public void ExplicitPlainTextSurvivesReloadWithoutMutatingOriginal(){using var db=new LyricsRepository(Path.Combine(dir,"lyrics.db"));var t=new Track("A","B","C",1000);db.Save(t,"[00:01]literal","本地导入",true,plain:true);var b=db.Find(t)!;Assert.True(b.Plain);Assert.Equal("[00:01]literal",b.Raw);}
    [Fact] public void RevalidatingCachedLyricsUsesPersistedFullTextAndOriginalMetadata()
    {
        var track=new Track("Own song","Own artist","Own album",180000);var raw="[00:00]Our first original verse\n[00:50]Our second original verse\n[02:05]Our last original verse";
        var source=System.Text.Json.JsonSerializer.Serialize(new{id=1,trackName=track.Title,artistName=track.Artist,albumName=track.Album,duration=180,instrumental=false,syncedLyrics=raw,plainLyrics="Our first original verse\nOur second original verse\nOur last original verse\nA whole additional original section missing from the timed document, which must never be silently accepted"});
        using var db=new LyricsRepository(Path.Combine(dir,"lyrics.db"));db.Save(track,raw,"LRCLIB #1",false,providerJson:source);var binding=db.Find(track)!;
        Assert.Equal(source,binding.ProviderJson);Assert.False(LibraryPrefetcher.QualityOf(binding,track));Assert.False(LibraryPrefetcher.QualityOf(binding,track with{Artist="Someone else"}));
        var contradictory=System.Text.Json.JsonSerializer.Serialize(new{id=2,trackName=track.Title,artistName=track.Artist,albumName=track.Album,duration=180,instrumental=true,syncedLyrics=raw,plainLyrics=(string?)null});
        Assert.False(LibraryPrefetcher.QualityOf(new("binding","track","","LRCLIB #2",false,0,true,ProviderJson:contradictory),track));
    }
    [Fact] public void CorruptSettingsAreBackedUpAndAtomicSaveReloads(){var store=new SettingsStore(dir);File.WriteAllText(Path.Combine(dir,"settings.json"),"{broken");Assert.True(store.Load().OnlineLookup);Assert.Single(Directory.GetFiles(dir,"*.corrupt-*"));store.Save(new(){OnlineLookup=false,GlobalOffsetMs=500});Assert.False(store.Load().OnlineLookup);Assert.Equal(500,store.Load().GlobalOffsetMs);}
    [Fact] public void MigratesOriginalDatabaseSchemaAndRetainsManualData()
    {
        var path=Path.Combine(dir,"legacy.db");using(var db=new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}")){db.Open();using var c=db.CreateCommand();c.CommandText="CREATE TABLE LyricsDocuments(Id TEXT PRIMARY KEY, Raw TEXT NOT NULL, Source TEXT, Hash TEXT, FetchedUtc TEXT, Instrumental INTEGER, ParserVersion INTEGER DEFAULT 1); PRAGMA user_version=1; INSERT INTO LyricsDocuments VALUES('original','Own lyrics','本地导入','hash','2026-10-18',0,1);";c.ExecuteNonQuery();}
        using(var migrated=new LyricsRepository(path)){migrated.Save(new("Title","Singer","Album",1000),"[00:01]Literal TXT","本地导入",true,plain:true);}
        using(var db=new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}")){db.Open();using var c=db.CreateCommand();c.CommandText="SELECT Raw FROM LyricsDocuments WHERE Id='original'";Assert.Equal("Own lyrics",c.ExecuteScalar());c.CommandText="PRAGMA user_version";Assert.Equal(5L,c.ExecuteScalar());}Assert.True(File.Exists(path+".pre-v5.backup"));
    }
    public void Dispose(){SqliteCleanup();Directory.Delete(dir,true);}
    static void SqliteCleanup()=>Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
}
