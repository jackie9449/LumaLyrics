using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Path=System.IO.Path;

namespace LumaLyrics.App;

public partial class MainWindow
{
    // Isolated --verify-ui fixtures only: exercise real navigation, then render original vector samples.
    async Task VerifyNavigationVisualsAsync(string directory,Dictionary<string,object?> checks,string theme)
    {
        Button[] buttons=[LyricsNav,MusicNav,CacheNav,AppearanceNav,SettingsNav,DiagnosticsNav];
        var icons=buttons.Select(b=>(ContentPresenter)b.Template.FindName("NavigationIcon",b)).ToArray();
        Rect Bounds(FrameworkElement icon,Button button)=>icon.TransformToAncestor(button).TransformBounds(new Rect(icon.RenderSize));
        var resting=icons.Select((icon,i)=>Bounds(icon,buttons[i])).ToArray();
        bool Centered()=>icons.Select((icon,i)=>(icon,i)).All(item=>
        {
            var bounds=Bounds(item.icon,buttons[item.i]);
            return Math.Abs(bounds.Width-22)<.01 && Math.Abs(bounds.Height-22)<.01 &&
                Math.Abs(bounds.Left+bounds.Width/2-buttons[item.i].ActualWidth/2)<.51 &&
                Math.Abs(bounds.Top+bounds.Height/2-buttons[item.i].ActualHeight/2)<.51;
        });
        bool Unmoved()=>icons.Select((icon,i)=>Bounds(icon,buttons[i])==resting[i]).All(value=>value);
        checks[$"navigation.{theme}.sixVectorIconsWithAccessibleNames"]=buttons.All(b=>b.Content is Geometry && !string.IsNullOrWhiteSpace(AutomationProperties.GetName(b)) && b.ToolTip is not null);
        checks[$"navigation.{theme}.centeredAndUnclipped"]=Centered() && buttons.All(b=>
        {
            var bounds=b.TransformToAncestor(NavigationRail).TransformBounds(new Rect(b.RenderSize));
            return bounds.Left>=0 && bounds.Right<=NavigationRail.ActualWidth && bounds.Top>=0 && bounds.Bottom<=NavigationRail.ActualHeight;
        });
        foreach(var button in buttons)AppMotion.PreviewFeedback(button,true);
        await Task.Delay(230);
        checks[$"navigation.{theme}.hoverKeepsIconsFixed"]=Unmoved() && buttons.All(b=>AppMotion.GetHover(b)==1);
        foreach(var button in buttons)AppMotion.PreviewFeedback(button,true,true);
        await Task.Delay(110);
        checks[$"navigation.{theme}.pressKeepsIconsFixed"]=Unmoved() && buttons.All(b=>AppMotion.GetPressed(b)==1);
        foreach(var button in buttons)AppMotion.PreviewFeedback(button,false);
        await Task.Delay(230);
        checks[$"navigation.{theme}.releaseClearsAnimationClocks"]=buttons.All(b=>!b.HasAnimatedProperties &&
            b.Template.FindName("InteractionVisual",b) is FrameworkElement surface && !surface.RenderTransform.HasAnimatedProperties);
        LyricsNav.Focus();await Task.Delay(230);
        checks[$"navigation.{theme}.keyboardFocusAndHitTargetPreserved"]=LyricsNav.IsKeyboardFocused && AppMotion.GetFocus(LyricsNav)==1 &&
            buttons.All(b=>b.Focusable && b.IsHitTestVisible && b.ActualWidth==48 && b.ActualHeight==48) && Unmoved();
        VisualVerification.Save(NavigationRail,Path.Combine(directory,$"navigation-{theme}.png"),true);

        string[] labels=["当前歌词","音乐库","缓存管理","歌词外观","设置","诊断"];
        string[] states=["默认","选中","悬停","按下","键盘焦点"];
        foreach(double scale in new[]{1d,1.25,1.5})
        {
            var sheet=new Grid{Width=500,Height=408,Background=(Brush)FindResource("WindowBrush"),UseLayoutRounding=true};
            VisualTreeHelper.SetRootDpi(sheet,new DpiScale(scale,scale));
            sheet.ColumnDefinitions.Add(new(){Width=new GridLength(120)});
            for(int column=0;column<states.Length;column++)sheet.ColumnDefinitions.Add(new(){Width=new GridLength(76)});
            sheet.RowDefinitions.Add(new(){Height=new GridLength(48)});
            for(int row=0;row<buttons.Length;row++)sheet.RowDefinitions.Add(new(){Height=new GridLength(60)});
            void Add(UIElement element,int row,int column){Grid.SetRow(element,row);Grid.SetColumn(element,column);sheet.Children.Add(element);}
            TextBlock Label(string text)=>new(){Text=text,FontSize=12,Foreground=(Brush)FindResource("MutedBrush"),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
            Add(Label($"{theme} · {scale*100:0}%"),0,0);
            for(int column=0;column<states.Length;column++)Add(Label(states[column]),0,column+1);
            var samples=new List<Button>();
            for(int row=0;row<buttons.Length;row++)
            {
                Add(Label(labels[row]),row+1,0);
                for(int column=0;column<states.Length;column++)
                {
                    var sample=new Button{Style=(Style)FindResource("NavButton"),Content=buttons[row].Content,Margin=new Thickness(0),VerticalAlignment=VerticalAlignment.Center};
                    if(column==1){sample.SetResourceReference(Control.ForegroundProperty,"AccentBrush");AppMotion.SetSelected(sample,1);}
                    if(column is 2 or 3)AppMotion.SetHover(sample,1);
                    if(column==3)AppMotion.SetPressed(sample,1);
                    if(column==4)AppMotion.SetFocus(sample,1);
                    Add(sample,row+1,column+1);samples.Add(sample);
                }
            }
            sheet.Measure(new Size(sheet.Width,sheet.Height));sheet.Arrange(new Rect(0,0,sheet.Width,sheet.Height));sheet.UpdateLayout();
            bool monochrome=true;
            foreach(var sample in samples)
            {
                var path=Descendants<System.Windows.Shapes.Path>(sample).Single();
                var ink=(SolidColorBrush)path.Stroke;
                var bitmap=VisualVerification.Render(path,true);int stride=bitmap.PixelWidth*4,visible=0;
                byte[] pixels=new byte[stride*bitmap.PixelHeight];bitmap.CopyPixels(pixels,stride,0);
                for(int i=0;i<pixels.Length;i+=4)
                {
                    int alpha=pixels[i+3];if(alpha==0)continue;visible++;
                    if(Math.Abs(pixels[i]-ink.Color.B*alpha/255d)>2 || Math.Abs(pixels[i+1]-ink.Color.G*alpha/255d)>2 || Math.Abs(pixels[i+2]-ink.Color.R*alpha/255d)>2)monochrome=false;
                }
                if(visible<30)monochrome=false;
            }
            checks[$"navigation.{theme}.{scale*100:0}.singleColorAntialiasing"]=monochrome;
            checks[$"navigation.{theme}.{scale*100:0}.vectorStrokesWithinViewport"]=samples.All(sample=>
            {
                var path=Descendants<System.Windows.Shapes.Path>(sample).Single();
                return new Rect(0,0,24,24).Contains(path.Data.GetRenderBounds(new Pen(path.Stroke,path.StrokeThickness)));
            });
            VisualVerification.Save(sheet,Path.Combine(directory,$"navigation-states-{theme}-{scale*100:0}.png"),true);
        }
    }
}
