using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
namespace LumaLyrics.Infrastructure;

public sealed record ArgosComponent(string Name,string Url,string Sha256,long Size,string Target);
public sealed record ArgosLock(string Version,IReadOnlyList<ArgosComponent> Components);
public sealed class ArgosInstallation
{
    public static string DefaultDirectory=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LumaLyricsTools","argos");
    public string DirectoryPath {get;}
    public string RuntimePath=>Path.Combine(DirectoryPath,Manifest.Version+"-"+lockHash[..12]);
    readonly string lockHash;
    public ArgosLock Manifest {get;}
    public long DownloadBytes=>Manifest.Components.Sum(c=>c.Size);
    public ArgosInstallation(string? directory=null)
    {DirectoryPath=Path.GetFullPath(directory??DefaultDirectory);string text=Resource("argos-engine-lock.json");Manifest=JsonSerializer.Deserialize<ArgosLock>(text)!;lockHash=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))).ToLowerInvariant();}
    internal static string Resource(string name){using var s=typeof(ArgosInstallation).Assembly.GetManifestResourceStream("LumaLyrics.Infrastructure.Resources."+name)!;using var r=new StreamReader(s);return r.ReadToEnd();}
    public bool Ready
    {
        get
        {
            try
            {
                var file=Path.Combine(RuntimePath,"installed.json");if(!File.Exists(file))return false;
                var files=JsonSerializer.Deserialize<Dictionary<string,long>>(File.ReadAllText(file));
                return files is {Count:>5} && files.All(p=>File.Exists(SafePath(RuntimePath,p.Key)) && new FileInfo(SafePath(RuntimePath,p.Key)).Length==p.Value);
            }
            catch(Exception e) when(e is IOException or JsonException or UnauthorizedAccessException or InvalidDataException){return false;}
        }
    }
    public FileStream Lease(){Directory.CreateDirectory(DirectoryPath);return new(Path.Combine(DirectoryPath,"engine.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
    public static async Task<bool> VerifyComponentAsync(string path,ArgosComponent item,CancellationToken token)
    {
        if(!File.Exists(path)||new FileInfo(path).Length!=item.Size)return false;
        using var file=File.OpenRead(path);return Convert.ToHexString(await SHA256.HashDataAsync(file,token)).Equals(item.Sha256,StringComparison.OrdinalIgnoreCase);
    }
    public static string SafePath(string root,string relative)
    {
        if(Path.IsPathRooted(relative)||relative.Contains(':')||relative.Contains('\\'))throw new InvalidDataException("Invalid archive path");
        string prefix=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,path=Path.GetFullPath(Path.Combine(root,relative));
        if(!path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Archive escaped destination");return path;
    }
    public static void Extract(string archive,string destination,bool wheel=false)
    {
        using var zip=ZipFile.OpenRead(archive);long total=0;
        foreach(var entry in zip.Entries)
        {
            total+=entry.Length;if(total>1024L*1024*1024 || entry.Length>512L*1024*1024 || ((entry.ExternalAttributes>>16)&0xf000)==0xa000)throw new InvalidDataException("Archive exceeds limits");
            string name=entry.FullName;
            if(wheel && name.Contains(".data/"))
            {int index=name.IndexOf(".data/",StringComparison.Ordinal)+6;string suffix=name[index..];if(!suffix.StartsWith("purelib/")&&!suffix.StartsWith("platlib/"))continue;name=suffix[8..];}
            string path=SafePath(destination,name);if(entry.Name.Length==0){Directory.CreateDirectory(path);continue;}
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);entry.ExtractToFile(path,true);
        }
    }
    public async Task PrepareAsync(IProgress<(int Done,int Total)>? progress,CancellationToken token,bool repair=false)
    {
        using var lease=Lease();if(Ready&&!repair){progress?.Report((Manifest.Components.Count,Manifest.Components.Count));return;}
        string staging=Path.Combine(DirectoryPath,"staging-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);
        try
        {
            using var client=new HttpClient(){Timeout=TimeSpan.FromMinutes(4)};client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 LumaLyrics/1.2.0");
            int done=0;
            foreach(var item in Manifest.Components)
            {
                token.ThrowIfCancellationRequested();string downloads=Path.Combine(DirectoryPath,"downloads");Directory.CreateDirectory(downloads);
                string archive=Path.Combine(downloads,item.Name);
                Task<bool> Valid()=>VerifyComponentAsync(archive,item,token);
                if(!await Valid())
                {
                    if(new Uri(item.Url).Scheme!="https")throw new InvalidDataException("HTTPS required");
                    string temp=archive+".tmp";
                    try
                    {
                        using var response=await client.GetAsync(item.Url,HttpCompletionOption.ResponseHeadersRead,token);response.EnsureSuccessStatusCode();
                        using var source=await response.Content.ReadAsStreamAsync(token);using(var file=File.Create(temp))
                        {var buffer=new byte[65536];long read=0;int n;while((n=await source.ReadAsync(buffer,token))>0){read+=n;if(read>item.Size)throw new InvalidDataException("Download exceeds pinned size");await file.WriteAsync(buffer.AsMemory(0,n),token);}}
                        token.ThrowIfCancellationRequested();File.Move(temp,archive,true);
                    }
                    finally{if(File.Exists(temp))File.Delete(temp);}
                    if(!await Valid())throw new InvalidDataException("Component hash mismatch");
                }
                string target=Path.Combine(staging,item.Target=="site"?"Lib/site-packages":item.Target=="model"?"model":".");
                Extract(archive,target,item.Target=="site");progress?.Report((++done,Manifest.Components.Count));
            }
            File.WriteAllText(Path.Combine(staging,"python312._pth"),"python312.zip\n.\nLib/site-packages\nimport site\n");
            // NumPy's verified wheel already carries the signed Microsoft C++ library.
            // Preserve its bytes and licenses; provide the canonical name for Argos/CT2.
            string cpp=Directory.GetFiles(Path.Combine(staging,"Lib/site-packages/numpy.libs"),"msvcp140-*.dll").Single();
            File.Copy(cpp,Path.Combine(staging,"msvcp140.dll"));
            var inventory=Directory.EnumerateFiles(staging,"*",SearchOption.AllDirectories).ToDictionary(f=>Path.GetRelativePath(staging,f).Replace('\\','/'),f=>new FileInfo(f).Length);
            File.WriteAllText(Path.Combine(staging,"installed.json"),JsonSerializer.Serialize(inventory));token.ThrowIfCancellationRequested();
            if(Directory.Exists(RuntimePath)){if((File.GetAttributes(RuntimePath)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Invalid tools directory");Directory.Delete(RuntimePath,true);}
            Directory.Move(staging,RuntimePath);
        }
        finally{if(Directory.Exists(staging))Directory.Delete(staging,true);}
    }
}
