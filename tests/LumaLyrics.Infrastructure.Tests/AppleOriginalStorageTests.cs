using System.Text.Json.Nodes;
using LumaLyrics.Core;
using Microsoft.Data.Sqlite;

namespace LumaLyrics.Infrastructure.Tests;

public sealed class AppleOriginalStorageTests:IDisposable
{
    readonly string root=Path.Combine(Path.GetTempPath(),"Luma-original-tests-"+Guid.NewGuid().ToString("N"));
    static readonly Track Track=new("Own original","Own performer","Own album",140000);
    static AppleTimedLyrics Original=>AppleTimedLyricsParser.Parse("<tt xmlns=\"http://www.w3.org/ns/ttml\"><body><div>"+string.Concat(Enumerable.Range(0,4).Select(i=>$"<p begin=\"{i*30}s\" end=\"{i*30+10}s\">This is an original line number {i} written for a private test</p>"))+"</div></body></tt>",140000);
    public AppleOriginalStorageTests()=>Directory.CreateDirectory(root);
    [Fact] public void NativeSourceSurvivesConsistentReadableBackupAndRestore()
    {
        string source=Path.Combine(root,"source");Directory.CreateDirectory(source);
        var original=Original;string provenance=AppleOriginalProvenance.Create(Track,original);
        using var repo=new LyricsRepository(Path.Combine(source,"lyrics.db"));repo.Save(Track,original.Lrc,"Apple Music · 本机 TTML · 自动检查",false,providerJson:provenance);
        Assert.True(LibraryPrefetcher.QualityOf(repo.Find(Track)!,Track));
        var retained=repo.Save(Track,"[00:01]Different community timing\n[00:40]A different time\n[01:30]A different end","LRCLIB · 自动检查",false);
        Assert.Equal(original.Lrc,retained.Raw);
        string exported=Path.Combine(root,"export");var manifest=PersonalLibraryArchive.Export(source,exported);
        var file=Assert.Single(manifest.Files,f=>f.Kind=="original");Assert.Equal(provenance,File.ReadAllText(Path.Combine(exported,file.Path)));
        PersonalLibraryArchive.CreateZip(exported,Path.Combine(root,"personal.zip"));PersonalLibraryArchive.Restore(Path.Combine(root,"personal.zip"),Path.Combine(root,"restored"));
        using var restored=new LyricsRepository(Path.Combine(root,"restored","lyrics.db"));Assert.Equal(repo.Find(Track),restored.Find(Track));
        Assert.Equal(original.Ttml,AppleOriginalProvenance.Validate(restored.Find(Track)!.ProviderJson!,original.Lrc).Ttml);
    }
    [Fact] public void NativeAutomaticSavePreservesManualOriginalOffsetAndTranslation()
    {
        using var repo=new LyricsRepository(Path.Combine(root,"lyrics.db"));string manual="[00:01]A hand edited original\n[00:30]A hand edited second line\n[01:30]A hand edited final line";
        var binding=repo.Save(Track,manual,"Manual",true);repo.SetOffset(binding.Id,650);
        using var translations=new TranslationRepository(root);var translation=new ChineseTranslation(TranslationAlignment.Hash(manual),[new(0,"自写并手动保留的中文译文")],"Manual","","manual",DateTimeOffset.UtcNow);translations.Save(Track,translation,true);
        repo.Save(Track,Original.Lrc,"Apple Music · 本机 TTML · 自动检查",false,providerJson:AppleOriginalProvenance.Create(Track,Original));
        var saved=repo.Find(Track)!;Assert.True(saved.Manual);Assert.Equal(manual,saved.Raw);Assert.Equal(650,saved.OffsetMs);
        Assert.Equal(translation.SourceHash,translations.Find(Track,new LrcParser().Parse(manual))!.SourceHash);
        Assert.Null(translations.Find(Track,new LrcParser().Parse(Original.Lrc)));Assert.True(Assert.Single(translations.ReadAll()).Manual);
    }
    [Fact] public void ProvenanceRejectsRetimingTamperingAndSemanticPromotion()
    {
        var original=Original;string raw=AppleOriginalProvenance.Create(Track,original);var json=JsonNode.Parse(raw)!;
        Assert.Throws<InvalidDataException>(()=>AppleOriginalProvenance.Validate(raw,original.Lrc+"\n"));
        json["ttml"]=original.Ttml.Replace("0s\"","1s\"");Assert.Throws<InvalidDataException>(()=>AppleOriginalProvenance.Validate(json.ToJsonString()));
        json=JsonNode.Parse(raw)!;json["semanticReview"]=true;Assert.Throws<InvalidDataException>(()=>AppleOriginalProvenance.Validate(json.ToJsonString()));
        Assert.NotEqual(AppleOriginalProvenance.Key(AppleOriginalProvenance.Validate(raw)),AppleOriginalProvenance.Key(AppleOriginalProvenance.Validate(raw) with{Track=Track with{Album="Other recording"}}));
    }
    [Fact] public void OldProfilesEnableReadOnlyAcquisitionAndPreferencePersists()
    {
        File.WriteAllText(Path.Combine(root,"settings.json"),"{\"OnlineLookup\":false}");var store=new SettingsStore(root);var settings=store.Load();Assert.True(settings.AppleMusicOriginalLyrics);Assert.False(settings.OnlineLookup);
        settings.AppleMusicOriginalLyrics=false;store.Save(settings);Assert.False(store.Load().AppleMusicOriginalLyrics);
    }
    public void Dispose(){SqliteConnection.ClearAllPools();if(Directory.Exists(root))Directory.Delete(root,true);}
}
