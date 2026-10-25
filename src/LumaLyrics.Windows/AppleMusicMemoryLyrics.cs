using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using LumaLyrics.Core;

namespace LumaLyrics.Windows;

public sealed class AppleMusicMemoryLyrics
{
    const int Chunk=1024*1024;
    const long ByteBudget=384L*1024*1024;
    readonly object cacheGate=new();
    readonly List<ulong> remembered=[];
    string processScope="";
    [StructLayout(LayoutKind.Sequential)]
    struct Region
    {
        public ulong Base,Allocation;
        public uint AllocationProtection,Alignment;
        public ulong Size;
        public uint State,Protection,Type,Padding;
    }
    [DllImport("kernel32.dll",SetLastError=true)]static extern SafeProcessHandle OpenProcess(uint access,bool inherit,int processId);
    [DllImport("kernel32.dll",SetLastError=true)]static extern nuint VirtualQueryEx(SafeProcessHandle process,nuint address,out Region region,nuint size);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool ReadProcessMemory(SafeProcessHandle process,nuint address,byte[] buffer,nuint size,out nuint read);

    public IReadOnlyList<AppleTimedLyrics> Read(Process process,double durationMs,CancellationToken token,Action<string,object?>? trace=null)
    {
        token.ThrowIfCancellationRequested();
        if(!Environment.Is64BitProcess || process.ProcessName!="AppleMusic")return [];
        string scope=process.Id+":"+process.StartTime.ToUniversalTime().Ticks;
        ulong[] hints;
        lock(cacheGate){if(processScope!=scope){remembered.Clear();processScope=scope;}hints=remembered.ToArray();}
        // Query + read only. No writes, injection, debug privilege or process dump.
        using var handle=OpenProcess(0x0400|0x0010,false,process.Id);if(handle.IsInvalid){trace?.Invoke("originalMemoryUnavailable",Marshal.GetLastWin32Error());return [];}
        var watch=Stopwatch.StartNew();long consumed=0;int regions=0;
        var documents=new Dictionary<string,AppleTimedLyrics>();var locations=new List<ulong>();var seen=new HashSet<string>();
        bool Budget()=>consumed<ByteBudget && watch.Elapsed<TimeSpan.FromSeconds(10) && documents.Count<32;
        byte[] ReadAt(ulong address,int length)
        {
            token.ThrowIfCancellationRequested();length=(int)Math.Min(length,Math.Max(0,ByteBudget-consumed));if(length==0)return [];var bytes=new byte[length];
            ReadProcessMemory(handle,(nuint)address,bytes,(nuint)length,out nuint count);consumed+=length;
            return count<=(nuint)length?bytes.AsSpan(0,(int)count).ToArray():[];
        }
        void Extract(byte[] bytes,ulong address)
        {
            foreach(var fragment in TtmlMemoryFragments.Read(bytes,bytes.Length))
            {
                token.ThrowIfCancellationRequested();
                if(!Budget())break;
                if(!seen.Add(TranslationAlignment.Hash(fragment.Xml)))continue;
                trace?.Invoke("originalTtmlCandidate",fragment.Xml);
                try
                {
                    var parsed=AppleTimedLyricsParser.Parse(fragment.Xml,durationMs);
                    documents.TryAdd(parsed.Lrc,parsed);locations.Add(address+(ulong)fragment.ByteOffset);
                }
                catch(Exception error) when(error is InvalidDataException or System.Xml.XmlException){trace?.Invoke("originalTtmlRejected",error.Message);}
            }
        }
        foreach(ulong address in hints.Take(8)){if(!Budget())break;Extract(ReadAt(address,TtmlMemoryFragments.MaximumBytes),address);}
        // Scan every eligible committed region within fixed time/byte/region limits. A
        // cache hit does not skip competing documents left by earlier recordings.
        var pages=new List<Region>();
        for(ulong address=0;Budget() && regions++<100000;)
        {
            token.ThrowIfCancellationRequested();
            if(VirtualQueryEx(handle,(nuint)address,out var region,(nuint)Marshal.SizeOf<Region>())==0 || region.Size==0 || region.Base>ulong.MaxValue-region.Size)break;
            ulong next=region.Base+region.Size;if(next<=address)break;
            if(region.State==0x1000 && (region.Protection&0x100)==0 && (region.Protection&0xff) is 0x02 or 0x04 or 0x08 or 0x20 or 0x40 or 0x80)
                pages.Add(region);
            address=next;
        }
        // Heap/private writable pages precede mapped images and executable code. The
        // priority is independent of any third-party scanner or undocumented address.
        foreach(var region in pages.OrderBy(r=>r.Type==0x20000?0:r.Type==0x40000?1:2).ThenBy(r=>(r.Protection&0xff) is 0x04 or 0x08?0:1))
        {
                if(!Budget())break;ulong next=region.Base+region.Size;
                byte[] carry=[];
                for(ulong at=region.Base;at<next && Budget();)
                {
                    int size=(int)Math.Min((ulong)Chunk,next-at);byte[] current=ReadAt(at,size);
                    byte[] combined=new byte[carry.Length+current.Length];carry.CopyTo(combined,0);current.CopyTo(combined,carry.Length);
                    Extract(combined,at-(ulong)carry.Length);
                    carry=current.Length==size?combined.AsSpan(Math.Max(0,combined.Length-TtmlMemoryFragments.MaximumBytes)).ToArray():[];
                    at+=(ulong)size;
                }
        }
        token.ThrowIfCancellationRequested();
        trace?.Invoke("originalMemorySummary",new{bytes=consumed,elapsed=watch.Elapsed.TotalSeconds,regions,pages=pages.Count,fragments=seen.Count,documents=documents.Count});
        lock(cacheGate){if(processScope==scope){remembered.Clear();remembered.AddRange(locations.Distinct().Take(8));}}
        return documents.Values.ToArray();
    }
}
