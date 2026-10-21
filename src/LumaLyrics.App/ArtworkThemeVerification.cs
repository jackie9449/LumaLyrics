using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

public partial class MainWindow
{
    async Task VerifyArtworkThemesAsync(string directory,Dictionary<string,object?> checks)
    {
        byte[] Encode(BitmapSource image)
        {
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));
            using var stream=new MemoryStream();encoder.Save(stream);return stream.ToArray();
        }
        byte[] Fixture(string start,string end,string light,string label)
        {
            var visual=new DrawingVisual();
            using(var canvas=visual.RenderOpen())
            {
                Color C(string hex)=>(Color)ColorConverter.ConvertFromString(hex);
                canvas.DrawRectangle(new LinearGradientBrush(C(start),C(end),65),null,new Rect(0,0,1200,1200));
                canvas.DrawEllipse(new SolidColorBrush(C(light)),null,new Point(800,385),280,280);
                canvas.DrawRectangle(new SolidColorBrush(Color.FromArgb(52,0,0,0)),null,new Rect(0,710,1200,490));
                for(int i=0;i<7;i++)canvas.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(65,255,255,255)),2),new Point(0,760+i*27),new Point(1200,610+i*27));
                var culture=System.Globalization.CultureInfo.GetCultureInfo("en-US");var typeface=new Typeface("Segoe UI Variable");
                canvas.DrawText(new FormattedText("L U M A  /  0 1",culture,FlowDirection.LeftToRight,typeface,28,Brushes.White,1),new Point(95,100));
                canvas.DrawText(new FormattedText(label,culture,FlowDirection.LeftToRight,typeface,74,Brushes.White,1),new Point(90,955));
                canvas.DrawText(new FormattedText("C O L O R   I N   S O U N D",culture,FlowDirection.LeftToRight,typeface,23,Brushes.White,1),new Point(95,1080));
            }
            var bitmap=new RenderTargetBitmap(1200,1200,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);return Encode(bitmap);
        }
        Color C(string hex)=>(Color)ColorConverter.ConvertFromString(hex);
        // Animated scRGB colors retain more precision than resource hex colors.
        // Compare the actual rendered channels when checking the restart frame.
        bool SamePixels(Color first,Color second)=>first.A==second.A && first.R==second.R && first.G==second.G && first.B==second.B;
        LinearGradientBrush WindowGradient()=>(LinearGradientBrush)FindResource("WindowBrush");
        bool WindowMatchesTarget()=>WindowGradient().GradientStops[0].Color==C(ThemeManager.Current.WindowStart) && WindowGradient().GradientStops[1].Color==C(ThemeManager.Current.WindowEnd);
        string Signature(string key)=>FindResource(key) is GradientBrush gradient?string.Join("/",gradient.GradientStops.Select(stop=>stop.Color.ToString())):FindResource(key).ToString()!;
        var warm=Fixture("#94401F","#DEA46B","#F6CE91","AFTERGLOW");
        var cool=Fixture("#183455","#4B8FA9","#BCD8E1","TIDAL LIGHT");
        var gray=Fixture("#323232","#9C9C9C","#D9D9D9","QUIET HOURS");
        var transparent=Encode(BitmapSource.Create(1,1,96,96,PixelFormats.Bgra32,null,new byte[4],4));
        bool reduce=settings.ReduceMotion;settings.ReduceMotion=true;
        var originalTitle=vm.Title;var originalArtist=vm.Artist;
        var overlayAppearance=OverlayAppearance.From(settings);
        string floating=overlay.FindResource("RaisedBrush").ToString()!,toolbar=overlay.Toolbar.Background.ToString();
        vm.Artist="浮光  /  封面取色 · 原创界面示例";vm.Time="00:24 / 01:36";vm.Duration=96000;vm.Position=24000;
        vm.Status="封面取色 · 界面预览";vm.Quality="原创示例 · 离线验证";currentIndex=2;
        foreach(string id in new[]{"liquid","frost"})
        {
            SelectUiTheme(id,false);SetArtwork(warm);vm.Title="余晖来信";
            string warmWindow=Signature("WindowBrush"),warmPanel=Signature("PanelBrush"),warmGlow=Signature("GlowBrush"),warmAccent=Signature("AccentBrush");
            checks[$"artwork.{id}.warmFromActualCover"]=artworkPalette is {Primary.R:>100} && artworkPalette.Primary.R>artworkPalette.Primary.B;
            checks[$"artwork.{id}.windowUsesPalette"]=WindowMatchesTarget();
            await Task.Delay(60);VisualVerification.Save((FrameworkElement)Content,Path.Combine(directory,$"cover-{id}-warm.png"),true);
            var preferences=new SettingsWindow(coordinator,id=>SelectUiTheme(id)){Owner=this};preferences.Show();
            var editor=new AppearanceWindow(settings,overlay.PreviewAppearance,coordinator.SaveSettings,()=>overlay.MaterialStatus){Owner=this};editor.Show();
            var editorBackground=editor.Background;var editorForeground=editor.Foreground;
            SetArtwork(cool);vm.Title="潮汐微光";
            checks[$"artwork.{id}.coolFromActualCover"]=artworkPalette is not null && artworkPalette.Primary.B>artworkPalette.Primary.R;
            checks[$"artwork.{id}.allSurfacesFollowCover"]=warmWindow!=Signature("WindowBrush") && warmPanel!=Signature("PanelBrush") && warmGlow!=Signature("GlowBrush") && warmAccent!=Signature("AccentBrush");
            checks[$"artwork.{id}.settingsFollowsCover"]=preferences.Background==FindResource("WindowBrush");
            checks[$"artwork.{id}.floatingEditorIndependent"]=editor.Background==editorBackground && editor.Foreground==editorForeground;
            editor.Close();preferences.Close();
            await Task.Delay(60);VisualVerification.Save((FrameworkElement)Content,Path.Combine(directory,$"cover-{id}-cool.png"),true);
            SetArtwork(gray);vm.Title="静默时分";
            var neutral=C(ThemeManager.Current.WindowStart);
            checks[$"artwork.{id}.grayIsNeutral"]=neutral.R==neutral.G && neutral.G==neutral.B;
            await Task.Delay(60);VisualVerification.Save((FrameworkElement)Content,Path.Combine(directory,$"cover-{id}-gray.png"),true);
            SetArtwork(null);
            checks[$"artwork.{id}.missingRestoresDefault"]=artworkPalette is null && ThemeManager.Current==AppThemes.Find(id) && WindowMatchesTarget();
            SetArtwork(warm);SetArtwork([1,2,3]);
            checks[$"artwork.{id}.brokenRestoresDefault"]=Artwork.Source is null && artworkPalette is null && ThemeManager.Current==AppThemes.Find(id) && CoverPlaceholder.Visibility==Visibility.Visible;
            SetArtwork(transparent);
            checks[$"artwork.{id}.transparentRestoresDefault"]=artworkPalette is null && ThemeManager.Current==AppThemes.Find(id);
        }
        SelectUiTheme("material",false);
        string[] keys=["WindowBrush","PanelBrush","GlowBrush","SecondaryGlowBrush","AccentBrush","RaisedBrush","ControlBrush","TextBrush"];
        var material=keys.Select(Signature).ToArray();SetArtwork(warm);SetArtwork(cool);
        checks["artwork.materialKeepsFixedPalette"]=material.SequenceEqual(keys.Select(Signature)) && ThemeManager.Current==AppThemes.Find("material");
        var cachedPalette=artworkPalette;SelectUiTheme("liquid",false);
        checks["artwork.returnToGlassReusesCurrentCover"]=ThemeManager.Current==cachedPalette!.ApplyTo(AppThemes.Find("liquid")) && WindowMatchesTarget();
        SetArtwork(warm);var transitionWarm=artworkPalette;var before=WindowGradient().GradientStops[0].Color;
        settings.ReduceMotion=false;SetArtwork(cool);
        checks["artwork.transitionHonorsWindowsPreference"]=WindowGradient().GradientStops.Any(stop=>stop.HasAnimatedProperties)==SystemParameters.ClientAreaAnimation;
        await Task.Delay(220);
        var intermediate=WindowGradient().GradientStops[0].Color;var target=C(ThemeManager.Current.WindowStart);
        checks["artwork.transitionInterpolates"]=!SystemParameters.ClientAreaAnimation || !SamePixels(intermediate,before) && !SamePixels(intermediate,target);
        // Isolate brush interruption from PNG decoding time: the old animation keeps
        // advancing while an image is decoded, so an earlier sample is not its restart value.
        ThemeManager.Apply("liquid",transitionWarm,animate:true);
        checks["artwork.interruptedTransitionStartsAtVisibleColor"]=!SystemParameters.ClientAreaAnimation || SamePixels(WindowGradient().GradientStops[0].Color,intermediate);
        SetArtwork(null);SetArtwork(warm);SetArtwork(cool);await Task.Delay(800);
        checks["artwork.rapidChangesFinishAtLatestCover"]=artworkPalette!.Primary.B>artworkPalette.Primary.R && WindowMatchesTarget();
        checks["artwork.completedTransitionReleasesClocks"]=!WindowGradient().GradientStops.Any(stop=>stop.HasAnimatedProperties);
        settings.ReduceMotion=true;SetArtwork(warm);
        checks["artwork.reducedMotionAppliesImmediately"]=WindowMatchesTarget() && WindowGradient().IsFrozen && !WindowGradient().GradientStops.Any(stop=>stop.HasAnimatedProperties);
        var narrowPixels=new byte[4096*4];
        for(int i=0;i<4096;i++){narrowPixels[i*4]=35;narrowPixels[i*4+1]=105;narrowPixels[i*4+2]=220;narrowPixels[i*4+3]=255;}
        var narrow=ArtworkPaletteSampler.Extract(BitmapSource.Create(1,4096,96,96,PixelFormats.Bgra32,null,narrowPixels,4));
        checks["artwork.thinCoverSampling"]=narrow?.Primary==new ArtworkColor(220,105,35);
        checks["artwork.floatingAppearancePreserved"]=overlayAppearance==OverlayAppearance.From(settings);
        checks["artwork.floatingChromeIndependent"]=floating==overlay.FindResource("RaisedBrush").ToString() && toolbar==overlay.Toolbar.Background.ToString();
        SetArtwork(null);settings.ReduceMotion=reduce;vm.Title=originalTitle;vm.Artist=originalArtist;
    }
}
