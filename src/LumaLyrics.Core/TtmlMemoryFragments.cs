using System.Text;

namespace LumaLyrics.Core;

public sealed record TtmlMemoryFragment(int ByteOffset,string Xml);
public static class TtmlMemoryFragments
{
    public const int MaximumBytes=2*1024*1024;
    // Only complete TTML roots are decoded. The caller retains a bounded overlap for
    // split buffers; arbitrary surrounding process bytes are never returned or saved.
    public static IReadOnlyList<TtmlMemoryFragment> Read(byte[] buffer,int count)
    {
        if(count<0 || count>buffer.Length || count>MaximumBytes*3)throw new ArgumentOutOfRangeException(nameof(count));
        var result=new List<TtmlMemoryFragment>();
        foreach(var encoding in new Encoding[]{new UTF8Encoding(false,true),new UnicodeEncoding(false,false,true)})
        foreach(string name in new[]{"tt","tt:tt"})
        {
            byte[] start=encoding.GetBytes("<"+name),end=encoding.GetBytes("</"+name+">");
            int offset=0;
            while(offset<count && result.Count<32)
            {
                int found=buffer.AsSpan(offset,count-offset).IndexOf(start);if(found<0)break;found+=offset;offset=found+start.Length;
                int width=encoding.CodePage==1200?2:1;
                if(offset+width>count || buffer[offset] is not (32 or 62 or 9 or 10 or 13) || width==2 && buffer[offset+1]!=0)continue;
                int length=Math.Min(MaximumBytes,count-found);
                int closing=buffer.AsSpan(found,length).IndexOf(end);if(closing<0)continue;
                length=closing+end.Length;
                try{result.Add(new(found,encoding.GetString(buffer,found,length)));}
                catch(DecoderFallbackException){}
            }
        }
        return result;
    }
}
