using System.Text;
using System.Text.Json;
using LumaLyrics.Core;
namespace LumaLyrics.Infrastructure;

public sealed record TranslationDraftLine(int GroupIndex,string Original,string? Chinese=null,string? UntranslatedReason=null);
public sealed record TranslationDraftSource(string Id,Track Track,string SourceHash,string OriginalSource,IReadOnlyList<TranslationDraftLine> Lines,string? DraftEngine=null);

public static class TranslationDraftExchange
{
    static readonly JsonSerializerOptions Json=new(){WriteIndented=true};
    public static IReadOnlyList<TranslationDraftSource> Sources(string root)
    {
        using var lyrics=new LyricsRepository(Path.Combine(root,"lyrics.db"));using var translations=new TranslationRepository(root);
        var result=new List<TranslationDraftSource>();var seen=new HashSet<string>();var parser=new LrcParser();
        foreach(var item in lyrics.GetLibraryQueue(includeDeferred:true))
        {
            var binding=lyrics.Find(item.Track);if(binding is null || binding.Instrumental || binding.Plain || !binding.Manual&&!LibraryPrefetcher.QualityOf(binding,item.Track))continue;
            var document=parser.Parse(binding.Raw);if(!document.IsSynced || translations.Find(item.Track,document) is not null)continue;
            var lines=document.Groups.Select((g,i)=>new TranslationDraftLine(i,g.Text)).Where(l=>TranslationAlignment.NeedsChineseTranslation(document,l.Original)).ToArray();
            if(lines.Length<1 || TranslationAlignment.IsChinese(string.Join("\n",lines.Select(l=>l.Original))))continue;
            string hash=TranslationAlignment.Hash(document.Raw),id=TranslationAlignment.Hash(item.Track.Fingerprint+"\0"+hash);if(!seen.Add(id))continue;
            result.Add(new(id,item.Track,hash,binding.Source,lines));
        }
        return result;
    }
    public static int Export(string root,string destination)
    {
        PersonalLibraryArchive.EnsureOutsideSoftware(destination);if(File.Exists(destination))throw new IOException("目标已存在，请使用新的译稿文件名。");
        var sources=Sources(root);File.WriteAllText(destination,JsonSerializer.Serialize(sources,Json),new UTF8Encoding(false));return sources.Count;
    }
    public static int Import(string root,string source)
    {
        if(new FileInfo(source).Length>32*1024*1024)throw new InvalidDataException("译稿文件超过 32 MB。");
        var records=JsonSerializer.Deserialize<List<TranslationDraftSource>>(File.ReadAllText(source),Json)??throw new InvalidDataException("译稿文件为空。");
        using var lyrics=new LyricsRepository(Path.Combine(root,"lyrics.db"));using var translations=new TranslationRepository(root);var parser=new LrcParser();
        var checkedRecords=new List<(Track Track,ChineseTranslation Translation)>();
        foreach(var record in records)
        {
            if(record is null || record.Track is null || record.Lines is null || record.Lines.Count is <1 or >20000)throw new InvalidDataException("AI 译稿记录无效。");
            var binding=lyrics.Find(record.Track);if(binding is null || binding.Instrumental || binding.Plain || !binding.Manual&&!LibraryPrefetcher.QualityOf(binding,record.Track))throw new InvalidDataException("AI 译稿没有可确认的动态原词。");
            var document=parser.Parse(binding.Raw);if(TranslationAlignment.Hash(document.Raw)!=record.SourceHash)throw new InvalidDataException("AI 译稿对应的原词已变化，未导入。");
            if(translations.Find(record.Track,document) is not null)continue;
            var expected=document.Groups.Select((g,i)=>(g,i)).Where(x=>TranslationAlignment.NeedsChineseTranslation(document,x.g.Text)).ToArray();
            if(expected.Length!=record.Lines.Count || record.Id!=TranslationAlignment.Hash(record.Track.Fingerprint+"\0"+record.SourceHash))throw new InvalidDataException("AI 译稿的原词或完整行数不一致。");
            var lines=new List<TranslationLine>();
            for(int i=0;i<expected.Length;i++)
            {
                var line=record.Lines[i];if(line is null || line.GroupIndex!=expected[i].i || line.Original!=expected[i].g.Text || line.Chinese is null || line.Chinese.Length>4000)throw new InvalidDataException("AI 译稿的逐行对应或中文内容不完整。");
                if(string.IsNullOrWhiteSpace(line.Chinese))
                {if(string.IsNullOrWhiteSpace(line.UntranslatedReason) || line.UntranslatedReason.Length>1000)throw new InvalidDataException("未译行需要明确记录原因，不能把缺失内容当成完整译稿。");continue;}
                if(!TranslationAlignment.ContainsHan(line.Chinese) && MatchRules.Normalize(line.Chinese)!=MatchRules.Normalize(line.Original))throw new InvalidDataException("AI 译稿存在未确认的中文行。");
                lines.Add(new(line.GroupIndex,line.Chinese));
            }
            string engine=record.DraftEngine is {Length:>0 and <600} && !record.DraftEngine.Any(char.IsControl)?" · "+record.DraftEngine:"";
            if(lines.Count==0)throw new InvalidDataException("译稿尚无可显示的中文行，未导入。");
            var translation=new ChineseTranslation(record.SourceHash,lines,"AI 中文译稿（待校对）"+engine,"local:ai",$"AI 译稿 · 待校对 · {lines.Count}/{expected.Length} 行 · 已核对原文哈希与逐行编号"+(lines.Count<expected.Length?" · 未译行留空":""),DateTimeOffset.UtcNow,true);TranslationRepository.Validate(translation);checkedRecords.Add((record.Track,translation));
        }
        foreach(var (track,translation) in checkedRecords){translations.Save(track,translation);translations.MarkDraft(track,translation.SourceHash);}
        return checkedRecords.Count;
    }
}
