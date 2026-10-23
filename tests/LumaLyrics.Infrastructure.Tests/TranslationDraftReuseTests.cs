using LumaLyrics.Core;
using Microsoft.Data.Sqlite;
namespace LumaLyrics.Infrastructure.Tests;

public sealed class TranslationDraftReuseTests:IDisposable
{
    readonly string root=Path.Combine(Path.GetTempPath(),"Luma-own-draft-reuse-"+Guid.NewGuid().ToString("N"));
    [Fact] public void OnlyWholeOriginalSequenceCanReuseADraftAndEachVersionKeepsItsOwnHashAndGroups()
    {
        Directory.CreateDirectory(root);using var lyrics=new LyricsRepository(Path.Combine(root,"lyrics.db"));using var translations=new TranslationRepository(root);
        var a=new Track("Own song","Own artist","Original album",10000);var b=a with{Album="Another own album"};var other=a with{Title="Different own song"};
        var original=new LrcParser().Parse("[00:01]We sail home\n[00:04]Across the bay\n[00:08]Under the stars");
        var differentTimeline=new LrcParser().Parse("[00:00]\n[00:02]We sail home\n[00:05]Across the bay\n[00:09]Under the stars");
        lyrics.SyncLibrary(new("own",3,[new("1",a),new("2",b),new("3",other)],"own"));lyrics.Save(a,original.Raw,"own",true);lyrics.Save(b,differentTimeline.Raw,"own",true);lyrics.Save(other,original.Raw.Replace("Across the bay","Across another ocean"),"own",true);
        translations.Save(a,new(TranslationAlignment.Hash(original.Raw),[new(0,"我们驶向家乡"),new(1,"穿过海湾"),new(2,"在星光之下")],"Own AI draft","local:ai","AI draft; semantic review pending",DateTimeOffset.UtcNow,true));
        Assert.Equal(1,TranslationDraftReuse.Run(root).Saved);var reused=translations.Find(b,differentTimeline)!;Assert.True(reused.Draft);Assert.Equal(new[]{1,2,3},reused.Lines.Select(line=>line.GroupIndex));Assert.Equal(TranslationAlignment.Hash(differentTimeline.Raw),reused.SourceHash);Assert.Null(translations.Find(other,new LrcParser().Parse(lyrics.Find(other)!.Raw)));
        Assert.Equal(0,TranslationDraftReuse.Run(root).Saved);Assert.Equal(differentTimeline.Raw,lyrics.Find(b)!.Raw);
    }
    public void Dispose(){SqliteConnection.ClearAllPools();Directory.Delete(root,true);}
}
