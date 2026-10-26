namespace LumaLyrics.Core;

public sealed record SeekResult(bool Confirmed,string Message);

public static class PlaybackSeeking
{
    public static bool Available(PlaybackSnapshot? snapshot,bool nativeSeek=false)=>snapshot is {Status:PlayState.Playing or PlayState.Paused,Track.DurationMs:>0} && (snapshot.CanSeek || nativeSeek) && double.IsFinite(snapshot.Track.DurationMs.Value);
    public static bool SameRecording(PlaybackSnapshot expected,PlaybackSnapshot? actual)=>actual is not null && expected.SessionId==actual.SessionId && expected.Generation==actual.Generation && expected.Revision==actual.Revision && expected.Track.Fingerprint==actual.Track.Fingerprint;
    public static bool MatchesNativeLabels(Track expected,string title,string details)
    {
        if(string.IsNullOrWhiteSpace(expected.Title) || string.IsNullOrWhiteSpace(expected.Artist) || MatchRules.Normalize(title)!=MatchRules.Normalize(expected.Title))return false;
        // WinUI's scrolling detail exposes artist + album, sometimes repeated for its
        // scrolling copies. Accept only an exact complete label or its exact double.
        string label=MatchRules.Normalize(expected.Artist+expected.Album),actual=MatchRules.Normalize(details);
        return actual==label || actual==label+label;
    }
    public static double? Target(PlaybackSnapshot? snapshot,double requested,bool nativeSeek=false)=>Available(snapshot,nativeSeek) && double.IsFinite(requested)?Math.Clamp(requested,0,snapshot!.Track.DurationMs!.Value):null;
    public static bool Confirmed(PlaybackSnapshot expected,PlaybackSnapshot? actual,double target,DateTimeOffset issued)
    {
        if(!SameRecording(expected,actual) || actual!.PositionMs is not { } position || !double.IsFinite(position) || actual.Quality==PositionQuality.Unavailable || actual.CapturedUtc<issued || actual.TimelineUtc is not { } timeline || timeline<issued.AddMilliseconds(-500) || timeline<=expected.TimelineUtc)return false;
        double elapsed=actual.Status==PlayState.Playing?Math.Max(0,(actual.CapturedUtc-issued).TotalMilliseconds)*(actual.Rate??1):0;
        return position>=target-2500 && position<=target+elapsed+2500;
    }
    public static async Task<SeekResult> ExecuteAsync(PlaybackSnapshot expected,double requested,Func<CancellationToken,Task<PlaybackSnapshot?>> read,Func<PlaybackSnapshot,double,CancellationToken,Task<bool>> command,CancellationToken token,bool nativeSeek=false)
    {
        if(Target(expected,requested,nativeSeek) is not { } target)return new(false,"当前播放器暂不支持进度跳转。");
        var before=await read(token);token.ThrowIfCancellationRequested();
        if(!SameRecording(expected,before) || !Available(before,nativeSeek))return new(false,"歌曲或播放会话已变化，请重新选择进度。");
        target=Math.Clamp(target,0,before!.Track.DurationMs!.Value);
        var issued=DateTimeOffset.UtcNow;
        token.ThrowIfCancellationRequested();
        if(!await command(before,target,token))return new(false,"播放器未接受进度跳转。");
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            while(true)
            {
                var actual=await read(timeout.Token);timeout.Token.ThrowIfCancellationRequested();
                if(!SameRecording(expected,actual))return new(false,"歌曲已切换，进度已跟随实际播放。");
                if(Confirmed(before,actual,target,issued))return new(true,"已跳转");
                await Task.Delay(150,timeout.Token);
            }
        }
        catch(OperationCanceledException) when(!token.IsCancellationRequested){return new(false,"跳转请求已发送，尚未确认新位置；进度继续跟随播放器。");}
    }
}
