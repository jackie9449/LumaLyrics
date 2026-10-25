using System.IO;
using System.Windows.Media.Imaging;
using Drawing=System.Drawing;
namespace LumaLyrics.App;

internal static class ApplicationIcon
{
    internal const string ResourceName="LumaLyrics.Assets.luma.ico";
    static Stream Open()=>typeof(ApplicationIcon).Assembly.GetManifestResourceStream(ResourceName)
        ?? throw new InvalidDataException("程序内嵌图标不可用。");
    internal static Drawing.Icon CreateTrayIcon()
    {
        // Launch.vbs runs the DLL through Microsoft's host. Its process icon is not our logo.
        using var stream=Open();using var icon=new Drawing.Icon(stream,System.Windows.Forms.SystemInformation.SmallIconSize);
        return (Drawing.Icon)icon.Clone();
    }
    internal static BitmapSource CreateWindowIcon()
    {
        using var stream=Open();var image=BitmapFrame.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
        image.Freeze();return image;
    }
}
