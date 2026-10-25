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
    readonly Button prepare,pause,retry,import;
    bool syncing;
    public ChineseTranslationPanel(PlaybackCoordinator coordinator)
    {
        this.coordinator=coordinator;
        var title=new TextBlock{Text="听见原词，也读懂心意。",FontSize=20,Margin=new(0,0,0,12)};ThemeManager.BindDisplay(title);Children.Add(title);
        AddText("中文译文跟随原词逐行显示。找不到可靠对应时，译文留空。",12);
        main=DialogUi.Check(this,"主窗口显示中文译文",coordinator.Settings.ShowChineseTranslation);
        floating=DialogUi.Check(this,"悬浮歌词显示中文译文",coordinator.Settings.OverlayShowChineseTranslation);
        main.Margin=new(0,8,0,4);floating.Margin=new(0,4,0,14);
        AutomationProperties.SetName(main,"主窗口中文译文开关");AutomationProperties.SetName(floating,"悬浮歌词中文译文开关");
        main.Checked+=VisibilityChanged;main.Unchecked+=VisibilityChanged;floating.Checked+=VisibilityChanged;floating.Unchecked+=VisibilityChanged;
        var sample=new StackPanel{Margin=new(18,14,18,14)};
        sample.Children.Add(new TextBlock{Text="The stars will guide us home",FontSize=19,TextWrapping=TextWrapping.Wrap});
        var sampleChinese=new TextBlock{Text="星光会指引我们归家",FontSize=14,Margin=new(0,6,0,0)};ThemeManager.Bind(sampleChinese,TextBlock.ForegroundProperty,"MutedBrush");sample.Children.Add(sampleChinese);
        var card=new Border{Child=sample,CornerRadius=new(14),BorderThickness=new(1),Margin=new(0,0,0,18)};ThemeManager.Bind(card,Border.BackgroundProperty,"RaisedBrush");ThemeManager.Bind(card,Border.BorderBrushProperty,"BorderBrush");Children.Add(card);
        ThemeManager.Bind(card,Border.CornerRadiusProperty,"PreviewCardRadius");
        AddText("资料库中文译文",15);progress.TextWrapping=TextWrapping.Wrap;progress.LineHeight=22;progress.FontSize=12;ThemeManager.Bind(progress,TextBlock.ForegroundProperty,"MutedBrush");Children.Add(progress);
        var actions=new WrapPanel{Margin=new(0,2,0,12)};Children.Add(actions);
        prepare=DialogUi.Button(actions,"查找 / 继续查找来源译文",()=>_=coordinator.StartTranslationPreparationAsync());prepare.SetResourceReference(StyleProperty,"PrimaryButton");
        pause=DialogUi.Button(actions,"暂停",coordinator.PauseTranslationPreparation);
        retry=DialogUi.Button(actions,"重试待补齐",()=>_=coordinator.StartTranslationPreparationAsync(true));
        AddText("准备时向网易云音乐公开接口发送歌名和歌手，并在本机核对专辑、版本、时长及完整原文。来源没有译文或对应未通过时保持留空。",11);
        AddText("来源译文与 AI 译文分别统计，可在当前歌曲查看来源。所有译文保存在个人资料中，可单独导出。",11);
        AddText("当前歌曲",15);source.TextWrapping=TextWrapping.Wrap;source.FontSize=12;source.LineHeight=21;ThemeManager.Bind(source,TextBlock.ForegroundProperty,"MutedBrush");Children.Add(source);
        import=DialogUi.Button(this,"导入当前歌曲的中文 LRC…",Import);
        var review=DialogUi.Button(this,"以 Apple Music 原词校对…",()=>
        {
            if(Window.GetWindow(this) is { } owner && owner.Owner is MainWindow main)main.ShowLyricsReview(owner);
        });AutomationProperties.SetName(review,"打开 Apple Music 原词校对");
        coordinator.Updated+=Refresh;Refresh();
    }
    void AddText(string text,double size)
    {var label=new TextBlock{Text=text,FontSize=size,TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,size>=15?9:7)};if(size<15)ThemeManager.Bind(label,TextBlock.ForegroundProperty,"MutedBrush");Children.Add(label);}
    void VisibilityChanged(object s,RoutedEventArgs e){if(!syncing)coordinator.SetTranslationVisibility(main.IsChecked==true,floating.IsChecked==true);}
    void Refresh()
    {
        syncing=true;
        try{main.IsChecked=coordinator.Settings.ShowChineseTranslation;floating.IsChecked=coordinator.Settings.OverlayShowChineseTranslation;}
        finally{syncing=false;}
        progress.Text=coordinator.TranslationProgress.ToString();source.Text=coordinator.TranslationStatus;
        bool running=coordinator.TranslationProgress.Running;prepare.IsEnabled=retry.IsEnabled=!running&&coordinator.Settings.OnlineLookup;pause.IsEnabled=running;
        import.IsEnabled=coordinator.Document is {IsSynced:true,Instrumental:false};
    }
    void Import()
    {
        var expected=coordinator.ActiveTrack;string? hash=coordinator.Document is { } doc?TranslationAlignment.Hash(doc.Raw):null;
        var dialog=new OpenFileDialog{Filter="中文逐行歌词|*.lrc",CheckFileExists=true};if(dialog.ShowDialog(Window.GetWindow(this))!=true)return;
        try
        {
            if(new FileInfo(dialog.FileName).Length>2*1024*1024)throw new InvalidDataException("中文 LRC 不能超过 2 MB。");
            if(expected!=coordinator.ActiveTrack || coordinator.Document is null || TranslationAlignment.Hash(coordinator.Document.Raw)!=hash)throw new InvalidDataException("当前歌曲或原词已经变化，请为当前歌曲重新选择译文。");
            coordinator.ImportChineseTranslation(File.ReadAllText(dialog.FileName,new UTF8Encoding(false,true)));
        }
        catch(Exception e) when(e is IOException or InvalidDataException or DecoderFallbackException){MessageBox.Show(Window.GetWindow(this),e.Message,"中文译文导入");}
    }
    internal void SetVisibility(bool showMain,bool showFloating){main.IsChecked=showMain;floating.IsChecked=showFloating;}
    public void Dispose()=>coordinator.Updated-=Refresh;
}
