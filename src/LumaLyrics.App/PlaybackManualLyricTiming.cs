using System.Diagnostics;
using System.IO;
using System.Text.Json;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
using LumaLyrics.Windows;

namespace LumaLyrics.App;

internal readonly record struct LyricTimingPlayback(PlaybackSnapshot? Recording,double? PositionMs,string Status);

public sealed partial class PlaybackCoordinator
{
    static readonly bool timingVerification=Environment.GetCommandLineArgs().Contains("--verify-ui");
    internal string ReadStaticTimingSource(LibraryPreparationTarget target)
    {
        var binding=repository.FindReadOnly(target.Track);
        if(binding?.Id!=target.BindingId)throw new InvalidDataException("所选歌曲的原词已变化，请从歌曲列表重新打开打轴。");
        if(binding is null)return "";
        if(binding.Instrumental || new LrcParser().Parse(binding.Raw,binding.Plain).IsSynced)
            throw new InvalidDataException("请选择缺少动态原词的歌曲来打轴。");
        return binding.Raw;
    }

    internal LyricTimingPlayback ReadLyricTimingPlayback(Track target,PlaybackSnapshot? lockedRecording)
    {
        var rendered=Snapshot;
        var actual=Source.Latest;
        if(actual is null && timingVerification)actual=rendered;
        if(actual is null || !SameRecording(actual.Track,target))return new(null,null,"请在 Apple Music 播放这首歌。");
        if(lockedRecording is not null && !PlaybackSeeking.SameRecording(lockedRecording,actual))
            return new(null,null,"播放会话已变化。请撤销标记后重新开始打轴。");
        if(rendered is null || !PlaybackSeeking.SameRecording(actual,rendered) || actual.MonotonicTicks!=rendered.MonotonicTicks
            || Stopwatch.GetElapsedTime(actual.MonotonicTicks)>TimeSpan.FromSeconds(4)
            || actual.Status is not (PlayState.Playing or PlayState.Paused) || Position is not {} position)
            return new(null,null,"正在等待可用的播放位置。");
        return new(actual,position,actual.Status==PlayState.Paused?"已暂停 · 可标记当前位置"
            :Quality==PositionQuality.Estimated?"正在播放 · 插值位置":"正在播放 · 播放位置");
    }

    internal void SaveManualLyricTiming(LibraryPreparationTarget target,ManualLyricTiming timing,LyricTimingModel? model)
    {
        var binding=repository.Save(target.Track,timing.MakeLrc(),timing.HasDraft?"离线打轴 · 已校对":"手动打轴",true,
            providerJson:timing.HasDraft && model is not null?JsonSerializer.Serialize(new
            {source=model.Source,modelRevision=model.ModelRevision,acceptedInTimingEditor=true}):null,
            expectedLibrary:Member(target),expectedBindingId:target.BindingId);
        ApplyLibraryOriginal(target,binding);
    }

    internal Task<LyricTimingAudio> CaptureTimingAudioAsync(Track target,IProgress<AppleMusicLyricCapture.Progress>? progress,CancellationToken token)
        =>AppleMusicLyricCapture.CaptureAsync(target,()=>Source.Latest,progress,token);
}
