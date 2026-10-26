using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using LumaLyrics.Core;
namespace LumaLyrics.Infrastructure;

public sealed record LibraryTrack(string PersistentId,Track Track,string? AlbumPersistentId=null,string AlbumArtist="");
public sealed record LibrarySnapshot(string Path,int DeclaredCount,IReadOnlyList<LibraryTrack> Tracks,string SourceHash)
{
    // The track table also contains recordings outside the user's library.
    public int StoredTrackCount {get;init;}=DeclaredCount;
    public IReadOnlyList<LibraryPlaylist> Playlists {get;init;}=[];
}

/// <summary>Original, read-only metadata reader. Binary field definitions are documented
/// by Gary Vollink (musicdb format). It never reads media, account keys or credentials.</summary>
public sealed partial class AppleMusicLibraryReader
{
    const int MaximumBytes=256*1024*1024;
    public static string? Discover()
    {
        var music=Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        foreach(var path in new[]{Path.Combine(music,"Apple Music","Apple Music Library.musiclibrary","Library.musicdb"),Path.Combine(music,"Music","Music Library.musiclibrary","Library.musicdb"),Path.Combine(music,"iTunes","iTunes Library.musiclibrary","Library.musicdb")})
            if(File.Exists(path))return path;
        return null;
    }
    public LibrarySnapshot Read(string path)
    {
        if(!Path.GetFileName(path).Equals("Library.musicdb",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("请选择 Apple Music 的 Library.musicdb。");
        byte[] bytes;
        using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
        {
            if(f.Length<160||f.Length>MaximumBytes)throw new InvalidDataException("资料库文件大小不受支持。");
            bytes=new byte[(int)f.Length];f.ReadExactly(bytes);
        }
        return Parse(bytes,Path.GetFullPath(path));
    }
    public LibrarySnapshot Parse(byte[] bytes,string path="fixture")
    {
        if(bytes.Length<160||!bytes.AsSpan(0,4).SequenceEqual("hfma"u8))throw new InvalidDataException("不是受支持的 Apple Music 资料库。");
        int header=Int(bytes,4),declared=Int(bytes,8);
        if(header<96||header>bytes.Length||declared!=bytes.Length)throw new InvalidDataException("资料库正在更新或格式不受支持，稍后自动重试。");
        byte[]? payload=null;
        // Known library revisions use the crypt-size field at 84 or 92.
        foreach(int field in new[]{84,92})
        {
            int size=Int(bytes,field);int available=bytes.Length-header;
            if(size<0||size>available)size=available-available%16;
            if(size%16!=0)continue;
            try
            {
                using var aes=Aes.Create();aes.Key=Encoding.ASCII.GetBytes("BHUILuilfghuila3");
                var compressed=new byte[available];aes.DecryptEcb(bytes.AsSpan(header,size),compressed,PaddingMode.None);bytes.AsSpan(header+size).CopyTo(compressed.AsSpan(size));
                using var z=new ZLibStream(new MemoryStream(compressed),CompressionMode.Decompress);using var output=new MemoryStream();var buffer=new byte[16384];int n;
                while((n=z.Read(buffer))>0){if(output.Length+n>MaximumBytes)throw new InvalidDataException("资料库解压大小超限。");output.Write(buffer,0,n);}
                payload=output.ToArray();break;
            }
            catch(Exception e) when(e is CryptographicException or InvalidDataException){ }
        }
        if(payload is null)throw new InvalidDataException("此资料库版本无法读取，未修改原文件。");
        var tracks=new List<LibraryTrack>();int expected=-1,offset=0;
        while(offset+8<=payload.Length)
        {
            var tag=Encoding.ASCII.GetString(payload,offset,4);if(tag=="ssma")break;
            int size=tag=="boma"?Int(payload,offset+8):Int(payload,offset+4);
            if(size<8||size>payload.Length-offset)throw new InvalidDataException($"资料库记录边界无效（{tag}，offset={offset}，size={size}，payload={payload.Length}，tracks={tracks.Count}/{expected}）。");
            if(tag=="ltma")
            {
                if(size<12)throw new InvalidDataException("曲目列表头不完整。");
                expected=Int(payload,offset+8);if(expected<0)throw new InvalidDataException("曲目计数无效。");
                if(expected==0){offset+=size;break;}
            }
            if(tag!="itma"){offset+=size;continue;}
            if(size<24)throw new InvalidDataException("曲目记录不完整。");
            string id=BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(offset+16,8)).ToString("X16");
            // Windows Music stores the album reference at 180 in the expanded itma header.
            // A fallback is used only if artwork.sqlite confirms it is an album (source_kind 45).
            string? albumId=size>=188?BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(offset+180,8)).ToString("X16"):null;
            int children=Int(payload,offset+12);if(children<0||children>10000)throw new InvalidDataException("曲目子记录数异常。");
            offset+=size;string title="",artist="",album="",albumArtist="";double? duration=null;
            for(int j=0;j<children;j++)
            {
                if(offset+20>payload.Length||!payload.AsSpan(offset,4).SequenceEqual("boma"u8))throw new InvalidDataException("曲目元数据记录不完整。");
                int length=Int(payload,offset+8),type=Int(payload,offset+12);
                if(length<20||length>payload.Length-offset)throw new InvalidDataException("曲目元数据边界无效。");
                if(type is 2 or 3 or 4 or 0x1B && length>=36)
                {
                    int encoding=Int(payload,offset+20),count=Int(payload,offset+24);
                    if(count<0||count>length-36)throw new InvalidDataException("曲目文字长度无效。");
                    var text=(encoding==1?Encoding.Unicode:encoding==2?Encoding.UTF8:throw new InvalidDataException("曲目文字编码不受支持。")).GetString(payload,offset+36,count).TrimEnd('\0');
                    if(type==2)title=text;else if(type==3)album=text;else if(type==4)artist=text;else albumArtist=text;
                }
                else if(type==1 && length>=180){var value=Int(payload,offset+176);if(value>0 && value<86400000)duration=value;}
                offset+=length;
            }
            tracks.Add(new(id,new(title,artist,album,duration),albumId,albumArtist));
            if(expected>=0 && tracks.Count==expected)break;
        }
        if(expected<0||tracks.Count!=expected)throw new InvalidDataException($"资料库曲目计数不一致：声明 {expected}，读取 {tracks.Count}，未把部分结果当作全库。");
        if(tracks.Select(t=>t.PersistentId).Distinct(StringComparer.Ordinal).Count()!=tracks.Count)throw new InvalidDataException("曲目表包含重复标识，未替换资料库清单。");
        var (membership,playlists)=ReadPlaylists(payload,offset,tracks);
        var storedIds=tracks.Select(t=>t.PersistentId).ToHashSet(StringComparer.Ordinal);
        if(!membership.IsSubsetOf(storedIds))throw new InvalidDataException("资料库主清单引用了缺失曲目，稍后同步完成后重试。");
        var library=tracks.Where(t=>membership.Contains(t.PersistentId)).ToArray();
        return new(path,library.Length,library,Convert.ToHexString(SHA256.HashData(bytes))){StoredTrackCount=expected,Playlists=playlists};
    }

    static int ChunkSize(byte[] payload,int offset,int end)
    {
        if(offset<0 || offset>end-12 || end>payload.Length)throw new InvalidDataException("资料库记录不完整。");
        bool metadata=payload.AsSpan(offset,4).SequenceEqual("boma"u8);
        int size=Int(payload,offset+(metadata?8:4));
        if(size<12 || size>end-offset)throw new InvalidDataException("资料库记录边界无效。");
        return size;
    }
    static int Int(byte[] bytes,int offset)
    {
        if(offset<0||offset+4>bytes.Length)throw new InvalidDataException("资料库字段越界。");
        return BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset,4));
    }
}
