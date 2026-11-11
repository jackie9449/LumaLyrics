using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using LumaLyrics.Core;
using Microsoft.Win32;
namespace LumaLyrics.App;

public sealed class ChineseTranslationPanel : StackPanel,IDisposable
{
    readonly PlaybackCoordinator coordinator;readonly CheckBox main,floating;readonly TextBlock progress=new(),source=new();
    readonly Button? prepare,pause,retry,import;
    bool syncing,disposed; readonly TranslationJobListPanel? jobs;
    TextBlock? scriptPreview;
    public ChineseTranslationPanel(PlaybackCoordinator coordinator,bool includeJobs=true)
    {
        this.coordinator=coordinator;
        if(!includeJobs)
        {
            Children.Add(LibraryWorkspaceUi.Heading(UiText.T("中文显示，随你选择"),UiText.T("译文开关独立记忆，简繁显示同步；修改后立即保存。")));
            var switches=new System.Windows.Controls.Primitives.UniformGrid{Columns=2,Margin=new(0,0,-10,0)};
            var mainPanel=new StackPanel();mainPanel.Children.Add(LibraryWorkspaceUi.Text(UiText.T("主窗口"),15,false));var mainHint=LibraryWorkspaceUi.Text(UiText.T("在原词下方显示中文。"),11);mainHint.Margin=new(0,5,0,12);mainPanel.Children.Add(mainHint);main=DialogUi.Check(mainPanel,UiText.T("主窗口显示中文译文"),coordinator.Settings.ShowChineseTranslation);var mainCard=LibraryWorkspaceUi.Card(mainPanel);mainCard.Margin=new(0,0,10,12);switches.Children.Add(mainCard);
            var floatingPanel=new StackPanel();floatingPanel.Children.Add(LibraryWorkspaceUi.Text(UiText.T("桌面歌词"),15,false));var floatingHint=LibraryWorkspaceUi.Text(UiText.T("与悬浮工具栏开关同步。"),11);floatingHint.Margin=new(0,5,0,12);floatingPanel.Children.Add(floatingHint);floating=DialogUi.Check(floatingPanel,UiText.T("悬浮歌词显示中文译文"),coordinator.Settings.OverlayShowChineseTranslation);var floatingCard=LibraryWorkspaceUi.Card(floatingPanel);floatingCard.Margin=new(0,0,10,12);switches.Children.Add(floatingCard);Children.Add(switches);switches.SizeChanged+=(_,_)=>switches.Columns=switches.ActualWidth>=560?2:1;
            AddScriptChoice();
            var preview=new StackPanel();preview.Children.Add(LibraryWorkspaceUi.Text(UiText.T("双语预览"),11));var original=LibraryWorkspaceUi.Text("The stars will guide us home",20,false);original.Margin=new(0,16,0,4);preview.Children.Add(original);var translated=LibraryWorkspaceUi.Text("星光会指引我们归家",14);preview.Children.Add(translated);var note=LibraryWorkspaceUi.Text(UiText.T("原文保持主位，中文补充其下。间奏和纯音乐保持留白。"),11);note.Margin=new(0,16,0,0);preview.Children.Add(note);Children.Add(LibraryWorkspaceUi.Card(preview,20));
            scriptPreview=translated;
            var attribution=new StackPanel();attribution.Children.Add(LibraryWorkspaceUi.Text(UiText.T("当前译文来源"),14,false));source.TextWrapping=TextWrapping.Wrap;source.FontSize=12;source.LineHeight=20;source.Margin=new(0,8,0,0);ThemeManager.Bind(source,TextBlock.ForegroundProperty,"MutedBrush");attribution.Children.Add(source);
            var reviewCurrent=DialogUi.Button(attribution,UiText.T("以 Apple Music 原词校对…"),OpenReview);AutomationProperties.SetName(reviewCurrent,UiText.T("打开 Apple Music 原词校对"));Children.Add(LibraryWorkspaceUi.Card(attribution));
            AutomationProperties.SetName(main,UiText.T("主窗口中文译文开关"));AutomationProperties.SetName(floating,UiText.T("悬浮歌词中文译文开关"));main.Checked+=VisibilityChanged;main.Unchecked+=VisibilityChanged;floating.Checked+=VisibilityChanged;floating.Unchecked+=VisibilityChanged;
            Attach();return;
        }
        var title=new TextBlock{Text=UiText.T("听见原词，也读懂心意。"),FontSize=20,Margin=new(0,0,0,12)};ThemeManager.BindDisplay(title);Children.Add(title);
        AddText(UiText.T("中文译文跟随原词逐行显示。找不到可靠对应时，译文留空。"),12);
        main=DialogUi.Check(this,UiText.T("主窗口显示中文译文"),coordinator.Settings.ShowChineseTranslation);
        floating=DialogUi.Check(this,UiText.T("悬浮歌词显示中文译文"),coordinator.Settings.OverlayShowChineseTranslation);
        main.Margin=new(0,8,0,4);floating.Margin=new(0,4,0,14);
        AutomationProperties.SetName(main,UiText.T("主窗口中文译文开关"));AutomationProperties.SetName(floating,UiText.T("悬浮歌词中文译文开关"));
        main.Checked+=VisibilityChanged;main.Unchecked+=VisibilityChanged;floating.Checked+=VisibilityChanged;floating.Unchecked+=VisibilityChanged;
        AddScriptChoice();
        var sample=new StackPanel{Margin=new(18,14,18,14)};
        sample.Children.Add(new TextBlock{Text="The stars will guide us home",FontSize=19,TextWrapping=TextWrapping.Wrap});
        var sampleChinese=new TextBlock{Text="星光会指引我们归家",FontSize=14,Margin=new(0,6,0,0)};ThemeManager.Bind(sampleChinese,TextBlock.ForegroundProperty,"MutedBrush");sample.Children.Add(sampleChinese);
        scriptPreview=sampleChinese;
        var card=new Border{Child=sample,CornerRadius=new(14),BorderThickness=new(1),Margin=new(0,0,0,18)};ThemeManager.Bind(card,Border.BackgroundProperty,"RaisedBrush");ThemeManager.Bind(card,Border.BorderBrushProperty,"BorderBrush");Children.Add(card);
        ThemeManager.Bind(card,Border.CornerRadiusProperty,"PreviewCardRadius");
        AddText(UiText.T("资料库中文译文"),15);progress.TextWrapping=TextWrapping.Wrap;progress.LineHeight=22;progress.FontSize=12;ThemeManager.Bind(progress,TextBlock.ForegroundProperty,"MutedBrush");Children.Add(progress);
        var actions=new WrapPanel{Margin=new(0,2,0,12)};Children.Add(actions);
        prepare=DialogUi.Button(actions,UiText.T("查找 / 继续查找来源译文"),()=>_=coordinator.StartTranslationPreparationAsync());prepare.SetResourceReference(StyleProperty,"PrimaryButton");
        pause=DialogUi.Button(actions,UiText.T("暂停"),coordinator.PauseTranslationPreparation);
        retry=DialogUi.Button(actions,UiText.T("重试待补齐"),()=>_=coordinator.StartTranslationPreparationAsync(true));
        AddText(UiText.T("准备时向网易云音乐公开接口发送歌名和歌手，并在本机核对专辑、版本、时长及完整原文。来源没有译文或对应未通过时保持留空。"),11);
        AddText(UiText.T("来源译文与 AI 译文分别统计，可在当前歌曲查看来源。所有译文保存在个人资料中，可单独导出。"),11);
        AddText(UiText.T("当前歌曲"),15);source.TextWrapping=TextWrapping.Wrap;source.FontSize=12;source.LineHeight=21;ThemeManager.Bind(source,TextBlock.ForegroundProperty,"MutedBrush");Children.Add(source);
        import=DialogUi.Button(this,UiText.T("导入当前歌曲的中文 LRC…"),Import);
        var review=DialogUi.Button(this,UiText.T("以 Apple Music 原词校对…"),OpenReview);AutomationProperties.SetName(review,UiText.T("打开 Apple Music 原词校对"));
        if(includeJobs){jobs=new(coordinator);Children.Add(jobs);}
        else{for(int i=Children.Count-1;i>=5;i--)if(!ReferenceEquals(Children[i],source))Children.RemoveAt(i);}
        Attach();
    }
    void AddScriptChoice()
    {
        var panel=new StackPanel();panel.Children.Add(LibraryWorkspaceUi.Text(UiText.T("歌词简繁显示"),14,false));
        var hint=LibraryWorkspaceUi.Text(UiText.T("中文原词与译文统一使用所选字形，主窗口和悬浮歌词同步。只改变显示，原始歌词保持不变。"),11);hint.Margin=new(0,6,0,12);panel.Children.Add(hint);
        var selector=ChineseScriptDisplay.CreateSelector(coordinator);selector.HorizontalAlignment=HorizontalAlignment.Left;panel.Children.Add(selector);
        Children.Add(LibraryWorkspaceUi.Card(panel));
    }
    void OpenReview(){if(Window.GetWindow(this) is { } owner && owner.Owner is MainWindow main)main.ShowLyricsReview(owner);}
    void Attach(){Loaded+=(_,_)=>{if(disposed)return;coordinator.Updated+=Refresh;Refresh();};Unloaded+=(_,_)=>coordinator.Updated-=Refresh;Refresh();}
    void AddText(string text,double size)
    {var label=new TextBlock{Text=text,FontSize=size,TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,size>=15?9:7)};if(size<15)ThemeManager.Bind(label,TextBlock.ForegroundProperty,"MutedBrush");Children.Add(label);}
    void VisibilityChanged(object s,RoutedEventArgs e){if(!syncing)coordinator.SetTranslationVisibility(main.IsChecked==true,floating.IsChecked==true);}
    void Refresh()
    {
        syncing=true;
        try{main.IsChecked=coordinator.Settings.ShowChineseTranslation;floating.IsChecked=coordinator.Settings.OverlayShowChineseTranslation;}
        finally{syncing=false;}
        if(scriptPreview is not null)
        {
            try{scriptPreview.Text=LumaLyrics.Windows.ChineseScriptConverter.Convert("星光会指引我们归家",coordinator.Settings.ChineseScript);}
            catch(InvalidOperationException){scriptPreview.Text="星光会指引我们归家";}
        }
        progress.Text=UiText.Status(coordinator.TranslationProgress.ToString());source.Text=UiText.Status(coordinator.TranslationStatus);
        bool running=coordinator.TranslationProgress.Running;if(prepare is not null)prepare.IsEnabled=retry!.IsEnabled=!running&&coordinator.Settings.OnlineLookup;if(pause is not null)pause.IsEnabled=running;
        if(import is not null)import.IsEnabled=coordinator.Document is {IsSynced:true,Instrumental:false};
    }
    void Import()
    {
        var expected=coordinator.ActiveTrack;string? hash=coordinator.Document is { } doc?TranslationAlignment.Hash(doc.Raw):null;
        var dialog=new OpenFileDialog{Filter=UiText.T("中文逐行歌词|*.lrc"),CheckFileExists=true};if(dialog.ShowDialog(Window.GetWindow(this))!=true)return;
        try
        {
            if(new FileInfo(dialog.FileName).Length>2*1024*1024)throw new InvalidDataException(UiText.T("中文 LRC 不能超过 2 MB。"));
            if(expected!=coordinator.ActiveTrack || coordinator.Document is null || TranslationAlignment.Hash(coordinator.Document.Raw)!=hash)throw new InvalidDataException(UiText.T("当前歌曲或原词已经变化，请为当前歌曲重新选择译文。"));
            coordinator.ImportChineseTranslation(File.ReadAllText(dialog.FileName,new UTF8Encoding(false,true)));
        }
        catch(Exception e) when(e is IOException or InvalidDataException or DecoderFallbackException){MessageBox.Show(Window.GetWindow(this),UiText.Status(e.Message),UiText.T("中文译文导入"));}
    }
    internal void SetVisibility(bool showMain,bool showFloating){main.IsChecked=showMain;floating.IsChecked=showFloating;}
    public void Dispose(){disposed=true;coordinator.Updated-=Refresh;jobs?.Dispose();}
}
