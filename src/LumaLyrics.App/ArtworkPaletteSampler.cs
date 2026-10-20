using System.Windows.Media;
using System.Windows.Media.Imaging;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

internal static class ArtworkPaletteSampler
{
    public static ArtworkPalette? Extract(BitmapSource source)
    {
        double scale=Math.Min(1,64d/Math.Max(source.PixelWidth,source.PixelHeight));
        int width=Math.Max(1,(int)Math.Round(source.PixelWidth*scale)),height=Math.Max(1,(int)Math.Round(source.PixelHeight*scale));
        BitmapSource sample=scale<1?new TransformedBitmap(source,new ScaleTransform(width/(double)source.PixelWidth,height/(double)source.PixelHeight)):source;
        sample=new FormatConvertedBitmap(sample,PixelFormats.Bgra32,null,0);
        int stride=sample.PixelWidth*4;
        var bytes=new byte[stride*sample.PixelHeight];sample.CopyPixels(bytes,stride,0);
        var pixels=new ArtworkColor[sample.PixelWidth*sample.PixelHeight];
        for(int i=0;i<pixels.Length;i++)pixels[i]=new(bytes[i*4+2],bytes[i*4+1],bytes[i*4],bytes[i*4+3]);
        return ArtworkPalette.Extract(pixels);
    }
}
