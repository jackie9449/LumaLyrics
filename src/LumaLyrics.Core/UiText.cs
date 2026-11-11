using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LumaLyrics.Core;

/// <summary>Application copy only. Never pass lyrics, song metadata or editable user text to this API.</summary>
public static class UiText
{
    static readonly IReadOnlyDictionary<string,IReadOnlyDictionary<string,string>> catalogs=LoadCatalogs();
    static readonly ConcurrentDictionary<string,string> statusCache=new(StringComparer.Ordinal);
    static readonly ConcurrentDictionary<string,byte> missing=new(StringComparer.Ordinal);
    static (Regex Pattern,string Target,int Count,int LiteralLength,bool NestedStatus)[] statusPatterns=[];
    public static string Language {get;private set;}="zh-Hans";
    public static CultureInfo Culture=>CultureInfo.GetCultureInfo(Language switch{"zh-Hant"=>"zh-TW","en"=>"en-US","ja"=>"ja-JP",_=>"zh-CN"});
    public static string Normalize(string? language)=>language is "zh-Hant" or "en" or "ja"?language:"zh-Hans";
    public static IReadOnlyCollection<string> MissingKeys=>missing.Keys.ToArray();
    public static IReadOnlyDictionary<string,IReadOnlyDictionary<string,string>> Catalogs=>catalogs;

    public static void SetLanguage(string? language)
    {
        Language=Normalize(language);statusCache.Clear();missing.Clear();
        CultureInfo.CurrentUICulture=Culture;CultureInfo.DefaultThreadCurrentUICulture=Culture;
        statusPatterns=Language=="zh-Hans"?[]:Catalog().Where(p=>p.Key.Contains('{')).Select(p=>Pattern(p.Key,p.Value)).Where(p=>p.HasValue).Select(p=>p!.Value).OrderByDescending(p=>p.LiteralLength).ToArray();
    }

    public static string T(string source)
    {
        if(Language=="zh-Hans" || source.Length==0)return source;
        if(Catalog().TryGetValue(source,out var text))return text;
        missing.TryAdd(source,0);return source;
    }

    public static string F(FormattableString source)=>string.Format(Culture,T(source.Format),source.GetArguments());

    /// <summary>Projects canonical application status messages without changing stored records or their arguments.</summary>
    public static string Status(string? source)=>StatusCore(source,0);
    static string StatusCore(string? source,int depth)
    {
        if(string.IsNullOrEmpty(source) || Language=="zh-Hans")return source??"";
        if(Catalog().TryGetValue(source,out var exact))return exact;
        if(source.Contains('\n'))return string.Join('\n',source.Split('\n').Select(line=>StatusCore(line,depth)));
        if(statusCache.TryGetValue(source,out var cached))return cached;
        string result=source;
        foreach(var item in statusPatterns)
        {
            var match=item.Pattern.Match(source);
            if(!match.Success)continue;
            var args=Enumerable.Range(0,item.Count).Select(i=>{string value=match.Groups["v"+i].Value;return (object)(item.NestedStatus && depth<4?StatusCore(value,depth+1):value);}).ToArray();
            result=string.Format(Culture,item.Target,args);break;
        }
        if(statusCache.Count>2048)statusCache.Clear();statusCache.TryAdd(source,result);return result;
    }

    static IReadOnlyDictionary<string,string> Catalog()=>catalogs.TryGetValue(Language,out var values)?values:Empty;
    static readonly IReadOnlyDictionary<string,string> Empty=new Dictionary<string,string>();
    static IReadOnlyDictionary<string,IReadOnlyDictionary<string,string>> LoadCatalogs()
    {
        var result=new Dictionary<string,IReadOnlyDictionary<string,string>>();
        var assembly=typeof(UiText).Assembly;
        foreach(string language in new[]{"zh-Hant","en","ja"})
        {
            var strings=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(string name in assembly.GetManifestResourceNames().Where(n=>n.Contains(".Localization.") && n.EndsWith("."+language+".json",StringComparison.Ordinal)).Order(StringComparer.Ordinal))
            {
                using var stream=assembly.GetManifestResourceStream(name)!;
                foreach(var pair in JsonSerializer.Deserialize<Dictionary<string,string>>(stream)!)
                {
                    if(Placeholders(pair.Key).SequenceEqual(Placeholders(pair.Value)) is false)throw new InvalidDataException("Localization placeholders differ: "+name+" / "+pair.Key);
                    // Shared wording may occur in more than one feature catalog.
                    strings.TryAdd(pair.Key,pair.Value);
                }
            }
            result[language]=strings;
        }
        return result;
    }

    public static string[] Placeholders(string text)=>Regex.Matches(text,@"(?<!\{)\{(\d+)(?:,[^}:]+)?(?::[^}]+)?\}(?!\})").Select(m=>m.Groups[1].Value).Distinct().Order(StringComparer.Ordinal).ToArray();
    static (Regex Pattern,string Target,int Count,int LiteralLength,bool NestedStatus)? Pattern(string source,string target)
    {
        var placeholders=Regex.Matches(source,@"(?<!\{)\{(\d+)(?:,[^}:]+)?(?::[^}]+)?\}(?!\})");
        if(placeholders.Count==0)return null;
        var expression=new StringBuilder(@"\A");int start=0,count=0,literal=0;
        foreach(Match placeholder in placeholders)
        {
            string prefix=source[start..placeholder.Index].Replace("{{","{").Replace("}}","}");literal+=prefix.Length;expression.Append(Regex.Escape(prefix));
            int index=int.Parse(placeholder.Groups[1].Value,CultureInfo.InvariantCulture);count=Math.Max(count,index+1);
            expression.Append($"(?<v{index}>[\\s\\S]*?)");start=placeholder.Index+placeholder.Length;
        }
        string suffix=source[start..].Replace("{{","{").Replace("}}","}");literal+=suffix.Length;expression.Append(Regex.Escape(suffix)).Append(@"\z");
        if(literal<2 || !source.Any(c=>c is >= '\u3400' and <= '\u9fff'))return null;
        bool nested=source is "资料库扫描失败：{0}" or "封面扫描失败：{0}" or "译文准备中断：{0}" or "准备中断：{0}" or "准备中止：{0}";
        return(new Regex(expression.ToString(),RegexOptions.CultureInvariant|RegexOptions.NonBacktracking,TimeSpan.FromMilliseconds(50)),target,count,literal,nested);
    }
}
