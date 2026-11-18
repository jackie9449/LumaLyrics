using System.IO;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;

namespace LumaLyrics.App;

public sealed partial class PlaybackCoordinator
{
    Task<IReadOnlyList<LyricsCandidate>> ReadManualCandidatesAsync(Track track,CancellationToken token)
        =>manualProvider is IManualLyricsSearch search?search.SearchManualAsync(track,token):manualProvider.LookupAsync(track,token);

    static string ManualCandidateStatus(IReadOnlyList<LyricsCandidate> results)
    {
        int synced=results.Count(c=>!c.Instrumental && !string.IsNullOrWhiteSpace(c.SyncedLyrics));
        int plain=results.Count(c=>!c.Instrumental && string.IsNullOrWhiteSpace(c.SyncedLyrics));
        string status=results.Count==0?"未找到候选，可只填歌名或简化歌手名重试。"
            :$"找到 {results.Count} 个候选：同步 {synced} · 静态 {plain} · 纯音乐 {results.Count(c=>c.Instrumental)}。请选择录音版本。";
        if(results is LyricsLookupResult{UnavailableSources.Count:>0} partial)
            status+=" 部分来源暂不可用："+string.Join("、",partial.UnavailableSources)+"。可稍后重试。";
        return status;
    }

    internal async Task<(IReadOnlyList<LyricsCandidate> Candidates,string Status)> SearchLibraryCandidatesAsync(Track query,CancellationToken cancellationToken)
    {
        if(!Settings.OnlineLookup)return ([],"请先在设置中开启联网查词。");
        using var request=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,lifetime.Token);
        try
        {
            var candidates=await ReadManualCandidatesAsync(query,request.Token);
            request.Token.ThrowIfCancellationRequested();
            return Settings.OnlineLookup?(candidates,ManualCandidateStatus(candidates)):([],"请先在设置中开启联网查词。");
        }
        catch(OperationCanceledException){return ([],"");}
        catch(Exception e) when(e is LyricsProviderException or System.Net.Http.HttpRequestException or IOException or InvalidDataException)
        {
            return ([],e.Message);
        }
    }

    internal void SelectLibraryCandidate(LibraryPreparationTarget target,LyricsCandidate candidate)
    {
        var binding=repository.Save(target.Track,candidate.Content,candidate.SourceLabel,true,candidate.Instrumental,
            plain:!candidate.Instrumental && string.IsNullOrWhiteSpace(candidate.SyncedLyrics),
            providerJson:candidate.RawJson,expectedLibrary:Member(target),expectedBindingId:target.BindingId);
        WorkspaceResultsRevision++;
        InvalidateWorkspace();
        var latest=Source.Latest;
        if(Snapshot is {} current && (latest is null || latest.SessionId==current.SessionId && latest.Generation==current.Generation
            && latest.Revision==current.Revision && SameRecording(latest.Track,target.Track))
            && SameRecording(current.Track,target.Track))
        {
            lookup?.Cancel();
            requests.Next();
            foregroundLookupActive=false;
            Apply(binding);
        }
        else Updated?.Invoke();
    }

    internal async Task ConfirmLibraryManualTargetAsync(LibraryPreparationTarget target,CancellationToken token)
    {
        var member=Member(target);
        if(repository.IsCurrentLibraryMember(member))return;
        await SynchronizeWorkspaceAsync(token);
        if(!repository.IsCurrentLibraryMember(member))throw new InvalidDataException("目标不在已确认资料库，请先刷新来源。");
    }

    static bool SameRecording(Track left,Track right)
    {
        left=TrackMetadata.FromAppleSession(left);
        right=TrackMetadata.FromAppleSession(right);
        return left.Fingerprint==right.Fingerprint && left.DurationMs is >0 && right.DurationMs is >0
            && Math.Abs(left.DurationMs.Value-right.DurationMs.Value)<=1000;
    }
}
