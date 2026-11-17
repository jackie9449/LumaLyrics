using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

public partial class MainWindow
{
    async Task VerifySearchHintsAsync(string directory,Dictionary<string,object?> checks)
    {
        string theme=settings.UiTheme;
        using var lyrics=new TranslationJobListPanel(coordinator);
        using var browser=new MusicLibraryView(_=>Task.FromResult(new MusicLibraryResult([],"")),(_,_)=>Task.FromResult(new LibraryPlayResult(false,"")),()=>{},()=>{},()=>{},vm,()=>{},()=>{},()=>{});
        var window=new Window{Owner=this,Width=980,Height=720,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        ThemeManager.Bind(window,Control.BackgroundProperty,"WindowBrush");ThemeManager.Bind(window,Control.ForegroundProperty,"TextBrush");
        try
        {
            window.Show();window.Activate();
            foreach(var appTheme in AppThemes.All)
            {
                SelectUiTheme(appTheme.Id,false);
                foreach(var (name,page,prompt) in new (string,FrameworkElement,string)[]{("lyrics",lyrics,"搜索歌名、歌手或专辑"),("music",browser,"搜索歌曲、艺人、专辑或歌单")})
                {
                    window.Content=page;await Task.Delay(80);window.UpdateLayout();
                    var hint=Descendants<TextBlock>(page).Single(t=>t.Text==UiText.T(prompt));
                    var input=(hint.Parent as Panel)!.Children.OfType<TextBox>().Single();
                    var other=Descendants<Button>(page).First(b=>b.IsEnabled&&b.IsVisible);
                    input.Clear();other.Focus();await Task.Delay(20);
                    string key=$"searchHint.{appTheme.Id}.{name}";
                    checks[key+".emptyUnfocusedShowsPrompt"]=hint.Visibility==Visibility.Visible&&!input.IsKeyboardFocusWithin;
                    checks[key+".focusReachesInput"]=input.Focus();await Task.Delay(20);
                    checks[key+".focusedCaretHasNoPrompt"]=input.IsKeyboardFocusWithin&&hint.Visibility==Visibility.Collapsed;
                    VisualVerification.Save((FrameworkElement)hint.Parent,Path.Combine(directory,$"search-focused-{appTheme.Id}-{name}.png"));
                    input.Text="Own fixture search";other.Focus();await Task.Delay(20);
                    checks[key+".typedTextKeepsPromptHidden"]=input.Text=="Own fixture search"&&hint.Visibility==Visibility.Collapsed;
                    input.Focus();input.Clear();await Task.Delay(20);
                    checks[key+".clearingWhileFocusedDoesNotOverlap"]=input.IsKeyboardFocusWithin&&hint.Visibility==Visibility.Collapsed;
                    other.Focus();await Task.Delay(20);
                    checks[key+".emptyBlurRestoresPrompt"]=hint.Visibility==Visibility.Visible;
                    checks[key+".promptDoesNotInterceptInput"]=!hint.IsHitTestVisible&&!string.IsNullOrWhiteSpace(AutomationProperties.GetName(input));
                    VisualVerification.Save((FrameworkElement)hint.Parent,Path.Combine(directory,$"search-idle-{appTheme.Id}-{name}.png"));
                }
            }
        }
        finally{window.Close();SelectUiTheme(theme,false);}
    }
}
