using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace LumaLyrics.Infrastructure;

internal static class TranslationJobFiles
{
    internal const int MaximumBytes=8*1024*1024;
    internal static readonly JsonSerializerOptions Json=new(){WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,Converters={new JsonStringEnumConverter()}};
    internal static string PathFor(string root,string id,string name)
    {if(id.Length!=32||!id.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f'))throw new InvalidDataException("任务 ID 无效。");return PersonalLibraryArchive.AssetPath(root,$"translation-jobs/{id}/{name}");}
    internal static void Write(string path,string text,bool replace=false)
    {
        if(Encoding.UTF8.GetByteCount(text)>MaximumBytes)throw new InvalidDataException("任务超过 8 MiB，未截断原词。");
        string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temp,text,new UTF8Encoding(false));File.Move(temp,path,replace);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
    internal static T Read<T>(string path,out string hash)
    {
        using var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(f.Length>MaximumBytes)throw new InvalidDataException("文件超过 8 MiB。");
        byte[] bytes=new byte[(int)f.Length];f.ReadExactly(bytes);if(f.ReadByte()!=-1)throw new InvalidDataException("文件在读取时增长。");
        hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        string raw;try{raw=new UTF8Encoding(false,true).GetString(bytes).TrimStart('\uFEFF');}catch(DecoderFallbackException e){throw new InvalidDataException("结果必须为 UTF-8。",e);}
        using var doc=JsonDocument.Parse(raw,new(){MaxDepth=32});Check(doc.RootElement);
        return JsonSerializer.Deserialize<T>(raw,Json)??throw new InvalidDataException("JSON 为空。");
    }
    static void Check(JsonElement element)
    {
        if(element.ValueKind==JsonValueKind.Object){var seen=new HashSet<string>();foreach(var p in element.EnumerateObject()){if(!seen.Add(p.Name))throw new InvalidDataException("JSON 含重复字段："+p.Name);Check(p.Value);}}
        else if(element.ValueKind==JsonValueKind.Array)foreach(var item in element.EnumerateArray())Check(item);
    }
}
