using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
using Microsoft.Win32;

namespace LumaLyrics.App;

public partial class LyricTimingWindow : Window
{
    readonly PlaybackCoordinator coordinator;
    readonly LibraryPreparationTarget target;
    readonly ObservableCollection<DisplayedLine> rows=[];
    readonly DispatcherTimer playback=new(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(100)};
    ManualLyricTiming? timing;
    PlaybackSnapshot? recording;
    bool saved,updatingRows;
    sealed record DisplayedLine(string Number,string Text,string Times,string Gap);

    internal LyricTimingWindow(PlaybackCoordinator coordinator,LibraryPreparationTarget target,string plain,
        Func<CancellationToken,Task<LyricTimingAudio>>? capture=null,IReadOnlyDictionary<LyricTimingEngine,IOfflineLyricTiming>? backends=null)
    {
        this.coordinator=coordinator;
        this.target=target;
        timingBackends=backends??Enum.GetValues<LyricTimingEngine>().ToDictionary(engine=>engine,
            engine=>(IOfflineLyricTiming)new OfflineLyricTiming(engine:engine));
        captureOverride=capture;
        InitializeComponent();
        Content=null;
        ApplicationWindowFrame.SetContent(this,Editor);
        RecordingLabel.Text=string.Join(" · ",new[]{target.Track.Title,target.Track.Artist,target.Track.Album}.Where(t=>t.Length>0));
        RecordingLabel.ToolTip=RecordingLabel.Text;
        TimedLines.ItemsSource=rows;
        SourceText.Text=plain;
        Step.Text=UiText.T("准备原词");
        Instruction.Text=UiText.T("粘贴静态原词或打开文本，每行一句。手动打轴时请播放这首歌。");
        Feedback.Text=UiText.T("空行会跳过。");
        Loaded+=(_,_)=>{RefreshPlayback();playback.Start();if(plain.Length==0)SourceText.Focus();else BeginTiming.Focus();};
        playback.Tick+=(_,_)=>RefreshPlayback();
        InitializeAutoTiming();
        Closed+=(_,_)=>{playback.Stop();autoClosed=true;autoOperation?.Cancel();};
        Closing+=(_,e)=>
        {
            if(captureOverride is null && !saved && timing is { } draft && (draft.CanUndo || draft.HasDraft))
                e.Cancel=MessageBox.Show(this,UiText.T("当前打轴尚未保存。要关闭编辑器吗？"),Title,MessageBoxButton.OKCancel,MessageBoxImage.Question)==MessageBoxResult.Cancel;
        };
        PreviewKeyDown+=TimingKeyDown;
    }

    void LoadTextClicked(object sender,RoutedEventArgs e)
    {
        var picker=new OpenFileDialog{Filter=UiText.T("静态歌词文本|*.txt")};
        if(picker.ShowDialog(this)!=true)return;
        try
        {
            if(new FileInfo(picker.FileName).Length>512*1024)throw new InvalidDataException("打轴原词超过 128 KB。");
            SourceText.Text=File.ReadAllText(picker.FileName,new UTF8Encoding(false,true));
            Feedback.Text="";
        }
        catch(Exception ex) when(ex is IOException or InvalidDataException or UnauthorizedAccessException or DecoderFallbackException)
        {Feedback.Text=UiText.Status(ex.Message);}
    }

    void BeginClicked(object sender,RoutedEventArgs e)
    {
        if(autoOperation is not null)return;
        try{timing=new ManualLyricTiming(SourceText.Text,target.Track.DurationMs);}
        catch(InvalidDataException ex){Feedback.Text=UiText.Status(ex.Message);return;}
        OpenTimingEditor();
    }

    void OpenTimingEditor()
    {
        SourceText.Visibility=LoadText.Visibility=BeginTiming.Visibility=AutoTimingPanel.Visibility=Visibility.Collapsed;
        TimedLines.Visibility=MarkActions.Visibility=FeaturedLine.Visibility=Visibility.Visible;
        LyricsHeading.Text=UiText.T("逐行时间轴");
        RefreshSession(followActive:true);
        Editor.Focus();
    }

    void TimingKeyDown(object sender,KeyEventArgs e)
    {
        if(timing is null || Keyboard.Modifiers!=ModifierKeys.None || e.Key is not (Key.Enter or Key.G or Key.Back)
            || OwnsKeyInput(e.OriginalSource as DependencyObject))return;
        if(e.Key==Key.G && !timing.CanInsertGap)return;
        e.Handled=true;
        if(e.IsRepeat)return;
        if(e.Key==Key.Back){Undo();return;}
        if(timing.Complete)return;
        if(e.Key==Key.G)BeginGap();
        else Mark();
    }

    static bool OwnsKeyInput(DependencyObject? element)
    {
        for(var parent=element;parent is not null;parent=parent is Visual?VisualTreeHelper.GetParent(parent):LogicalTreeHelper.GetParent(parent))
            if(parent is TextBoxBase or ButtonBase)return true;
        return false;
    }

    void MarkClicked(object sender,RoutedEventArgs e){Mark();Editor.Focus();}
    void GapClicked(object sender,RoutedEventArgs e){BeginGap();Editor.Focus();}
    void UndoClicked(object sender,RoutedEventArgs e){Undo();Editor.Focus();}

    void Mark()
    {
        if(timing is null)return;
        var sample=coordinator.ReadLyricTimingPlayback(target.Track,recording);
        if(sample.PositionMs is not {} position){Feedback.Text=UiText.Status(sample.Status);return;}
        try
        {
            timing.Mark(position);
            recording??=sample.Recording;
            RefreshSession(followActive:true);
        }
        catch(InvalidDataException ex){Feedback.Text=UiText.Status(ex.Message);}
    }

    void BeginGap()
    {
        if(timing?.CanInsertGap!=true)return;
        timing.BeginGap();
        RefreshSession(followActive:true);
    }

    void Undo()
    {
        if(timing?.CanUndo!=true)return;
        timing.Undo();
        if(!timing.CanUndo)recording=null;
        RefreshSession(followActive:true);
    }

    void RefreshSession(bool followActive)
    {
        if(timing is null)return;
        int index=followActive?Math.Min(timing.ActiveIndex,timing.Lines.Count-1):TimedLines.SelectedIndex;
        updatingRows=true;
        try
        {
            for(int i=0;i<timing.Lines.Count;i++)
            {
                var line=timing.Lines[i];
                string times=line.StartMs is {} start?ManualLyricTiming.FormatTime(start)+"  →  "+(line.EndMs is {} end?ManualLyricTiming.FormatTime(end):timing.HasDraft?UiText.T("待填写时间"):"—")
                    :timing.HasDraft?UiText.T("待填写时间"):"—";
                string gap="";
                if(i+1<timing.Lines.Count && line.EndMs is {} boundary)
                {
                    if(timing.Lines[i+1].StartMs is {} next && next>boundary)
                        gap=UiText.F($"随后留白 {ManualLyricTiming.FormatTime(next-boundary)}");
                    else if(timing.WaitingForStart && timing.ActiveIndex==i+1)
                        gap=UiText.F($"间奏开始于 {ManualLyricTiming.FormatTime(boundary)}");
                }
                var row=new DisplayedLine((i+1).ToString("00",UiText.Culture),line.Text,times,gap);
                if(i==rows.Count)rows.Add(row);
                else if(rows[i]!=row)rows[i]=row;
            }
            TimedLines.SelectedIndex=index;
            if(followActive && index>=0)TimedLines.ScrollIntoView(rows[index]);
        }
        finally{updatingRows=false;}
        Completion.Maximum=timing.Lines.Count;
        Completion.Value=timing.MarkedCount;
        DraftShiftPanel.Visibility=timing.CanShiftDraft?Visibility.Visible:Visibility.Collapsed;
        CountLabel.Text=UiText.F($"已标记 {timing.MarkedCount}/{timing.Lines.Count} 句");
        UndoMark.IsEnabled=timing.CanUndo;
        SaveTiming.IsEnabled=timing.Complete;
        SaveTiming.Visibility=timing.Complete?Visibility.Visible:Visibility.Collapsed;
        MarkLine.Visibility=timing.Complete?Visibility.Collapsed:Visibility.Visible;
        InsertGap.Visibility=timing.CanInsertGap?Visibility.Visible:Visibility.Collapsed;
        Completion.Visibility=timing.Complete?Visibility.Collapsed:Visibility.Visible;
        bool inGap=timing.WaitingForStart && timing.ActiveIndex>0;
        Step.Text=timing.Complete?UiText.T("校对时间轴"):inGap?UiText.T("间奏"):UiText.F($"第 {timing.ActiveIndex+1} 句");
        FeaturedLine.Text=timing.Complete?"":inGap?UiText.T("等待下一句开唱"):timing.Lines[timing.ActiveIndex].Text;
        MarkLine.Content=UiText.T(inGap?"Enter · 结束间奏":timing.WaitingForStart?"Enter · 标记开唱"
            :timing.ActiveIndex+1<timing.Lines.Count?"Enter · 唱完，下一句":"Enter · 标记句尾");
        Instruction.Text=UiText.T(timing.Complete?"播放时可预览，选中左侧歌词可微调起止时间。"
            :inGap?"听到下一句开唱时按 Enter，结束间奏。"
            :timing.WaitingForStart?"听到这句开唱时按 Enter。"
            :timing.CanInsertGap?"唱完按 Enter。听到间奏时按 G，留白从上一句结束时开始。":"这句唱完时按 Enter。");
        Feedback.Text=UiText.T(timing.HasDraft?timing.Complete?"这是自动生成的草稿。试听确认后再保存。":"补齐待填写的时间，试听确认后再保存。"
            :timing.Complete?"确认后将以手动动态歌词保存。":"Backspace 撤销上一次标记或调整。");
        RefreshSelectedTimes();
        RefreshPlayback();
    }

    void RefreshPlayback()
    {
        var sample=coordinator.ReadLyricTimingPlayback(target.Track,recording);
        PlaybackTime.Text=sample.PositionMs is {} position?ManualLyricTiming.FormatTime(position):"--:--.---";
        PlaybackStatus.Text=UiText.Status(sample.Status);
        if(timing is null)return;
        MarkLine.IsEnabled=!timing.Complete && sample.PositionMs.HasValue;
        if(timing.Complete)
        {
            int index=sample.PositionMs is {} time?timing.LineAt(time):-1;
            FeaturedLine.Text=index>=0?timing.Lines[index].Text:UiText.T(sample.PositionMs.HasValue?"当前没有歌词":"等待播放预览");
        }
    }

    void LineSelectionChanged(object sender,SelectionChangedEventArgs e){if(!updatingRows)RefreshSelectedTimes();}
    void RefreshSelectedTimes()
    {
        int index=TimedLines.SelectedIndex;
        var line=timing is not null && index>=0?timing.Lines[index]:null;
        Review.Visibility=line is not null && (line.EndMs is not null || timing?.HasDraft==true)?Visibility.Visible:Visibility.Collapsed;
        EditStart.Text=line?.StartMs is {} start?ManualLyricTiming.FormatTime(start):"";
        EditEnd.Text=line?.EndMs is {} end?ManualLyricTiming.FormatTime(end):"";
    }

    void EditClicked(object sender,RoutedEventArgs e)
    {
        if(timing is null || TimedLines.SelectedIndex<0)return;
        if(!ManualLyricTiming.TryParseTime(EditStart.Text,out var start) || !ManualLyricTiming.TryParseTime(EditEnd.Text,out var end))
        {Feedback.Text=UiText.T("时间请输入分:秒.毫秒，或直接输入秒数。");return;}
        try{timing.Edit(TimedLines.SelectedIndex,start,end);RefreshSession(followActive:false);}
        catch(InvalidDataException ex){Feedback.Text=UiText.Status(ex.Message);}
    }

    void ShiftClicked(object sender,RoutedEventArgs e)
    {
        if(timing is null)return;
        if(!double.TryParse(ShiftSeconds.Text,System.Globalization.NumberStyles.Float,UiText.Culture,out var seconds)
            && !double.TryParse(ShiftSeconds.Text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out seconds))
        {Feedback.Text=UiText.T("整体校准请输入 −5 到 5 秒。");return;}
        try{timing.ShiftDraft(seconds*1000);ShiftSeconds.Text="0";RefreshSession(followActive:false);}
        catch(InvalidDataException ex){Feedback.Text=UiText.Status(ex.Message);}
    }

    void SaveClicked(object sender,RoutedEventArgs e)
    {
        if(timing?.Complete!=true)return;
        try{coordinator.SaveManualLyricTiming(target,timing,draftModel);saved=true;DialogResult=true;}
        catch(Exception ex) when(ex is InvalidDataException or IOException or Microsoft.Data.Sqlite.SqliteException)
        {Feedback.Text=UiText.Status(ex.Message);}
    }
    void CloseClicked(object sender,RoutedEventArgs e)=>Close();
}
