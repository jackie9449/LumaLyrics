using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using LumaLyrics.Core;

namespace LumaLyrics.Windows;

public sealed class AppleMusicLyricCapture
{
    const int Rate=44100;
    const string NoAudio="没有采集到这首歌的可用声音。";
    const string Changed="播放中断或进度变动，请从头重新采样。";
    public sealed record Progress(bool Recording,int Seconds,int TotalSeconds);

    public static Task<LyricTimingAudio> CaptureAsync(Track target,Func<PlaybackSnapshot?> latest,
        IProgress<Progress>? progress,CancellationToken token,Action<string>? trace=null)=>Task.Run(()=>Capture(target,latest,progress,token,trace),token);

    static LyricTimingAudio Capture(Track target,Func<PlaybackSnapshot?> latest,IProgress<Progress>? progress,CancellationToken token,Action<string>? trace)
    {
        if(!OperatingSystem.IsWindowsVersionAtLeast(10,0,20348))throw new InvalidDataException("当前 Windows 版本不支持按应用采样。请使用本地 WAV。");
        if(target.DurationMs is not (>=5000 and <=LyricTimingAudio.MaximumSeconds*1000d))throw new InvalidDataException("此功能需要已知时长，最长支持 15 分钟。");
        var wait=Stopwatch.StartNew();
        PlaybackSnapshot? locked=null;
        int total=(int)Math.Ceiling(target.DurationMs.Value/1000);
        progress?.Report(new(false,0,total));
        while(wait.Elapsed<TimeSpan.FromMinutes(2))
        {
            token.ThrowIfCancellationRequested();
            var read=latest();
            if(read is {Status:PlayState.Playing,Quality:not PositionQuality.Unavailable,PositionMs:>=0 and <=2000} && Matches(target,read)
                && Stopwatch.GetElapsedTime(read.MonotonicTicks)<TimeSpan.FromSeconds(3)) {locked=read;break;}
            token.WaitHandle.WaitOne(120);
        }
        if(locked is null)throw new InvalidDataException("等待播放超时，请在 Apple Music 将这首歌从头播放。");
        int pid=ResolveRenderer();
        using var loopback=new Loopback(pid,token);
        if(!PlaybackSeeking.SameRecording(locked,latest()))throw new InvalidDataException(Changed);
        var expectedMs=target.DurationMs.Value;
        var maximumWallMs=expectedMs+5000;
        var pcm=new LyricPcmBuffer(Rate,checked((int)Math.Ceiling(maximumWallMs*LyricTimingAudio.SampleRate/1000)));
        var clock=new LyricCaptureTimeline(locked);
        double? firstPacket=null;
        long inputFrames=0;
        int emitted=-1;
        var sampleBuffer=ArrayPool<short>.Shared.Rent(2048);
        var run=Stopwatch.StartNew();
        long lastReceived=0;
        long startupFrames=0;
        bool retain=false;
        try
        {
            loopback.Start();
            while(run.Elapsed.TotalMilliseconds<maximumWallMs)
            {
                token.ThrowIfCancellationRequested();
                var current=latest();
                double listened=pcm.Count*1000d/LyricTimingAudio.SampleRate;
                if(current is null || !PlaybackSeeking.SameRecording(locked,current) || current.Status!=PlayState.Playing)
                {
                    if(clock.StartMs is {} offset && offset+listened>=expectedMs-1500)break; // An automatic transition at the actual end.
                    throw new InvalidDataException(Changed);
                }
                if(Stopwatch.GetElapsedTime(current.MonotonicTicks)>TimeSpan.FromSeconds(5))throw new InvalidDataException(Changed);
                if(firstPacket is {} first && !clock.Observe(current,first,trace))throw new InvalidDataException(Changed);
                if(clock.StartMs is {} start && listened+start>=expectedMs-12)break;
                if(run.ElapsedMilliseconds-lastReceived>3500)throw new InvalidDataException(NoAudio);
                if(run.ElapsedMilliseconds>11500 && clock.StartMs is null)throw new InvalidDataException("无法校准采样与播放进度，请从头重新采样。");
                if(!loopback.Wait(token))continue;
                while(loopback.TryRead(out var packet))
                {
                    try
                    {
                        bool silent=(packet.Flags&2)!=0;
                        if(firstPacket is null && startupFrames<Rate*2)
                        {
                            // The audio session can publish Play before useful render packets arrive.
                            // Skip only initial silent/untimed packets; align the first retained packet to the clock.
                            if((packet.Flags&4)!=0 || silent){startupFrames+=packet.Frames;continue;}
                        }
                        if((packet.Flags&4)!=0 || firstPacket.HasValue&&(packet.Flags&1)!=0)throw new InvalidDataException(Changed);
                        double stamp=packet.Qpc/10000d;
                        // Loopback provides QPC at the packet boundary but may jitter around endpoint start.
                        // Compare against the accumulated sample position instead of concatenating wall time.
                        if(firstPacket.HasValue && Math.Abs(stamp-(firstPacket.Value+inputFrames*1000d/Rate))>185)throw new InvalidDataException(Changed);
                        firstPacket??=stamp;inputFrames+=packet.Frames;
                        lastReceived=run.ElapsedMilliseconds;
                        if(packet.Frames*2>sampleBuffer.Length)throw new InvalidDataException(NoAudio);
                        if(!silent)Marshal.Copy(packet.Data,sampleBuffer,0,packet.Frames*2);
                        for(int i=0;i<packet.Frames && !pcm.Full;i++)
                        {
                            int sample=silent?0:(sampleBuffer[2*i]+sampleBuffer[2*i+1])/2;
                            if(Math.Abs(sample)>=128)clock.ObserveSignal(pcm.Count*1000d/LyricTimingAudio.SampleRate);
                            pcm.Push(sample);
                        }
                    }
                    finally{loopback.Release(packet.Frames);}
                }
                int seconds=pcm.Count/LyricTimingAudio.SampleRate;
                if(seconds!=emitted){emitted=seconds;progress?.Report(new(true,Math.Min(seconds,total),total));}
            }
            token.ThrowIfCancellationRequested();
            if(clock.StartMs is not {} actual || actual+pcm.Count*1000d/LyricTimingAudio.SampleRate<expectedMs-1600)
                throw new InvalidDataException("没有采到完整录音，请从头重新采样。");
            var audio=pcm.Finish(Math.Max(0,actual),clock.SpreadMs);
            int trim=actual<0?(int)Math.Floor(-actual*LyricTimingAudio.SampleRate/1000):0;
            int count=Math.Min(audio.Count-trim,(int)((expectedMs-audio.StartMs)*LyricTimingAudio.SampleRate/1000));
            if(trim>0)Array.Copy(audio.Samples,trim,audio.Samples,0,count);
            Array.Clear(audio.Samples,count,audio.Samples.Length-count);
            retain=true;
            return audio with{Count=count};
        }
        finally
        {
            if(!retain)pcm.Clear();
            ArrayPool<short>.Shared.Return(sampleBuffer,clearArray:true);
        }
    }

    static bool Matches(Track target,PlaybackSnapshot snapshot)=>snapshot.SessionId.StartsWith("AppleInc.AppleMusicWin_",StringComparison.OrdinalIgnoreCase)
        && snapshot.Track.Fingerprint==target.Fingerprint && snapshot.Track.DurationMs is {} actual
        && target.DurationMs is {} expected && Math.Abs(actual-expected)<=1800;

    static int ResolveRenderer()
    {
        static (int Id,string? Folder)[] Find(string name)
        {
            var result=new List<(int,string?)>();
            foreach(var process in Process.GetProcessesByName(name))using(process)
            {
                try{result.Add((process.Id,Path.GetDirectoryName(process.MainModule?.FileName)));}
                catch(System.ComponentModel.Win32Exception){}
                catch(InvalidOperationException){}
            }
            return result.ToArray();
        }
        var main=Find("AppleMusic");
        if(main.Length!=1 || main[0].Folder is not {} folder || !Path.GetFileName(folder).StartsWith("AppleInc.AppleMusicWin_",StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("无法确认 Apple Music 的音频进程。");
        var background=Find("AMPLibraryAgent").Where(x=>string.Equals(folder,x.Folder,StringComparison.OrdinalIgnoreCase)).ToArray();
        if(background.Length>1)throw new InvalidDataException("无法确认 Apple Music 的音频进程。");
        return background.Length==1?background[0].Id:main[0].Id;
    }

    readonly record struct Packet(IntPtr Data,int Frames,uint Flags,ulong Qpc);

    sealed class Loopback : IDisposable
    {
        readonly AutoResetEvent ready=new(false);
        IAudioClient? client;
        IAudioCaptureClient? capture;
        bool started;

        public Loopback(int pid,CancellationToken token)
        {
            using var completion=new Completion();
            IntPtr allocation=Marshal.AllocHGlobal(12),operation=default,raw=default;
            try
            {
                Marshal.Copy(new[]{1,pid,0},0,allocation,3); // Include this render process tree; never the whole endpoint.
                var variant=new BlobVariant{Type=65,Length=12,Data=allocation};
                var iid=typeof(IAudioClient).GUID;
                Check(ActivateAudioInterfaceAsync(@"VAD\Process_Loopback",in iid,in variant,completion,out operation));
                raw=completion.Take(token);
                client=(IAudioClient)Marshal.GetObjectForIUnknown(raw);
                var fmt=new WaveFormat{Tag=1,Channels=2,Rate=Rate,Average=Rate*4,Align=4,Bits=16};
                Check(client.Initialize(0,0x80060000,0,0,in fmt,IntPtr.Zero));
                iid=typeof(IAudioCaptureClient).GUID;
                Check(client.GetService(in iid,out var captureRaw));
                try{capture=(IAudioCaptureClient)Marshal.GetObjectForIUnknown(captureRaw);}finally{Marshal.Release(captureRaw);}
                Check(client.SetEventHandle(ready.SafeWaitHandle.DangerousGetHandle()));
            }
            catch{Dispose();throw;}
            finally{if(raw!=IntPtr.Zero)Marshal.Release(raw);if(operation!=IntPtr.Zero)Marshal.Release(operation);Marshal.FreeHGlobal(allocation);}
        }
        public void Start(){Check(client!.Start());started=true;}
        public bool Wait(CancellationToken token)=>WaitHandle.WaitAny([ready,token.WaitHandle],140)==0;
        public bool TryRead(out Packet packet)
        {
            Check(capture!.GetNextPacketSize(out uint pending));
            if(pending==0){packet=default;return false;}
            Check(capture.GetBuffer(out var data,out uint frames,out var flags,out _,out var qpc));
            packet=new(data,checked((int)frames),flags,qpc);return frames>0;
        }
        public void Release(int frames)=>Check(capture!.ReleaseBuffer((uint)frames));
        public void Dispose()
        {
            if(started)client!.Stop();
            if(capture is not null)Marshal.FinalReleaseComObject(capture);
            if(client is not null)Marshal.FinalReleaseComObject(client);
            ready.Dispose();
        }
    }
    static void Check(int hr){if(hr<0)throw new COMException("无法读取 Apple Music 音频。",hr);}

    [StructLayout(LayoutKind.Explicit,Size=24)]
    struct BlobVariant{[FieldOffset(0)]public ushort Type;[FieldOffset(8)]public uint Length;[FieldOffset(16)]public IntPtr Data;}
    [StructLayout(LayoutKind.Sequential,Pack=2)]
    struct WaveFormat{public ushort Tag,Channels;public uint Rate,Average;public ushort Align,Bits,ExtraSize;}
    [DllImport("Mmdevapi.dll",ExactSpelling=true,CharSet=CharSet.Unicode)]
    static extern int ActivateAudioInterfaceAsync(string device,in Guid id,in BlobVariant parameters,[MarshalAs(UnmanagedType.Interface)] ICompletion completion,out IntPtr operation);
    [ComVisible(true),Guid("41D949AB-9862-444A-80F6-C261334DA5EB"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICompletion{[PreserveSig]int ActivateCompleted(IntPtr operation);}
    [ComImport,Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IActivation{[PreserveSig]int GetActivateResult(out int result,out IntPtr instance);}
    [ComVisible(true),ClassInterface(ClassInterfaceType.None)]
    public sealed class Completion : ICompletion,IDisposable
    {
        readonly object gate=new();
        readonly ManualResetEvent ready=new(false);
        int error=unchecked((int)0x8000FFFF);
        IntPtr instance;
        bool abandoned;
        public int ActivateCompleted(IntPtr operation)
        {
            IntPtr raw=default;int hr;
            try
            {
                var result=(IActivation)Marshal.GetObjectForIUnknown(operation);
                int status=result.GetActivateResult(out hr,out raw);if(status<0)hr=status;
            }
            catch(Exception e){hr=e.HResult;}
            lock(gate)
            {
                if(abandoned){if(raw!=IntPtr.Zero)Marshal.Release(raw);}
                else{error=hr;instance=raw;ready.Set();}
            }
            return 0;
        }
        public IntPtr Take(CancellationToken token)
        {
            int signaled=WaitHandle.WaitAny([ready,token.WaitHandle],6000);
            token.ThrowIfCancellationRequested();
            if(signaled!=0)throw new InvalidDataException(NoAudio);
            lock(gate){Check(error);var result=instance;instance=default;return result;}
        }
        public void Dispose(){lock(gate){abandoned=true;if(instance!=IntPtr.Zero)Marshal.Release(instance);ready.Dispose();}}
    }
    [ComImport,Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioClient
    {
        [PreserveSig]int Initialize(int share,uint flags,long duration,long period,in WaveFormat fmt,IntPtr session);
        [PreserveSig]int GetBufferSize(out uint size);
        [PreserveSig]int GetStreamLatency(out long latency);
        [PreserveSig]int GetCurrentPadding(out uint padding);
        [PreserveSig]int IsFormatSupported(int share,IntPtr fmt,out IntPtr closest);
        [PreserveSig]int GetMixFormat(out IntPtr fmt);
        [PreserveSig]int GetDevicePeriod(out long normal,out long minimum);
        [PreserveSig]int Start();
        [PreserveSig]int Stop();
        [PreserveSig]int Reset();
        [PreserveSig]int SetEventHandle(IntPtr handle);
        [PreserveSig]int GetService(in Guid iid,out IntPtr service);
    }
    [ComImport,Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioCaptureClient
    {
        [PreserveSig]int GetBuffer(out IntPtr data,out uint frames,out uint flags,out ulong position,out ulong qpc);
        [PreserveSig]int ReleaseBuffer(uint frames);
        [PreserveSig]int GetNextPacketSize(out uint frames);
    }
}
