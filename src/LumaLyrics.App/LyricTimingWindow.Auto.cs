using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
using LumaLyrics.Windows;
using Microsoft.Win32;

namespace LumaLyrics.App;

public partial class LyricTimingWindow
{
    readonly IReadOnlyDictionary<LyricTimingEngine,IOfflineLyricTiming> timingBackends;
    LyricTimingEngine SelectedEngine=>(AutoTimingModel.SelectedItem as ComboBoxItem)?.Tag is LyricTimingEngine engine
        ?engine:LyricTimingEngine.Multilingual;
    IOfflineLyricTiming SelectedTiming=>timingBackends[SelectedEngine];
    LyricTimingModel? draftModel;
    readonly Func<CancellationToken,Task<LyricTimingAudio>>? captureOverride;
    CancellationTokenSource? autoOperation;
    bool autoClosed;

    void InitializeAutoTiming()
    {
        AutoTimingModel.SelectedIndex=0;
        AutoTimingModel.SelectionChanged+=(_,_) =>
        {
            ClearCoverage();
            Feedback.Text="";
            RefreshAutoControls();
        };
        SourceText.TextChanged+=(_,_) =>
        {
            if(AutoTimingCoverage.Text.Length==0)return;
            ClearCoverage();
            if(autoOperation is not null)return;
            Feedback.Text="";
            RefreshAutoControls();
        };
        RefreshAutoControls();
    }

    void ClearCoverage()
    {
        AutoTimingCoverage.Text="";
        AutoTimingUncovered.Text="";
    }

    void RefreshAutoControls()
    {
        bool busy=autoOperation is not null,ready=SelectedTiming.Ready;
        string size=(Math.Ceiling(SelectedTiming.DownloadBytes/100_000_000d)/10).ToString("0.0",UiText.Culture);
        PrepareAutoTiming.Content=UiText.F($"下载组件（约 {size} GB）");
        AutoTimingModelInfo.Text=UiText.T(SelectedEngine switch
        {
            LyricTimingEngine.Mandarin=>"适合人声清晰的普通话录音。伴奏较重或原词漏写重复句时，部分时间可能需要手动补齐。",
            LyricTimingEngine.Japanese=>"按日语读音对齐汉字和假名，草稿保留原文。模型权重仅限非商业用途。",
            _=>""
        });
        PrepareAutoTiming.Visibility=ready?Visibility.Collapsed:Visibility.Visible;
        PrepareAutoTiming.IsEnabled=AutoTimingModel.IsEnabled=!busy;
        CaptureAutoTiming.IsEnabled=OpenAutoAudio.IsEnabled=ready&&!busy;
        BeginTiming.IsEnabled=LoadText.IsEnabled=!busy;
        SourceText.IsReadOnly=busy;
        CancelAutoTiming.Visibility=AutoTimingProgress.Visibility=busy?Visibility.Visible:Visibility.Collapsed;
        CancelAutoTiming.IsEnabled=busy;
        if(!busy)AutoTimingStatus.Text=AutoTimingUncovered.Text.Length>0?""
            :UiText.T(ready?"从头播放这首歌，或选择同一录音的 WAV。结果可先试听校对。":"下载离线组件后，可用这首歌的声音生成逐句时间草稿。");
    }

    async void PrepareAutoClicked(object sender,RoutedEventArgs e)
    {
        var backend=SelectedTiming;
        await RunAuto(async token=>
        {
            AutoTimingStatus.Text=UiText.T("正在下载离线打轴组件…");
            var progress=new Progress<(long Done,long Total)>(p=>
            {
                if(Active(token))SetAutoProgress(p.Done,p.Total);
            });
            await backend.PrepareAsync(progress,token);
            token.ThrowIfCancellationRequested();
            return UiText.T("离线打轴组件已准备好。");
        });
    }

    async void CaptureAutoClicked(object sender,RoutedEventArgs e)
    {
        await StartAuto(async token=>
        {
            AutoTimingStatus.Text=UiText.T("请在 Apple Music 从头播放这首歌。采样时请连续播放。");
            AutoTimingProgress.IsIndeterminate=true;
            var progress=new Progress<AppleMusicLyricCapture.Progress>(p=>
            {
                if(!Active(token)||!p.Recording)return;
                AutoTimingStatus.Text=UiText.F($"正在采样 {p.Seconds}/{p.TotalSeconds} 秒");
                SetAutoProgress(p.Seconds,p.TotalSeconds);
            });
            return await (captureOverride?.Invoke(token)??coordinator.CaptureTimingAudioAsync(target.Track,progress,token));
        });
    }

    async void OpenAutoClicked(object sender,RoutedEventArgs e)
    {
        if(autoOperation is not null || !SelectedTiming.Ready)return;
        if(target.Track.DurationMs is not {} duration){Feedback.Text=UiText.T("此功能需要已知时长，最长支持 15 分钟。");return;}
        await StartAuto(async token=>
        {
            var picker=new OpenFileDialog{Filter=UiText.T("PCM WAV 音频|*.wav"),Title=UiText.T("选择同一录音的音频")};
            if(picker.ShowDialog(this)!=true)throw new OperationCanceledException(token);
            return await Task.Run(()=>
            {
                using var stream=File.OpenRead(picker.FileName);
                return LyricTimingAudio.ReadWave(stream,duration,token);
            },token);
        });
    }

    async Task StartAuto(Func<CancellationToken,Task<LyricTimingAudio>> capture)
    {
        var backend=SelectedTiming;
        if(!backend.Ready)return;
        await RunAuto(async token=>
        {
            var document=new ManualLyricTiming(SourceText.Text,target.Track.DurationMs);
            coordinator.ReadStaticTimingSource(target);
            var lines=document.Lines.Select(l=>l.Text).ToArray();
            ClearCoverage();
            AutoTimingStatus.Text=UiText.T("正在检查原词覆盖…");
            var coverage=await backend.CheckCoverageAsync(lines,token);
            token.ThrowIfCancellationRequested();
            AutoTimingCoverage.Text=UiText.F($"此模型可识别 {coverage.SupportedLines}/{coverage.TotalLines} 行原词");
            if(!coverage.Complete)
            {
                string lineNumbers=string.Join(", ",coverage.Lines.Select((covered,i)=>(covered,number:i+1))
                    .Where(line=>!line.covered).Select(line=>line.number.ToString(UiText.Culture)));
                AutoTimingUncovered.Text=UiText.F($"未覆盖的行号：{lineNumbers}");
                return UiText.T("请选择覆盖全文的模型，或继续手动打轴。");
            }
            coordinator.ReadStaticTimingSource(target);
            AutoTimingStatus.Text=UiText.T("正在读取音频…");
            using var audio=new AudioLifetime(await capture(token));
            token.ThrowIfCancellationRequested();
            AutoTimingStatus.Text=UiText.T("正在本机分析原词与人声…");
            AutoTimingProgress.IsIndeterminate=true;
            var result=await backend.AlignAsync(audio.Value,lines,token);
            token.ThrowIfCancellationRequested();
            coordinator.ReadStaticTimingSource(target);
            document.LoadDraft(result);
            if(!document.Lines.Any(l=>l.StartMs.HasValue))
                return UiText.T("原词检查已通过，但音频中没找到可靠的逐句时间。请核对实唱版本和重复句，或继续手动打轴。");
            draftModel=backend.Model;
            timing=document;
            OpenTimingEditor();
            return Feedback.Text;
        });
    }

    async Task RunAuto(Func<CancellationToken,Task<string>> action)
    {
        if(autoOperation is not null || timing is not null)return;
        using var cancellation=new CancellationTokenSource();
        autoOperation=cancellation;
        RefreshAutoControls();
        AutoTimingProgress.IsIndeterminate=true;
        string detail;
        try{detail=await action(cancellation.Token);}
        catch(OperationCanceledException){detail=UiText.T("已取消自动打轴。");}
        catch(InvalidDataException e){detail=UiText.Status(e.Message);}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or HttpRequestException or System.Runtime.InteropServices.COMException or System.ComponentModel.Win32Exception)
        {detail=UiText.T("无法完成离线打轴。请重试或手动打轴。");}
        finally
        {
            autoOperation=null;
            if(!autoClosed)RefreshAutoControls();
        }
        if(!autoClosed)Feedback.Text=detail;
    }

    bool Active(CancellationToken token)=>!autoClosed && !token.IsCancellationRequested && autoOperation?.Token==token;
    void SetAutoProgress(long done,long total)
    {AutoTimingProgress.IsIndeterminate=false;AutoTimingProgress.Maximum=Math.Max(1,total);AutoTimingProgress.Value=Math.Clamp(done,0,total);}

    void CancelAutoClicked(object sender,RoutedEventArgs e)
    {CancelAutoTiming.IsEnabled=false;autoOperation?.Cancel();}

    sealed class AudioLifetime(LyricTimingAudio value):IDisposable
    {
        public LyricTimingAudio Value=>value;
        public void Dispose()=>Array.Clear(value.Samples);
    }
}
