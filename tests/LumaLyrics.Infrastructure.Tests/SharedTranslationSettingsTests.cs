using System.Text.Json;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.Infrastructure.Tests;
public sealed class SharedTranslationSettingsTests:IDisposable
{
    readonly string root=Path.Combine(Path.GetTempPath(),"Luma-shared-translation-"+Guid.NewGuid().ToString("N"));
    [Theory]
    [InlineData("{}",false)]
    [InlineData("{\"ShowChineseTranslation\":false,\"OverlayShowChineseTranslation\":true}",false)]
    [InlineData("{\"OverlayShowChineseTranslation\":false,\"ShowChineseTranslation\":true}",true)]
    [InlineData("{\"OverlayShowChineseTranslation\":true}",true)]
    [InlineData("{\"OverlayShowChineseTranslation\":false}",false)]
    public void MigratesLegacyStatesAndWritesOneCanonicalValue(string json,bool expected)
    {
        var store=new SettingsStore(root);string path=Path.Combine(root,"settings.json");File.WriteAllText(path,json);
        var settings=store.Load();Assert.Equal(expected,settings.ShowChineseTranslation);Assert.Equal(expected,settings.OverlayShowChineseTranslation);
        Assert.Equal(json,File.ReadAllText(path)); // Loading does not overwrite the original profile.
        store.Save(settings);using var saved=JsonDocument.Parse(File.ReadAllText(path));
        Assert.False(saved.RootElement.TryGetProperty("OverlayShowChineseTranslation",out _));
        Assert.Equal(expected,new SettingsStore(root).Load().ShowChineseTranslation);
    }
    [Fact] public void LegacyCodeAliasCannotDivergeFromCanonicalSwitch()
    {
        var settings=new AppSettings{ShowChineseTranslation=true};Assert.True(settings.OverlayShowChineseTranslation);
        settings.OverlayShowChineseTranslation=false;Assert.False(settings.ShowChineseTranslation);
        settings.ShowChineseTranslation=true;Assert.True(settings.OverlayShowChineseTranslation);
    }
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
}
