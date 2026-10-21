using System.Text;
using System.Text.Json;
namespace LumaLyrics.Infrastructure;

/// <summary>Machine-local location only. It is never part of a software or personal distribution.</summary>
public static class DataDirectoryConfiguration
{
    public static string DefaultRoot=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LumaLyrics");
    static string DefaultFile=>Path.Combine(DefaultRoot,"data-location.json");
    sealed record Location(int Version,string DataDirectory);
    public static string Resolve(string? explicitRoot=null,string? configurationFile=null)
    {
        if(explicitRoot is not null)return Path.GetFullPath(explicitRoot);
        string file=configurationFile??DefaultFile;
        if(!File.Exists(file))return DefaultRoot;
        try
        {
            var location=JsonSerializer.Deserialize<Location>(File.ReadAllText(file));
            if(location is null || location.Version!=1 || string.IsNullOrWhiteSpace(location.DataDirectory) || !Path.IsPathFullyQualified(location.DataDirectory))throw new InvalidDataException("个人资料位置配置无效。");
            return CheckExisting(location.DataDirectory);
        }
        catch(JsonException e){throw new InvalidDataException("个人资料位置配置损坏；请重新选择已有资料目录。",e);}
    }
    static string CheckExisting(string root)
    {
        root=Path.GetFullPath(root);PersonalLibraryArchive.EnsureOutsideSoftware(root);
        if(!Directory.Exists(root)||!File.Exists(Path.Combine(root,"lyrics.db")))throw new DirectoryNotFoundException("已选个人资料目录不可用；请连接原磁盘，或恢复备份后重新选择。不会自动创建空资料库。");
        PersonalLibrarySnapshot.CheckDatabase(Path.Combine(root,"lyrics.db"),true);return root;
    }
    public static void SetDefault(string root,string? configurationFile=null)
    {
        root=CheckExisting(root);string file=configurationFile??DefaultFile;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        string temp=file+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temp,JsonSerializer.Serialize(new Location(1,root)),new UTF8Encoding(false));File.Move(temp,file,true);}
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
    internal static void ProtectFromGit(string root)
    {
        string file=Path.Combine(root,".gitignore");
        try{using var stream=new FileStream(file,FileMode.CreateNew);using var writer=new StreamWriter(stream,new UTF8Encoding(false));writer.Write("*\n");}
        catch(IOException) when(File.Exists(file)){ }
    }
}
