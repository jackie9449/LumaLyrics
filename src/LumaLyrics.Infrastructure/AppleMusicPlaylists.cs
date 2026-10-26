using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
namespace LumaLyrics.Infrastructure;

public sealed record LibraryPlaylist(string Id,string Name,int DeclaredCount,IReadOnlyList<LibraryTrack> Tracks,bool Complete);
public sealed record MusicPlaylist(string Source,string Id,string Name,int DeclaredCount,IReadOnlyList<MusicLibrarySong> Songs,bool Complete,bool UniqueName=true)
{
    public string Key=>Source+"\0"+Id;
    public string Summary=>$"{DeclaredCount:N0} 首歌曲"+(Complete?"":" · 正在等待资料库同步");
    public bool CanPlay=>Complete && UniqueName && Songs.Count>0 && Songs.All(s=>s.CanPlay);
    public bool Matches(string query)=>query.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries)
        .All(t=>LumaLyrics.Core.MatchRules.Normalize(Name).Contains(LumaLyrics.Core.MatchRules.Normalize(t),StringComparison.Ordinal)
            || Songs.Any(s=>MusicLibraryCatalog.Matches(s,t)));
}

public sealed partial class AppleMusicLibraryReader
{
    static (HashSet<string>,IReadOnlyList<LibraryPlaylist>) ReadPlaylists(byte[] payload,int offset,IReadOnlyList<LibraryTrack> tracks)
    {
        var byId=tracks.ToDictionary(t=>t.PersistentId,StringComparer.Ordinal);
        while(offset+12<=payload.Length)
        {
            string tag=Encoding.ASCII.GetString(payload,offset,4);if(tag=="ssma")break;
            int size=ChunkSize(payload,offset,payload.Length);if(tag!="lPma"){offset+=size;continue;}
            int count=Int(payload,offset+8);if(count<1||count>100000)throw new InvalidDataException("资料库播放清单计数无效。");
            offset+=size;HashSet<string>? members=null;var playlists=new List<LibraryPlaylist>();
            for(int playlist=0;playlist<count;playlist++)
            {
                int header=ChunkSize(payload,offset,payload.Length);
                if(header<48 || !payload.AsSpan(offset,4).SequenceEqual("lpma"u8))throw new InvalidDataException("资料库播放清单头不受支持。");
                int total=Int(payload,offset+8),children=Int(payload,offset+12),declaredMembers=Int(payload,offset+16);
                if(total<header || total>payload.Length-offset || children<0 || children>1000000 || declaredMembers<0)throw new InvalidDataException("资料库播放清单边界无效。");
                bool master=Int(payload,offset+44)==1;
                if(master && members is not null)throw new InvalidDataException("资料库出现多个主清单，未合并未确认的曲目。");
                var ids=new List<string>();string name="",identity="";bool displayMetadata=false,validMetadata=true;
                int end=offset+total,childOffset=offset+header;
                for(int child=0;child<children;child++)
                {
                    int length=ChunkSize(payload,childOffset,end);
                    if(length<20 || !payload.AsSpan(childOffset,4).SequenceEqual("boma"u8))throw new InvalidDataException("资料库播放清单内容不完整。");
                    int type=Int(payload,childOffset+12);
                    if(type==200 && length>=36)
                    {
                        int encoding=Int(payload,childOffset+20),textBytes=Int(payload,childOffset+24);
                        if(textBytes<0 || textBytes>length-36 || encoding is not (1 or 2))validMetadata=false;
                        else name=(encoding==1?Encoding.Unicode:Encoding.UTF8).GetString(payload,childOffset+36,textBytes).TrimEnd('\0');
                    }
                    if(type==205)
                    {
                        displayMetadata=true;int metadataHeader=Int(payload,childOffset+4);
                        if(metadataHeader<20 || metadataHeader>length || length-metadataHeader>1024*1024)validMetadata=false;
                        else try{identity=PlaylistIdentity(payload,childOffset+metadataHeader,length-metadataHeader);}
                        catch(XmlException){validMetadata=false;}
                    }
                    if(type==206)
                    {
                        int metadataHeader=Int(payload,childOffset+4);
                        if(metadataHeader<20 || metadataHeader>length-28)throw new InvalidDataException("资料库成员引用不完整。");
                        int reference=childOffset+metadataHeader;
                        if(!payload.AsSpan(reference,4).SequenceEqual("ipfa"u8))throw new InvalidDataException("资料库成员引用格式不受支持。");
                        int referenceSize=Int(payload,reference+4);
                        if(referenceSize<28 || referenceSize>length-metadataHeader)throw new InvalidDataException("资料库成员引用边界无效。");
                        ids.Add(BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(reference+20,8)).ToString("X16"));
                    }
                    childOffset+=length;
                }
                if(childOffset!=end)throw new InvalidDataException("资料库播放清单计数与边界不一致。");
                if(master)
                {
                    members=ids.ToHashSet(StringComparer.Ordinal);
                    if(members.Count!=ids.Count)throw new InvalidDataException("资料库主清单包含重复曲目引用。");
                    if(members.Count!=declaredMembers)throw new InvalidDataException("资料库成员计数不一致，未把部分清单当作全库。");
                }
                else if(displayMetadata && !string.IsNullOrWhiteSpace(name))
                {
                    // Windows display metadata distinguishes user/smart playlists from internal
                    // music, downloads, TV and hidden cloud membership lists. Names alone do not.
                    string key=identity.Length>0?identity:"name:"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name)));
                    var ordered=ids.Where(byId.ContainsKey).Select(id=>byId[id]).ToArray();
                    playlists.Add(new(key,name,declaredMembers,ordered,validMetadata && ids.Count==declaredMembers && ordered.Length==ids.Count));
                }
                offset=end;
            }
            return (members??throw new InvalidDataException("未找到可确认的资料库主清单，未把播放或浏览缓存当成个人资料库。"),playlists);
        }
        throw new InvalidDataException("尚未取得资料库主清单，请先在 Apple Music 完成同步。");
    }
    static string PlaylistIdentity(byte[] bytes,int offset,int count)
    {
        using var stream=new MemoryStream(bytes,offset,count,false);
        using var reader=XmlReader.Create(stream,new XmlReaderSettings{DtdProcessing=DtdProcessing.Ignore,XmlResolver=null,MaxCharactersInDocument=1024*1024});
        var doc=XDocument.Load(reader);var dict=doc.Root?.Element("dict");if(dict is null)throw new XmlException("Invalid playlist metadata.");
        var keys=dict.Elements().ToArray();
        for(int i=0;i+1<keys.Length;i++)if(keys[i].Name=="key" && keys[i].Value=="universal-library-id" && keys[i+1].Name=="string")return keys[i+1].Value;
        return "";
    }
}

public static class MusicPlaylistPlayback
{
    public static bool IsCurrent(MusicPlaylist requested,IReadOnlyList<LibraryPlaylist> actual)
    {
        var matching=actual.Where(p=>p.Id==requested.Id).ToArray();
        return requested.CanPlay && actual.Count(p=>string.Equals(p.Name,requested.Name,StringComparison.OrdinalIgnoreCase))==1
            && matching is [{Complete:true} current] && current.Name==requested.Name && current.DeclaredCount==requested.DeclaredCount
            && current.Tracks.Select(t=>(t.PersistentId,t.Track)).SequenceEqual(requested.Songs.Select(s=>(s.PersistentId,s.Track)));
    }
    public static bool Confirms(MusicPlaylist playlist,MusicQueueSnapshot after)
    {
        if(!playlist.CanPlay || !after.Available || after.Current is null || !MusicLibraryPlayback.IsRequestedRecording(playlist.Songs[0].Track,after.Current))return false;
        var remaining=playlist.Songs.Skip(1).ToArray();
        if(after.IsComplete && remaining.Length!=after.Upcoming.Count)return false;
        if(!after.IsComplete && (after.Upcoming.Count<Math.Min(10,remaining.Length) || remaining.Length==0))return false;
        return after.Upcoming.Count<=remaining.Length && after.Upcoming.Select((entry,index)=>entry.MetadataComplete && MusicLibraryPlayback.IsRequestedRecording(remaining[index].Track,entry.Track)).All(b=>b);
    }
}
