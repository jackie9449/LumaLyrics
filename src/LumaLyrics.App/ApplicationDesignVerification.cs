using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
using Track=LumaLyrics.Core.Track;

namespace LumaLyrics.App;

public partial class MainWindow
{
    async Task VerifyApplicationDesignAsync(string directory,Dictionary<string,object?> checks)
    {
        string originalTheme=settings.UiTheme;bool reduce=settings.ReduceMotion;
        var floating=OverlayAppearance.From(settings);overlay.ApplyStyle();string toolbar=overlay.Toolbar.Background.ToString();
        coordinator.Import(new Track("微光之间","浮光工作室","原创界面样例",96000),"[00:00]微风轻轻推开了窗\n[00:12]城市在远处慢慢发亮\n[00:24]把此刻，轻轻唱成一束光。\n[00:36]让旋律停留在你身旁\n[00:48]每一句，都有回响。\n[01:00]留一束光，给正在听的你", "原创界面样例",false);
        settings.ReduceMotion=false;AppMotion.Configure(false);
        checks["design.dpiScale"]=VisualTreeHelper.GetDpi(this).DpiScaleX;
        checks["design.osVersion"]=Environment.OSVersion.VersionString;
        try
        {
            foreach(var theme in AppThemes.All)
            {
                SelectUiTheme(theme.Id);await Task.Delay(300);
                await VerifyNavigationVisualsAsync(directory,checks,theme.Id);
                var primary=new Button{Content="播放音乐",Margin=new(0,0,10,0)};primary.SetResourceReference(StyleProperty,"PrimaryButton");
                var secondary=new Button{Content="加入播放列表",Margin=new(0)};
                var input=new TextBox{Text="搜索此刻的心情",Margin=new(0,12,0,0)};
                var choice=new ComboBox{ItemsSource=new[]{"按专辑浏览","按歌曲浏览","按艺人浏览"},SelectedIndex=0,Margin=new(0,12,0,0)};
                var toggle=new CheckBox{Content="显示中文译文",Margin=new(0,18,0,4)};
                var rows=new ListBox{ItemsSource=new[]{"微光之间  ·  浮光工作室","安静的海面  ·  原创演示","夜航  ·  原创演示"},Margin=new(0,16,0,0)};
                var slider=new Slider{Value=42,Maximum=100,Margin=new(0,18,0,0)};
                var content=new Grid{Margin=new(28,20,28,28)};content.RowDefinitions.Add(new(){Height=GridLength.Auto});content.RowDefinitions.Add(new());
                var heading=new StackPanel{Margin=new(0,0,0,24)};var eyebrow=new TextBlock{Text="L U M A  /  "+theme.EnglishName,FontSize=10};ThemeManager.Bind(eyebrow,TextBlock.ForegroundProperty,"AccentBrush");heading.Children.Add(eyebrow);heading.Children.Add(new TextBlock{Text="每一个轻触，都有回应。",FontSize=28,FontWeight=FontWeights.SemiBold,Margin=new(0,10,0,8)});
                var description=new TextBlock{Text="安静的界面，流动的细节。让注意力回到正在听的音乐。",FontSize=12};ThemeManager.Bind(description,TextBlock.ForegroundProperty,"MutedBrush");heading.Children.Add(description);content.Children.Add(heading);
                var columns=new Grid();columns.ColumnDefinitions.Add(new());columns.ColumnDefinitions.Add(new());Grid.SetRow(columns,1);content.Children.Add(columns);
                StackPanel Card(int column,string title)
                {
                    var panel=new StackPanel();var card=new Border{Padding=new(24),Margin=new(column==0?0:10,0,column==0?10:0,0),BorderThickness=new(1),Child=panel};ThemeManager.Bind(card,Border.BackgroundProperty,"PanelBrush");ThemeManager.Bind(card,Border.BorderBrushProperty,"BorderBrush");card.SetResourceReference(Border.CornerRadiusProperty,"CardRadius");Grid.SetColumn(card,column);columns.Children.Add(card);panel.Children.Add(new TextBlock{Text=title,FontSize=16,FontWeight=FontWeights.SemiBold,Margin=new(0,0,0,22)});return panel;
                }
                var controls=Card(0,"随心掌控");var actions=new WrapPanel();actions.Children.Add(primary);actions.Children.Add(secondary);controls.Children.Add(actions);controls.Children.Add(input);controls.Children.Add(choice);controls.Children.Add(toggle);controls.Children.Add(slider);
                var collection=Card(1,"此刻，想听这些");collection.Children.Add(rows);
                var busy=new ProgressBar{IsIndeterminate=true,Height=4,Margin=new(0,24,0,0)};collection.Children.Add(busy);
                int initialActive=AppMotion.ActiveFeedbackCount;
                var gallery=new Window{Title="浮光歌词 · 交互设计",Owner=this,Width=940,Height=620,ShowInTaskbar=false};ApplicationWindowFrame.SetContent(gallery,content);gallery.Show();await Task.Delay(340);gallery.UpdateLayout();
                try
                {
                    checks[$"design.{theme.Id}.windowAndCardsSharePalette"]=gallery.Background.ToString()==FindResource("WindowBrush").ToString() && primary.Template==FindResource("AppActionTemplate");
                    AppMotion.PreviewFeedback(primary,false);await Task.Delay(230);var hitBounds=primary.TransformToAncestor(gallery).TransformBounds(new Rect(0,0,primary.ActualWidth,primary.ActualHeight));AppMotion.PreviewFeedback(primary,true);await Task.Delay(55);
                    checks[$"motion.{theme.Id}.hoverInterpolates"]=!AppMotion.Allowed || AppMotion.GetHover(primary) is >0 and <1;
                    await Task.Delay(210);checks[$"motion.{theme.Id}.hoverReachesTarget"]=Math.Abs(AppMotion.GetHover(primary)-1)<.001;
                    var hoverBounds=primary.TransformToAncestor(gallery).TransformBounds(new Rect(0,0,primary.ActualWidth,primary.ActualHeight));checks[$"motion.{theme.Id}.hoverKeepsHitBounds"]=Math.Abs(hoverBounds.Left-hitBounds.Left)<.01 && Math.Abs(hoverBounds.Top-hitBounds.Top)<.01 && Math.Abs(hoverBounds.Width-hitBounds.Width)<.01;
                    VisualVerification.Save(gallery,Path.Combine(directory,$"interaction-{theme.Id}.png"));
                    AppMotion.PreviewFeedback(primary,true,true);await Task.Delay(35);checks[$"motion.{theme.Id}.pressHasFeedback"]=AppMotion.GetPressed(primary)>0;
                    AppMotion.PreviewFeedback(primary,true,false);await Task.Delay(230);checks[$"motion.{theme.Id}.releaseRestoresButton"]=AppMotion.GetPressed(primary)==0;
                    AppMotion.PreviewFeedback(primary,false);await Task.Delay(45);double interrupted=AppMotion.GetHover(primary);AppMotion.PreviewFeedback(primary,true);
                    checks[$"motion.{theme.Id}.interruptionContinuesVisibleValue"]=Math.Abs(AppMotion.GetHover(primary)-interrupted)<.06;
                    await Task.Delay(250);checks[$"motion.{theme.Id}.completedClocksReleased"]=!primary.HasAnimatedProperties;
                    primary.Focus();await Task.Delay(240);checks[$"motion.{theme.Id}.keyboardFocusVisible"]=primary.IsKeyboardFocused && AppMotion.GetFocus(primary)==1;
                    int changes=0;toggle.Checked+=(_,_)=>changes++;toggle.IsChecked=true;rows.SelectedIndex=1;await Task.Delay(240);
                    checks[$"motion.{theme.Id}.switchAnimatesWithoutDuplicateCommands"]=toggle.IsChecked==true && changes==1 && AppMotion.GetSelected(toggle)==1;
                    var knob=(Grid)toggle.Template.FindName("SwitchKnob",toggle);checks[$"motion.{theme.Id}.switchThumbActuallyMoves"]=knob.RenderTransform is TranslateTransform{X:16} && ((SolidColorBrush)FindResource("SwitchKnobBrush")).Color==(Color)ColorConverter.ConvertFromString(theme.Muted);
                    checks[$"motion.{theme.Id}.listSelectionPreserved"]=rows.SelectedIndex==1 && AppMotion.GetSelected((ListBoxItem)rows.ItemContainerGenerator.ContainerFromIndex(1))==1;
                    input.Focus();await Task.Delay(240);checks[$"motion.{theme.Id}.inputFocusVisible"]=AppMotion.GetFocus(input)==1;
                    choice.IsDropDownOpen=true;await Task.Delay(240);checks[$"motion.{theme.Id}.menuUsesThemedPopup"]=choice.IsDropDownOpen && choice.Template.FindName("PART_Popup",choice) is Popup{Child:Border popup} && popup.Background.ToString()==FindResource("RaisedBrush").ToString();choice.IsDropDownOpen=false;
                    primary.IsEnabled=false;AppMotion.PreviewFeedback(primary,true,true);await Task.Delay(240);checks[$"motion.{theme.Id}.disabledHasNoHoverOrPress"]=AppMotion.GetHover(primary)==0 && AppMotion.GetPressed(primary)==0 && AppMotion.GetAvailability(primary)<.5;primary.IsEnabled=true;
                    AppMotion.Configure(true);AppMotion.PreviewFeedback(primary,true,true);AppMotion.Reveal(content);
                    checks[$"motion.{theme.Id}.reducedMotionIsImmediate"]=AppMotion.GetHover(primary)==1 && AppMotion.GetPressed(primary)==1 && !primary.HasAnimatedProperties && AppMotion.ActiveEntranceCount==0;
                    checks[$"motion.{theme.Id}.reducedMotionDisablesMenuAnimation"]=Equals(System.Windows.Application.Current.Resources["AppPopupAnimation"],PopupAnimation.None);
                    await Task.Delay(40);var pulse=(Border)busy.Template.FindName("Pulse",busy);checks[$"motion.{theme.Id}.reducedMotionKeepsBusyIndicator"]=pulse.IsVisible && pulse.RenderTransform is TranslateTransform{X:0,HasAnimatedProperties:false};
                    AppMotion.PreviewFeedback(primary,false);AppMotion.Configure(false);
                }
                finally{gallery.Close();await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);}
                checks[$"motion.{theme.Id}.closedWindowDetachesFeedback"]=AppMotion.ActiveFeedbackCount==initialActive;

                var preferences=new SettingsWindow(coordinator,id=>SelectUiTheme(id)){Owner=this};preferences.Show();await Task.Delay(340);
                try
                {
                    var tabs=Descendants<TabControl>(preferences).Single();
                    checks[$"design.{theme.Id}.settingsUsesSharedChrome"]=WindowChrome.GetWindowChrome(preferences) is not null && tabs.TabStripPlacement==Dock.Left;
                    for(int page=0;page<tabs.Items.Count;page++)
                    {
                        tabs.SelectedIndex=page;await Task.Delay(340);preferences.UpdateLayout();
                        var host=(ContentPresenter)tabs.Template.FindName("PART_SelectedContentHost",tabs);
                        var location=host.TransformToAncestor(preferences).Transform(new Point());
                        checks[$"design.{theme.Id}.settingsPageFits.{page}"]=host.ActualWidth>300 && location.X>=146 && location.Y+host.ActualHeight<=preferences.ActualHeight;
                        VisualVerification.Save((FrameworkElement)preferences.Content,Path.Combine(directory,$"settings-{theme.Id}-{page}.png"));
                    }
                    foreach(double width in new[]{840d,640d}){preferences.Width=width;await Task.Delay(100);preferences.UpdateLayout();checks[$"design.{theme.Id}.settingsFits.{width:0}"]=tabs.ActualWidth<=preferences.ActualWidth && tabs.ActualWidth>=500;}
                }
                finally{preferences.Close();}
                var import=new ImportWindow(coordinator,null){Owner=this};import.Show();await Task.Delay(340);
                checks[$"design.{theme.Id}.importUsesSharedChrome"]=WindowChrome.GetWindowChrome(import) is not null && import.Background.ToString()==FindResource("WindowBrush").ToString();VisualVerification.Save((FrameworkElement)import.Content,Path.Combine(directory,$"import-{theme.Id}.png"));import.Close();
                ShowLyricsPage();await Task.Delay(320);VisualVerification.Save((FrameworkElement)Content,Path.Combine(directory,$"application-{theme.Id}.png"));
            }
            checks["design.floatingAppearanceUnchanged"]=OverlayAppearance.From(settings)==floating && overlay.Toolbar.Background.ToString()==toolbar && !AppMotion.GetEnabled(overlay);
        }
        finally{settings.ReduceMotion=reduce;AppMotion.Configure(reduce);SelectUiTheme(originalTheme);}
    }
}
