using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;

namespace LumaLyrics.App;

public partial class MainWindow
{
    async Task VerifyInstrumentsAsync(string directory, Dictionary<string, object?> checks)
    {
        bool shown = settings.ShowInstrumentActivity, queue = settings.ShowPlaybackQueue;
        string theme = settings.UiTheme, modelId = settings.InstrumentModelId; double width = Width, height = Height;
        var appearance = OverlayAppearance.From(settings);
        async Task Settle() { ApplyPlaybackLayout(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); await Task.Delay(50); UpdateLayout(); }
        Rect Bounds(FrameworkElement element) => element.TransformToAncestor(Body).TransformBounds(new Rect(element.RenderSize));
        bool Inside(Rect rect) => rect.Left >= -.5 && rect.Top >= -.5 && rect.Right <= Body.ActualWidth + .5 && rect.Bottom <= Body.ActualHeight + .5;
        var states = new[]
        {
            new InstrumentViewState(InstrumentStatus.Disabled, false, new Dictionary<InstrumentKind,InstrumentVisualState>(), ""),
            new(InstrumentStatus.Preparing, false, new Dictionary<InstrumentKind,InstrumentVisualState>(), "准备实验识别…"),
            new(InstrumentStatus.Learning, false, new Dictionary<InstrumentKind,InstrumentVisualState>{{InstrumentKind.Strings,InstrumentVisualState.Active}}, "实时识别 · 清单逐步完善"),
            new(InstrumentStatus.Ready, true, Enum.GetValues<InstrumentKind>().ToDictionary(kind=>kind,_=>InstrumentVisualState.Dim), "整曲清单 · 原创状态夹具"),
            new(InstrumentStatus.Paused, true, Enum.GetValues<InstrumentKind>().ToDictionary(kind=>kind,_=>InstrumentVisualState.Dim), "已暂停"),
            new(InstrumentStatus.Unavailable, false, new Dictionary<InstrumentKind,InstrumentVisualState>{{InstrumentKind.Strings,InstrumentVisualState.Unknown}}, "尚未取得有效音乐信号，继续等待…")
        };
        try
        {
            await VerifyInstrumentOwnershipAsync(checks);
            ShowLyricsPage(); timer.Stop(); settings.ShowInstrumentActivity = true; InstrumentHost.Visibility = Visibility.Visible;
            vm.Title = "微光弦间"; vm.Artist = "原创界面样例 · 非音乐识别结果";
            foreach (var app in AppThemes.All)
            {
                SelectUiTheme(app.Id, false);
                foreach (var state in states)
                {
                    instrumentView.Apply(state); await Settle();
                    string key = $"instrument.{app.Id}.{state.Status}";
                    checks[key + ".visibility"] = instrumentView.Visibility == (state.Status == InstrumentStatus.Disabled ? Visibility.Collapsed : Visibility.Visible);
                    checks[key + ".accessibleState"] = state.Icons.All(pair => Descendants<TextBlock>(instrumentView).Any(text => AutomationProperties.GetName(text).StartsWith(InstrumentIcons.Name(pair.Key) + "，", StringComparison.Ordinal)));
                    if (state.Status != InstrumentStatus.Disabled)
                        VisualVerification.Save(instrumentView, Path.Combine(directory, $"instruments-{app.Id}-{state.Status}.png"), true);
                }
                instrumentView.Apply(states[3]);
                foreach (bool visible in new[] { true, false })
                foreach (double size in new[] { 820d, 980d, 1220d })
                {
                    SetPlaybackQueueVisible(visible, false); Width = size; Height = size == 820 ? 640 : 820; await Settle();
                    string key = $"instrument.{app.Id}.{size:0}.queue-{visible}";
                    checks[key + ".insideViewport"] = Inside(Bounds(InstrumentHost)) && Inside(Bounds(PlaybackControls)) && Inside(Bounds(SeekSlider)) && Inside(Bounds(LyricsPanel));
                    checks[key + ".geometry"] = new { Body=Body.RenderSize.ToString(), Host=Bounds(InstrumentHost).ToString(), Transport=Bounds(PlaybackControls).ToString() };
                    checks[key + ".noPanelOverlap"] = Rect.Intersect(Bounds(InfoPanel), Bounds(LyricsPanel)).IsEmpty;
                    checks[key + ".squareCover"] = Math.Abs(ArtworkBorder.ActualWidth - ArtworkBorder.ActualHeight) < 1 && Inside(Bounds(ArtworkBorder));
                    checks[key + ".scrollBounded"] = Descendants<ScrollViewer>(instrumentView).All(scroll => scroll.ActualWidth <= InstrumentHost.ActualWidth + .5);
                    if (visible) VisualVerification.Save((FrameworkElement)Content, Path.Combine(directory, $"instruments-{app.Id}-{size:0}.png"), true);
                }
                var settingsDialog = new SettingsWindow(coordinator, id => SelectUiTheme(id, false)) { Owner = this };
                settingsDialog.Show(); Descendants<TabControl>(settingsDialog).First().SelectedIndex = 6;
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); await Task.Delay(80);
                var panel = Descendants<InstrumentSettingsPanel>(settingsDialog).Single();
                checks[$"instrument.{app.Id}.settingsUsesAppResources"] = panel.FindResource("AccentBrush") == FindResource("AccentBrush") && panel.IsVisible;
                settingsDialog.Width = 640; await Task.Delay(60); settingsDialog.UpdateLayout();
                var models = Descendants<ComboBox>(panel).Single();
                models.SelectedValue = InstrumentModelCatalog.GeneralId;
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); settingsDialog.UpdateLayout();
                checks[$"instrument.{app.Id}.generalSelectionPersists"] = settings.InstrumentModelId == InstrumentModelCatalog.GeneralId;
                checks[$"instrument.{app.Id}.modelDisplaysUserName"] = models.SelectedItem?.ToString() == InstrumentModelCatalog.Get(InstrumentModelCatalog.GeneralId).Name;
                checks[$"instrument.{app.Id}.modelChoiceFits"] = models.TransformToAncestor(panel).TransformBounds(new Rect(models.RenderSize)).Right <= panel.ActualWidth + .5;
                checks[$"instrument.{app.Id}.settingsButtonsFit"] = Descendants<Button>(panel).All(button => button.TransformToAncestor(panel).TransformBounds(new Rect(button.RenderSize)).Right <= panel.ActualWidth + .5);
                VisualVerification.Save((FrameworkElement)settingsDialog.Content, Path.Combine(directory, $"instruments-settings-{app.Id}.png"), true); settingsDialog.Close();
                checks[$"instrument.{app.Id}.floatingAppearanceUnchanged"] = OverlayAppearance.From(settings) == appearance;
            }
            Width = 1220; Height = 820; instrumentView.Apply(states[2]); await Settle();
            instrumentView.Apply(states[5]); await Task.Delay(35); instrumentView.Apply(states[2]); await Task.Delay(200);
            checks["instrument.interruptedAnimationsReleaseClocks"] = instrumentView.ActiveClockCount == 0;
            AppMotion.SetAnimationsEnabled(instrumentView, false); instrumentView.Apply(states[5]);
            checks["instrument.reducedMotionHasNoClocks"] = instrumentView.ActiveClockCount == 0;
            AppMotion.SetAnimationsEnabled(instrumentView, true); instrumentView.Apply(states[3]); await Task.Delay(200);
            foreach (double dpi in new[] { 96d, 144d, 192d })
            {
                // Render scale coverage, not a claim of physical monitor DPI switching.
                int pixelWidth = (int)Math.Ceiling(instrumentView.ActualWidth * dpi / 96);
                var bitmap = new RenderTargetBitmap(pixelWidth, (int)Math.Ceiling(instrumentView.ActualHeight * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
                bitmap.Render(instrumentView);
                checks[$"instrument.renderScale.{dpi:0}.validSize"] = bitmap.PixelWidth == pixelWidth && bitmap.PixelHeight > 0;
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = File.Create(Path.Combine(directory, $"instruments-dpi-{dpi:0}.png")); encoder.Save(output);
            }
            InstrumentHost.Visibility = Visibility.Collapsed; await Settle();
            checks["instrument.hiddenViewReleasesClocks"] = instrumentView.ActiveClockCount == 0;
            checks["instrument.defaultDisabled"] = !new AppSettings().ShowInstrumentActivity && !new AppSettings().AnalyzeInstrumentsInBackground;
            checks["instrument.generalAccuracyValidated"] = false; // Metadata, excluded from pass/fail assertions below.
        }
        finally
        { settings.InstrumentModelId = modelId; coordinator.Instruments.Configure(shown, settings.AnalyzeInstrumentsInBackground, modelId); settings.ShowInstrumentActivity = shown; InstrumentHost.Visibility = shown ? Visibility.Visible : Visibility.Collapsed; Width = width; Height = height; SetPlaybackQueueVisible(queue, false); SelectUiTheme(theme, false); }
        checks.Remove("instrument.generalAccuracyValidated");
        checks["instrument.validationScope"] = "Self-authored WPF state/layout fixtures only. No media session, audio, accuracy or physical monitor DPI proof.";
    }

    async Task VerifyInstrumentOwnershipAsync(Dictionary<string, object?> checks)
    {
        var source = new InstrumentFixtureSource();
        var callbacks = new List<(InstrumentEpoch Epoch, Action<InstrumentFrame> Accept, CancellationToken Token)>();
        object sync = new();
        async Task Worker(PlaybackSnapshot snapshot, InstrumentEpoch epoch, Action<InstrumentFrame> accept, CancellationToken token)
        {
            lock (sync) callbacks.Add((epoch, accept, token));
            await Task.Delay(Timeout.Infinite, token);
        }
        async Task Until(Func<bool> predicate)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!predicate()) { if (watch.Elapsed.TotalSeconds > 5) throw new TimeoutException("Instrument ownership fixture timeout."); await Task.Delay(10); }
        }
        int Count() { lock (sync) return callbacks.Count; }
        (InstrumentEpoch Epoch, Action<InstrumentFrame> Accept, CancellationToken Token) Callback(int index) { lock (sync) return callbacks[index]; }
        static PlaybackSnapshot Snapshot(long revision, PlayState state = PlayState.Playing) => new("fixture-AppleMusic", 1, revision,
            new("原创版本 " + revision, "原创状态夹具", "", 10000), state, 1, 0, DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow, System.Diagnostics.Stopwatch.GetTimestamp(), PositionQuality.Estimated);
        static InstrumentFrame Frame(InstrumentEpoch epoch) => new(epoch, System.Diagnostics.Stopwatch.GetTimestamp(), null, null, "self-authored-fixture",
            new Dictionary<InstrumentKind, float> { [InstrumentKind.Strings] = .9f });
        var controller = new InstrumentController(source, () => source.Latest, coordinator.DataRoot, Worker);
        try
        {
            controller.Configure(true, false, InstrumentModelCatalog.StringsId); controller.SetDemand(true); source.Publish(Snapshot(1));
            await Until(() => Count() == 1); var first = Callback(0); first.Accept(Frame(first.Epoch));
            checks["instrument.ownership.currentFrameVisible"] = controller.View(0).Icons.Values.Contains(InstrumentVisualState.Active);
            source.SetRaw(Snapshot(2)); first.Accept(Frame(first.Epoch));
            checks["instrument.ownership.rawSnapshotBeatsDelayedDelivery"] = !controller.View(0).Icons.Values.Contains(InstrumentVisualState.Active);
            source.Publish(source.Latest); await Until(() => Count() == 2);
            first.Accept(Frame(first.Epoch));
            checks["instrument.ownership.oldCallbackRejected"] = !controller.View(0).Icons.Values.Contains(InstrumentVisualState.Active);
            var second = Callback(1); second.Accept(Frame(second.Epoch)); source.Publish(Snapshot(2, PlayState.Paused));
            checks["instrument.ownership.pauseDimsRoster"] = controller.View(0).Icons.Count == 1 && controller.View(0).Icons.Values.All(value => value == InstrumentVisualState.Dim);
            checks["instrument.ownership.pauseCancelsWorker"] = second.Token.IsCancellationRequested;
            source.Publish(Snapshot(2)); await Until(() => Count() == 3);
            var third = Callback(2); controller.SetDemand(false);
            checks["instrument.ownership.hiddenPageCancelsWorker"] = third.Token.IsCancellationRequested;
            await Task.Delay(300); checks["instrument.ownership.hiddenPageDoesNotRestart"] = Count() == 3;
            controller.Configure(true, true); await Until(() => Count() == 4);
            var fourth = Callback(3); controller.SetDemand(true); controller.SetDemand(false);
            checks["instrument.ownership.backgroundOptInRetainsWorker"] = !fourth.Token.IsCancellationRequested;
            controller.SetSeeking(true);
            checks["instrument.ownership.seekImmediatelyCancelsWorker"] = fourth.Token.IsCancellationRequested;
            await Task.Delay(300); checks["instrument.ownership.seekDoesNotRestartWhileEditing"] = Count() == 4;
            controller.SetSeeking(false); await Until(() => Count() == 5); var fifth = Callback(4);
            controller.Configure(true, true, InstrumentModelCatalog.RockId);
            checks["instrument.ownership.modelSwitchCancelsOldWorker"] = fifth.Token.IsCancellationRequested;
            fifth.Accept(Frame(fifth.Epoch));
            checks["instrument.ownership.modelSwitchRejectsOldCategories"] = !controller.View(0).Icons.ContainsKey(InstrumentKind.Strings);
            await Until(() => Count() == 6); var sixth = Callback(5);
            InstrumentFrame RockFrame() => new(sixth.Epoch, System.Diagnostics.Stopwatch.GetTimestamp(), null, null, "self-authored-rock-fixture",
                new Dictionary<InstrumentKind, float> { [InstrumentKind.Guitar] = .9f, [InstrumentKind.Bass] = .9f, [InstrumentKind.Drums] = .9f });
            sixth.Accept(RockFrame()); await Task.Delay(10); sixth.Accept(RockFrame());
            checks["instrument.ownership.rockThreeCategoriesVisible"] = controller.View(0).Icons.Count == 3 && controller.View(0).Icons.Values.All(value => value == InstrumentVisualState.Active);
            controller.Configure(true, true, InstrumentModelCatalog.GeneralId);
            checks["instrument.ownership.generalModelCancelsOldThreeClasses"] = sixth.Token.IsCancellationRequested && controller.View(0).Icons.Count == 0;
            await Until(() => Count() == 7); var seventh = Callback(6);
            InstrumentFrame ChamberFrame() => new(seventh.Epoch, System.Diagnostics.Stopwatch.GetTimestamp(), null, null, "self-authored-chamber-fixture",
                new Dictionary<InstrumentKind, float> { [InstrumentKind.Violin] = .95f, [InstrumentKind.Cello] = .95f });
            for (int i = 0; i < 3; i++) { seventh.Accept(ChamberFrame()); await Task.Delay(10); }
            checks["instrument.ownership.songSpecificConcreteInstruments"] = controller.View(0).Icons.Keys.Order().SequenceEqual(new[] { InstrumentKind.Cello, InstrumentKind.Violin });
            controller.Configure(false, true);
            checks["instrument.ownership.disableCancelsAndHides"] = seventh.Token.IsCancellationRequested && controller.View(0).Status == InstrumentStatus.Disabled;
        }
        finally { await Task.WhenAll(controller.DisposeAsync().AsTask(), controller.DisposeAsync().AsTask()); }
        checks["instrument.ownership.disposeUnsubscribes"] = source.Subscribers == 0;
    }
    sealed class InstrumentFixtureSource : IPlaybackSource
    {
        PlaybackSnapshot? latest;
        Action<PlaybackSnapshot?>? changed;
        public PlaybackSnapshot? Latest => Volatile.Read(ref latest);
        public int Subscribers => changed?.GetInvocationList().Length ?? 0;
        public event Action<PlaybackSnapshot?>? Changed { add => changed += value; remove => changed -= value; }
        public void SetRaw(PlaybackSnapshot? snapshot) => Volatile.Write(ref latest, snapshot);
        public void Publish(PlaybackSnapshot? snapshot) { SetRaw(snapshot); changed?.Invoke(snapshot); }
        public Task StartAsync(CancellationToken token = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
