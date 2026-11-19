using System.IO;
using System.Net.Http;
using System.Windows;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
using LumaLyrics.Windows;
using Microsoft.Win32;

namespace LumaLyrics.App;

public partial class LyricTimingWindow
{
    readonly IOfflineLyricTiming autoTiming;
    readonly Func<CancellationToken,Task<LyricTimingAudio>>? captureOverride;
    CancellationTokenSource? autoOperation;
    bool autoClosed;

    void InitializeAutoTiming()
    {
        string size=(Math.Ceiling(autoTiming.DownloadBytes/100_000_000d)/10).ToString("0.0",UiText.Culture);
        PrepareAutoTiming.Content=UiText.F($"下载组件（约 {size} GB）");
        RefreshAutoControls();
    }

    void RefreshAutoControls()
    {
        bool busy=autoOperation is not null,ready=autoTiming.Ready;
        PrepareAutoTiming.Visibility=ready?Visibility.Collapsed:Visibility.Visible;
        PrepareAutoTiming.IsEnabled=!busy;
        CaptureAutoTiming.IsEnabled=OpenAutoAudio.IsEnabled=ready&&!busy;
        BeginTiming.IsEnabled=LoadText.IsEnabled=!busy;
        SourceText.IsReadOnly=busy;
        CancelAutoTiming.Visibility=AutoTimingProgress.Visibility=busy?Visibility.Visible:Visibility.Collapsed;
        CancelAutoTiming.IsEnabled=busy;
        if(!busy)AutoTimingStatus.Text=UiText.T(ready?"从头播放这首歌，或选择同一录音的 WAV。结果可先试听校对。":"下载离线组件后，可用这首歌的声音生成逐句时间草稿。" );
    }

    async void PrepareAutoClicked(object sender,RoutedEventArgs e)
    {
        await RunAuto(async token=>
        {
            AutoTimingStatus.Text=UiText.T("正在下载离线打轴组件…");
            var progress=new Progress<(long Done,long Total)>(p=>
            {
                if(Active(token))SetAutoProgress(p.Done,p.Total);
            });
            await autoTiming.PrepareAsync(progress,token);
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
        if(autoOperation is not null || !autoTiming.Ready)return;
        if(target.Track.DurationMs is not {} duration){Feedback.Text=UiText.T("此功能需要已知时长，最长支持 15 分钟。");return;}
        var picker=new OpenFileDialog{Filter=UiText.T("PCM WAV 音频|*.wav"),Title=UiText.T("选择同一录音的音频")};
        if(picker.ShowDialog(this)!=true)return;
        await StartAuto(token=>Task.Run(()=>
        {
            using var stream=File.OpenRead(picker.FileName);
            return LyricTimingAudio.ReadWave(stream,duration,token);
        },token));
    }

    async Task StartAuto(Func<CancellationToken,Task<LyricTimingAudio>> capture)
    {
        if(!autoTiming.Ready)return;
        await RunAuto(async token=>
        {
            var document=new ManualLyricTiming(SourceText.Text,target.Track.DurationMs);
            coordinator.ReadStaticTimingSource(target);
            AutoTimingStatus.Text=UiText.T("正在读取音频…");
            using var audio=new AudioLifetime(await capture(token));
            token.ThrowIfCancellationRequested();
            AutoTimingStatus.Text=UiText.T("正在本机分析原词与人声…");
            AutoTimingProgress.IsIndeterminate=true;
            var result=await autoTiming.AlignAsync(audio.Value,document.Lines.Select(l=>l.Text).ToArray(),token);
            token.ThrowIfCancellationRequested();
            coordinator.ReadStaticTimingSource(target);
            document.LoadDraft(result);
            if(!document.Lines.Any(l=>l.StartMs.HasValue))throw new InvalidDataException("没找到可靠的句首和句尾。可以继续手动打轴。");
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
