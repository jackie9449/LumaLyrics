using System.Text.Json;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.Probe;
internal static class TranslationJobCommands
{
    internal static async Task<bool> TryRunAsync(string[] args)
    {
        if(args.FirstOrDefault() is not ("--validate-translation-job-result" or "--import-translation-job-result"))
        {if(args.FirstOrDefault()?.StartsWith("--validate-translation-job",StringComparison.Ordinal)==true||args.FirstOrDefault()?.StartsWith("--import-translation-job",StringComparison.Ordinal)==true){Console.Error.WriteLine("未知的译文任务命令。");Environment.ExitCode=2;return true;}return false;}
        try
        {
            TranslationJobCommandLine.TryParse(args,out var command);
            if(command!.Kind==TranslationJobCommandKind.Validate){var v=TranslationJobExchange.Validate(command.Root,command.File);Console.WriteLine(JsonSerializer.Serialize(new{v.Request.JobId,v.TranslatedCount,v.RequiredCount}));}
            else{var receipt=await TranslationJobExchange.ImportAsync(command.Root,command.File);Console.WriteLine(JsonSerializer.Serialize(receipt));if(receipt.State is not (TranslationJobState.Applied or TranslationJobState.AppliedPartial))Environment.ExitCode=3;}
        }
        catch(Exception e) when(e is ArgumentException or InvalidDataException or JsonException){Console.Error.WriteLine(e.Message);Environment.ExitCode=2;}
        catch(Exception e) when(e is IOException or Microsoft.Data.Sqlite.SqliteException){Console.Error.WriteLine(e.Message);Environment.ExitCode=4;}
        return true;
    }
}
