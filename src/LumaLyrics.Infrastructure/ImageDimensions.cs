using System.Buffers.Binary;
namespace LumaLyrics.Infrastructure;

public static class ImageDimensions
{
    public const int MaximumBytes=15*1024*1024;
    public static (int Width,int Height) Read(ReadOnlySpan<byte> bytes)
    {
        if(bytes.Length>MaximumBytes)throw new InvalidDataException("封面超过 15 MB。");
        if(bytes.Length>=33 && bytes[..8].SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}))
        {
            int width=0,height=0;bool data=false,end=false;
            for(int offset=8;offset+12<=bytes.Length;)
            {
                int length=BinaryPrimitives.ReadInt32BigEndian(bytes[offset..]);
                if(length<0 || length>bytes.Length-offset-12)throw new InvalidDataException("PNG 封面不完整。");
                var tag=bytes.Slice(offset+4,4);
                if(offset==8)
                {
                    if(length!=13 || !tag.SequenceEqual("IHDR"u8))throw new InvalidDataException("PNG 封面头无效。");
                    width=BinaryPrimitives.ReadInt32BigEndian(bytes[(offset+8)..]);height=BinaryPrimitives.ReadInt32BigEndian(bytes[(offset+12)..]);
                }
                if(tag.SequenceEqual("IDAT"u8) && length>0)data=true;
                if(tag.SequenceEqual("IEND"u8)){end=length==0;break;}
                offset+=length+12;
            }
            if(data&&end)return Validate(width,height);
        }
        else if(bytes.Length>8 && bytes[0]==0xff && bytes[1]==0xd8 && bytes[^2]==0xff && bytes[^1]==0xd9)
        {
            for(int offset=2;offset+4<=bytes.Length;)
            {
                if(bytes[offset++]!=0xff)break;
                while(offset<bytes.Length && bytes[offset]==0xff)offset++;
                if(offset>=bytes.Length)break;
                byte marker=bytes[offset++];
                if(marker is 0xd8 or 0x01 || marker is >=0xd0 and <=0xd7)continue;
                if(marker is 0xda or 0xd9 || offset+2>bytes.Length)break;
                int length=BinaryPrimitives.ReadUInt16BigEndian(bytes[offset..]);
                if(length<2 || length>bytes.Length-offset)break;
                if(marker is 0xc0 or 0xc1 or 0xc2 or 0xc3 or 0xc5 or 0xc6 or 0xc7 or 0xc9 or 0xca or 0xcb or 0xcd or 0xce or 0xcf)
                {
                    if(length<8)break;
                    return Validate(BinaryPrimitives.ReadUInt16BigEndian(bytes[(offset+5)..]),BinaryPrimitives.ReadUInt16BigEndian(bytes[(offset+3)..]));
                }
                offset+=length;
            }
        }
        throw new InvalidDataException("封面不是完整、受支持的 JPEG / PNG 图片。");
    }
    static (int,int) Validate(int width,int height)=>width is >0 and <=12000 && height is >0 and <=12000
        ?(width,height):throw new InvalidDataException("封面像素尺寸异常。");
}
