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
    public IReadOnlyDictionary<string,ArgosComponent> SourceModels {get;}
    public long DownloadBytes=>Manifest.Components.Sum(c=>c.Size);
    public ArgosInstallation(string? directory=null)
    {DirectoryPath=Path.GetFullPath(directory??DefaultDirectory);string text=Resource("argos-engine-lock.json");Manifest=JsonSerializer.Deserialize<ArgosLock>(text)!;lockHash=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        SourceModels=JsonSerializer.Deserialize<ArgosLock>(Resource("argos-model-lock.json"))!.Components.ToDictionary(c=>c.Name.Split('-')[1].Split('_')[0]);
    }
    internal static string Resource(string name){using var s=typeof(ArgosInstallation).Assembly.GetManifestResourceStream("LumaLyrics.Infrastructure.Resources."+name)!;using var r=new StreamReader(s);return r.ReadToEnd();}
    public bool Ready=>InventoryReady(RuntimePath,6);
    static bool InventoryReady(string directory,int minimum=3)
    {
        try
        {
            var file=Path.Combine(directory,"installed.json");if(!File.Exists(file))return false;
            var files=JsonSerializer.Deserialize<Dictionary<string,long>>(File.ReadAllText(file));
            return files is not null && files.Count>=minimum && files.All(p=>File.Exists(SafePath(directory,p.Key)) && new FileInfo(SafePath(directory,p.Key)).Length==p.Value);
        }
        catch(Exception e) when(e is IOException or JsonException or UnauthorizedAccessException or InvalidDataException){return false;}
    }
    string ModelRoot(string source)=>Path.Combine(DirectoryPath,"source-models",source+"-"+SourceModels[source].Sha256[..12]);
    public string ModelPath(string source)=>source=="en"?Path.Combine(RuntimePath,"model"):Path.Combine(ModelRoot(source),"model");
    IEnumerable<KeyValuePair<string,ArgosComponent>> Requested(string selection)
    {
        if(!ArgosLanguages.IsSelection(selection))throw new InvalidDataException("Unsupported source language");
        return SourceModels.Where(p=>selection=="auto"||p.Key==selection);
    }
    public bool ReadyFor(string selection)=>Ready&&Requested(selection).All(p=>InventoryReady(ModelRoot(p.Key)));
    public long DownloadBytesFor(string selection)=>DownloadBytes+Requested(selection).Sum(p=>p.Value.Size);
    public IReadOnlyDictionary<string,string> InstalledSourcePaths()=>SourceModels.Where(p=>InventoryReady(ModelRoot(p.Key))).ToDictionary(p=>p.Key,p=>ModelPath(p.Key));
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
    public static void Extract(string archive,string destination,bool wheel=false)=>Extract(archive,destination,wheel,null,default);
    internal static void Extract(string archive,string destination,bool wheel,Func<string,bool>? skip,CancellationToken token)
    {
        using var zip=ZipFile.OpenRead(archive);long total=0;
        foreach(var entry in zip.Entries)
        {
            token.ThrowIfCancellationRequested();
            if(skip?.Invoke(entry.FullName)==true)continue;
            total+=entry.Length;if(total>1024L*1024*1024 || entry.Length>512L*1024*1024 || ((entry.ExternalAttributes>>16)&0xf000)==0xa000)throw new InvalidDataException("Archive exceeds limits");
            string name=entry.FullName;
            if(wheel && name.Contains(".data/"))
            {int index=name.IndexOf(".data/",StringComparison.Ordinal)+6;string suffix=name[index..];if(!suffix.StartsWith("purelib/")&&!suffix.StartsWith("platlib/"))continue;name=suffix[8..];}
            string path=SafePath(destination,name);if(entry.Name.Length==0){Directory.CreateDirectory(path);continue;}
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);entry.ExtractToFile(path,true);
        }
    }
    public async Task PrepareAsync(IProgress<(int Done,int Total)>? progress,CancellationToken token,bool repair=false,string language="en")
    {
        var requested=Requested(language).ToArray();using var lease=Lease();
        using var client=new HttpClient(){Timeout=TimeSpan.FromMinutes(4)};client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 LumaLyrics/1.2.0");
        int done=0,total=Manifest.Components.Count+requested.Length;
        void Report(){progress?.Report((++done,total));}
        if(!Ready||repair)await InstallSet(Manifest.Components,RuntimePath,true);
        else{done=Manifest.Components.Count;progress?.Report((done,total));}
        foreach(var pair in requested)
        {
            token.ThrowIfCancellationRequested();
            if(!InventoryReady(ModelRoot(pair.Key))||repair)await InstallSet([pair.Value],ModelRoot(pair.Key),false);else Report();
        }
        async Task InstallSet(IReadOnlyList<ArgosComponent> components,string destination,bool runtime)
        {
            string staging=Path.Combine(DirectoryPath,"staging-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);
            try
            {
                foreach(var item in components)
                {
                    token.ThrowIfCancellationRequested();string downloads=Path.Combine(DirectoryPath,"downloads");Directory.CreateDirectory(downloads);
                    string archive=Path.Combine(downloads,item.Name);
                    if(!await VerifyComponentAsync(archive,item,token))
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
                        if(!await VerifyComponentAsync(archive,item,token))throw new InvalidDataException("Component hash mismatch");
                    }
                    string target=Path.Combine(staging,item.Target=="site"?"Lib/site-packages":item.Target=="model"?"model":".");
                    Extract(archive,target,item.Target=="site");Report();
                }
                if(runtime)
                {
                    File.WriteAllText(Path.Combine(staging,"python312._pth"),"python312.zip\n.\nLib/site-packages\nimport site\n");
                    // Preserve the signed Microsoft runtime and licenses from the pinned NumPy wheel.
                    string cpp=Directory.GetFiles(Path.Combine(staging,"Lib/site-packages/numpy.libs"),"msvcp140-*.dll").Single();
                    File.Copy(cpp,Path.Combine(staging,"msvcp140.dll"));
                }
                var inventory=Directory.EnumerateFiles(staging,"*",SearchOption.AllDirectories).ToDictionary(f=>Path.GetRelativePath(staging,f).Replace('\\','/'),f=>new FileInfo(f).Length);
                File.WriteAllText(Path.Combine(staging,"installed.json"),JsonSerializer.Serialize(inventory));token.ThrowIfCancellationRequested();
                if(Directory.Exists(destination)){if((File.GetAttributes(destination)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Invalid tools directory");Directory.Delete(destination,true);}
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);Directory.Move(staging,destination);
            }
            finally{if(Directory.Exists(staging))Directory.Delete(staging,true);}
        }
    }
}
