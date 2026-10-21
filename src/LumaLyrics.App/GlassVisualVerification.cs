using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

public partial class MainWindow
{
    async Task VerifyGlassVisualsAsync(string directory,Dictionary<string,object?> checks)
    {
        var root=new Grid();var scene=new Window{Owner=this,Title="浮光歌词 · 原创玻璃场景",Left=50,Top=35,Width=980,Height=510,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Topmost=true,ShowInTaskbar=false,Content=root};
        var labels=new StackPanel{Margin=new(62,38,0,0)};
        labels.Children.Add(new TextBlock{Text="M A T E R I A L   /   L U M A",FontSize=11,FontWeight=FontWeights.Medium,Foreground=new SolidColorBrush(Color.FromArgb(165,255,255,255))});
        labels.Children.Add(new TextBlock{Text="透亮与柔雾，各有层次。",FontSize=25,FontWeight=FontWeights.Medium,Foreground=Brushes.White,Margin=new(0,10,0,0)});
        var captions=new Canvas();
        var first=new TextBlock{Text="LIQUID GLASS   ·   透亮厚边",FontSize=10,Foreground=Brushes.White};Canvas.SetLeft(first,62);Canvas.SetTop(first,132);captions.Children.Add(first);
        var second=new TextBlock{Text="FROSTED GLASS   ·   细腻柔雾",FontSize=10,Foreground=Brushes.White};Canvas.SetLeft(second,62);Canvas.SetTop(second,302);captions.Children.Add(second);
        var previews=new List<OverlayWindow>();
        scene.Show();
        try
        {
            for(int index=0;index<2;index++)
            {
                var options=OverlayThemes.All[index].Appearance with{Animations=false};var fixture=new AppSettings();options.Apply(fixture);
                var floating=new OverlayWindow(this,new PlayerViewModel{Current="把此刻，轻轻唱成一束光。",Next="让旋律停留在你身旁"},fixture){Owner=scene};floating.Show();previews.Add(floating);
            }
            foreach(var (name,id) in new[]{("dark",0),("light",1),("color",2)})
            {
                root.Children.Clear();root.Children.Add(GlassPreviewScene.CreateWallpaper(id));root.Children.Add(labels);root.Children.Add(captions);
                foreach(var label in labels.Children.OfType<TextBlock>())label.Foreground=id==1?new SolidColorBrush(Color.FromRgb(42,56,70)):Brushes.White;
                first.Foreground=second.Foreground=id==1?new SolidColorBrush(Color.FromRgb(54,69,83)):Brushes.White;
                scene.UpdateLayout();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                var position=NativeWindow.Location(scene);var dpi=VisualTreeHelper.GetDpi(scene);
                for(int i=0;i<previews.Count;i++)NativeWindow.Place(previews[i],position.Left+62*dpi.DpiScaleX,position.Top+(154+i*170)*dpi.DpiScaleY);
                await Task.Delay(520);
                checks["glassVisuals."+name+".bothNativeMaterialsVisible"]=previews.All(p=>p.BlurAccepted && p.GlassHostVisible && p.GlassHostInputTransparent);
                CornerNative.GetWindowRect(NativeWindow.Handle(scene),out var bounds);
                using var image=new System.Drawing.Bitmap(bounds.Right-bounds.Left,bounds.Bottom-bounds.Top);using(var graphics=System.Drawing.Graphics.FromImage(image))graphics.CopyFromScreen(bounds.Left,bounds.Top,0,0,image.Size);
                image.Save(Path.Combine(directory,"glass-materials-"+name+".png"));
            }
            var handles=previews.Select(p=>p.GlassHostHandle).ToArray();foreach(var preview in previews)preview.Close();
            checks["glassVisuals.closeDestroysSubstrates"]=handles.All(h=>h!=0 && !IsGlassWindow(h));
        }
        finally{foreach(var preview in previews.Where(p=>p.IsVisible))preview.Close();scene.Close();}
    }
    [DllImport("user32.dll",EntryPoint="IsWindow")] static extern bool IsGlassWindow(nint hwnd);
}
