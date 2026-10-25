using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using LumaLyrics.Infrastructure;
using Microsoft.Win32;
namespace LumaLyrics.App;

public sealed class SettingsWindow : Window
{
    readonly TabControl tabs=new();
    readonly Dictionary<string,RadioButton> themeChoices=[];
    readonly ContentControl overlayHost=new(){HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};
    AppearanceEditor? overlayEditor;
    bool closingSettings;
    internal AppearanceEditor? OverlayEditor=>overlayEditor;
    readonly AppSettings settings;
    internal PersonalLibraryPanel PersonalLibrary {get;}
    internal ChineseTranslationPanel ChineseTranslations {get;}
    internal LibraryPanel Library {get;}

    public SettingsWindow(PlaybackCoordinator coordinator,Action<string> changeApplicationTheme)
    {
        settings=coordinator.Settings;
        Title="浮光歌词 · 设置";Width=1120;Height=940;MinWidth=640;MinHeight=520;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        FontFamily=new("Segoe UI Variable, Microsoft YaHei UI");
        ThemeManager.Bind(this,BackgroundProperty,"WindowBrush");ThemeManager.Bind(this,ForegroundProperty,"TextBrush");
        var root=new Grid{Margin=new(24,18,24,22)};ApplicationWindowFrame.SetContent(this,root);
        root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new());root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var heading=new StackPanel{Margin=new(0,0,0,22)};
        heading.Children.Add(Text("P R E F E R E N C E S",10,true));
        heading.Children.Add(new TextBlock{Text="让浮光，更合你心意。",FontSize=25,FontWeight=FontWeights.SemiBold,Margin=new(0,8,0,0)});
        root.Children.Add(heading);Grid.SetRow(tabs,1);root.Children.Add(tabs);
        tabs.Padding=new(0,18,0,0);

        var appPanel=new StackPanel();
        appPanel.Children.Add(new TextBlock{Text="应用界面",FontSize=18,FontWeight=FontWeights.SemiBold,Margin=new(0,0,0,8)});
        appPanel.Children.Add(Text("为主窗口和应用设置挑选一种氛围。选择后立即保存。",12));
        appPanel.Children.Add(Text("两种玻璃主题随当前封面取色，切歌时柔和过渡。",12));
        appPanel.Children.Add(Text("悬浮歌词拥有自己的外观，不会随这里的选择改变。",12));
        foreach(var theme in AppThemes.All)
        {
            var row=new Grid();row.ColumnDefinitions.Add(new(){Width=new(112)});row.ColumnDefinitions.Add(new());
            var swatch=new Border{Height=76,CornerRadius=new(13),Background=Brush(theme.WindowStart),BorderBrush=Brush(theme.Border),BorderThickness=new(1),Margin=new(0,0,18,0)};
            var sample=new StackPanel{Margin=new(16,10,0,0)};
            sample.Children.Add(new TextBlock{Text="浮光",FontSize=20,FontWeight=FontWeights.SemiBold,Foreground=Brush(theme.Text)});
            sample.Children.Add(new TextBlock{Text=theme.EnglishName,FontSize=8,Foreground=Brush(theme.Accent),Margin=new(0,7,0,0)});
            swatch.Child=sample;row.Children.Add(swatch);
            var copy=new StackPanel{VerticalAlignment=VerticalAlignment.Center};
            copy.Children.Add(new TextBlock{Text=theme.Name,FontSize=15,FontWeight=FontWeights.SemiBold,Margin=new(0,0,0,7)});
            copy.Children.Add(Text(theme.Caption,12));Grid.SetColumn(copy,1);row.Children.Add(copy);
            var option=new RadioButton{Content=row,Tag=theme.Id,GroupName="ApplicationTheme",HorizontalAlignment=HorizontalAlignment.Stretch,Margin=new(0,10,0,0),Padding=new(12),IsChecked=AppThemes.Find(settings.UiTheme).Id==theme.Id};
            option.SetResourceReference(StyleProperty,"ThemeChoice");AutomationProperties.SetName(option,theme.Name+"应用主题");
            option.Checked+=(_,_)=>changeApplicationTheme(theme.Id);
            themeChoices.Add(theme.Id,option);appPanel.Children.Add(option);
        }
        tabs.Items.Add(Tab("应用界面",appPanel));

        tabs.Items.Add(Tab("悬浮歌词",overlayHost));
        ((ScrollViewer)((TabItem)tabs.Items[1]).Content).SizeChanged+=(_,_)=>SizeAppearanceEditor();
        ((ScrollViewer)((TabItem)tabs.Items[1]).Content).ScrollChanged+=(_,e)=>{if(e.ViewportHeightChange!=0)SizeAppearanceEditor();};
        tabs.SelectionChanged+=(_,e)=>{
            if(!ReferenceEquals(e.Source,tabs))return;
            if(tabs.SelectedIndex==1)BeginAppearanceEditing();
            else overlayEditor?.Close();
        };
        Closing+=(_,_)=>{closingSettings=true;overlayEditor?.Close();};

        var general=new StackPanel();
        general.Children.Add(Text("常规选项修改后，点击下方“保存常规设置”。",12));
        var nativeOriginal=DialogUi.Check(general,"优先获取 Apple Music 已加载的原语言歌词（本机只读）",settings.AppleMusicOriginalLyrics);
        general.Children.Add(Text("先在 Apple Music 展开当前歌曲的动态歌词，再重新查词。只接收可核对的时间轴；后台不会打开原生窗口或歌词面板。已有可靠缓存与手动绑定优先保留。",12));
        var nativeStatus=Text(coordinator.AppleOriginalStatus,12);general.Children.Add(nativeStatus);
        void RefreshNativeStatus()=>nativeStatus.Text=coordinator.AppleOriginalStatus;
        coordinator.Updated+=RefreshNativeStatus;Closed+=(_,_)=>coordinator.Updated-=RefreshNativeStatus;
        DialogUi.Button(general,"重新查找当前歌曲原词",async()=>await coordinator.LookupAsync(true));
        var online=DialogUi.Check(general,"联网查词（原词：LRCLIB / 网易云；译文准备：网易云公开接口）",settings.OnlineLookup);
        var prefetch=DialogUi.Check(general,"后台预缓存 Apple Music 全部资料库（自动断点继续）",settings.AutoPrefetchLibrary);
        var close=DialogUi.Check(general,"关闭主窗口后继续在托盘运行",settings.CloseToTray);
        var reduce=DialogUi.Check(general,"减少界面动画（含歌词切换）",settings.ReduceMotion);
        var fade=DialogUi.Check(general,"暂停 10 秒后淡出悬浮歌词",settings.FadeOnPause);
        var offset=DialogUi.Field(general,"全局歌词偏移（毫秒；正数延后，负数提前）",settings.GlobalOffsetMs.ToString());
        var toggle=DialogUi.Field(general,"显示 / 隐藏快捷键",settings.ToggleHotkey);
        var unlock=DialogUi.Field(general,"解锁快捷键",settings.UnlockHotkey);
        bool originalStartup=StartupEnabled();
        var startup=DialogUi.Check(general,"登录 Windows 时启动（默认关闭）",originalStartup);
        DialogUi.Button(general,"清理查词响应缓存（保留全部个人资料）",()=>{coordinator.ClearCache();MessageBox.Show(this,"查词响应缓存已清理。个人歌词、封面、绑定与校准均已保留。");});
        var save=DialogUi.Button(general,"保存常规设置",()=>{
            if(!double.TryParse(offset.Text,out var off)||!double.IsFinite(off)||Math.Abs(off)>3600000){MessageBox.Show(this,"请检查歌词偏移数值。");return;}
            // Only write the startup preference when the user actually changed it.
            try{if((startup.IsChecked==true)!=originalStartup)SetStartup(startup.IsChecked==true);}
            catch(Exception ex){MessageBox.Show(this,"开机启动保存失败："+ex.Message);return;}
            settings.AppleMusicOriginalLyrics=nativeOriginal.IsChecked==true;settings.OnlineLookup=online.IsChecked==true;settings.AutoPrefetchLibrary=prefetch.IsChecked==true;
            settings.CloseToTray=close.IsChecked==true;settings.ReduceMotion=reduce.IsChecked==true;settings.FadeOnPause=fade.IsChecked==true;
            settings.GlobalOffsetMs=off;settings.ToggleHotkey=toggle.Text;settings.UnlockHotkey=unlock.Text;
            AppMotion.Configure(settings.ReduceMotion);coordinator.SettingsChanged();coordinator.SaveSettings();Close();
        });
        save.SetResourceReference(StyleProperty,"PrimaryButton");tabs.Items.Add(Tab("常规",general));
        PersonalLibrary=new(coordinator.DataRoot);tabs.Items.Add(Tab("个人资料",PersonalLibrary));Closed+=(_,_)=>PersonalLibrary.Cancel();
        ChineseTranslations=new(coordinator);tabs.Items.Add(Tab("中文译文",ChineseTranslations));Closed+=(_,_)=>ChineseTranslations.Dispose();
        Library=new(coordinator);tabs.Items.Add(Tab("资料库",Library));Closed+=(_,_)=>Library.Dispose();
        var done=new Button{Content="完成",MinWidth=88,HorizontalAlignment=HorizontalAlignment.Right,Margin=new(0,20,0,0),IsCancel=true};
        done.Click+=(_,_)=>Close();Grid.SetRow(done,2);root.Children.Add(done);
        Loaded+=(_,_)=>{
            var area=SystemParameters.WorkArea;
            MinWidth=Math.Min(MinWidth,area.Width);MinHeight=Math.Min(MinHeight,area.Height);
            MaxWidth=area.Width;MaxHeight=area.Height;
        };
    }

    static Brush Brush(string value)=>new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
    static TextBlock Text(string text,double size,bool accent=false)
    {
        var label=new TextBlock{Text=text,FontSize=size,TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,7)};
        ThemeManager.Bind(label,TextBlock.ForegroundProperty,accent?"AccentBrush":"MutedBrush");return label;
    }
    static TabItem Tab(string heading,UIElement content)=>new(){Header=heading,Content=new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new(0,0,12,4)}};
    internal void BeginAppearanceEditing()
    {
        if(overlayEditor is null)CreateAppearanceEditor();
        overlayEditor?.BeginEditing();
    }
    void CreateAppearanceEditor()
    {
        if(Owner is not MainWindow main || closingSettings)return;
        var editor=overlayEditor=main.CreateAppearanceEditor();overlayHost.Content=editor;
        editor.SizeChanged+=(_,_)=>SizeAppearanceEditor();SizeAppearanceEditor();
        editor.Completed+=()=>{
            if(!ReferenceEquals(editor,overlayEditor))return;
            overlayEditor=null;overlayHost.Content=null;
            if(!closingSettings && tabs.SelectedIndex==1)CreateAppearanceEditor();
        };
    }
    void SizeAppearanceEditor()
    {
        if(overlayEditor is null)return;
        var viewport=(ScrollViewer)((TabItem)tabs.Items[1]).Content;
        overlayHost.Height=Math.Max(overlayEditor.MinHeight,viewport.ViewportHeight>0?viewport.ViewportHeight:viewport.ActualHeight-4);
    }
    internal void SelectApplicationTheme(string id)=>themeChoices[AppThemes.Find(id).Id].IsChecked=true;
    internal void SelectApplicationTab()=>tabs.SelectedIndex=0;
    internal void SelectGeneralTab()=>tabs.SelectedIndex=2;
    internal void SelectOverlayTab()=>tabs.SelectedIndex=1;
    internal void SelectPersonalTab()=>tabs.SelectedIndex=3;
    internal void SelectTranslationTab()=>tabs.SelectedIndex=4;
    internal void SelectLibraryTab()=>tabs.SelectedIndex=5;
    static bool StartupEnabled(){using var key=Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");return key?.GetValue("LumaLyrics") is not null;}
    static void SetStartup(bool enabled){using var key=Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");if(enabled)key.SetValue("LumaLyrics","\""+System.IO.Path.Combine(AppContext.BaseDirectory,"LumaLyrics.exe")+"\"");else key.DeleteValue("LumaLyrics",false);}
}
