using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using LumaLyrics.Infrastructure;

namespace LumaLyrics.App;

// This editor owns transient hover and controls only. AppearanceEditor owns the edit transaction.
internal sealed class LanguageFontEditor : StackPanel
{
    readonly StackPanel rows = new();
    readonly Button add = new() { Content = "+", Width = 42, HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0,0,0,12) };
    readonly Dictionary<string,InstalledFontPicker> pickers = new();
    readonly Dictionary<string,TextBlock> notices = new();
    OverlayAppearance current = new();
    bool syncing;
    string? hoverLanguage, hoverFamily;
    internal event Action<OverlayAppearance>? Changed;
    internal event Action<OverlayAppearance?>? PreviewChanged;
    internal event Action<string>? PreviewLanguageChanged;
    internal InstalledFontPicker DefaultPicker => pickers[current.FontLanguage];
    internal IReadOnlyDictionary<string,InstalledFontPicker> Pickers => pickers;
    internal Button AddButton => add;
    internal OverlayAppearance? HoverAppearance => hoverLanguage is null || hoverFamily is null ? null : WithFont(current,hoverLanguage,hoverFamily);
    internal LanguageFontEditor()
    {
        Children.Add(rows);Children.Add(add);
        add.ToolTip=UiText.T("添加语言字体");AutomationProperties.SetName(add,UiText.T("添加语言字体"));
        add.Click+=(_,_)=>OpenLanguages();
        Unloaded+=(_,_)=>EndPreview();
    }
    internal void UpdateAppearance(OverlayAppearance appearance) => current=appearance.Normalize();
    internal void Sync(OverlayAppearance appearance)
    {
        current=appearance.Normalize();
        string[] tags=new[]{current.FontLanguage}.Concat(current.LanguageFonts.Languages).ToArray();
        if(!pickers.Keys.SequenceEqual(tags))
        {
            EndPreview();rows.Children.Clear();pickers.Clear();notices.Clear();
            foreach(string tag in tags)BuildRow(tag,tag==current.FontLanguage);
        }
        syncing=true;
        foreach(var (tag,picker) in pickers)
        {
            string selected=tag==current.FontLanguage?current.Font:current.LanguageFonts.Fonts[tag];
            picker.SelectedItem=picker.Items.Cast<string>().FirstOrDefault(f=>f.Equals(selected,StringComparison.OrdinalIgnoreCase));
            notices[tag].Text=picker.SelectedItem is null?UiText.F($"已保存：{selected}；当前不可用或不适合此语言，显示时使用内置字体。") : "";
            notices[tag].Visibility=picker.SelectedItem is null?Visibility.Visible:Visibility.Collapsed;
        }
        syncing=false;add.IsEnabled=pickers.Count<LyricFontLanguages.All.Count;
    }
    void BuildRow(string tag,bool first)
    {
        var spec=LyricFontLanguages.Get(tag);
        var heading=new DockPanel{Margin=new(0,0,0,5)};
        if(!first)
        {
            var remove=new Button{Content="−",Width=32,Padding=new(4),Margin=new(6,0,0,0),ToolTip=UiText.T("移除此语言字体")};
            AutomationProperties.SetName(remove,UiText.T("移除此语言字体")+" · "+UiText.T(spec.Name));
            DockPanel.SetDock(remove,Dock.Right);heading.Children.Add(remove);remove.Click+=(_,_)=>RemoveLanguage(tag);
        }
        var label=new TextBlock{Text=UiText.T(spec.Name)+(first?UiText.T(" · 系统语言字体"):""),TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center};
        ThemeManager.Bind(label,TextBlock.ForegroundProperty,"MutedBrush");heading.Children.Add(label);rows.Children.Add(heading);
        var picker=new InstalledFontPicker(spec.Sample){ItemsSource=LanguageFontCatalog.For(tag),MinWidth=80,Margin=new(0,0,0,5)};
        AutomationProperties.SetName(picker,UiText.T(spec.Name)+" · "+UiText.T("字体"));pickers[tag]=picker;rows.Children.Add(picker);
        var notice=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new(0,0,0,12)};ThemeManager.Bind(notice,TextBlock.ForegroundProperty,"MutedBrush");notices[tag]=notice;rows.Children.Add(notice);
        picker.SelectionChanged+=(_,_)=>{if(!syncing && picker.SelectedItem is string family){EndPreview();current=WithFont(current,tag,family);Changed?.Invoke(current);Sync(current);}};
        picker.PreviewFontChanged+=family=>
        {
            if(syncing)return;
            if(family is not null){hoverLanguage=tag;hoverFamily=family;PreviewLanguageChanged?.Invoke(tag);}
            else if(hoverLanguage==tag){hoverLanguage=null;hoverFamily=null;}
            PreviewChanged?.Invoke(HoverAppearance);
        };
        picker.DropDownOpened+=(_,_)=>PreviewLanguageChanged?.Invoke(tag);
    }
    static OverlayAppearance WithFont(OverlayAppearance a,string tag,string family)=>tag==a.FontLanguage?a with{Font=family}:a with{LanguageFonts=a.LanguageFonts.Set(tag,family)};
    internal void EndPreview()
    {
        foreach(var picker in pickers.Values)picker.EndPreview();
        hoverLanguage=null;hoverFamily=null;
    }
    internal bool AddLanguage(string tag)
    {
        if(LyricFontLanguages.NormalizeTag(tag) is not {} normalized || pickers.ContainsKey(normalized))return false;
        EndPreview();current=current with{LanguageFonts=current.LanguageFonts.Set(normalized,LyricFontLanguages.Get(normalized).Fallback)};
        Changed?.Invoke(current);Sync(current);PreviewLanguageChanged?.Invoke(normalized);pickers[normalized].Focus();return true;
    }
    internal void RemoveLanguage(string tag)
    {
        if(tag==current.FontLanguage || !pickers.ContainsKey(tag))return;
        EndPreview();current=current with{LanguageFonts=current.LanguageFonts.Remove(tag)};Changed?.Invoke(current);Sync(current);add.Focus();
    }
    internal void OpenLanguages()
    {
        var menu=new ContextMenu{PlacementTarget=add,MaxHeight=360};
        ScrollViewer.SetVerticalScrollBarVisibility(menu,ScrollBarVisibility.Auto);
        foreach(var language in LyricFontLanguages.All.Where(l=>!pickers.ContainsKey(l.Tag)))
        {
            var item=new MenuItem{Header=UiText.T(language.Name),Tag=language.Tag};
            item.Click+=(_,_)=>AddLanguage(language.Tag);menu.Items.Add(item);
        }
        add.ContextMenu=menu;menu.IsOpen=true;
    }
}
