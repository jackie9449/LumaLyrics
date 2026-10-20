using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
using Microsoft.Data.Sqlite;
namespace LumaLyrics.Infrastructure.Tests;

public class ArtworkTests : IDisposable
{
    readonly string root=Path.Combine(Path.GetTempPath(),"Luma-artwork-"+Guid.NewGuid().ToString("N"));
    public ArtworkTests()=>Directory.CreateDirectory(root);
    sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> send):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(send(request));}
    static readonly Track track=new("Own song","Own artist","Own album",180000);
    static byte[] Png(int width,int height)
    {
        using var output=new MemoryStream();output.Write(new byte[]{137,80,78,71,13,10,26,10});
        void Chunk(string type,byte[] data)
        {
            var name=System.Text.Encoding.ASCII.GetBytes(type);var length=new byte[4];BinaryPrimitives.WriteInt32BigEndian(length,data.Length);output.Write(length);output.Write(name);output.Write(data);
            uint crc=0xffffffff;foreach(byte b in name.Concat(data)){crc^=b;for(int k=0;k<8;k++)crc=(crc&1)!=0?(crc>>1)^0xedb88320:crc>>1;}BinaryPrimitives.WriteUInt32BigEndian(length,~crc);output.Write(length);
        }
        var header=new byte[13];BinaryPrimitives.WriteInt32BigEndian(header,width);BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4),height);header[8]=8;header[9]=6;Chunk("IHDR",header);
        using var compressed=new MemoryStream();using(var z=new ZLibStream(compressed,CompressionLevel.Fastest,true)){var line=new byte[width*4+1];for(int y=0;y<height;y++)z.Write(line);}Chunk("IDAT",compressed.ToArray());Chunk("IEND",[]);return output.ToArray();
    }
    [Fact] public void AppleIndexUsesExactSignedPersistentIdWithoutChangingSource()
    {
        var file=Path.Combine(root,"artwork.sqlite");using(var db=new SqliteConnection($"Data Source={file}")){db.Open();using var c=db.CreateCommand();c.CommandText="CREATE TABLE artwork_source(artwork_id TEXT,location_type INTEGER,location TEXT);CREATE TABLE item_to_artwork(item_id INTEGER,artwork_id TEXT,source_kind INTEGER);CREATE TABLE cache_items(artwork_id TEXT,size_kind INTEGER,extension TEXT,width INTEGER,height INTEGER,status INTEGER);INSERT INTO artwork_source VALUES('01234567-89AB-CDEF-0123-456789ABCDEF',2,'https://is1-ssl.mzstatic.com/image/thumb/Music/test.jpg/{w}x{h}bb.jpg');INSERT INTO item_to_artwork VALUES(-1,'01234567-89AB-CDEF-0123-456789ABCDEF',12);";c.ExecuteNonQuery();}
        SqliteConnection.ClearAllPools();var before=File.ReadAllBytes(file);
        var found=new AppleMusicArtworkReader().Read(Path.Combine(root,"Library.musicdb"));
        Assert.Equal("FFFFFFFFFFFFFFFF",Assert.Single(found).Key);Assert.EndsWith("/1200x1200bb.jpg",found.Values.Single().RemoteUri!.AbsoluteUri);Assert.Equal(before,File.ReadAllBytes(file));
    }
    [Theory]
    [InlineData("https://evil.example/cover.jpg")]
    [InlineData("http://is1-ssl.mzstatic.com/image/thumb/x.jpg")]
    [InlineData("https://is1-ssl.mzstatic.com.evil.example/x.jpg")]
    [InlineData("../local.jpg")]
    public void ForeignOrUnsafeArtworkSourcesAreRejected(string url)=>Assert.Null(AppleMusicArtworkReader.HighResolutionUri(url,2));
    [Fact] public void AppleTemplatesUpgradeSmallImagesAndRetainLargerImages()
    {
        Assert.EndsWith("/1200x1200bb.jpg",AppleMusicArtworkReader.HighResolutionUri("Music124/v4/test.jpg",4)!.AbsoluteUri);
        Assert.EndsWith("/1200x1200bb.jpg",AppleMusicArtworkReader.HighResolutionUri("https://is1-ssl.mzstatic.com/image/thumb/Music/test.jpg/600x600bb.jpg",2)!.AbsoluteUri);
        Assert.EndsWith("/3000x3000bb.jpg",AppleMusicArtworkReader.HighResolutionUri("https://is1-ssl.mzstatic.com/image/thumb/Music/test.jpg/3000x3000bb.jpg",2)!.AbsoluteUri);
    }
    [Fact] public async Task AlbumImagesAreDownloadedOnceAndSurviveRestart()
    {
        var snapshot=new LibrarySnapshot("fixture",2,[new("01",track),new("02",track with{Title="Second own song"})],"hash");
        var uri=new Uri("https://is1-ssl.mzstatic.com/image/thumb/test/1200x1200bb.png");var sources=new Dictionary<string,AppleArtworkSource>{{"01",new("01","album",uri,null)},{"02",new("02","album",uri,null)}};int requests=0;
        using(var importer=new LibraryArtworkImporter(root,new Handler(r=>{requests++;return new(HttpStatusCode.OK){Content=new ByteArrayContent(Png(1200,1200))};}),TimeSpan.Zero))
        {await importer.RunAsync(snapshot,default,sources:sources);await importer.RunAsync(snapshot,default,sources:sources);}
        Assert.Equal(1,requests);using var cache=new ArtworkCache(root);Assert.Equal(2,cache.GetProgress().Cached);Assert.Equal(2,cache.GetProgress().HighResolution);Assert.Equal(1200,cache.Find(track)!.Width);
        Assert.Null(cache.Find(track with{Album="Different album"}));Assert.Null(cache.Find(track with{DurationMs=240000}));
        Assert.Single(Directory.GetFiles(Path.Combine(root,"cache","artwork"),"*.img"));
    }
    [Fact] public async Task LowResolutionFallbackIsNotReportedAsHighResolution()
    {
        var snapshot=new LibrarySnapshot("fixture",1,[new("01",track)],"hash");var local=Path.Combine(root,"local.png");File.WriteAllBytes(local,Png(600,600));var sources=new Dictionary<string,AppleArtworkSource>{{"01",new("01","album",new("https://is1-ssl.mzstatic.com/test/1200x1200bb.jpg"),local)}};
        using(var importer=new LibraryArtworkImporter(root,new Handler(r=>new(HttpStatusCode.NotFound)),TimeSpan.Zero))await importer.RunAsync(snapshot,default,sources:sources);
        using var cache=new ArtworkCache(root);Assert.Equal(1,cache.GetProgress().Cached);Assert.Equal(0,cache.GetProgress().HighResolution);Assert.Equal(600,cache.Find(track)!.Width);
    }
    [Fact] public async Task MissingSourcesAndMalformedImagesStayUnresolved()
    {
        var snapshot=new LibrarySnapshot("fixture",2,[new("01",track),new("02",track with{Album="Other"})],"hash");var sources=new Dictionary<string,AppleArtworkSource>{{"01",new("01","album",new("https://is1-ssl.mzstatic.com/test.jpg"),null)}};
        using(var importer=new LibraryArtworkImporter(root,new Handler(r=>new(HttpStatusCode.OK){Content=new StringContent("<html>not an image</html>")}),TimeSpan.Zero))await importer.RunAsync(snapshot,default,sources:sources);
        using var cache=new ArtworkCache(root);Assert.Null(cache.Find(track));Assert.Equal(0,cache.GetProgress().Cached);Assert.Equal(1,cache.GetProgress().Unavailable);Assert.Equal(1,cache.GetProgress().Errors);
    }
    [Fact] public void ImageDimensionsRejectTruncatedFilesAndReadActualSize()
    {
        var image=Png(1200,1190);Assert.Equal((1200,1190),ImageDimensions.Read(image));Assert.Throws<InvalidDataException>(()=>ImageDimensions.Read(image[..24]));
        Assert.Throws<InvalidDataException>(()=>ImageDimensions.Read("not image"u8.ToArray()));
    }
    [Fact] public async Task ConfirmedAlbumReferenceSuppliesCoverAcrossContributingArtists()
    {
        var snapshot=new LibrarySnapshot("fixture",2,[new("01",track,"A1"),new("02",track with{Artist="Contributing artist"},"A1")],"hash");
        var sources=new Dictionary<string,AppleArtworkSource>{{"A1",new("A1","album",new("https://is1-ssl.mzstatic.com/test.jpg"),null,45)}};int requests=0;
        using(var importer=new LibraryArtworkImporter(root,new Handler(r=>{requests++;return new(HttpStatusCode.OK){Content=new ByteArrayContent(Png(1200,1200))};}),TimeSpan.Zero))await importer.RunAsync(snapshot,default,sources:sources);
        using var cache=new ArtworkCache(root);Assert.Equal(2,cache.GetProgress().HighResolution);Assert.Equal(1,requests);
    }
    [Fact] public async Task SharedLowResolutionAlbumIsAttemptedOnlyOnceInOnePass()
    {
        var snapshot=new LibrarySnapshot("fixture",2,[new("01",track),new("02",track with{Title="Second own song"})],"hash");
        var uri=new Uri("https://is1-ssl.mzstatic.com/test.jpg");var sources=new Dictionary<string,AppleArtworkSource>{{"01",new("01","album",uri,null)},{"02",new("02","album",uri,null)}};int requests=0;
        using(var importer=new LibraryArtworkImporter(root,new Handler(r=>{requests++;return new(HttpStatusCode.OK){Content=new ByteArrayContent(Png(600,600))};}),TimeSpan.Zero))await importer.RunAsync(snapshot,default,sources:sources);
        Assert.Equal(1,requests);using var cache=new ArtworkCache(root);Assert.Equal(2,cache.GetProgress().Cached);Assert.Equal(0,cache.GetProgress().HighResolution);
    }
    [Fact] public async Task CorruptCachedImageIsRequeuedInsteadOfDisplayedOrCountedAsHighResolution()
    {
        var snapshot=new LibrarySnapshot("fixture",1,[new("01",track)],"hash");var sources=new Dictionary<string,AppleArtworkSource>{{"01",new("01","album",new("https://is1-ssl.mzstatic.com/test.jpg"),null)}};
        using(var importer=new LibraryArtworkImporter(root,new Handler(r=>new(HttpStatusCode.OK){Content=new ByteArrayContent(Png(1200,1200))}),TimeSpan.Zero))await importer.RunAsync(snapshot,default,sources:sources);
        File.WriteAllText(Directory.GetFiles(Path.Combine(root,"cache","artwork"),"*.img").Single(),"corrupt");using var cache=new ArtworkCache(root);
        Assert.Null(cache.Find(track));Assert.Equal(0,cache.GetProgress().HighResolution);Assert.Single(cache.GetQueue());
    }
    public void Dispose(){SqliteConnection.ClearAllPools();Directory.Delete(root,true);}
}
