using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;

namespace LumaLyrics.App;

public partial class MainWindow
{
    async Task VerifyLyricTimingAsync(string directory,Dictionary<string,object?> checks)
    {
        string root=Path.Combine(store.Root,"timing-fixture");
        var fixtureStore=new SettingsStore(root);
        var fixtureSettings=new AppSettings{LibraryPath="own-timing",GlobalOffsetMs=1250,OnlineLookup=false,AutoPrefetchLibrary=false};
        await using var subject=new PlaybackCoordinator(Dispatcher,fixtureStore,fixtureSettings);
        var target=new Track("Quiet Road","Own Performer","Own Record",60000);
        var other=target with{Title="Other Track"};
        using var repository=new LyricsRepository(Path.Combine(root,"lyrics.db"));
        repository.SyncLibrary(new("own-timing",2,[new("one",target),new("two",other)],"own"));
        const string plain="The quiet road\nWe follow the light\nThe quiet road";
        var previous=repository.Save(target,plain,"Own text",true,plain:true);
        var unrelated=repository.Save(other,"[00:02]Own unrelated line","Own text",true);
        string theme=settings.UiTheme;
        var preferences=new SettingsWindow(subject,id=>SelectUiTheme(id,false)){Owner=this,Width=960,Height=840};
        try
        {
            preferences.Show();preferences.SelectLibrarySongs();
            var panel=preferences.Library.Songs;
            await panel.Reload(true);
            await Until(()=>panel.SongList.Items.Count==2);
            var items=panel.SongList.Items.Cast<LibraryManagedSong>().ToArray();
            panel.SelectKeys(items.Where(i=>i.Track==other).Select(i=>i.Key));
            preferences.UpdateLayout();
            var row=(ListBoxItem)panel.SongList.ItemContainerGenerator.ContainerFromItem(items.Single(i=>i.Track==target));
            var otherRow=(ListBoxItem)panel.SongList.ItemContainerGenerator.ContainerFromItem(items.Single(i=>i.Track==other));
            var menu=row.ContextMenu!;
            var command=menu.Items.OfType<MenuItem>().Single(m=>Equals(m.Header,UiText.T("制作动态歌词…")));
            menu.PlacementTarget=otherRow;menu.IsOpen=true;
            await Task.Delay(50);
            checks["timing.readyLyricsCannotOpenTiming"]=!command.IsEnabled;
            menu.IsOpen=false;
            // WPF can still finish closing the shared popup after IsOpen goes false.
            await Task.Delay(180);
            menu.PlacementTarget=row;menu.IsOpen=true;
            await Until(()=>menu.IsOpen && command.IsEnabled);
            checks["timing.staticRowCanOpenThemedMenu"]=command.IsEnabled && menu.Style==FindResource("LibraryActionMenu");
            VisualVerification.Save(menu,Path.Combine(directory,"timing-menu.png"));

            var finished=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _=Dispatcher.BeginInvoke(async()=>
            {
                LyricTimingWindow? dialog=null;
                try
                {
                    await Until(()=>Application.Current.Windows.OfType<LyricTimingWindow>().Any(w=>w.IsVisible));
                    dialog=Application.Current.Windows.OfType<LyricTimingWindow>().Single(w=>w.IsVisible);
                    checks["timing.prefillsClickedStaticLyrics"]=dialog.SourceText.Text==plain && dialog.RecordingLabel.Text.Contains(target.Title);
                    if(UiText.Language=="zh-Hans")
                    {await Task.Delay(240);VisualVerification.Save(dialog,Path.Combine(directory,"timing-source.png"));}
                    Click(dialog.BeginTiming);
                    checks["timing.saveNeedsComplete"]=!dialog.SaveTiming.IsEnabled && dialog.TimedLines.Items.Count==3;
                    SetTime(4000);
                    checks["timing.enterStartsFirstLine"]=KeyPress(dialog.Editor,Key.Enter) && dialog.MarkLine.Content?.ToString()==UiText.T("Enter · 标记句尾");
                    SetTime(7200);
                    Click(dialog.JoinLine);
                    checks["timing.joinEndsAndStartsTogether"]=dialog.TimedLines.SelectedIndex==1 && dialog.FeaturedLine.Text=="We follow the light"
                        && dialog.MarkLine.Content?.ToString()==UiText.T("Enter · 标记句尾");
                    SetTime(12500);
                    checks["timing.enterAtVocalEndWaitsForNextStart"]=KeyPress(dialog.Editor,Key.Enter) && dialog.TimedLines.SelectedIndex==2
                        && dialog.Step.Text==UiText.T("等待下一句开唱") && dialog.MarkLine.Content?.ToString()==UiText.T("Enter · 标记开唱")
                        && dialog.JoinLine.Visibility==Visibility.Collapsed;
                    if(UiText.Language=="zh-Hans")VisualVerification.Save(dialog,Path.Combine(directory,"timing-waiting-for-vocals.png"));
                    SetTime(16800,other,revision:2);KeyPress(dialog.Editor,Key.Enter);
                    checks["timing.wrongRecordingBlocksMark"]=dialog.Feedback.Text==UiText.T("请在 Apple Music 播放这首歌。");
                    SetTime(30000,target,revision:2);KeyPress(dialog.Editor,Key.Enter);
                    checks["timing.newPlaybackRevisionBlocksMark"]=dialog.Feedback.Text==UiText.T("播放会话已变化。请撤销标记后重新开始打轴。");
                    SetTime(39000);
                    checks["timing.nextEnterMarksVocalReturn"]=KeyPress(dialog.Editor,Key.Enter) && dialog.MarkLine.Content?.ToString()==UiText.T("Enter · 标记句尾");
                    SetTime(44200);KeyPress(dialog.Editor,Key.Enter);
                    checks["timing.completeEnablesSave"]=dialog.SaveTiming.IsEnabled;
                    KeyPress(dialog.Editor,Key.Back);
                    checks["timing.backspaceUndoesLastEnd"]=!dialog.SaveTiming.IsEnabled;
                    KeyPress(dialog.Editor,Key.Enter);
                    dialog.TimedLines.SelectedIndex=0;
                    dialog.EditStart.Text="4.100";
                    checks["timing.enterInTimeFieldPreservesEditing"]=!KeyPress(dialog.EditStart,Key.Enter);
                    Click(dialog.ApplyTimes);
                    SetTime(23000);
                    await Task.Delay(150);
                    checks["timing.previewClearsLyricsDuringGap"]=dialog.FeaturedLine.Text==UiText.T("当前没有歌词");

                    foreach(var appTheme in UiText.Language=="zh-Hans"?AppThemes.All:AppThemes.All.Take(1))
                    {
                        SelectUiTheme(appTheme.Id,false);
                        dialog.Width=680;dialog.Height=600;
                        await Task.Delay(140);
                        dialog.UpdateLayout();
                        var bounds=dialog.SaveTiming.TransformToAncestor(dialog.Editor).TransformBounds(new Rect(dialog.SaveTiming.RenderSize));
                        checks["timing."+appTheme.Id+".narrowFooterVisible"]=bounds.Right<=dialog.Editor.ActualWidth+1 && bounds.Bottom<=dialog.Editor.ActualHeight+1
                            && dialog.TimedLines.ActualHeight>=90 && dialog.TimedLines.ActualWidth>=180;
                        if(UiText.Language=="zh-Hans" || UiText.Language=="en")
                            VisualVerification.Save(dialog,Path.Combine(directory,"timing-review-"+appTheme.Id+".png"),cropToLayout:true);
                    }
                    Click(dialog.SaveTiming);
                    finished.SetResult();
                }
                catch(Exception e)
                {
                    finished.SetException(e);
                    if(dialog?.IsVisible==true)
                    {
                        while(dialog.UndoMark.IsEnabled && dialog.UndoMark.IsVisible)Click(dialog.UndoMark);
                        dialog.Close();
                    }
                }
            });
            command.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(14));
            var saved=repository.FindReadOnly(target);
            var lrc=saved is null?null:new LrcParser().Parse(saved.Raw);
            checks["timing.savedAsManualTimedOriginal"]=saved is {Manual:true,Plain:false} && saved.Id!=previous.Id && lrc?.IsSynced==true;
            checks["timing.savedRawPlayerAxisAndInstrumentalClear"]=lrc?.Groups.Select(g=>g.TimeMs).SequenceEqual([4100d,7200d,12500d,39000d,44200d])==true
                && lrc?.Groups.Select(g=>g.Text).SequenceEqual(["The quiet road","We follow the light","","The quiet road",""])==true;
            checks["timing.originalSelectionAndOtherBindingRetained"]=panel.SelectedSongs.Single().Track==other && repository.FindReadOnly(other)?.Id==unrelated.Id;
            await Until(()=>panel.SongList.Items.Cast<LibraryManagedSong>().Single(r=>r.Track==target).Status.Original==LibraryOriginalState.Ready);
            checks["timing.workspaceRefreshesStatus"]=true;
        }
        finally{preferences.Close();SelectUiTheme(theme,false);}

        void SetTime(double position,Track? track=null,long revision=1)
        {
            var now=DateTimeOffset.UtcNow;
            subject.SetManualLyricsVerificationSnapshot(new("AppleMusic-Fixture",1,revision,track??target,PlayState.Paused,1,position,now,now,Stopwatch.GetTimestamp(),PositionQuality.Reliable));
        }
        static void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        static bool KeyPress(FrameworkElement element,Key key)
        {
            var e=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(element),Environment.TickCount,key){RoutedEvent=Keyboard.PreviewKeyDownEvent};
            element.RaiseEvent(e);
            return e.Handled;
        }
        static async Task Until(Func<bool> ready)
        {
            long end=Environment.TickCount64+5000;
            while(!ready() && Environment.TickCount64<end)await Task.Delay(30);
            if(!ready())throw new TimeoutException("Timed lyric fixture did not reach the expected state.");
        }
    }
}
