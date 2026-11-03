using LumaLyrics.Core;
namespace LumaLyrics.Infrastructure;
public enum TranslationLookupState {Found,NotFound,Mismatch,Offline,TransientError,MissingOriginal,NotRequired}
public sealed record TranslationLookupResult(Track Track,TranslationLookupState State,string Message);
public sealed record TranslationWorkflowResult(TranslationLookupResult Lookup,TranslationJobTicket? Ticket);
public sealed class TranslationWorkflow(string root,ITranslationProvider provider)
{
    public async Task<TranslationWorkflowResult> PrepareAsync(Track track,bool onlineLookup,bool createTaskIfMissing,CancellationToken token)
    {
        using var lyrics=new LyricsRepository(Path.Combine(root,"lyrics.db"),true);using var translations=new TranslationRepository(root);
        var binding=lyrics.FindReadOnly(track);if(binding is null||binding.Plain||binding.Instrumental||!binding.Manual&&!LibraryPrefetcher.QualityOf(binding,track))return new(new(track,TranslationLookupState.MissingOriginal,"没有可靠动态原词。"),null);
        var doc=new LrcParser().Parse(binding.Raw);if(translations.Find(track,doc) is not null)return new(new(track,TranslationLookupState.Found,"沿用已有译文。"),null);
        if(!doc.Groups.Any(g=>TranslationAlignment.NeedsChineseTranslation(doc,g.Text))||TranslationAlignment.IsChinese(string.Join("\n",doc.Groups.Select(g=>g.Text))))return new(new(track,TranslationLookupState.NotRequired,"无需翻译。"),null);
        var state=onlineLookup?TranslationLookupState.NotFound:TranslationLookupState.Offline;string message=onlineLookup?"来源暂无可用译文。":"离线制作任务。";
        if(onlineLookup)
        {
            try
            {
                var candidates=await provider.LookupAsync(track,token);token.ThrowIfCancellationRequested();
                foreach(var candidate in candidates)
                {
                    if(!NetEaseTranslationProvider.RecordingMatches(track,candidate.Track)||!AutomaticLyricsMatcher.Assess(candidate.OriginalCandidate).Accepted)continue;
                    var parser=new LrcParser();var aligned=TranslationAlignment.Align(doc,parser.Parse(candidate.Original),parser.Parse(candidate.Translated),"网易云音乐社区译文 #"+candidate.Id,candidate.SourceUrl);
                    if(aligned is null){state=TranslationLookupState.Mismatch;continue;}
                    token.ThrowIfCancellationRequested();var fresh=lyrics.FindReadOnly(track);if(fresh?.Id!=binding.Id||TranslationAlignment.Hash(fresh.Raw)!=TranslationAlignment.Hash(doc.Raw))throw new InvalidDataException("原词已变化，请重新准备。");
                    translations.SaveMissing(track,aligned);return new(new(track,TranslationLookupState.Found,"已有来源译文可用。"),null);
                }
            }
            catch(LyricsProviderException){state=TranslationLookupState.TransientError;message="来源暂不可用；可以制作本地任务。";}
        }
        token.ThrowIfCancellationRequested();TranslationJobTicket? ticket=null;
        if(createTaskIfMissing)try{ticket=TranslationJobExchange.Create(root,track,TranslationJobMode.FillMissing);}catch(InvalidDataException){if(translations.Find(track,doc) is null)throw;}
        return new(new(track,state,message),ticket);
    }
}
