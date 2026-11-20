using System.Diagnostics;
using System.IO;
using System.Text.Json;
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
                    checks["timing.enterStartsFirstLine"]=KeyPress(dialog.Editor,Key.Enter) && dialog.MarkLine.Content?.ToString()==UiText.T("Enter · 唱完，下一句");
                    SetTime(7200);
                    checks["timing.enterJoinsConsecutiveLyrics"]=KeyPress(dialog.Editor,Key.Enter) && dialog.TimedLines.SelectedIndex==1
                        && dialog.FeaturedLine.Text=="We follow the light" && dialog.InsertGap.Visibility==Visibility.Visible;
                    SetTime(12500);
                    checks["timing.nextLineAppearsBeforeGapIsMarked"]=KeyPress(dialog.Editor,Key.Enter) && dialog.TimedLines.SelectedIndex==2
                        && dialog.FeaturedLine.Text=="The quiet road";
                    SetTime(18100);
                    checks["timing.gEntersGapAfterAdvancing"]=KeyPress(dialog.Editor,Key.G) && dialog.Step.Text==UiText.T("间奏")
                        && dialog.FeaturedLine.Text==UiText.T("等待下一句开唱") && dialog.MarkLine.Content?.ToString()==UiText.T("Enter · 结束间奏")
                        && dialog.InsertGap.Visibility==Visibility.Collapsed;
                    checks["timing.undoGapRestoresNextLine"]=KeyPress(dialog.Editor,Key.Back) && dialog.FeaturedLine.Text=="The quiet road"
                        && dialog.InsertGap.Visibility==Visibility.Visible;
                    SetTime(21100);KeyPress(dialog.Editor,Key.G);
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
            var draftTrack=target with{Title="Draft Road"};
            repository.SyncLibrary(new("own-timing",3,[new("one",target),new("two",other),new("draft",draftTrack)],"own"));
            var originalDraft=repository.Save(draftTrack,plain,"Own draft fixture",true,plain:true);
            var draftTarget=new LibraryPreparationTarget(new("own-timing","draft"),draftTrack,originalDraft.Id,null,null);
            var multilingual=new TimingEngineFixture(new("Multilingual fixture","v1"),[false,true,false]);
            var mandarin=new TimingEngineFixture(new("SOFA Mandarin","Fixture v1","zh"),[true,true,true]);
            var japanese=new TimingEngineFixture(new("MMS Japanese","Fixture v1","ja"),[true,true,true]);
            var backends=new Dictionary<LyricTimingEngine,IOfflineLyricTiming>
            {
                [LyricTimingEngine.Multilingual]=multilingual,
                [LyricTimingEngine.Mandarin]=mandarin,
                [LyricTimingEngine.Japanese]=japanese,
                [LyricTimingEngine.French]=new TimingEngineFixture(new("W2V-BERT","Fixture v1","fr"),[true,false,true]),
                [LyricTimingEngine.German]=new TimingEngineFixture(new("W2V-BERT","Fixture v1","de"),[true,false,false])
            };
            int captureCalls=0;
            var draftWindow=new LyricTimingWindow(subject,draftTarget,plain,async token=>
            {
                captureCalls++;
                await Task.Delay(55,token);
                return new LyricTimingAudio(new short[16000],16000,0,0);
            },backends){Owner=preferences,Width=680,Height=600};
            void SelectEngine(LyricTimingEngine engine)=>draftWindow.AutoTimingModel.SelectedItem=
                draftWindow.AutoTimingModel.Items.OfType<ComboBoxItem>().Single(item=>Equals(item.Tag,engine));
            var reviewFinished=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _=Dispatcher.BeginInvoke(async()=>
            {
                try
                {
                    Click(draftWindow.CaptureAutoTiming);
                    checks["timing.coverageStopsCaptureAndShowsUncoveredLines"]=captureCalls==0
                        && draftWindow.AutoTimingCoverage.Text==UiText.F($"此模型可识别 {1}/{3} 行原词")
                        && draftWindow.AutoTimingUncovered.Text==UiText.F($"未覆盖的行号：{"1, 3"}")
                        && draftWindow.BeginTiming.IsEnabled && repository.FindReadOnly(draftTrack)?.Id==originalDraft.Id;
                    Click(draftWindow.OpenAutoAudio);
                    checks["timing.coverageStopsWavePicker"]=draftWindow.AutoTimingCoverage.IsVisible
                        && draftWindow.Feedback.Text==UiText.T("请选择覆盖全文的模型，或继续手动打轴。");
                    await Task.Delay(180);
                    if(UiText.Language=="zh-Hans")VisualVerification.Save(draftWindow,Path.Combine(directory,"timing-coverage.png"));
                    SelectEngine(LyricTimingEngine.French);
                    Click(draftWindow.CaptureAutoTiming);
                    bool french=captureCalls==0 && draftWindow.AutoTimingUncovered.Text==UiText.F($"未覆盖的行号：{"2"}")
                        && Equals((draftWindow.AutoTimingModel.SelectedItem as ComboBoxItem)?.Content,UiText.T("法语 · W2V-BERT"));
                    SelectEngine(LyricTimingEngine.German);
                    Click(draftWindow.CaptureAutoTiming);
                    checks["timing.frenchAndGermanUseSelectedProfiles"]=french && captureCalls==0
                        && draftWindow.AutoTimingUncovered.Text==UiText.F($"未覆盖的行号：{"2, 3"}")
                        && Equals((draftWindow.AutoTimingModel.SelectedItem as ComboBoxItem)?.Content,UiText.T("德语 · W2V-BERT"));
                    SelectEngine(LyricTimingEngine.Mandarin);
                    checks["timing.switchingModelClearsOldCoverage"]=draftWindow.AutoTimingCoverage.Text.Length==0
                        && draftWindow.AutoTimingUncovered.Text.Length==0;
                    checks["timing.mandarinShowsRecordingGuidance"]=draftWindow.AutoTimingModelInfo.IsVisible
                        && draftWindow.AutoTimingModelInfo.Text==UiText.T("适合人声清晰的普通话录音。伴奏较重或原词漏写重复句时，部分时间可能需要手动补齐。");
                    Click(draftWindow.CaptureAutoTiming);
                    checks["timing.autoCanCancelWhileManualActionsAreDisabled"]=draftWindow.CancelAutoTiming.IsVisible
                        && !draftWindow.BeginTiming.IsEnabled && !draftWindow.AutoTimingModel.IsEnabled && captureCalls==1;
                    Click(draftWindow.CancelAutoTiming);
                    await Until(()=>draftWindow.CaptureAutoTiming.IsEnabled);
                    checks["timing.cancelKeepsStaticOriginal"]=repository.FindReadOnly(draftTrack)?.Id==originalDraft.Id;
                    SelectEngine(LyricTimingEngine.Japanese);
                    checks["timing.japaneseProfileExplainsReading"]=draftWindow.AutoTimingModelInfo.IsVisible
                        && draftWindow.AutoTimingModelInfo.Text==UiText.T("按日语读音对齐汉字和假名，草稿保留原文。模型权重仅限非商业用途。");
                    Click(draftWindow.CaptureAutoTiming);
                    await Until(()=>draftWindow.TimedLines.Items.Count==3);
                    checks["timing.autoDraftDoesNotWriteBeforeReview"]=repository.FindReadOnly(draftTrack)?.Id==originalDraft.Id && !draftWindow.SaveTiming.IsEnabled;
                    checks["timing.unalignedLineCanBeEdited"]=draftWindow.Review.IsVisible && draftWindow.EditStart.Text.Length==0;
                    draftWindow.DraftShiftPanel.IsExpanded=true;
                    draftWindow.ShiftSeconds.Text="0.5";Click(draftWindow.ApplyShift);
                    draftWindow.TimedLines.SelectedIndex=0;
                    checks["timing.autoDraftShiftAndUndo"]=draftWindow.EditStart.Text=="00:04.500" && KeyPress(draftWindow.Editor,Key.Back);
                    draftWindow.DraftShiftPanel.IsExpanded=false;
                    draftWindow.TimedLines.SelectedIndex=1;
                    draftWindow.EditStart.Text="9.250";draftWindow.EditEnd.Text="14.125";
                    Click(draftWindow.ApplyTimes);
                    checks["timing.autoDraftCanSaveOnlyWhenComplete"]=draftWindow.SaveTiming.IsEnabled;
                    await Task.Delay(180);
                    if(UiText.Language=="zh-Hans")VisualVerification.Save(draftWindow,Path.Combine(directory,"timing-auto-draft.png"));
                    Click(draftWindow.SaveTiming);
                    reviewFinished.TrySetResult();
                }
                catch(Exception e){reviewFinished.TrySetException(e);}
                finally{if(draftWindow.IsVisible)draftWindow.Close();}
            });
            draftWindow.ShowDialog();
            await reviewFinished.Task.WaitAsync(TimeSpan.FromSeconds(8));
            var reviewed=repository.FindReadOnly(draftTrack);
            var draftGroups=reviewed is null?null:new LrcParser().Parse(reviewed.Raw).Groups;
            checks["timing.reviewedDraftPreservesRepeatedLinesAndGaps"]=reviewed is {Manual:true,Plain:false,Source:"离线打轴 · 已校对"}
                && draftGroups?.Select(g=>g.TimeMs).SequenceEqual([4000d,7200d,9250d,14125d,39000d,44200d])==true
                && draftGroups?.Select(g=>g.Text).SequenceEqual(["The quiet road","","We follow the light","","The quiet road",""])==true;
            using var provenance=JsonDocument.Parse(reviewed?.ProviderJson??"{}");
            checks["timing.reviewedDraftIdentifiesChosenModel"]=provenance.RootElement.TryGetProperty("source",out var source)
                && source.GetString()==japanese.Model.Source && provenance.RootElement.GetProperty("modelRevision").GetString()==japanese.Model.ModelRevision
                && provenance.RootElement.GetProperty("lyricsLanguage").GetString()==japanese.Model.LyricsLanguage;
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

    sealed class TimingEngineFixture(LyricTimingModel model,bool[] coverage) : IOfflineLyricTiming
    {
        public LyricTimingModel Model=>model;
        public bool Ready=>true;
        public long DownloadBytes=>1;
        public Task PrepareAsync(IProgress<(long Done,long Total)>? progress,CancellationToken token)=>Task.CompletedTask;
        public Task<LyricTimingCoverage> CheckCoverageAsync(string[] lines,CancellationToken token)
            =>Task.FromResult(new LyricTimingCoverage(coverage));
        public Task<IReadOnlyList<LyricTimingSpan?>> AlignAsync(LyricTimingAudio audio,string[] lines,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<LyricTimingSpan?>>([new(4000,7200),null,new(39000,44200)]);
        }
    }
}
