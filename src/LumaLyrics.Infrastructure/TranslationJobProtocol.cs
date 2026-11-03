using LumaLyrics.Core;
namespace LumaLyrics.Infrastructure;

public static class TranslationJobProtocol
{
    public static (int TranslatedCount,int RequiredCount) ValidateShape(TranslationJobRequest request,TranslationJobResult result)
    {
        if(result.Format!="LumaLyrics.TranslationJobResult"||result.SchemaVersion!=1||result.JobId!=request.JobId||result.SourceHash!=request.Target.SourceHash||result.DraftEngine is null||result.DraftEngine.Length>600||result.DraftEngine.Any(char.IsControl)||result.Lines is null||result.Lines.Count!=request.Target.Groups.Count||result.Lines.Count is <1 or >20000)throw new InvalidDataException("任务身份、版本或完整行数不一致。");
        int translated=0,required=0;
        for(int i=0;i<result.Lines.Count;i++)
        {
            var expected=request.Target.Groups[i];var line=result.Lines[i];
            if(line is null||line.GroupIndex!=expected.GroupIndex||line.Original!=expected.Original||line.Chinese is null||line.Chinese.Length>4000||line.UntranslatedReason?.Length>1000)throw new InvalidDataException($"第 {i} 组原文、编号或译文无效。");
            if(!expected.NeedsTranslation){if(line.Chinese!=""||line.UntranslatedReason is not null)throw new InvalidDataException($"第 {i} 组不需要翻译。");continue;}
            required++;
            if(string.IsNullOrWhiteSpace(line.Chinese)){if(string.IsNullOrWhiteSpace(line.UntranslatedReason))throw new InvalidDataException($"第 {i} 组未译，需要具体原因。");continue;}
            if(line.UntranslatedReason is not null||!TranslationAlignment.ContainsHan(line.Chinese)&&MatchRules.Normalize(line.Chinese)!=MatchRules.Normalize(line.Original))throw new InvalidDataException($"第 {i} 组不是有效中文译文。");
            translated++;
        }
        if(translated==0)throw new InvalidDataException("没有可显示的中文译文。");return(translated,required);
    }
}
