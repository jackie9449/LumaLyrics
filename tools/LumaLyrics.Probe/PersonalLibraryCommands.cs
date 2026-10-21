using System.Text.Json;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.Probe;

internal static class PersonalLibraryCommands
{
    sealed class ProgressReport:IProgress<string>{public void Report(string value)=>Console.Error.WriteLine(value);}
    public static bool TryRun(string[] args)
    {
        string? operation=args.FirstOrDefault();
        if(operation is not ("--export-personal" or "--pack-personal" or "--restore-personal" or "--validate-personal" or "--use-data"))return false;
        using var cancellation=new CancellationTokenSource();
        ConsoleCancelEventHandler cancel=(_,eventArgs)=>{eventArgs.Cancel=true;cancellation.Cancel();};Console.CancelKeyPress+=cancel;
        try
        {
            int required=operation is "--validate-personal" or "--use-data"?2:3;
            if(args.Length!=required)throw new ArgumentException(operation is "--validate-personal"?"用法：--validate-personal <个人文件夹或 ZIP>":operation is "--use-data"?"用法：--use-data <已有个人数据目录>":$"用法：{operation} <来源> <新的目标路径>");
            var token=cancellation.Token;var progress=new ProgressReport();PersonalLibraryManifest? manifest=null;
            switch(operation)
            {
                case "--export-personal":manifest=PersonalLibraryArchive.Export(args[1],args[2],token,progress);break;
                case "--pack-personal":PersonalLibraryArchive.CreateZip(args[1],args[2],token,progress);manifest=PersonalLibraryArchive.Validate(args[1],token);break;
                case "--restore-personal":manifest=PersonalLibraryArchive.Restore(args[1],args[2],token,progress);break;
                case "--validate-personal":manifest=PersonalLibraryArchive.Validate(args[1],token);break;
                case "--use-data":DataDirectoryConfiguration.SetDefault(args[1]);break;
            }
            Console.WriteLine(JsonSerializer.Serialize(new{operation,path=Path.GetFullPath(args[^1]),summary=manifest?.Summary,warnings=manifest?.Warnings.Count??0,formatVersion=manifest?.Version}));
        }
        catch(OperationCanceledException){Console.Error.WriteLine("已取消；原有资料保持不变。");Environment.ExitCode=130;}
        catch(Exception ex){Console.Error.WriteLine(ex.Message);Environment.ExitCode=1;}
        finally{Console.CancelKeyPress-=cancel;}
        return true;
    }
}
