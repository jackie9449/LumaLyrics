using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Drawing=System.Drawing;
using Forms=System.Windows.Forms;
namespace LumaLyrics.App;

public partial class MainWindow
{
    [StructLayout(LayoutKind.Sequential)]
    struct TrayIdentifier {public uint Size;public nint Window;public uint Id;public Guid Guid;}
    [DllImport("shell32.dll",EntryPoint="Shell_NotifyIconGetRect")]
    static extern int TrayGetRect(in TrayIdentifier identifier,out NativeWindow.RectNative rectangle);

    static bool TryTrayIdentifier(Forms.NotifyIcon icon,out TrayIdentifier identifier)
    {
        // Verification only: inspect our own WinForms notification, never another application's tray data.
        var fields=typeof(Forms.NotifyIcon).GetFields(BindingFlags.NonPublic|BindingFlags.Instance);
        var window=fields.Select(field=>field.GetValue(icon)).OfType<Forms.NativeWindow>().SingleOrDefault();
        var id=fields.SingleOrDefault(field=>field.Name is "_id" or "id");
        identifier=new(){Size=(uint)Marshal.SizeOf<TrayIdentifier>(),Window=window?.Handle??0,Id=id is null?0:Convert.ToUInt32(id.GetValue(icon))};
        return identifier.Window!=0 && id is not null;
    }
    static async Task<bool> RegisteredTrayAsync(TrayIdentifier identifier)
    {
        for(int attempt=0;attempt<30;attempt++)
        {
            // HRESULT 1 also accompanies a valid Shell rectangle on this desktop.
            // Require a successful HRESULT and actual bounds; Visible alone is insufficient.
            if(TrayGetRect(in identifier,out var rect)>=0 && rect.Right>rect.Left && rect.Bottom>rect.Top)return true;
            await Task.Delay(50);
        }
        return false;
    }
    async Task VerifyTrayIconAsync(string directory,Dictionary<string,object?> checks)
    {
        bool closeToTray=settings.CloseToTray,visible=IsVisible;var state=WindowState;
        async Task Settle(){await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(60);}
        try
        {
            checks["tray.embeddedLogoPresent"]=typeof(ApplicationIcon).Assembly.GetManifestResourceNames().Contains(ApplicationIcon.ResourceName);
            checks["tray.ownsEmbeddedIconInsteadOfHostIcon"]=tray is not null && trayIcon is not null && ReferenceEquals(tray.Icon,trayIcon) && CanRecoverOverlay;
            checks["tray.windowIconDecodedAndFrozen"]=Icon is BitmapSource{IsFrozen:true,PixelWidth:>=32,PixelHeight:>=32};
            using var actual=trayIcon!.ToBitmap();using var expected=ApplicationIcon.CreateTrayIcon();using var reference=expected.ToBitmap();
            bool same=actual.Size==reference.Size;int opaque=0,clear=0,darkVisible=0,lightVisible=0;
            for(int y=0;y<actual.Height;y++)for(int x=0;x<actual.Width;x++)
            {
                var color=actual.GetPixel(x,y);same&=color.ToArgb()==reference.GetPixel(x,y).ToArgb();
                if(color.A<10){clear++;continue;}if(color.A<200)continue;opaque++;
                int luminance=(color.R+color.G+color.B)/3;
                if(Math.Abs(luminance-25)>35)darkVisible++;if(Math.Abs(luminance-242)>35)lightVisible++;
            }
            checks["tray.nativeIconMatchesEmbeddedLogo"]=same;
            checks["tray.logoHasVisibleInkAndTransparentCorners"]=opaque>actual.Width*actual.Height/4 && clear>actual.Width*actual.Height/10 && actual.GetPixel(0,0).A==0;
            checks["tray.logoVisibleOnLightAndDarkBackgrounds"]=darkVisible>opaque/3 && lightVisible>opaque/3;
            var source=Imaging.CreateBitmapSourceFromHIcon(trayIcon.Handle,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());source.Freeze();
            var sheet=new Grid{Width=740,Height=230,Background=(Brush)FindResource("WindowBrush")};sheet.RowDefinitions.Add(new(){Height=new(42)});sheet.RowDefinitions.Add(new());sheet.RowDefinitions.Add(new());
            sheet.Children.Add(new TextBlock{Text="L U M A  /  托盘图标 · 原创程序素材",Foreground=(Brush)FindResource("TextBrush"),Margin=new(20,12,0,0),FontSize=13});
            for(int row=1;row<=2;row++)
            {
                var samples=new UniformGrid{Columns=7,Margin=new(18,4,18,4)};var background=new Border{Child=samples,CornerRadius=new(14),Margin=new(12,4,12,4),Background=row==1?new SolidColorBrush(Color.FromRgb(25,31,42)):new SolidColorBrush(Color.FromRgb(242,244,246))};Grid.SetRow(background,row);sheet.Children.Add(background);
                foreach(int size in new[]{16,20,24,32,40,48,64})
                {
                    var sample=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};sample.Children.Add(new Image{Source=source,Width=size,Height=size});sample.Children.Add(new TextBlock{Text=$"{size} px",FontSize=10,HorizontalAlignment=HorizontalAlignment.Center,Margin=new(0,2,0,0),Foreground=row==1?Brushes.White:Brushes.Black});samples.Children.Add(sample);
                }
            }
            sheet.Measure(new(sheet.Width,sheet.Height));sheet.Arrange(new(0,0,sheet.Width,sheet.Height));sheet.UpdateLayout();VisualVerification.Save(sheet,Path.Combine(directory,"tray-icon-preview.png"),true);
            bool identity=TryTrayIdentifier(tray!,out var identifier);checks["tray.nativeIdentifierAvailable"]=identity;
            checks["tray.shellHasRegisteredIconRectangle"]=identity && await RegisteredTrayAsync(identifier);
            var menu=tray!.ContextMenuStrip!;
            checks["tray.recoveryAndAppearanceCommandsPresent"]=new[]{"打开浮光歌词","显示 / 隐藏歌词","解锁歌词","恢复默认位置","桌面歌词外观…","退出"}.All(text=>menu.Items.OfType<Forms.ToolStripMenuItem>().Any(item=>item.Text==text));
            settings.CloseToTray=true;Close();await Settle();
            checks["tray.closeHidesWindowAndKeepsRecovery"]=!IsVisible && !quitting && CanRecoverOverlay && identity && await RegisteredTrayAsync(identifier);
            menu.Items.OfType<Forms.ToolStripMenuItem>().Single(item=>item.Text=="打开浮光歌词").PerformClick();await Settle();
            checks["tray.openMenuCommandRestoresWindow"]=IsVisible && WindowState==WindowState.Normal;
            WindowState=WindowState.Minimized;
            typeof(Forms.NotifyIcon).GetMethod("OnDoubleClick",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(tray,[EventArgs.Empty]);await Settle();
            checks["tray.doubleClickHandlerRestoresMinimizedWindow"]=IsVisible && WindowState==WindowState.Normal;
            DisposeTray();await Settle();
            checks["tray.disposalClearsOwnedResources"]=tray is null && trayIcon is null && menu.IsDisposed && !CanRecoverOverlay;
            checks["tray.disposalRemovesShellRegistration"]=identity && TrayGetRect(in identifier,out _)<0;
            SetupTray();bool recreated=tray is not null && TryTrayIdentifier(tray,out identifier);
            bool registered=recreated && await RegisteredTrayAsync(identifier);
            checks["tray.recreatedIconRegistersAndRecovers"]=CanRecoverOverlay && registered;
            checks["tray.recreatedShellResult"]=TrayGetRect(in identifier,out _);
        }
        finally
        {
            settings.CloseToTray=closeToTray;if(tray is null)SetupTray();WindowState=state;if(visible)Show();else Hide();
        }
    }
}
