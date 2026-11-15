using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using LumaLyrics.Infrastructure;

namespace LumaLyrics.App;

public partial class MainWindow
{
    static bool DecorationsRemoved(DependencyObject root)
    {
        string[] removed=["A P P E A R A N C E","M Y   M U S I C","你的音乐，随时抵达。","让每一句，都有你的风格。","让每一句，更接近原意。","听见原词，也读懂心意。","中文显示，随你选择","单独备份，安心更新。","同步歌曲、准备原词，让中文译文跟上你的音乐。","让歌词，浮在音乐之上。"];
        var localized=removed.Select(text=>UiText.Catalogs.TryGetValue(UiText.Language,out var catalog) && catalog.TryGetValue(text,out var translated)?translated:text).ToHashSet(StringComparer.Ordinal);
        return !Descendants<TextBlock>(root).Any(t=>localized.Contains(t.Text));
    }

    static Rect VisibleInk(Visual visual)
    {
        // GetDescendantBounds includes cached drawings of collapsed theme labels.
        // Ignore those, but retain unclipped ink so actual clipped feedback still fails.
        if(visual is UIElement {Visibility:not Visibility.Visible})return Rect.Empty;
        var bounds=VisualTreeHelper.GetContentBounds(visual);
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(visual);i++)
        {
            if(VisualTreeHelper.GetChild(visual,i) is not Visual child)continue;
            var ink=VisibleInk(child);if(!ink.IsEmpty)bounds.Union(child.TransformToAncestor(visual).TransformBounds(ink));
        }
        return bounds;
    }

    static bool FeedbackFits(FrameworkElement control)
    {
        var ink=VisibleInk(control);
        var bounds=VisualTreeHelper.GetClip(control)?.Bounds??new Rect(control.RenderSize);
        return ink.IsEmpty || ink.Left>=bounds.Left-.75 && ink.Top>=bounds.Top-.75 && ink.Right<=bounds.Right+.75 && ink.Bottom<=bounds.Bottom+.75;
    }

    static void AuditButtonBounds(DependencyObject root,Dictionary<string,object?> checks,string key)
    {
        var buttons=Descendants<ButtonBase>(root).Where(b=>b.IsVisible && b.ActualWidth>0 && b.ActualHeight>0).ToArray();
        var failures=buttons.Where(b=>!FeedbackFits(b)).Select(b=>new{Type=b.GetType().Name,Label=b.Content?.ToString(),b.ActualWidth,b.ActualHeight,Ink=VisualTreeHelper.GetDescendantBounds(b).ToString(),Clip=VisualTreeHelper.GetClip(b)?.Bounds.ToString()}).ToArray();
        checks[key+".buttonCount"]=buttons.Length;checks[key+".buttonFeedbackFits"]=failures.Length==0;checks[key+".buttonFailures"]=failures;
    }

    async Task VerifyControlRenderingAsync(string directory,Dictionary<string,object?> checks)
    {
        string original=settings.UiTheme;bool reduced=settings.ReduceMotion;
        var editor=new AppearanceEditor(new AppSettings(),_=>{},()=>{},()=>"");
        var scroll=new ScrollViewer{Content=editor,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new(16)};
        var window=new Window{Owner=this,Width=1040,Height=920,Content=scroll,ShowInTaskbar=false};
        async Task Settle(){await Task.Delay(220);window.UpdateLayout();}
        try
        {
            AppMotion.SetEnabled(window,true);window.Show();await Settle();
            var tabs=Descendants<TabControl>(editor).Single();
            foreach(var theme in AppThemes.All)
            {
                SelectUiTheme(theme.Id,false);
                foreach(double width in new[]{1040d,740d,560d})
                {
                    window.Width=width;await Settle();
                    for(int index=0;index<tabs.Items.Count;index++)
                    {
                        tabs.SelectedIndex=index;var tab=(TabItem)tabs.Items[index];tab.BringIntoView();tab.Focus();await Settle();
                        string key=$"controls.{theme.Id}.{width}.{index}";
                        checks[key+".tabFeedbackFits"]=FeedbackFits(tab);
                        checks[key+".pageFontWeight"]=((Control)tab.Content).FontWeight.ToString();
                        checks[key+".pageKeepsNormalWeight"]=((Control)tab.Content).FontWeight==FontWeights.Normal;
                        checks[key+".selectionFocus"]=tab.IsSelected && tab.IsKeyboardFocused;
                        var strip=(FrameworkElement)VisualTreeHelper.GetParent(tab);
                        var position=tab.TransformToAncestor(tabs).TransformBounds(new Rect(tab.RenderSize));
                        checks[key+".tabFitsStrip"]=position.Left>=-.5 && position.Right<=tabs.ActualWidth+.5;
                        checks[key+".geometry"]=new{tab.ActualWidth,tab.ActualHeight,Ink=VisualTreeHelper.GetDescendantBounds(tab).ToString(),Clip=VisualTreeHelper.GetClip(tab)?.Bounds.ToString()};
                        AppMotion.PreviewFeedback(tab,true,true);await Settle();
                        checks[key+".pressedFeedbackFits"]=FeedbackFits(tab);
                        VisualVerification.Save(strip,Path.Combine(directory,$"sections-{theme.Id}-{width}-{index}.png"));
                        AppMotion.PreviewFeedback(tab,false);await Settle();
                        AuditButtonBounds(editor,checks,key);
                        var buttons=Descendants<ButtonBase>(editor).Where(b=>b.IsVisible).ToArray();
                        foreach(var button in buttons)AppMotion.PreviewFeedback(button,true);await Settle();
                        AuditButtonBounds(editor,checks,key+".hover");
                        foreach(var button in buttons)AppMotion.PreviewFeedback(button,true,true);await Settle();
                        AuditButtonBounds(editor,checks,key+".press");
                        foreach(var button in buttons)AppMotion.PreviewFeedback(button,false);await Settle();
                        if(index==0)VisualVerification.Save(window,Path.Combine(directory,$"appearance-{theme.Id}-{width}.png"));
                    }
                    tabs.SelectedIndex=0;var first=(TabItem)tabs.Items[0];first.Focus();await Settle();
                    var second=(TabItem)tabs.Items[1];bool wraps=second.TranslatePoint(new Point(),tabs).Y>first.TranslatePoint(new Point(),tabs).Y+1;
                    first.MoveFocus(new TraversalRequest(wraps?FocusNavigationDirection.Down:FocusNavigationDirection.Right));await Settle();
                    checks[$"controls.{theme.Id}.{width}.keyboardNext"]=tabs.SelectedIndex==1;
                    var last=(TabItem)tabs.Items[1];last.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(last),Environment.TickCount,Key.Home){RoutedEvent=Keyboard.KeyDownEvent});await Settle();
                    checks[$"controls.{theme.Id}.{width}.keyboardHome"]=tabs.SelectedIndex==0;
                }
            }
            AppMotion.Configure(true);var selected=(TabItem)tabs.Items[tabs.SelectedIndex];AppMotion.PreviewFeedback(selected,true,true);
            checks["controls.reducedMotionImmediate"]=AppMotion.GetHover(selected)==1 && AppMotion.GetPressed(selected)==1 && !selected.HasAnimatedProperties;
            checks["controls.appearanceDecorationRemoved"]=!Descendants<TextBlock>(editor).Any(t=>t.Text=="A P P E A R A N C E" || t.Text==UiText.T("让每一句，都有你的风格。"));
            checks["controls.appearanceInstructionsPreserved"]=Descendants<TextBlock>(editor).Any(t=>t.Text==UiText.T("每个主题独立记忆。保存保留修改；取消或离开此页还原本次预览。"));
            checks["controls.language"]=UiText.Language;
            var review=new LyricsReviewWindow(coordinator,(_,_,_,_)=>throw new InvalidOperationException("Rendering verification must not capture Apple Music")){Owner=this};
            try{review.Show();await Settle();review.UpdateLayout();checks["controls.reviewDecorationsRemoved"]=DecorationsRemoved(review);VisualVerification.Save(review,Path.Combine(directory,"review.png"));}
            finally{review.Close();}
        }
        finally{editor.Close();window.Close();AppMotion.Configure(reduced);SelectUiTheme(original,false);}
    }
}
