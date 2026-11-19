using System.Buffers.Binary;
using System.Diagnostics;

namespace LumaLyrics.Core;

public sealed record LyricTimingAudio(short[] Samples,int Count,double StartMs,double ClockSpreadMs)
{
    public const int SampleRate=16000;
    public const int MaximumSeconds=900;
    public double LengthMs=>Count*1000d/SampleRate;

    public static LyricTimingAudio ReadWave(Stream input,double durationMs,CancellationToken token=default)
    {
        if(!input.CanSeek || input.Length<44 || input.Length>360_000_000)throw BadWave();
        using var reader=new BinaryReader(input,System.Text.Encoding.UTF8,leaveOpen:true);
        if(reader.ReadUInt32()!=0x46464952 || reader.ReadUInt32()+8L>input.Length || reader.ReadUInt32()!=0x45564157)throw BadWave();
        int rate=0,channels=0,bytesPerFrame=0;
        while(input.Position+8<=input.Length)
        {
            token.ThrowIfCancellationRequested();
            uint tag=reader.ReadUInt32();long size=reader.ReadUInt32(),next=input.Position+size+(size&1);
            if(next>input.Length)throw BadWave();
            if(tag==0x20746d66 && size>=16)
            {
                int format=reader.ReadUInt16();channels=reader.ReadUInt16();rate=reader.ReadInt32();
                _=reader.ReadUInt32();bytesPerFrame=reader.ReadUInt16();int bits=reader.ReadUInt16();
                if(format!=1 || channels is not (1 or 2) || rate is <8000 or >96000 || bits!=16 || bytesPerFrame!=channels*2)throw BadWave();
            }
            else if(tag==0x61746164 && rate>0 && size>=bytesPerFrame && size%bytesPerFrame==0)
            {
                long frames=size/bytesPerFrame;
                var length=frames*1000d/rate;
                if(length>MaximumSeconds*1000d || !double.IsFinite(durationMs) || Math.Abs(length-durationMs)>1800)
                    throw new InvalidDataException("音频时长与这首歌不符，或超过 15 分钟。");
                var mono=new LyricPcmBuffer(rate,checked((int)(frames*SampleRate/rate+1)));
                byte[] bytes=new byte[bytesPerFrame*4096];
                while(frames>0)
                {
                    token.ThrowIfCancellationRequested();
                    int take=(int)Math.Min(4096,frames)*bytesPerFrame;
                    input.ReadExactly(bytes.AsSpan(0,take));
                    for(int i=0;i<take;i+=bytesPerFrame)
                    {
                        int value=BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(i,2));
                        if(channels==2)value=(value+BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(i+2,2)))/2;
                        mono.Push(value);
                    }
                    frames-=take/bytesPerFrame;
                }
                return mono.Finish(0,0);
            }
            input.Position=next;
        }
        throw BadWave();
    }

    static InvalidDataException BadWave()=>new("请选择同一录音的 16 位 PCM WAV 文件。");
}

/// <summary>Bounded, area-weighted resampling. Packet boundaries do not reset sample phase.</summary>
public sealed class LyricPcmBuffer(int inputRate,int capacity)
{
    readonly short[] samples=new short[capacity];
    int weight,count,signal;
    long sum;
    public int Count=>count;
    public bool Full=>count==samples.Length;
    public void Clear()=>Array.Clear(samples);

    public void Push(int sample)
    {
        int remaining=LyricTimingAudio.SampleRate;
        while(remaining>0 && !Full)
        {
            int next=Math.Min(remaining,inputRate-weight);
            sum+=(long)sample*next;weight+=next;remaining-=next;
            if(weight!=inputRate)continue;
            short value=(short)(sum/inputRate);
            samples[count++]=value;
            if(Math.Abs((int)value)>=128)signal++;
            sum=0;weight=0;
        }
    }

    public LyricTimingAudio Finish(double startMs,double spreadMs)
    {
        if(count<LyricTimingAudio.SampleRate || signal<Math.Max(160,count/750))throw new InvalidDataException("没有采集到这首歌的可用声音。");
        return new(samples,count,startMs,spreadMs);
    }
}

public sealed class LyricCaptureTimeline(PlaybackSnapshot recording,double captureStartLimitMs=3500)
{
    const double ClockToleranceMs=1550;
    readonly List<double> starts=[];
    double? anchor;
    long previousTick=-1;
    double lastRaw=-1;
    double? rollingOrigin;
    DateTimeOffset rollingUtc;
    double? previousReading,firstNonSilent;
    int outliers;
    public double? StartMs=>anchor is {} a?Math.Max(a,-(firstNonSilent??3500)):null;
    public double SpreadMs=>starts.Count<2?0:starts.Max()-starts.Min();
    public void ObserveSignal(double elapsedMs)=>firstNonSilent??=Math.Max(0,elapsedMs);

    public bool Observe(PlaybackSnapshot snapshot,double firstPacketMonotonicMs,Action<string>? trace=null)
    {
        // The WinRT session sometimes emits a transitional rate of zero as the track starts.
        if(!PlaybackSeeking.SameRecording(recording,snapshot) || snapshot.Status!=PlayState.Playing || snapshot.Rate is >0 and {} rate && Math.Abs(rate-1)>0.001)
        {trace?.Invoke("media-state");return false;}
        if(snapshot.MonotonicTicks<=previousTick || snapshot.PositionMs is not {} position || snapshot.Quality==PositionQuality.Unavailable
            || snapshot.TimelineUtc is not {} utc || (snapshot.CapturedUtc-utc).TotalMilliseconds is < -750 or > 4000)return true;
        previousTick=snapshot.MonotonicTicks;
        if(position<=0 || position==lastRaw)return true; // The published zero at startup is not a timing observation.
        if(lastRaw>=0 && position+1000<lastRaw){trace?.Invoke($"backward: {lastRaw:F0} -> {position:F0}");return false;}
        if(position<lastRaw)return true; // One quantized out-of-order sample is not a seek.
        lastRaw=position;
        double atCapture=position+(snapshot.CapturedUtc-utc).TotalMilliseconds;
        double start=atCapture+firstPacketMonotonicMs-snapshot.MonotonicTicks*(1000d/Stopwatch.Frequency);
        if(!double.IsFinite(start))return false;
        trace?.Invoke($"position={position:F0} origin={start:F0} age={(snapshot.CapturedUtc-utc).TotalMilliseconds:F0}");
        if(anchor.HasValue && rollingOrigin is {} value)
        {
            double elapsed=(utc-rollingUtc).TotalMilliseconds;
            if(Math.Abs(start-value)>ClockToleranceMs+Math.Clamp(elapsed*0.05,0,250))return ++outliers<2;
            outliers=0;
            if(previousReading is {} old && Math.Abs(old-start)>1400)return false;
            previousReading=start;
            rollingOrigin=value+Math.Clamp(start-value,-Math.Max(70,elapsed*0.05),Math.Max(70,elapsed*0.05));
            rollingUtc=utc;
        }
        else
        {
            starts.Add(start);
            if(starts.Count>=5)
            {
                starts.Sort();
                if(starts[^2]-starts[1]>ClockToleranceMs || starts[2]<-3500 || starts[2]>captureStartLimitMs)return false;
                anchor=starts[2];
                rollingOrigin=start;
                previousReading=start;
                rollingUtc=utc;
            }
        }
        return true;
    }
}
