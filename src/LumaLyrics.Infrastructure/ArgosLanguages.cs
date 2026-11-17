using System.Text;
namespace LumaLyrics.Infrastructure;

public static class ArgosLanguages
{
    public static IReadOnlyList<string> Supported {get;}=Array.AsReadOnly(new[]{"en","fr","de","es","ja","ko"});
    internal const string MachineUrlsSql="('local:argos/en-zh','local:argos/fr-en-zh','local:argos/de-en-zh','local:argos/es-en-zh','local:argos/ja-en-zh','local:argos/ko-en-zh')";
    public static bool IsSelection(string? code)=>code=="auto" || code is not null&&Supported.Contains(code);
    public static string SourceUrl(string code)=>Supported.Contains(code)?"local:argos/"+code+(code=="en"?"-zh":"-en-zh"):throw new InvalidDataException("Unsupported source language");
    public static bool IsMachineUrl(string url)=>Supported.Any(code=>SourceUrl(code)==url);
    public static string Name(string code)=>code switch{"auto"=>"自动识别","en"=>"英文","fr"=>"法文","de"=>"德文","es"=>"西班牙文","ja"=>"日文","ko"=>"韩文",_=>throw new InvalidDataException("Unsupported source language")};
    public static string ProbeText(string code)=>code switch{"en"=>"The wind crosses the valley.","fr"=>"Le vent traverse la vallée.","de"=>"Der Wind zieht durch das Tal.","es"=>"El viento cruza el valle.","ja"=>"風が谷を渡る。","ko"=>"바람이 계곡을 건넌다.",_=>throw new InvalidDataException("Unsupported source language")};
    static bool Kana(char c)=>c is >= '\u3040' and <= '\u30ff' or >= '\u31f0' and <= '\u31ff' or '\u3005' or '\u3006' or '\u303b' or >= '\u3031' and <= '\u3035';
    static bool Hangul(char c)=>c is >= '\uac00' and <= '\ud7ff' or >= '\u1100' and <= '\u11ff' or >= '\ua960' and <= '\ua97f';
    static bool Han(char c)=>c is >= '\u3400' and <= '\u9fff';
    static bool Latin(char c)=>c is <= '\u024f' or >= '\u1e00' and <= '\u1eff';
    public static string Resolve(string text,string selection,Func<string,string?>? detector)
    {
        if(!IsSelection(selection))throw new InvalidDataException("Unsupported source language");
        try{text=text.Normalize(NormalizationForm.FormKC);}catch(ArgumentException){throw new InvalidDataException("Invalid source language text");}
        bool kana=text.Any(Kana),hangul=text.Any(Hangul);
        string? source=selection=="auto"?null:selection;
        if(source is null)
        {
            if(kana&&hangul)throw new InvalidDataException("Uncertain source language");
            source=kana?"ja":hangul?"ko":detector?.Invoke(text)?.Trim().ToLowerInvariant().Split('-','_')[0];
            // Preserve the legacy English-only API when no detector was supplied.
            if(source is null&&detector is null&&OfflineTranslationWorkflow.EnglishCompatible(text))source="en";
        }
        if(source is null||!Supported.Contains(source))throw new InvalidDataException("Uncertain or unsupported source language");
        bool compatible=text.Any(char.IsLetter)&&text.Where(char.IsLetter).All(c=>Latin(c)||source=="ja"&&(Kana(c)||Han(c))||source=="ko"&&(Hangul(c)||Han(c)));
        if(!compatible)throw new InvalidDataException("Incompatible source language script");
        return source;
    }
}
