using System.Text.Json;
using LumaLyrics.Core;
namespace LumaLyrics.Infrastructure;

public sealed record TranslationCacheReconciliationResult(int Candidates,int CheckedOriginals,int Saved,int Unaligned);
public static class TranslationCacheReconciliation
{
    public static TranslationCacheReconciliationResult Run(string root,CancellationToken token=default)
    {
        string folder=Path.Combine(root,"cache","translations");if(!Directory.Exists(folder))return new(0,0,0,0);
        using var lyrics=new LyricsRepository(Path.Combine(root,"lyrics.db"));using var translations=new TranslationRepository(root);var parser=new LrcParser();
        var library=lyrics.GetLibraryQueue(true).GroupBy(t=>MatchRules.Normalize(t.Track.Title),StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.ToArray(),StringComparer.Ordinal);
        int candidates=0,checkedOriginals=0,saved=0,unaligned=0;
        foreach(string file in Directory.EnumerateFiles(folder,"*.json"))
        {
            token.ThrowIfCancellationRequested();if(!long.TryParse(Path.GetFileNameWithoutExtension(file),out _) || new FileInfo(file).Length>2*1024*1024)continue;
            TranslationCandidate? candidate;try{candidate=JsonSerializer.Deserialize<TranslationCandidate>(File.ReadAllText(file));}catch(JsonException){continue;}
            if(candidate?.Track is null || candidate.Instrumental || string.IsNullOrWhiteSpace(candidate.Original) || string.IsNullOrWhiteSpace(candidate.Translated))continue;candidates++;
            foreach(var item in library.GetValueOrDefault(MatchRules.Normalize(candidate.Track.Title))??[])
            {
                if(!NetEaseTranslationProvider.RecordingMatches(item.Track,candidate.Track))continue;var bound=lyrics.Find(item.Track);
                if(bound is null || bound.Instrumental || bound.Plain || !bound.Manual&&!LibraryPrefetcher.QualityOf(bound,item.Track))continue;
                var document=parser.Parse(bound.Raw);if(translations.Find(item.Track,document) is not null || !document.Groups.Any(g=>TranslationAlignment.NeedsChineseTranslation(document,g.Text)))continue;checkedOriginals++;
                if(!AutomaticLyricsMatcher.Assess(candidate.OriginalCandidate).Accepted){unaligned++;continue;}
                var aligned=TranslationAlignment.Align(document,parser.Parse(candidate.Original),parser.Parse(candidate.Translated),$"网易云音乐社区译文 #{candidate.Id}",candidate.SourceUrl);
                if(aligned is null){unaligned++;continue;}
                if(translations.SaveMissing(item.Track,aligned)){translations.MarkPrepared(item.Track,aligned);saved++;}
            }
        }
        return new(candidates,checkedOriginals,saved,unaligned);
    }
}
