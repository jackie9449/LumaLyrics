using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
using LumaLyrics.Windows;
namespace LumaLyrics.App;

internal sealed class LyricsReviewWindow:Window
{
    readonly PlaybackCoordinator coordinator;
    readonly Func<Track,LyricsDocument,bool,CancellationToken,Task<(LyricsOcrObservation Observation,NativeLyricsCapture Capture)>> capture;
    readonly TextBlock current=new(){FontSize=15,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap},status=new(){FontSize=12,LineHeight=21,TextWrapping=TextWrapping.Wrap};
    readonly DataGrid comparisons=new(){IsReadOnly=true,AutoGenerateColumns=false,CanUserAddRows=false,CanUserDeleteRows=false,HeadersVisibility=DataGridHeadersVisibility.Column,GridLinesVisibility=DataGridGridLinesVisibility.None,RowHeaderWidth=0,BorderThickness=new(0),Margin=new(0,18,0,0),MinColumnWidth=90,SelectionMode=DataGridSelectionMode.Single};
    readonly Button recognize,reference,stop;
    CancellationTokenSource? running;
    bool closed;
    internal string Status=>status.Text;
    internal IReadOnlyList<LyricsOcrComparedLine> ComparedLines {get;private set;}=[];
    internal LyricsReviewWindow(PlaybackCoordinator coordinator,Func<Track,LyricsDocument,bool,CancellationToken,Task<(LyricsOcrObservation,NativeLyricsCapture)>> capture)
    {
        this.coordinator=coordinator;this.capture=capture;Title="浮光歌词 · 原词校对";Width=960;Height=730;MinWidth=680;MinHeight=500;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        FontFamily=new("Segoe UI Variable, Microsoft YaHei UI");ThemeManager.Bind(this,BackgroundProperty,"WindowBrush");ThemeManager.Bind(this,ForegroundProperty,"TextBrush");
        var root=new Grid{Margin=new(26,18,26,24)};ApplicationWindowFrame.SetContent(this,root);root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new());
        var heading=new StackPanel();root.Children.Add(heading);heading.Children.Add(new TextBlock{Text="让每一句，更接近原意。",FontSize=25,FontWeight=FontWeights.SemiBold,Margin=new(0,0,0,13)});heading.Children.Add(current);
        var hint=new TextBlock{Text="以 Apple Music 当前歌曲的歌词为参考，查看与个人原词的差异。OCR 只比较可见文字，保留现有时间轴。",FontSize=12,TextWrapping=TextWrapping.Wrap,LineHeight=21,Margin=new(0,10,0,12)};ThemeManager.Bind(hint,TextBlock.ForegroundProperty,"MutedBrush");heading.Children.Add(hint);
        var actions=new WrapPanel();heading.Children.Add(actions);recognize=DialogUi.Button(actions,"OCR 校对可见歌词",()=>_=CaptureAsync(true));recognize.SetResourceReference(StyleProperty,"PrimaryButton");reference=DialogUi.Button(actions,"读取原生文字参考",()=>_=CaptureAsync(false));stop=DialogUi.Button(actions,"停止",()=>running?.Cancel());stop.IsEnabled=false;
        AutomationProperties.SetName(recognize,"识别 Apple Music 当前可见歌词并校对");AutomationProperties.SetName(reference,"读取 Apple Music 原生歌词文字参考");
        var installed=LocalLyricsOcr.Languages;
        var languages=new TextBlock{Text="本机 OCR："+(installed.Count==0?"当前不可用":string.Join("、",installed))+"。未安装的语言可先用原生文字参考。",FontSize=11,TextWrapping=TextWrapping.Wrap,Margin=new(0,12,0,15)};ThemeManager.Bind(languages,TextBlock.ForegroundProperty,"MutedBrush");heading.Children.Add(languages);
        Grid.SetRow(status,1);root.Children.Add(status);ThemeManager.Bind(status,TextBlock.ForegroundProperty,"MutedBrush");status.Text="采集时会短暂显示 Apple Music 歌词面板，完成后恢复窗口与面板状态。记录和截图仅保存在个人资料中。";
        DataGridTextColumn Column(string title,string path,double width)=>new(){Header=title,Binding=new Binding(path),Width=new DataGridLength(width,DataGridLengthUnitType.Star),ElementStyle=new Style(typeof(TextBlock)){Setters={new Setter(TextBlock.TextWrappingProperty,TextWrapping.Wrap),new Setter(FrameworkElement.MarginProperty,new Thickness(8,10,8,10))}}};
        comparisons.Columns.Add(Column("比对",nameof(ReviewRow.Kind),.65));comparisons.Columns.Add(Column("Apple Music 参考",nameof(ReviewRow.Recognized),2));comparisons.Columns.Add(Column("个人原词",nameof(ReviewRow.Original),2));
        var headerStyle=new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
        headerStyle.Setters.Add(new Setter(Control.BackgroundProperty,new DynamicResourceExtension("RaisedBrush")));headerStyle.Setters.Add(new Setter(Control.ForegroundProperty,new DynamicResourceExtension("MutedBrush")));headerStyle.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(0)));headerStyle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(9,10,9,10)));headerStyle.Setters.Add(new Setter(Control.FontSizeProperty,11d));comparisons.ColumnHeaderStyle=headerStyle;
        var rowStyle=new Style(typeof(DataGridRow),TryFindResource(typeof(DataGridRow)) as Style);rowStyle.Setters.Add(new Setter(Control.BackgroundProperty,new DynamicResourceExtension("PanelBrush")));rowStyle.Setters.Add(new Setter(Control.ForegroundProperty,new DynamicResourceExtension("TextBrush")));rowStyle.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(0)));comparisons.RowStyle=rowStyle;
        var cellStyle=new Style(typeof(DataGridCell));cellStyle.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(0)));cellStyle.Setters.Add(new Setter(Control.ForegroundProperty,new DynamicResourceExtension("TextBrush")));cellStyle.Setters.Add(new Setter(Control.BackgroundProperty,System.Windows.Media.Brushes.Transparent));comparisons.CellStyle=cellStyle;
        ThemeManager.Bind(comparisons,Control.BackgroundProperty,"PanelBrush");ThemeManager.Bind(comparisons,Control.ForegroundProperty,"TextBrush");Grid.SetRow(comparisons,2);root.Children.Add(comparisons);
        coordinator.Updated+=RefreshTrack;RefreshTrack();Closed+=(_,_)=>{closed=true;running?.Cancel();coordinator.Updated-=RefreshTrack;};
    }
    void RefreshTrack(){if(!closed)current.Text=coordinator.ActiveTrack is { } track?track.Title+" · "+track.Artist:"请在 Apple Music 中选择歌曲";}
    internal async Task CaptureAsync(bool image)
    {
        if(running is not null || coordinator.ActiveTrack is not { } track || coordinator.Document is not {IsSynced:true,Instrumental:false} original){status.Text="请先为当前歌曲准备可确认的动态原词，再开始校对。";return;}
        running=new CancellationTokenSource();var token=running.Token;recognize.IsEnabled=reference.IsEnabled=false;stop.IsEnabled=true;status.Text=image?"正在定位 Apple Music 歌词，并在本机识别…":"正在读取 Apple Music 的原生文字参考…";
        try
        {
            var result=await capture(TrackMetadata.FromAppleSession(track),original,image,token);token.ThrowIfCancellationRequested();
            if(closed)return;var report=LyricsOcrComparison.Compare(TrackMetadata.FromAppleSession(track),original,result.Observation);
            if(!report.Accepted)throw new InvalidDataException(report.Message);
            string saved=new LyricsReviewStore(coordinator.DataRoot).Save(TrackMetadata.FromAppleSession(track),original,result.Observation,image?"windows-ocr":"native-text",image?result.Capture.Image:null);
            ShowReport(report);status.Text=(image?"屏幕 OCR":"原生文字参考")+" · "+report.Message+"\n校对记录已保存到个人资料。文字一致不代表时间轴或译文含义已核实。";
        }
        catch(OperationCanceledException){if(!closed)status.Text="已停止；已有歌词与校对记录已保留。";}
        catch(Exception ex) when(ex is InvalidOperationException or IOException or System.Runtime.InteropServices.ExternalException or UnauthorizedAccessException)
        {if(!closed)status.Text=ex.Message;}
        finally{running.Dispose();running=null;if(!closed){recognize.IsEnabled=reference.IsEnabled=true;stop.IsEnabled=false;}}
    }
    internal void ShowReport(LyricsOcrReport report){ComparedLines=report.Lines;comparisons.ItemsSource=report.Lines.Select(line=>new ReviewRow(line)).ToArray();}
    sealed record ReviewRow(LyricsOcrComparedLine Line)
    {
        public string Kind=>Line.Match switch{LyricsOcrMatch.Matched=>"文字一致",LyricsOcrMatch.Similar=>"文字相近",LyricsOcrMatch.Ambiguous=>"重复句",_=>"未匹配"};
        public string Recognized=>Line.Recognized;
        public string Original=>Line.Original??"—";
    }
}
