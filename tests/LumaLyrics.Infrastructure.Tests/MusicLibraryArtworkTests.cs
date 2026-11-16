using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
using Microsoft.Data.Sqlite;
namespace LumaLyrics.Infrastructure.Tests;

public sealed class MusicLibraryArtworkTests:IDisposable
{
    readonly string root=Path.Combine(Path.GetTempPath(),"Luma-artwork-rebind-"+Guid.NewGuid().ToString("N"));
    static readonly byte[] png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aL1sAAAAASUVORK5CYII=");
    static readonly Track song=new("Own song","Own artist","Own album",180000);
    LibrarySnapshot Snapshot(string id="new",string? album=null)=>new("native",1,[new(id,song,album)],"hash");
    AppleArtworkSource Source(string id="new",string suffix="one",string? local=null,int kind=12)=>new(id,suffix,new Uri("https://is1-ssl.mzstatic.com/"+suffix+".png"),local,kind);
    string Save(AppleArtworkSource source,string id="old")
    {
        using var cache=new ArtworkCache(root);cache.Sync(Snapshot(id),new Dictionary<string,AppleArtworkSource>{{id,source}});
        var item=Assert.Single(cache.GetQueue());cache.Save(item,png);return Path.Combine(root,"cache","artwork",item.ImageKey+".img");
    }
    public void Dispose(){SqliteConnection.ClearAllPools();if(Directory.Exists(root))Directory.Delete(root,true);}
    [Fact] public void ChangedSongIdsReuseVerifiedImagesWhileDownloadsRemainPaused()
    {
        var source=Source();string path=Save(source);string db=Path.Combine(root,"artwork.db");
        SqliteConnection.ClearAllPools();var before=File.ReadAllBytes(db);File.WriteAllText(Path.Combine(root,"library-prefetch.pause"),"");
        var result=new MusicLibraryArtworkCatalog(root).Load(Snapshot(),new Dictionary<string,AppleArtworkSource>{{"new",source}});
        Assert.Equal(path,result["native\0new"]);Assert.Equal(before,File.ReadAllBytes(db));Assert.True(File.Exists(Path.Combine(root,"library-prefetch.pause")));
    }
    [Fact] public void CurrentNativeMappingOverridesOldExactSongImage()
    {
        Save(Source(),"new");var source=Source(suffix:"different");
        Assert.DoesNotContain("native\0new",new MusicLibraryArtworkCatalog(root).Load(Snapshot(),new Dictionary<string,AppleArtworkSource>{{"new",source}}).Keys);
    }
    [Fact] public void CorruptedOrMissingCachedImagesAreRejected()
    {
        var source=Source();string path=Save(source);var resolver=new MusicLibraryArtworkCatalog(root);var sources=new Dictionary<string,AppleArtworkSource>{{"new",source}};
        File.WriteAllBytes(path,png.Concat(new byte[]{1}).ToArray());Assert.Empty(resolver.Load(Snapshot(),sources));
        File.Delete(path);Assert.Empty(resolver.Load(Snapshot(),sources));
    }
    [Fact] public void NativeLocalImageWorksWithoutPersonalDatabase()
    {
        Directory.CreateDirectory(root);string path=Path.Combine(root,"native.png");File.WriteAllBytes(path,png);
        var result=new MusicLibraryArtworkCatalog(root).Load(Snapshot(),new Dictionary<string,AppleArtworkSource>{{"new",Source(local:path)}});
        Assert.Equal(path,result["native\0new"]);Assert.False(File.Exists(Path.Combine(root,"artwork.db")));
    }
    [Theory] [InlineData(45,true)] [InlineData(12,false)]
    public void OnlyConfirmedAlbumReferencesSupplyArtwork(int kind,bool expected)
    {
        var source=Source(id:"album",kind:kind);Save(source);
        var result=new MusicLibraryArtworkCatalog(root).Load(Snapshot(album:"album"),new Dictionary<string,AppleArtworkSource>{{"album",source}});
        Assert.Equal(expected,result.ContainsKey("native\0new"));
    }
    [Fact] public void MissingNativeIndexRetainsExactVerifiedCachedBinding()
    {
        string path=Save(Source(),"new");Assert.Equal(path,new MusicLibraryArtworkCatalog(root).Load(Snapshot(),null)["native\0new"]);
    }
    [Fact] public void CancelledReadCreatesNothing()
    {
        using var cancel=new CancellationTokenSource();cancel.Cancel();
        Assert.Throws<OperationCanceledException>(()=>new MusicLibraryArtworkCatalog(root).Load(Snapshot(),null,cancel.Token));Assert.False(Directory.Exists(root));
    }
}
