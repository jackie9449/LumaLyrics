using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LumaLyrics.Core;
using LumaLyrics.Infrastructure;
using LumaLyrics.Windows;
using Microsoft.Win32;

namespace LumaLyrics.App;

public sealed class InstrumentSettingsPanel : StackPanel, IDisposable
{
    readonly PlaybackCoordinator coordinator;
    readonly CancellationTokenSource lifetime = new();
    readonly TextBlock status = Label("准备模型后可开启。", 12);
    readonly ComboBox modelChoice = new() { HorizontalAlignment = HorizontalAlignment.Stretch, MaxWidth = 620, DisplayMemberPath = nameof(InstrumentModelOption.Name), SelectedValuePath = nameof(InstrumentModelOption.Id), Margin = new(0, 0, 0, 12) };
    readonly ProgressBar progress = new() { Minimum = 0, Maximum = 100, Height = 5, Visibility = Visibility.Collapsed, Margin = new(0, 8, 0, 10) };
    readonly DispatcherTimer timer;
    CancellationTokenSource? work;
    bool busy, disposed;
    InstrumentTimeline? analyzed;
    PlaybackSnapshot? expected;
    public InstrumentSettingsPanel(PlaybackCoordinator coordinator)
    {
        this.coordinator = coordinator;
        Children.Add(Label("听见正在演奏的乐器", 20));
        var scope = Label(CurrentModel.Scope, 12); Children.Add(scope);
        modelChoice.ItemsSource = InstrumentModelCatalog.All; modelChoice.SelectedValue = CurrentModel.Id;
        System.Windows.Automation.AutomationProperties.SetName(modelChoice, "乐器识别模型"); Children.Add(modelChoice);
        Children.Add(Label("清单根据当前录音的音频生成，只有检出的乐器才会出现，允许多件乐器同时亮起。检测范围取决于当前模型；不会根据歌名、曲风或全部可支持类别预设本曲清单。识别仍可能漏报或误报。", 12));
        var enabled = DialogUi.Check(this, "在播放页显示实验乐器识别（本机处理）", coordinator.Settings.ShowInstrumentActivity);
        var background = DialogUi.Check(this, "隐藏播放页或最小化后继续分析", coordinator.Settings.AnalyzeInstrumentsInBackground);
        void Changed()
        {
            coordinator.Settings.ShowInstrumentActivity = enabled.IsChecked == true;
            coordinator.Settings.AnalyzeInstrumentsInBackground = background.IsChecked == true;
            coordinator.Instruments.Configure(enabled.IsChecked == true, background.IsChecked == true, coordinator.Settings.InstrumentModelId);
            coordinator.SaveSettings();
        }
        enabled.Checked += (_, _) => Changed(); enabled.Unchecked += (_, _) => Changed();
        background.Checked += (_, _) => Changed(); background.Unchecked += (_, _) => Changed();
        var context = Label(ContextMessage(), 12);
        modelChoice.SelectionChanged += (_, _) =>
        {
            if (modelChoice.SelectedItem is not InstrumentModelOption option || disposed) return;
            work?.Cancel(); analyzed = null; expected = null; coordinator.Settings.InstrumentModelId = option.Id;
            scope.Text = option.Scope; context.Text = ContextMessage(); Changed(); Refresh();
        };
        Children.Add(context);
        Children.Add(status); Children.Add(progress);
        DialogUi.Button(this, "准备本机已下载模型", async () => await PrepareAsync());
        Children.Add(Label("模型保存在个人数据目录。只导入经过版本和 SHA-256 校验的实验模型；安装说明见项目的乐器识别文档。", 11));
        DialogUi.Button(this, "选择本地 WAV 并预分析", async () => await AnalyzeAsync());
        Children.Add(Label("支持 RIFF PCM16 / PCM24 / float32、1–8 普通声道、8–192 kHz；暂不支持压缩 WAV、RF64 或其他音频格式。", 11));
        Children.Add(Label("完整录音预分析可让检出的乐器从开头以暗色显示；完成音频覆盖不代表所有实际乐器均已识别。选择文件后仍需确认它与当前 Apple Music 的录音版本相同，绑定仅限本次播放会话，切歌后失效。", 12));
        DialogUi.Button(this, "将上次分析绑定到当前播放版本…", Bind);
        DialogUi.Button(this, "取消正在执行的准备或文件分析", () => work?.Cancel());
        DialogUi.Button(this, "清理乐器分析缓存", Clear);
        timer = new(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Refresh(), Dispatcher);
        Loaded += OnLoaded; Unloaded += OnUnloaded;
    }
    InstrumentModelOption CurrentModel => InstrumentModelCatalog.Get(coordinator.Settings.InstrumentModelId);
    string ContextMessage() => $"当前模型需要约 {CurrentModel.Profile.WindowSamples / (double)CurrentModel.Profile.SampleRate:0.##} 秒音频上下文，实际响应还受计算和活动平滑影响。首次实时播放时清单逐步补全；暂停时图标变暗。音频只在内存中处理。";
    static TextBlock Label(string text, double size)
    {
        var label = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) };
        ThemeManager.Bind(label, TextBlock.ForegroundProperty, "MutedBrush"); return label;
    }
    void OnLoaded(object sender, RoutedEventArgs e) { Refresh(); timer.Start(); }
    void OnUnloaded(object sender, RoutedEventArgs e) { timer.Stop(); work?.Cancel(); }
    void Refresh()
    {
        if (disposed || busy) return;
        string path = new InstrumentModelStore(coordinator.DataRoot).LocalPath(CurrentModel.Profile);
        status.Text = File.Exists(path) ? "实验模型已就绪 · " + coordinator.Instruments.View(coordinator.Position).Message : "尚未准备实验模型";
    }
    async Task WorkAsync(Func<CancellationToken, Task> action)
    {
        if (disposed || busy) return;
        busy = true; work = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        try { await action(work.Token); }
        catch (OperationCanceledException) { status.Text = "操作已取消，原有分析保留。"; }
        catch (Exception error) when (error is not OutOfMemoryException)
        { status.Text = error is FileNotFoundException ? "未找到模型文件，请按文档下载并生成，或选择已校验模型。" : "操作失败：" + error.Message; }
        finally { progress.Visibility = Visibility.Collapsed; work.Dispose(); work = null; busy = false; }
    }
    async Task PrepareAsync()
    {
        if (busy) return;
        var option = CurrentModel;
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LumaLyrics-InstrumentChecks", option.ExportFolder, option.ExportFile);
        if (!File.Exists(path))
        {
            var choose = new OpenFileDialog { Title = "选择经过校验的实验 ONNX 模型", Filter = "ONNX 模型|*.onnx", CheckFileExists = true };
            if (choose.ShowDialog(Window.GetWindow(this)) != true) return;
            path = choose.FileName;
        }
        await WorkAsync(async token =>
        {
            status.Text = "正在校验和安装模型…";
            await new InstrumentModelStore(coordinator.DataRoot).ImportAsync(path, option.Profile, option.Bytes, token);
            token.ThrowIfCancellationRequested();
            coordinator.Instruments.Restart(); status.Text = "模型准备完成。可开启实验识别。";
        });
    }
    async Task AnalyzeAsync()
    {
        if (busy) return;
        var choose = new OpenFileDialog { Title = "选择完整的本地 WAV 录音", Filter = "WAV 音频|*.wav", CheckFileExists = true };
        if (choose.ShowDialog(Window.GetWindow(this)) != true) return;
        expected = coordinator.Source.Latest; analyzed = null;
        await WorkAsync(async token =>
        {
            status.Text = "正在分析本地文件…"; progress.Value = 0; progress.Visibility = Visibility.Visible;
            var profile = CurrentModel.Profile;
            string model = new InstrumentModelStore(coordinator.DataRoot).LocalPath(profile);
            string path = Path.GetFullPath(choose.FileName);
            var result = await Task.Run(async () =>
            {
                await using var engine = new InstrumentEngine(new OnnxInstrumentClassifier(model, profile));
                using var repository = new InstrumentRepository(coordinator.DataRoot);
                var info = WaveAudioReader.Inspect(path);
                string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path))).ToLowerInvariant();
                var key = new InstrumentRecordingKey("explicit-local-file", id, checked((long)Math.Ceiling(info.DurationMs)), null);
                return await new InstrumentFileAnalysis(engine, repository, WaveAudioReader.ReadAsync)
                    .AnalyzeAsync(path, key, new Progress<double>(value => Dispatcher.BeginInvoke(() => { if (!disposed && !token.IsCancellationRequested) progress.Value = value * 100; })), token);
            }, token);
            token.ThrowIfCancellationRequested(); analyzed = result;
            status.Text = $"文件分析完成 · {(result.Complete ? "整曲已分析" : "部分覆盖")} · 识别到 {InstrumentVocabulary.VisibleKinds(result.Roster).Length} 种支持的乐器（模型判断）。可核对后绑定当前版本。";
        });
    }
    void Bind()
    {
        if (busy || analyzed is null) { status.Text = "请先完成本地文件分析。"; return; }
        var current = coordinator.Source.Latest;
        if (expected is null || current is null || expected.SessionId != current.SessionId || expected.Generation != current.Generation || expected.Revision != current.Revision)
        { status.Text = "播放版本已改变，请在当前录音下重新选择文件分析。"; return; }
        if (current.Track.DurationMs is not { } duration || Math.Abs(duration - analyzed.Recording.DurationMs) > 2000)
        { status.Text = "当前播放时长与文件不匹配，不能绑定。"; return; }
        var answer = MessageBox.Show(Window.GetWindow(this), $"确认选中的文件与当前 Apple Music 正在播放的《{current.Track.Title}》是完全相同的录音版本？不同演奏或剪辑不能共享时间轴。", "确认录音版本", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        status.Text = coordinator.Instruments.Bind(analyzed, current) ? "已绑定本次播放版本。播放页将显示整曲分析检出的清单；切歌后自动解除。" : "播放已改变，绑定未生效。";
    }
    void Clear()
    {
        if (busy) { status.Text = "请先取消正在执行的分析。"; return; }
        using var repository = new InstrumentRepository(coordinator.DataRoot);
        repository.Clear(); coordinator.Instruments.ClearBinding(); analyzed = null;
        status.Text = "乐器分析缓存已清理，原始音乐、歌词和模型保留。";
    }
    public void Dispose()
    { if (disposed) return; disposed = true; Loaded -= OnLoaded; Unloaded -= OnUnloaded; timer.Stop(); lifetime.Cancel(); work?.Cancel(); lifetime.Dispose(); }
}
