using LumaLyrics.Core;
namespace LumaLyrics.Infrastructure.Tests;
public sealed class OfflineLanguageTests
{
    [Theory]
    [InlineData("fr","Le vent traverse la vallée")]
    [InlineData("de","Der Wind zieht durch das Tal")]
    [InlineData("es","El viento cruza el valle")]
    [InlineData("ja","風が谷を渡る")]
    [InlineData("ja","日々の光")]
    [InlineData("ja","思い出を辿る日々")]
    [InlineData("ko","바람이 계곡을 건넌다")]
    [InlineData("ko","바람이 계곡을 건넌다")]
    [InlineData("en","The wind crosses the valley")]
    public void RoutesSupportedDetectedAndExplicitSources(string language,string text)
    {
        Assert.Equal(language,ArgosLanguages.Resolve(text,"auto",_=>language));
        Assert.Equal(language,ArgosLanguages.Resolve(text,language,_=>null));
        Assert.Equal(language,ArgosLanguages.Resolve(text,"auto",_=>language+"-XX"));
    }
    [Theory][InlineData("ru")][InlineData("it")][InlineData("zh")][InlineData(null)]
    public void UncertainOrUnsupportedDetectorDoesNotGuessEnglish(string? detected)=>Assert.Throws<InvalidDataException>(()=>ArgosLanguages.Resolve("Uncertain short lyric","auto",_=>detected));
    [Theory][InlineData("ja","en")][InlineData("ko","fr")][InlineData("ja","ko")]
    public void ExplicitSourceCannotMisrouteIncompatibleScripts(string actual,string selected)
        =>Assert.Throws<InvalidDataException>(()=>ArgosLanguages.Resolve(actual=="ja"?"風が谷を渡る":"바람이 계곡을 건넌다",selected,null));
    [Theory][InlineData("en","local:argos/en-zh")][InlineData("fr","local:argos/fr-en-zh")][InlineData("de","local:argos/de-en-zh")][InlineData("es","local:argos/es-en-zh")][InlineData("ja","local:argos/ja-en-zh")][InlineData("ko","local:argos/ko-en-zh")]
    public void OnlyExactReservedSourcesAreMachineTranslations(string language,string source)
    {
        Assert.Equal(source,ArgosLanguages.SourceUrl(language));
        ChineseTranslation Value(string url)=>new(new string('a',64),[new(0,"自写中文")],"Own",url,"Own",DateTimeOffset.UtcNow,true);
        Assert.True(OfflineTranslationWorkflow.IsMachine(Value(source)));Assert.False(OfflineTranslationWorkflow.IsMachine(Value(source+"/untrusted")));
    }
    [Fact]public void AutoUsesStrongKanaAndHangulAndLegacyNoDetectorEnglish()
    {
        Assert.Equal("ja",ArgosLanguages.Resolve("風が谷を渡る","auto",_=>null));
        Assert.Equal("ko",ArgosLanguages.Resolve("바람이 계곡을 건넌다","auto",_=>null));
        Assert.Equal("en",ArgosLanguages.Resolve("We sail home","auto",null));
        Assert.Throws<InvalidDataException>(()=>ArgosLanguages.Resolve("風が 바람이","auto",_=>"ja"));
    }
    [Fact]public void ExistingEnglishRuntimeRemainsTheBaseAndExtraModelsAreOptIn()
    {
        var install=new ArgosInstallation(Path.Combine(Path.GetTempPath(),"Luma-language-inspect-"+Guid.NewGuid().ToString("N")));
        Assert.Equal(install.DownloadBytes,install.DownloadBytesFor("en"));
        Assert.InRange(install.DownloadBytesFor("auto"),850_000_000,865_000_000);
        Assert.Equal(5,install.SourceModels.Count);
        foreach(var language in new[]{"fr","de","es","ja","ko"})Assert.InRange(install.DownloadBytesFor(language)-install.DownloadBytes,60_000_000,290_000_000);
        Assert.False(install.ReadyFor("auto"));
    }
    [Fact]public void PartialOrDamagedSourceModelsDoNotDisableTheEnglishRuntime()
    {
        string root=Path.Combine(Path.GetTempPath(),"Luma-model-state-"+Guid.NewGuid().ToString("N"));var install=new ArgosInstallation(root);
        void Inventory(string folder,int count)
        {
            Directory.CreateDirectory(folder);var files=new Dictionary<string,long>();
            for(int i=0;i<count;i++){File.WriteAllText(Path.Combine(folder,"own-"+i),"fixture");files["own-"+i]=7;}
            File.WriteAllText(Path.Combine(folder,"installed.json"),System.Text.Json.JsonSerializer.Serialize(files));
        }
        try
        {
            Inventory(install.RuntimePath,6);Assert.True(install.ReadyFor("en"));Assert.False(install.ReadyFor("fr"));Assert.False(install.ReadyFor("auto"));
            string modelRoot=Path.GetDirectoryName(install.ModelPath("fr"))!;Inventory(modelRoot,3);Assert.True(install.ReadyFor("fr"));Assert.False(install.ReadyFor("de"));
            File.WriteAllText(Path.Combine(modelRoot,"own-0"),"damaged fixture");Assert.False(install.ReadyFor("fr"));Assert.True(install.ReadyFor("en"));
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
