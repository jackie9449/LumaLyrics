using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LumaLyrics.Core;
namespace LumaLyrics.App;

public partial class MainWindow
{
    async Task VerifyRuntimePerformanceAsync(string directory,Dictionary<string,object?> checks)
    {
        byte[] Picture(int width,int height)
        {
            var image=BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,new byte[width*height*4],width*4);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var stream=new MemoryStream();encoder.Save(stream);return stream.ToArray();
        }
        SetArtwork(Picture(4096,4096));
        checks["performance.largeCoverBounded"]=Artwork.Source is BitmapImage{PixelWidth:1024,PixelHeight:1024,IsFrozen:true};
        checks["performance.largeCoverPixelBytes"]=1024*1024*4;
        SetArtwork(Picture(48,24));checks["performance.smallCoverNotUpscaled"]=Artwork.Source is BitmapImage{PixelWidth:48,PixelHeight:24};
        SetArtwork(Picture(64,4096));checks["performance.tallCoverBounded"]=Artwork.Source is BitmapImage tall && tall.PixelWidth<=1024 && tall.PixelHeight<=1024;
        var thumbnail=new MusicLibraryView.ThumbnailCache(360);string first="";
        var bytes=Picture(400,400);
        for(int i=0;i<48;i++)
        {
            string file=Path.Combine(directory,$"original-cover-{i}.img");File.WriteAllBytes(file,bytes);if(i==0)first=file;
            checks[$"performance.thumbnail{i}Decoded"]=thumbnail.Get(file) is BitmapImage{PixelWidth:360,IsFrozen:true};
        }
        checks["performance.thumbnailBudget"]=thumbnail.Count<=32;
        checks["performance.evictedCoverReloads"]=thumbnail.Get(first) is BitmapImage{PixelWidth:360};
        thumbnail.Clear();checks["performance.thumbnailClear"]=thumbnail.Count==0;
        var track=new Track("Own performance lines","Own artist","Own album",180000);
        coordinator.Import(track,string.Join("\n",Enumerable.Range(0,80).Select(i=>$"[00:{i/2:00}.{i%2*500:000}]Own light beside the sea {i}")),"Own fixture",false);
        settings.ReduceMotion=true;BuildLyrics();StyleLyricLines(-1,10);
        var untouched=lyricBlocks[50].Foreground;
        double active=lyricBlocks[10].FontSize;StyleLyricLines(10,11);
        checks["performance.onlyAffectedRowsRestyled"]=ReferenceEquals(untouched,lyricBlocks[50].Foreground);
        checks["performance.oldHighlightCleared"]=lyricBlocks[10].FontSize<active;
        checks["performance.newHighlightApplied"]=lyricBlocks[11].FontSize==active;
        StyleLyricLines(-1,11);checks["performance.fullThemeRefreshRetained"]=!ReferenceEquals(untouched,lyricBlocks[50].Foreground);
        if(SystemParameters.ClientAreaAnimation)
        {
            settings.ReduceMotion=false;settings.OverlayAnimations=true;
            StyleLyricLines(11,12);await Task.Delay(40);StyleLyricLines(12,13);await Task.Delay(500);
            checks["performance.completedLineClocksReleased"]=new[]{11,12,13}.All(i=>!lyricBlocks[i].HasAnimatedProperties && lyricBlocks[i].Foreground is SolidColorBrush brush && !brush.HasAnimatedProperties && (lyricBlocks[i].RenderTransform is not ScaleTransform scale || !scale.HasAnimatedProperties));
        }
        else checks["performance.animationCheckMode"]="System animations disabled; completed-clock check not exercised";
        SetArtwork(null);
    }
}
