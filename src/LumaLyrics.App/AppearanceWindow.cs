using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Automation;
using LumaLyrics.Infrastructure;
namespace LumaLyrics.App;

public sealed class AppearanceWindow : Window
{
    readonly AppSettings settings;
    readonly Action<OverlayAppearance?> previewChanged;
    readonly Action saveSettings;
    readonly Func<string> materialStatus;
    readonly OverlayThemeSession edit;
    OverlayAppearance current=>edit.Current;
    readonly OverlaySurface preview=new();
    readonly GlassPreviewScene scene;
    readonly TextBlock subtitle=new(),status=new(),validation=new(){Foreground=Brushes.Salmon,FontSize=11};
    readonly ListBox themes=new(){Padding=new(0),HorizontalContentAlignment=HorizontalAlignment.Stretch};
    readonly Dictionary<string,Slider> sliders=[];
    readonly Dictionary<string,TextBlock> values=[];
    readonly Dictionary<string,CheckBox> checks=[];
    readonly Dictionary<string,TextBox> colors=[];
    InstalledFontPicker font=null!;
    ComboBox weight=null!,alignment=null!;
    string? hoverFont;
    Button save=null!;
    bool syncing=true,committed;
    internal bool Saved=>committed;
    internal InstalledFontPicker FontPicker=>font;
    internal OverlaySurface PreviewSurface=>preview;
    readonly PlayerViewModel previewText=new(){Current="把此刻，轻轻唱成一束光。",Next="让旋律停留在你身旁"};
    int previewPhrase;
    public AppearanceWindow(AppSettings settings,Action<OverlayAppearance?> previewChanged,Action saveSettings,Func<string> materialStatus)
    {
        this.settings=settings;this.previewChanged=previewChanged;this.saveSettings=saveSettings;this.materialStatus=materialStatus;
        edit=new(settings);
        scene=new(preview);
        Title="浮光歌词 · 悬浮歌词独立外观";Width=1120;Height=850;MinWidth=920;MinHeight=720;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        ThemeManager.ApplyOverlayResources(this,current);
        ThemeManager.Bind(this,BackgroundProperty,"WindowBrush");ThemeManager.Bind(this,ForegroundProperty,"TextBrush");FontFamily=new("Segoe UI Variable, Microsoft YaHei UI");
        Resources.MergedDictionaries.Add(new(){Source=new Uri("/LumaLyrics;component/AppearanceStyles.xaml",UriKind.Relative)});
        var root=new Grid{Margin=new(28,22,28,18)};Content=root;
        root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new());root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var heading=new StackPanel{Margin=new(0,0,0,22)};
        heading.Children.Add(Label("A P P E A R A N C E",10,"#B8AEED"));
        heading.Children.Add(new TextBlock{Text="让每一句，都有你的风格。",FontSize=26,FontWeight=FontWeights.SemiBold,Margin=new(0,6,0,7)});
        heading.Children.Add(Label("每个主题分别记住你的调整。实时预览，保存后下次继续使用。",12,"#9BA4B8"));root.Children.Add(heading);
        var body=new Grid();Grid.SetRow(body,1);root.Children.Add(body);body.ColumnDefinitions.Add(new(){Width=new(248)});body.ColumnDefinitions.Add(new(){Width=new(24)});body.ColumnDefinitions.Add(new());
        var left=new Grid();left.RowDefinitions.Add(new(){Height=GridLength.Auto});left.RowDefinitions.Add(new());
        left.Children.Add(new TextBlock{Text="精选主题",FontWeight=FontWeights.SemiBold,FontSize=14,Margin=new(0,0,0,12)});
        Grid.SetRow(themes,1);left.Children.Add(themes);body.Children.Add(left);
        foreach(var theme in OverlayThemes.All)
        {
            var row=new Grid();row.ColumnDefinitions.Add(new(){Width=new(58)});row.ColumnDefinitions.Add(new());
            var swatch=new Border{Width=48,Height=48,CornerRadius=new(14),Background=Brush(theme.Appearance.Background),BorderBrush=Brush(theme.Appearance.Accent),BorderThickness=new(1),HorizontalAlignment=HorizontalAlignment.Left};
            swatch.Child=new TextBlock{Text=theme.Appearance.Transparent?"Aa":"Aa",FontSize=21,FontWeight=FontWeights.SemiBold,Foreground=Brush(theme.Appearance.Color),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};row.Children.Add(swatch);
            var text=new StackPanel{VerticalAlignment=VerticalAlignment.Center};text.Children.Add(new TextBlock{Text=theme.Name,FontSize=14,FontWeight=FontWeights.SemiBold});text.Children.Add(Label(theme.Caption,10,"#A6AFC2"));Grid.SetColumn(text,1);row.Children.Add(text);
            var item=new ListBoxItem{Content=row,Tag=theme,Padding=new(10),Margin=new(0,0,0,5),ToolTip=theme.Caption};
            item.SetResourceReference(StyleProperty,"ThemeItem");themes.Items.Add(item);
        }
        themes.SelectionChanged+=(_,_)=>{if(!syncing && themes.SelectedItem is ListBoxItem{Tag:OverlayTheme theme})SelectTheme(theme);};
        var right=new Grid();Grid.SetColumn(right,2);body.Children.Add(right);right.RowDefinitions.Add(new(){Height=new(236)});right.RowDefinitions.Add(new(){Height=new(16)});right.RowDefinitions.Add(new());
        var previewPanel=new Grid();previewPanel.RowDefinitions.Add(new(){Height=GridLength.Auto});previewPanel.RowDefinitions.Add(new());previewPanel.RowDefinitions.Add(new(){Height=GridLength.Auto});right.Children.Add(previewPanel);
        var previewHeader=new DockPanel{Margin=new(0,0,0,10)};previewHeader.Children.Add(new TextBlock{Text="实时预览",FontSize=14,FontWeight=FontWeights.SemiBold});
        var backgrounds=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        foreach(var (label,id) in new[]{("深色桌面",0),("浅色桌面",1),("彩色桌面",2)}){var b=new Button{Content=label,FontSize=10,Padding=new(9,5,9,5),Margin=new(6,0,0,0)};b.Click+=(_,_)=>SetScene(id);backgrounds.Children.Add(b);}previewHeader.Children.Add(backgrounds);previewPanel.Children.Add(previewHeader);
        var tryMotion=new Button{Content="试播切换",FontSize=10,Padding=new(9,5,9,5),Margin=new(6,0,0,0)};
        tryMotion.Click+=(_,_)=>{string[] phrases=["把此刻，轻轻唱成一束光。","让旋律停留在你身旁","下一句，随微风慢慢抵达。"];previewPhrase=(previewPhrase+1)%phrases.Length;previewText.Current=phrases[previewPhrase];previewText.Next=phrases[(previewPhrase+1)%phrases.Length];};backgrounds.Children.Insert(0,tryMotion);
        Grid.SetRow(scene,1);previewPanel.Children.Add(scene);
        preview.DataContext=previewText;
        subtitle.FontSize=10;ThemeManager.Bind(subtitle,TextBlock.ForegroundProperty,"MutedBrush");subtitle.Margin=new(0,7,0,0);Grid.SetRow(subtitle,2);previewPanel.Children.Add(subtitle);
        SetScene(2);
        var tabs=new TabControl{Background=Brushes.Transparent,BorderThickness=new(0),Padding=new(0,12,0,0)};Grid.SetRow(tabs,2);right.Children.Add(tabs);
        var typography=Columns();tabs.Items.Add(Tab("文字与排版",typography.Grid));
        typography.Left.Children.Add(Label("字体 · 悬停预览，点击选定"));
        var savedFonts=settings.OverlayThemeProfiles?.Values.Where(a=>a is not null).Select(a=>a.Normalize().Font)??[];
        font=new InstalledFontPicker{ItemsSource=Fonts.SystemFontFamilies.Select(f=>f.Source).Concat(savedFonts).Append(current.Font).Distinct().OrderBy(x=>x).ToArray(),Margin=new(0,0,0,16),MinWidth=80};
        AutomationProperties.SetName(font,"字体");typography.Left.Children.Add(font);
        font.SelectionChanged+=(_,_)=>{if(!syncing)Change(a=>a with{Font=font.SelectedItem?.ToString()??a.Font});};
        font.PreviewFontChanged+=family=>{hoverFont=family;previewChanged(family is null?null:current with{Font=family});RefreshPreview();};
        weight=Choice(typography.Right,"字重",["常规","中等","半粗","粗体"],a=>a with{Weight=new[]{"Normal","Medium","SemiBold","Bold"}[Math.Max(0,weight.SelectedIndex)]});
        AddSlider(typography.Left,"size","主歌词字号",18,96,1,a=>a with{FontSize=sliders["size"].Value});
        AddSlider(typography.Right,"nextScale","次行字号比例",.45,1,.05,a=>a with{NextScale=sliders["nextScale"].Value},true);
        AddColor(typography.Left,"text","文字颜色",a=>a.Color,(a,v)=>a with{Color=v,AdaptiveContrast=false});
        AddColor(typography.Right,"accent","强调色",a=>a.Accent,(a,v)=>a with{Accent=v});
        alignment=Choice(typography.Left,"文字对齐",["居左","居中","居右"],a=>a with{Alignment=new[]{"Left","Center","Right"}[Math.Max(0,alignment.SelectedIndex)]});
        AddSlider(typography.Right,"nextOpacity","次行文字不透明度",.15,1,.05,a=>a with{NextOpacity=sliders["nextOpacity"].Value},true);
        AddCheck(typography.Left,"two","显示下一句歌词",a=>a with{TwoLines=checks["two"].IsChecked==true});
        AddCheck(typography.Left,"adaptive","液态玻璃文字随背景明暗调整",a=>a with{AdaptiveContrast=checks["adaptive"].IsChecked==true});
        AddCheck(typography.Right,"shadow","文字柔和阴影",a=>a with{Shadow=checks["shadow"].IsChecked==true});
        AddSlider(typography.Right,"shadowStrength","阴影强度",0,1,.05,a=>a with{ShadowStrength=sliders["shadowStrength"].Value},true);
        AddCheck(typography.Left,"animations","歌词切换动画",a=>a with{Animations=checks["animations"].IsChecked==true});
        AddSlider(typography.Right,"transition","切换时长",120,500,20,a=>a with{TransitionMs=sliders["transition"].Value});
        var layout=Columns();tabs.Items.Add(Tab("背景与布局",layout.Grid));
        AddCheck(layout.Left,"transparent","完全透明背景",a=>a with{Transparent=checks["transparent"].IsChecked==true});
        AddCheck(layout.Right,"blur","系统背景模糊（玻璃主题）",a=>a with{SystemBlur=checks["blur"].IsChecked==true});
        AddColor(layout.Left,"background","背景底色",a=>a.Background,(a,v)=>a with{Background=v});
        AddSlider(layout.Right,"opacity","背景不透明度",0,1,.001,a=>a with{Opacity=sliders["opacity"].Value},true);
        AddSlider(layout.Left,"glass","材质高光 / 渐层强度",0,1,.05,a=>a with{GlassIntensity=sliders["glass"].Value},true);
        AddSlider(layout.Right,"radius","圆角比例",0,1,.01,a=>a with{CornerRatio=sliders["radius"].Value},true);
        AddSlider(layout.Left,"width","桌面歌词宽度",360,1800,20,a=>a with{Width=sliders["width"].Value});
        AddSlider(layout.Right,"padding","卡片留白",8,56,1,a=>a with{Padding=sliders["padding"].Value});
        AddSlider(layout.Left,"gap","两行间距",0,36,1,a=>a with{LineGap=sliders["gap"].Value});
        AddCheck(layout.Right,"border","精细边框与玻璃轮廓",a=>a with{Border=checks["border"].IsChecked==true});
        layout.Left.Children.Add(Label("不透明度同时控制背景模糊、高光和边框，文字保持清晰。圆角 100% 为短边一半的圆角半径。",11,"#8E99AE"));
        var footer=new Grid{Margin=new(0,18,0,0)};Grid.SetRow(footer,2);root.Children.Add(footer);footer.ColumnDefinitions.Add(new());footer.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var info=new StackPanel{VerticalAlignment=VerticalAlignment.Center};status.FontSize=10;ThemeManager.Bind(status,TextBlock.ForegroundProperty,"MutedBrush");status.TextWrapping=TextWrapping.Wrap;info.Children.Add(status);info.Children.Add(validation);footer.Children.Add(info);
        var actions=new StackPanel{Orientation=Orientation.Horizontal};Grid.SetColumn(actions,1);footer.Children.Add(actions);
        var reset=new Button{Content="重置当前主题",Padding=new(12,10,12,10)};reset.Click+=(_,_)=>ResetCurrentTheme();actions.Children.Add(reset);
        var cancel=new Button{Content="取消",Padding=new(16,10,16,10),IsCancel=true};cancel.Click+=(_,_)=>Close();actions.Children.Add(cancel);
        save=new Button{Content="保存外观",Style=(Style)FindResource("PrimaryButton"),Padding=new(22,10,22,10),Margin=new(0),IsDefault=true};save.Click+=(_,_)=>Save();actions.Children.Add(save);
        Loaded+=(_,_)=>{
            var area=SystemParameters.WorkArea;double maxHeight=Math.Max(320,area.Height-24),maxWidth=Math.Max(480,area.Width-24);
            MinHeight=Math.Min(MinHeight,maxHeight);MinWidth=Math.Min(MinWidth,maxWidth);MaxHeight=maxHeight;MaxWidth=maxWidth;
            Height=Math.Min(Height,maxHeight);Width=Math.Min(Width,maxWidth);UpdateLayout();NativeWindow.ClampVisible(this);
        };
        Closing+=(_,_)=>{hoverFont=null;if(!committed){edit.Cancel();previewChanged(null);saveSettings();}else previewChanged(null);};
        Sync();
    }
    static Brush Brush(string hex)=>new SolidColorBrush(OverlaySurface.ColorOf(hex));
    static TextBlock Label(string text,double size=12,string color="#C8CFDF")
    {
        var label=new TextBlock{Text=text,FontSize=size,Margin=new(0,0,0,6),TextWrapping=TextWrapping.Wrap};
        ThemeManager.Bind(label,TextBlock.ForegroundProperty,color is "#C7B8FF" or "#B8AEED"?"AccentBrush":"MutedBrush");return label;
    }
    static (Grid Grid,StackPanel Left,StackPanel Right) Columns()
    {
        var grid=new Grid();grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new(){Width=new(24)});grid.ColumnDefinitions.Add(new());
        var left=new StackPanel();var right=new StackPanel();grid.Children.Add(left);Grid.SetColumn(right,2);grid.Children.Add(right);return(grid,left,right);
    }
    static TabItem Tab(string title,UIElement body)=>new(){Header=title,Content=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new(2,0,12,0)}};
    ComboBox Choice(Panel parent,string label,string[] items,Func<OverlayAppearance,OverlayAppearance> change)
    {
        parent.Children.Add(Label(label));var combo=new ComboBox{ItemsSource=items,Margin=new(0,0,0,16),MinWidth=80};AutomationProperties.SetName(combo,label);parent.Children.Add(combo);combo.SelectionChanged+=(_,_)=>{if(!syncing)Change(change);};return combo;
    }
    void AddSlider(Panel panel,string id,string label,double min,double max,double step,Func<OverlayAppearance,OverlayAppearance> change,bool percent=false)
    {
        var row=new DockPanel();var value=values[id]=Label("",11,"#C7B8FF");value.HorizontalAlignment=HorizontalAlignment.Right;DockPanel.SetDock(value,Dock.Right);row.Children.Add(value);row.Children.Add(Label(label));panel.Children.Add(row);
        var slider=sliders[id]=new(){Minimum=min,Maximum=max,TickFrequency=step,SmallChange=step,LargeChange=step*5,IsSnapToTickEnabled=true,Margin=new(0,4,0,19)};AutomationProperties.SetName(slider,label);panel.Children.Add(slider);
        slider.ValueChanged+=(_,_)=>{value.Text=FormatValue(id,slider.Value,percent);if(!syncing)Change(change);};
    }
    static string FormatValue(string id,double value,bool percent)=>percent?(id=="opacity"?$"{value:P1}":$"{value:P0}"):$"{value:0} {(id=="transition"?"ms":"px")}";
    void AddCheck(Panel panel,string id,string text,Func<OverlayAppearance,OverlayAppearance> change)
    {
        var check=checks[id]=new(){Content=text,FontSize=12,Margin=new(0,6,0,18)};panel.Children.Add(check);check.Checked+=(_,_)=>{if(!syncing)Change(change);};check.Unchecked+=(_,_)=>{if(!syncing)Change(change);};
    }
    void AddColor(Panel panel,string id,string title,Func<OverlayAppearance,string> get,Func<OverlayAppearance,string,OverlayAppearance> set)
    {
        panel.Children.Add(Label(title));var box=colors[id]=new(){MaxLength=7,Margin=new(0,0,0,7),FontSize=12,Padding=new(10,7,10,7)};AutomationProperties.SetName(box,title);panel.Children.Add(box);
        var chips=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(0,0,0,15)};panel.Children.Add(chips);
        foreach(var color in new[]{"#F5F7FA","#202B40","#B8DFFF","#D9B8FA","#EDF3E9","#25332C","#F6EFE2","#B07957"})
        {var chip=new Button{Width=20,Height=20,MinHeight=20,Padding=new(0),Margin=new(0,0,5,0),Background=Brush(color),ToolTip=color};AutomationProperties.SetName(chip,title+color);chip.Click+=(_,_)=>box.Text=color;chips.Children.Add(chip);}
        box.TextChanged+=(_,_)=>{if(syncing)return;ValidateColors();if(ValidColor(box.Text))Change(a=>set(a,box.Text.ToUpperInvariant()));};
    }
    static bool ValidColor(string text)=>System.Text.RegularExpressions.Regex.IsMatch(text,"^#[0-9A-Fa-f]{6}$");
    void ValidateColors(){bool valid=colors.Values.All(x=>ValidColor(x.Text));validation.Text=valid?"":"颜色请填写完整的 #RRGGBB。";if(save is not null)save.IsEnabled=valid;}
    void Change(Func<OverlayAppearance,OverlayAppearance> change)
    {
        edit.Update(change(current));previewChanged(hoverFont is null?null:current with{Font=hoverFont});RefreshPreview();
    }
    void SelectTheme(OverlayTheme theme)
    {
        font.EndPreview();edit.Select(theme);previewChanged(null);Sync();
    }
    internal void ResetCurrentTheme(){font.EndPreview();edit.ResetCurrent();previewChanged(null);Sync();}
    void Sync()
    {
        syncing=true;
        themes.SelectedItem=themes.Items.Cast<ListBoxItem>().FirstOrDefault(x=>((OverlayTheme)x.Tag).Name==current.Preset);
        font.SelectedItem=current.Font;weight.SelectedIndex=Array.IndexOf(new[]{"Normal","Medium","SemiBold","Bold"},current.Weight);alignment.SelectedIndex=Array.IndexOf(new[]{"Left","Center","Right"},current.Alignment);
        var data=new Dictionary<string,double>{{"size",current.FontSize},{"nextScale",current.NextScale},{"nextOpacity",current.NextOpacity},{"shadowStrength",current.ShadowStrength},{"opacity",current.Opacity},{"glass",current.GlassIntensity},{"radius",current.CornerRatio!.Value},{"width",current.Width},{"padding",current.Padding},{"gap",current.LineGap},{"transition",current.TransitionMs}};
        foreach(var (id,value) in data){sliders[id].Value=value;values[id].Text=FormatValue(id,value,id is "nextScale" or "nextOpacity" or "shadowStrength" or "opacity" or "glass" or "radius");}
        colors["text"].Text=current.Color;colors["background"].Text=current.Background;colors["accent"].Text=current.Accent;
        checks["two"].IsChecked=current.TwoLines;checks["shadow"].IsChecked=current.Shadow;checks["transparent"].IsChecked=current.Transparent;checks["blur"].IsChecked=current.SystemBlur;checks["border"].IsChecked=current.Border;checks["animations"].IsChecked=current.Animations;
        checks["adaptive"].IsChecked=current.AdaptiveContrast;
        syncing=false;ValidateColors();RefreshPreview();
    }
    void RefreshPreview()
    {
        ThemeManager.ApplyOverlayResources(this,current);
        // Same renderer and actual type sizes as the desktop overlay; scrolling handles oversized previews.
        var effective=hoverFont is null?current:current with{Font=hoverFont};
        preview.Apply(effective,settings.ReduceMotion);status.Text=materialStatus();subtitle.Text=hoverFont is null?$"{current.Preset}  ·  {current.FontSize:0} px  ·  可直接拖动桌面歌词调整位置":$"正在预览 {hoverFont}  ·  点击字体选定，移开恢复";
        bool hasBackground=!current.Transparent;
        sliders["opacity"].IsEnabled=hasBackground;colors["background"].IsEnabled=hasBackground;checks["border"].IsEnabled=hasBackground;
        sliders["glass"].IsEnabled=hasBackground && current.Material is "liquid" or "frost" or "aurora" or "material" or "paper";
        checks["blur"].IsEnabled=hasBackground && current.Material is "liquid" or "frost";
        sliders["shadowStrength"].IsEnabled=current.Shadow;
        checks["adaptive"].IsEnabled=current.Material=="liquid" && !current.Transparent;
        if(checks["adaptive"].IsChecked!=current.AdaptiveContrast){bool wasSyncing=syncing;syncing=true;checks["adaptive"].IsChecked=current.AdaptiveContrast;syncing=wasSyncing;}
        sliders["nextScale"].IsEnabled=sliders["nextOpacity"].IsEnabled=sliders["gap"].IsEnabled=current.TwoLines;
        sliders["transition"].IsEnabled=current.Animations && !settings.ReduceMotion;
        if(settings.ReduceMotion)status.Text+=" · 已开启减少界面动画";
        else if(!SystemParameters.ClientAreaAnimation)status.Text+=" · 系统已关闭界面动画";
    }
    void SetScene(int id)
    {
        scene.SetScene(id);
    }
    internal void PreviewTheme(OverlayTheme theme)=>SelectTheme(theme);
    internal void PreviewOptions(OverlayAppearance options){font.EndPreview();edit.Update(options);previewChanged(null);Sync();}
    internal Dictionary<string,bool> VerifyControls()
    {
        var result=new Dictionary<string,bool>();var before=current;
        font.SelectedIndex=font.SelectedIndex==0?1:0;result["installedFontSelectionUpdatesSettings"]=settings.OverlayFont==font.SelectedItem?.ToString();
        font.IsDropDownOpen=true;UpdateLayout();result["fontDropdownOpens"]=font.Template.FindName("PART_Popup",font) is System.Windows.Controls.Primitives.Popup{IsOpen:true};font.IsDropDownOpen=false;
        sliders["size"].Value=44;result["fontSliderUpdatesSettings"]=settings.OverlayFontSize==44;
        weight.SelectedIndex=3;result["weightUpdatesSettings"]=settings.OverlayFontWeight=="Bold";
        colors["text"].Text="#BAD";result["invalidColorDisablesSave"]=!save.IsEnabled;
        colors["text"].Text="#B8DFFF";result["validColorUpdatesAndEnablesSave"]=save.IsEnabled && settings.OverlayColor=="#B8DFFF";
        checks["transparent"].IsChecked=true;result["clearBackgroundDisablesBackdrop"]=settings.OverlayTransparent && !checks["blur"].IsEnabled;
        sliders["transition"].Value=340;result["transitionDurationUpdatesSettings"]=settings.OverlayTransitionMs==340;
        checks["animations"].IsChecked=false;result["animationSwitchUpdatesSettings"]=!settings.OverlayAnimations && !sliders["transition"].IsEnabled;
        PreviewOptions(before);return result;
    }
    internal void SelectLayoutTab(){foreach(var tab in FindTabs(this))tab.SelectedIndex=1;}
    static IEnumerable<TabControl> FindTabs(DependencyObject root)
    {for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is TabControl tabs)yield return tabs;foreach(var found in FindTabs(child))yield return found;}}
    public void Save()
    {
        ValidateColors();if(!save.IsEnabled)return;font.EndPreview();current.Apply(settings);previewChanged(null);
        try{edit.Save(saveSettings);committed=true;Close();}catch(Exception ex) when(ex is System.IO.IOException or UnauthorizedAccessException){validation.Text="保存失败："+ex.Message;}
    }
}
