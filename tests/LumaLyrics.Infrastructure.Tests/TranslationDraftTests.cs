using System.Text.Json;
using LumaLyrics.Core;
using Microsoft.Data.Sqlite;
namespace LumaLyrics.Infrastructure.Tests;
public sealed class TranslationDraftTests : IDisposable
{
    readonly string root=Path.Combine(Path.GetTempPath(),"Luma-draft-tests-"+Guid.NewGuid().ToString("N"));
    static readonly Track Track=new("Own song","Own artist","Own album",10000);
    const string Original="[00:01]We sail home\n[00:04]Across the quiet bay\n[00:08]The stars will guide us";
    public TranslationDraftTests(){Directory.CreateDirectory(root);using var repo=new LyricsRepository(Path.Combine(root,"lyrics.db"));repo.SyncLibrary(new("own-library",1,[new("1",Track)],"own"));repo.Save(Track,Original,"Own source",true);}
    TranslationDraftSource Completed()
    {
        var source=Assert.Single(TranslationDraftExchange.Sources(root));return source with{Lines=source.Lines.Select((l,i)=>l with{Chinese=new[]{"我们驶向家乡","穿过宁静海湾","星光为我们引路"}[i]}).ToArray(),DraftEngine="Own AI fixture"};
    }
    string Write(TranslationDraftSource source){string path=Path.Combine(root,"own-draft.json");File.WriteAllText(path,JsonSerializer.Serialize(new[]{source}));return path;}
    [Fact] public void ImportsCompleteDraftWithVisibleReviewStatusAndOriginalHash()
    {
        Assert.Equal(1,TranslationDraftExchange.Import(root,Write(Completed())));using var repo=new TranslationRepository(root,true);var t=repo.Find(Track,new LrcParser().Parse(Original))!;Assert.True(t.Draft);Assert.Contains("待校对",t.Review);Assert.Contains("Own AI fixture",t.Source);Assert.Empty(TranslationDraftExchange.Sources(root));
    }
    [Fact] public void ChangedOriginalOrIncompleteMappingCannotBeImported()
    {
        var complete=Completed();Assert.Throws<InvalidDataException>(()=>TranslationDraftExchange.Import(root,Write(complete with{Lines=complete.Lines.Take(2).ToArray()})));
        Assert.Throws<InvalidDataException>(()=>TranslationDraftExchange.Import(root,Write(complete with{Lines=complete.Lines.Select(l=>l with{Original=l.Original+" changed"}).ToArray()})));
        using(var repo=new LyricsRepository(Path.Combine(root,"lyrics.db")))repo.Save(Track,Original+"\n[00:09]More original text","Changed source",true);
        Assert.Throws<InvalidDataException>(()=>TranslationDraftExchange.Import(root,Write(complete)));
    }
    [Fact] public void DraftCannotReplaceAnExistingPersonalTranslation()
    {
        var complete=Completed();using(var repo=new TranslationRepository(root))repo.Save(Track,new(TranslationAlignment.Hash(Original),[new(0,"个人修订的第一行")],"Own manual translation","manual","Own review",DateTimeOffset.UtcNow),true);
        Assert.Equal(0,TranslationDraftExchange.Import(root,Write(complete)));using var current=new TranslationRepository(root,true);Assert.Equal("个人修订的第一行",current.Find(Track,new LrcParser().Parse(Original))!.TextAt(0));
    }
    [Fact] public void DraftExportDoesNotOverwriteAnExistingFile()
    {
        string path=Path.Combine(root,"own-source.json");Assert.Equal(1,TranslationDraftExchange.Export(root,path));Assert.Throws<IOException>(()=>TranslationDraftExchange.Export(root,path));
    }
    [Fact] public void ExplicitlyUntranslatedDraftRowsStayBlankAndCoverageIsVisible()
    {
        var complete=Completed();var partial=complete with{Lines=complete.Lines.Select((l,i)=>i==1?l with{Chinese="",UntranslatedReason="Unconfirmed original language"}:l).ToArray()};
        Assert.Equal(1,TranslationDraftExchange.Import(root,Write(partial)));using var repo=new TranslationRepository(root,true);var t=repo.Find(Track,new LrcParser().Parse(Original))!;Assert.Equal("",t.TextAt(1));Assert.Contains("2/3",t.Review);Assert.Contains("未译行留空",t.Review);
    }
    public void Dispose(){SqliteConnection.ClearAllPools();if(Directory.Exists(root))Directory.Delete(root,true);}
}
