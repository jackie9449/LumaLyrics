using System.Diagnostics;
using System.Text;
using System.Text.Json;
namespace LumaLyrics.Infrastructure;

public interface IOfflineTranslationEngine:IAsyncDisposable
{
    Task<IReadOnlyList<string>> TranslateAsync(IReadOnlyList<string> texts,string sourceLanguage,CancellationToken token);
    Task<IReadOnlyList<string>> TranslateAsync(IReadOnlyList<string> texts,CancellationToken token)=>TranslateAsync(texts,"en",token);
}
public sealed class ArgosEngine:IOfflineTranslationEngine
{
    readonly ArgosInstallation installation;Process? worker;FileStream? lease;Task<string>? errors;HashSet<string> preparedSources=[];
    public ArgosEngine(ArgosInstallation installation)=>this.installation=installation;
    public Task<IReadOnlyList<string>> TranslateAsync(IReadOnlyList<string> texts,CancellationToken token)=>TranslateAsync(texts,"en",token);
    public async Task<IReadOnlyList<string>> TranslateAsync(IReadOnlyList<string> texts,string sourceLanguage,CancellationToken token)
    {
        if(!ArgosLanguages.Supported.Contains(sourceLanguage))throw new InvalidDataException("Unsupported source language");
        if(worker is null)
        {
            if(!installation.ReadyFor(sourceLanguage))throw new InvalidDataException("Missing source language model");lease=installation.Lease();
            string script=Path.Combine(installation.DirectoryPath,"argos-worker-v2.py");File.WriteAllText(script,ArgosInstallation.Resource("argos-worker.py"),new UTF8Encoding(false));
            var info=new ProcessStartInfo(Path.Combine(installation.RuntimePath,"python.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardInputEncoding=new UTF8Encoding(false),StandardOutputEncoding=Encoding.UTF8};
            info.ArgumentList.Add("-X");info.ArgumentList.Add("utf8");info.ArgumentList.Add("-I");info.ArgumentList.Add("-u");info.ArgumentList.Add(script);info.ArgumentList.Add(Path.Combine(installation.RuntimePath,"model"));
            var paths=installation.InstalledSourcePaths();preparedSources=paths.Keys.Append("en").ToHashSet();info.ArgumentList.Add(JsonSerializer.Serialize(paths));
            info.Environment["PYTHONIOENCODING"]="utf-8";info.Environment["XDG_DATA_HOME"]=installation.DirectoryPath;info.Environment["XDG_CONFIG_HOME"]=installation.DirectoryPath;
            worker=Process.Start(info)??throw new IOException("Cannot start offline engine");errors=worker.StandardError.ReadToEndAsync();
            try{worker.PriorityClass=ProcessPriorityClass.BelowNormal;}catch(System.ComponentModel.Win32Exception){}
        }
        if(!preparedSources.Contains(sourceLanguage))throw new InvalidDataException("Missing source language model");
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            await worker.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new{Texts=texts,SourceLanguage=sourceLanguage}).AsMemory(),timeout.Token);await worker.StandardInput.FlushAsync(timeout.Token);
            string? line=await worker.StandardOutput.ReadLineAsync(timeout.Token);
            if(line is null||line.Length>1024*1024)throw new InvalidDataException("Offline engine failed");
            using var json=JsonDocument.Parse(line);if(!json.RootElement.TryGetProperty("Texts",out var results))throw new InvalidDataException("Offline translation failed or group exceeds model limit");
            var values=results.Deserialize<string[]>()!;if(values.Length!=texts.Count||values.Any(s=>string.IsNullOrWhiteSpace(s)||s.Length>4000))throw new InvalidDataException("Invalid offline results");return values;
        }
        catch{await ResetAsync();throw;}
    }
    async Task ResetAsync()
    {
        var process=worker;worker=null;
        try{if(process is not null){try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}await process.WaitForExitAsync();if(errors is not null)await errors;}}
        finally{errors=null;preparedSources.Clear();process?.Dispose();lease?.Dispose();lease=null;}
    }
    public ValueTask DisposeAsync()=>new(ResetAsync());
}
