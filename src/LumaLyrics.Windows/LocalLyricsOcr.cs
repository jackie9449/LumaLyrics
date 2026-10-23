using LumaLyrics.Core;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using Windows.Security.Cryptography;
namespace LumaLyrics.Windows;

public sealed record LocalOcrResult(string Language,IReadOnlyList<string> Lines);
public static class LocalLyricsOcr
{
    public static IReadOnlyList<string> Languages
    {
        get
        {
            try{return OcrEngine.AvailableRecognizerLanguages.Select(x=>x.LanguageTag).ToArray();}
            catch(Exception ex) when(Unavailable(ex)){return [];}
        }
    }
    public static string PreferredLanguage(LyricsDocument document)
    {
        string text=string.Join("\n",document.Groups.Select(g=>g.Text));
        if(text.Any(c=>c is >= '\u3040' and <= '\u30ff'))return "ja-JP";
        if(text.Any(c=>c is >= '\uac00' and <= '\ud7af'))return "ko-KR";
        return TranslationAlignment.IsChinese(text)?"zh-Hans-CN":"en-US";
    }
    public static bool Supports(string language)
    {
        try{return OcrEngine.IsLanguageSupported(new Language(language));}
        catch(Exception ex) when(Unavailable(ex)){return false;}
    }
    static bool Unavailable(Exception error)=>error is System.Runtime.InteropServices.ExternalException or TypeLoadException or NotSupportedException;
    public static async Task<LocalOcrResult> RecognizeAsync(byte[] png,string language,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();var engine=OcrEngine.TryCreateFromLanguage(new Language(language))??throw new InvalidOperationException($"本机尚未安装 {language} 的 Windows OCR。可先使用原生文字参考，或在 Windows 语言选项中安装对应的光学字符识别组件。");
        using var stream=new InMemoryRandomAccessStream();using(var writer=new DataWriter(stream.GetOutputStreamAt(0))){writer.WriteBytes(png);await writer.StoreAsync().AsTask(token);writer.DetachStream();}
        var decoder=await BitmapDecoder.CreateAsync(stream).AsTask(token);
        if(decoder.PixelWidth>OcrEngine.MaxImageDimension || decoder.PixelHeight>OcrEngine.MaxImageDimension)throw new InvalidOperationException("歌词区域超出本机 OCR 的图像尺寸限制。");
        using var pixels=await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied).AsTask(token);
        using var contrast=ImproveContrast(pixels);
        var result=await engine.RecognizeAsync(contrast).AsTask(token);token.ThrowIfCancellationRequested();
        return new(engine.RecognizerLanguage.LanguageTag,result.Lines.Select(x=>x.Text).ToArray());
    }
    static SoftwareBitmap ImproveContrast(SoftwareBitmap source)
    {
        var buffer=new global::Windows.Storage.Streams.Buffer(checked((uint)(source.PixelWidth*source.PixelHeight*4)));source.CopyToBuffer(buffer);CryptographicBuffer.CopyToByteArray(buffer,out byte[] bytes);
        var histogram=new int[256];for(int i=0;i<bytes.Length;i+=4)histogram[(bytes[i]*19+bytes[i+1]*183+bytes[i+2]*54)/256]++;
        int pixels=bytes.Length/4,total=0,low=0,high=255;
        for(int i=0;i<256;i++){total+=histogram[i];if(total>=pixels*.02){low=i;break;}}
        total=0;for(int i=255;i>=0;i--){total+=histogram[i];if(total>=pixels*.005){high=i;break;}}
        int range=Math.Max(8,high-low);
        for(int i=0;i<bytes.Length;i+=4)
        {
            int gray=(bytes[i]*19+bytes[i+1]*183+bytes[i+2]*54)/256;
            byte value=(byte)(255-Math.Clamp((gray-low)*255/range,0,255));bytes[i]=bytes[i+1]=bytes[i+2]=value;bytes[i+3]=255;
        }
        var result=new SoftwareBitmap(BitmapPixelFormat.Bgra8,source.PixelWidth,source.PixelHeight,BitmapAlphaMode.Premultiplied);result.CopyFromBuffer(CryptographicBuffer.CreateFromByteArray(bytes));return result;
    }
}
