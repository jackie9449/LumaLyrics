using System.Text.Json;
using LumaLyrics.Core;
using Microsoft.Data.Sqlite;
namespace LumaLyrics.Infrastructure.Tests;

public sealed class TranslationCacheReconciliationTests:IDisposable
{
    readonly string root=Path.Combine(Path.GetTempPath(),"Luma-own-pair-cache-"+Guid.NewGuid().ToString("N"));
    [Fact] public void ReusesOnlyCompletelyMatchingCachedPairsAndNeverReplacesManualData()
    {
        Directory.CreateDirectory(Path.Combine(root,"cache","translations"));var track=new Track("Own song","Own artist","Own album",10000);
        string original="[00:01]We sail home\n[00:04]Across the bay\n[00:08]Under the stars",local="[00:02]We sail home\n[00:02]Across the bay\n[00:09]Under the stars";
        using var lyrics=new LyricsRepository(Path.Combine(root,"lyrics.db"));lyrics.SyncLibrary(new("own",1,[new("1",track)],"own"));var binding=lyrics.Save(track,local,"own manual",true);lyrics.SetOffset(binding.Id,350);
        var candidate=new TranslationCandidate(91,track,original,"[00:01]我们驶向家乡\n[00:04]穿过海湾\n[00:08]在星光之下","https://example.org/own");File.WriteAllText(Path.Combine(root,"cache","translations","91.json"),JsonSerializer.Serialize(candidate));
        var result=TranslationCacheReconciliation.Run(root);Assert.Equal(1,result.Saved);Assert.Equal(local,lyrics.Find(track)!.Raw);Assert.Equal(350,lyrics.Find(track)!.OffsetMs);
        using var translations=new TranslationRepository(root);var document=new LrcParser().Parse(local);var prepared=translations.Find(track,document)!;Assert.Equal(2,prepared.Lines.Count);Assert.False(prepared.Draft);
        var manual=prepared with{Source="Own manual translation",Review="Own explicit confirmation"};translations.Save(track,manual,true);
        Assert.Equal(0,TranslationCacheReconciliation.Run(root).Saved);Assert.Equal(manual.Source,translations.Find(track,document)!.Source);
    }
    public void Dispose(){SqliteConnection.ClearAllPools();if(Directory.Exists(root))Directory.Delete(root,true);}
}
