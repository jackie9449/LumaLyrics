using System.Diagnostics;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;

namespace LumaLyrics.App;

public partial class MainWindow
{
    async Task VerifyOriginalLyricsAsync(string directory,Dictionary<string,object?> checks)
    {
        string[] lines=["The morning light is resting on the window", "Our quiet footsteps follow winding rivers", "We keep a little lantern for the journey", "And every road will bring us home together"];
        string xml="<tt xmlns=\"http://www.w3.org/ns/ttml\"><body><div>"+string.Concat(lines.Select((s,i)=>$"<p begin=\"{i*30}s\" end=\"{i*30+10}s\">{s}</p>"))+"</div></body></tt>";
        var document=AppleTimedLyricsParser.Parse(xml,140000);
        var track=new Track("原创原词采集示例","浮光工作室","原创验证",140000);
        var expected=new PlaybackSnapshot("AppleMusic.fixture",1,1,track,PlayState.Playing,1,0,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,Stopwatch.GetTimestamp(),PositionQuality.Reliable);
        PlaybackSnapshot? latest=expected;int reads=0,scans=0;bool unavailable=false,restarted=false,changedText=false,ambiguous=false;
        TaskCompletionSource<IReadOnlyList<AppleTimedLyrics>>? delayed=null;
        Task<AppleOriginalAnchors?> Anchors(Track t,CancellationToken token)
        {
            reads++;return Task.FromResult<AppleOriginalAnchors?>(unavailable?null:new(restarted && reads%2==0?2:1,10,changedText && reads%2==0?lines.Reverse().ToArray():lines));
        }
        Task<IReadOnlyList<AppleTimedLyrics>> Scan(AppleOriginalAnchors identity,double duration,CancellationToken token)
        {
            scans++;return delayed?.Task??Task.FromResult<IReadOnlyList<AppleTimedLyrics>>(ambiguous?[document,AppleTimedLyricsParser.Parse(xml.Replace("0s\"","1s\""),140000)]:[document]);
        }
        var reader=new AppleOriginalLyricsReader(Anchors,Scan);
        var result=await reader.ReadAsync(expected,()=>latest,CancellationToken.None);
        checks["appleOriginal.checkedNativeTtmlAccepted"]=result?.Lyrics.Lrc==document.Lrc && reads==2 && scans==1;
        checks["appleOriginal.rawSourceAndHashRetainedWithoutSemanticClaim"]=result is not null && AppleOriginalProvenance.Validate(result.Provenance,result.Lyrics.Lrc) is {SemanticReview:false,Ttml:var original} && original==xml;
        unavailable=true;int before=scans;checks["appleOriginal.closedOrUnavailableLyricsNeverScanMemory"]=await reader.ReadAsync(expected,()=>latest,CancellationToken.None) is null && scans==before;unavailable=false;
        checks["appleOriginal.missingDurationDoesNotAcquire"]=await reader.ReadAsync(expected with{Track=track with{DurationMs=null}},()=>latest,CancellationToken.None) is null;
        checks["appleOriginal.otherSessionDoesNotAcquire"]=await reader.ReadAsync(expected with{SessionId="OtherPlayer"},()=>latest,CancellationToken.None) is null;
        delayed=new(TaskCreationOptions.RunContinuationsAsynchronously);var pending=reader.ReadAsync(expected,()=>latest,CancellationToken.None);latest=expected with{Revision=2};delayed.SetResult([document]);
        checks["appleOriginal.staleNativeScanCannotSaveNewRecording"]=await pending is null;delayed=null;latest=expected;
        delayed=new(TaskCreationOptions.RunContinuationsAsynchronously);using(var cancel=new CancellationTokenSource())
        {
            pending=reader.ReadAsync(expected,()=>latest,cancel.Token);cancel.Cancel();delayed.SetResult([document]);
            checks["appleOriginal.providerIgnoringCancellationCannotApply"]=await pending is null;
        }
        delayed=null;reads=0;restarted=true;checks["appleOriginal.processRestartDiscardsCapture"]=await reader.ReadAsync(expected,()=>latest,CancellationToken.None) is null;restarted=false;
        reads=0;changedText=true;checks["appleOriginal.changedNativeTextDiscardsCapture"]=await reader.ReadAsync(expected,()=>latest,CancellationToken.None) is null;changedText=false;
        reads=0;ambiguous=true;checks["appleOriginal.competingTimelinesRemainUncertain"]=await reader.ReadAsync(expected,()=>latest,CancellationToken.None) is null;
        string originalTheme=settings.UiTheme;
        try
        {
            foreach(var theme in AppThemes.All)
            {
                SelectUiTheme(theme.Id,false);var preferences=new SettingsWindow(coordinator,id=>SelectUiTheme(id,false)){Owner=this,Width=1000,Height=850};preferences.Show();preferences.SelectGeneralTab();
                try
                {
                    await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);await Task.Delay(300);preferences.UpdateLayout();
                    checks["appleOriginal.settings."+theme.Id+".nativeOptionAndRetryVisible"]=Descendants<System.Windows.Controls.CheckBox>(preferences).Any(c=>c.IsVisible && c.Content?.ToString()?.Contains("Apple Music 已加载",StringComparison.Ordinal)==true)
                        && Descendants<System.Windows.Controls.Button>(preferences).Any(b=>b.IsVisible && b.Content?.ToString()=="重新查找当前歌曲原词");
                    checks["appleOriginal.settings."+theme.Id+".followsApplicationTheme"]=preferences.Background.ToString()==FindResource("WindowBrush").ToString();
                    VisualVerification.Save((System.Windows.FrameworkElement)preferences.Content,System.IO.Path.Combine(directory,"original-lyrics-settings-"+theme.Id+".png"),true);
                }
                finally{preferences.Close();}
            }
        }
        finally{SelectUiTheme(originalTheme,false);}
    }
}
