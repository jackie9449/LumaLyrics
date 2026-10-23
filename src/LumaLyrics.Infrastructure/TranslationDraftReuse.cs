using LumaLyrics.Core;
using System.Text.Json;
namespace LumaLyrics.Infrastructure;

public sealed record TranslationDraftReuseResult(int ExactOriginals,int Saved);
public static class TranslationDraftReuse
{
    public static TranslationDraftReuseResult Run(string root,CancellationToken token=default)
    {
        using var lyrics=new LyricsRepository(Path.Combine(root,"lyrics.db"));using var translations=new TranslationRepository(root);var parser=new LrcParser();
        var existing=new Dictionary<string,ChineseTranslation>(StringComparer.Ordinal);
        static (int Index,string Text)[] Required(LyricsDocument doc)=>doc.Groups.Select((g,i)=>(Index:i,Text:g.Text)).Where(x=>TranslationAlignment.NeedsChineseTranslation(doc,x.Text)).ToArray();
        static string Key((int Index,string Text)[] lines)=>JsonSerializer.Serialize(lines.Select(x=>x.Text));
        foreach(var record in translations.ReadAll().Where(record=>record.Translation.Draft && !record.Manual))
        {
            token.ThrowIfCancellationRequested();var binding=lyrics.Find(record.Track);
            if(binding is null || binding.Instrumental || binding.Plain || !binding.Manual&&!LibraryPrefetcher.QualityOf(binding,record.Track))continue;
            var doc=parser.Parse(binding.Raw);if(TranslationAlignment.Hash(doc.Raw)!=record.Translation.SourceHash)continue;
            var required=Required(doc);if(required.Length<3 || required.Any(line=>string.IsNullOrWhiteSpace(record.Translation.TextAt(line.Index))))continue;
            // A complete original sequence is the unit of reuse. A phrase cache cannot
            // establish that a line has the same meaning in another song's context.
            existing.TryAdd(Key(required),record.Translation with{Lines=required.Select((line,index)=>new TranslationLine(index,record.Translation.TextAt(line.Index))).ToArray()});
        }
        int exact=0,saved=0;
        foreach(var item in lyrics.GetLibraryQueue(true))
        {
            token.ThrowIfCancellationRequested();var binding=lyrics.Find(item.Track);
            if(binding is null || binding.Instrumental || binding.Plain || !binding.Manual&&!LibraryPrefetcher.QualityOf(binding,item.Track))continue;
            var doc=parser.Parse(binding.Raw);if(translations.Find(item.Track,doc) is not null)continue;
            var required=Required(doc);if(required.Length<3 || !existing.TryGetValue(Key(required),out var source))continue;exact++;
            var mapped=source with{SourceHash=TranslationAlignment.Hash(doc.Raw),Lines=required.Select((line,index)=>new TranslationLine(line.Index,source.TextAt(index))).ToArray(),Source=source.Source+" · 相同全文复用",Review="AI 译稿 · 完整原词顺序一致 · 沿用原稿语义校对状态；未转移时间戳 · 原稿 "+source.SourceHash,PreparedUtc=DateTimeOffset.UtcNow,Draft=true};
            if(translations.SaveMissing(item.Track,mapped)){translations.MarkDraft(item.Track,mapped.SourceHash);saved++;}
        }
        return new(exact,saved);
    }
}
