namespace LumaLyrics.Infrastructure;
public enum TranslationJobCommandKind {Validate,Import}
public sealed record TranslationJobCommand(TranslationJobCommandKind Kind,string Root,string File);
public static class TranslationJobCommandLine
{
    public static bool TryParse(string[] args,out TranslationJobCommand? command)
    {
        command=null;if(args.FirstOrDefault() is not ("--validate-translation-job-result" or "--import-translation-job-result"))return false;
        if(args.Length!=3)throw new ArgumentException("需要明确的个人目录和结果文件路径。");
        command=new(args[0]=="--validate-translation-job-result"?TranslationJobCommandKind.Validate:TranslationJobCommandKind.Import,Path.GetFullPath(args[1]),Path.GetFullPath(args[2]));return true;
    }
}
