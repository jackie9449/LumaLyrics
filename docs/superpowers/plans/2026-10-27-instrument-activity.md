# 乐器活动显示 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 LumaLyrics 主播放页显示本曲已知乐器，以暗色、活动高亮和未知状态表达演奏情况，支持精确录音的整曲预分析与历史缓存。

**Architecture:** Windows 进程回环取得当前 Apple Music 音频，本地模型识别多种乐器的活动。Core 负责时间、状态和覆盖规则，Infrastructure 负责模型及个人缓存，App 复用媒体会话和播放时钟驱动图标。完整缓存优先；首次未知流媒体实时累积，不预先编造整曲清单。

**Tech Stack:** C# 14、.NET 10、WPF、Windows WASAPI process loopback、ONNX Runtime CPU、Microsoft.Data.Sqlite、xUnit；YAMNet 与 PANNs 为评估候选，最终权重和运行时包在任务 3 固定版本与哈希。

**Spec:** [产品与技术约定](../specs/2026-10-27-instrument-activity-design.md)。执行前同时阅读仓库根目录 `AGENTS.md`。

## Global Constraints

- 新旧配置均默认关闭 `ShowInstrumentActivity` 和 `AnalyzeInstrumentsInBackground`。
- 第一版在主播放页显示；设置、模型安装、缓存清理和本地音频分析均位于 Settings。
- 不增加主题选择器到播放页，不新增左侧导航项，不创建第二个资料库管理窗口。
- 不自动降级到系统总输出或麦克风。
- 原始 PCM 只存在于有界内存缓冲；默认不保存录音、不上传音频、不复制用户本地音乐文件。
- 分数不是乐器音量；“已分析整曲”不等于所有乐器已被准确识别。
- 只对精确录音及兼容分析版本复用时间线；未知身份只允许当前会话的实时展示。
- 切歌、seek、循环、暂停恢复及设备改变必须使旧窗口失效；模型忽略取消也不能提交旧结果。
- 项目最低 Windows 平台保持 19041；进程回环必须运行时检测 build 20348 及接口能力。
- 主要布局验收为宽度 1220、980、820，队列开关两种状态，100%、150%、200% DPI。
- Liquid、Frosted、Material 必测，并回归 `AppThemes.All` 中已有其他主题；浮动歌词、工具栏和预览资源保持独立。
- 模型、数据库、逐曲分析、音频及原生诊断均保存在个人目录，拒绝进入 Git 和软件压缩包。
- 使用 `scripts/common.ps1` 选择 SDK，UI 验证必须使用独立 `--data-dir`。
- UI 模拟验证和实际 Apple Music 音频验证分别报告。
- 每个完成的实现任务使用 conventional commit；同步前检查远端，不强推，不覆盖其他人的改动。

---

## 使用方式与执行顺序

执行中：任务 1 已完成，Core 全部 106 项测试通过（其中乐器相关 18 项）。任务 2 开始实现；G1 仍待实际播放验证。仓库检查基线为 `4cc273f7b8d4ed842915071a3d9e0a81d8fa3da1`，编写时本地与远端 main 一致、工作区干净；实现时重新检查。

上方技能提示是 writing-plans 的标准交接格式。当前已提供的技能目录没有列出那两个 `superpowers` 执行技能；选择执行方式时先检查可用性，不能声称已经调用。任务本身包含独立的操作和验收步骤，用户若直接要求按计划执行，可按本计划逐项执行；子代理方式需遵守当时的用户授权和会话规则。

依赖顺序：`1 → 2 → 3 → 4 → 5 → 6 → 7 → 8 → 9 → 10`。同一条功能链只使用这一份计划，不拆成缺少上下文的独立项目。

| 里程碑 | 任务 | 继续条件 | 失败处理 |
| --- | --- | --- | --- |
| G1 音频可用 | 1–2 | 确认进程归属，实际播放有有效 PCM，取消与释放正常 | 标记实时路径不可用，继续评估本地 WAV／缓存路径；不伪装实时成功 |
| G2 模型可用 | 3–4 | 有达到准确度、延迟、资源目标的类别，转换数值可核对 | 缩小类别／更换候选；仍失败则保留实验成果，不推进正式识别 UI 发布 |
| G3 数据可信 | 5–7 | 身份、缓存完整度、seek、过期结果和生命周期测试通过 | 修复相应任务，不用仅按歌名的缓存回避身份问题 |
| G4 可交付 | 8–10 | UI、真实播放、备份与分发边界通过 | 保持功能默认关闭，记录限制，不以模拟 UI 检查代替实测 |

## 仓库落点

以下“新增”均为计划文件，不表示现有实现。修改现有类时以成员名定位，避免沿用过期行号。

| 层 | 新增文件 | 修改的现有文件／成员 |
| --- | --- | --- |
| Core | `src/LumaLyrics.Core/InstrumentModels.cs`、`InstrumentActivityFilter.cs`、`InstrumentCoverage.cs`、`InstrumentEpochGuard.cs`、`InstrumentWindowBuilder.cs` | 不修改歌词匹配及 `Track.Fingerprint` 的既有含义 |
| Windows | `src/LumaLyrics.Windows/ProcessAudioCapture.cs`、`ProcessAudioInterop.cs`、`AppleMusicAudioTargetResolver.cs`、`PcmNormalizer.cs`、`WaveAudioReader.cs` | 通过引用使用 `GsmtcPlaybackSource.Latest`，不改变播放控制逻辑 |
| Infrastructure | `src/LumaLyrics.Infrastructure/InstrumentModelManifest.cs`、`InstrumentModelStore.cs`、`OnnxInstrumentClassifier.cs`、`InstrumentEngine.cs`、`InstrumentSession.cs`、`InstrumentRecordingIdentity.cs`、`InstrumentRepository.cs`、`InstrumentTimelineBuilder.cs`、`InstrumentFileAnalysis.cs`、`InstrumentBackup.cs` | `SettingsStore.cs/AppSettings`、`PersonalLibrarySnapshot.cs/Create`、`PersonalLibraryArchive.cs`、项目引用与 lock 文件 |
| App | `src/LumaLyrics.App/PlaybackInstruments.cs`、`InstrumentActivityView.cs`、`InstrumentIcons.cs`、`InstrumentSettingsPanel.cs`、`InstrumentVerification.cs` | `PlaybackCoordinator.cs/Receive,DisposeAsync`、`MainWindow.xaml`、`MainWindow.xaml.cs`、`PlaybackLayout.cs`、`SettingsWindow.cs`、`UiThemeVerification.cs`、`App.xaml.cs` |
| 工具 | `tools/LumaLyrics.Probe/InstrumentAudioProbe.cs`、`tools/LumaLyrics.Probe/InstrumentBenchmarkCommands.cs`、`tools/instruments/evaluate.py`、`scripts/verify-instruments.ps1` | `tools/LumaLyrics.Probe/Program.cs`、必要的项目引用 |
| 测试 | `InstrumentActivityTests.cs`、`InstrumentCoverageTests.cs`、`InstrumentWindowTests.cs`、`InstrumentEpochTests.cs`（Core.Tests）；`InstrumentModelTests.cs`、`InstrumentStorageTests.cs`、`InstrumentEngineTests.cs`、`InstrumentBackupTests.cs`（Infrastructure.Tests） | 新建 `tests/LumaLyrics.Windows.Tests/` 测试项目，登记到 `LumaLyrics.slnx`；扩展 `StorageTests.cs`、`PersonalLibraryTests.cs`、分发测试 |
| 文档与隔离 | `docs/instrument-activity.md`、`docs/instrument-evaluation.md` | `README.md`、`docs/manual-test-checklist.md`、`docs/release-notes.md`、`.gitignore`、`scripts/verify-distribution.ps1`、`scripts/test-distribution.ps1`、`THIRD-PARTY-NOTICES.md`、`licenses/` |

不新增通用插件框架，不将音频识别揉进 `PlaybackCoordinator.cs`；该类仅保留少量调用，功能实现放在 partial 文件及独立服务。

## 公共契约

任务 1 将以下契约放入 `LumaLyrics.Core` 命名空间。各任务不得私自改变字段语义；确需调整时，同步调用方及测试。

```csharp
public enum InstrumentKind { Drums, Bass, Guitar, Piano, Strings, Brass, Woodwind, Synth }
public enum InstrumentStatus { Disabled, Preparing, Learning, Ready, Paused, Unavailable }
public enum InstrumentVisualState { Unknown, Dim, Active }
public sealed record InstrumentEpoch(string SessionId, long Generation, long Revision, long Discontinuity);
public sealed record InstrumentRecordingKey(string Source, string PersistentId, long DurationMs, string? AudioSha256);
public sealed record InstrumentSpan(double StartMs, double EndMs);
public sealed record InstrumentInterval(InstrumentKind Kind, double StartMs, double EndMs);
public sealed record InstrumentScorePoint(double StartMs, double EndMs, Dictionary<InstrumentKind, float> Scores);
public sealed record ActivityParameters(float On, float Off, int AttackFrames, double ReleaseMs);
public sealed record AudioTarget(uint ProcessId, DateTime StartTimeUtc, string PackageFamily);
public sealed record PcmChunk(float[] Samples, int SampleRate, int Channels, long FirstSampleTicks,
    bool Discontinuous, bool Silent);
public sealed record InstrumentWindow(InstrumentEpoch Epoch, long StartTicks, long EndTicks,
    double? StartMs, double? EndMs, int SampleRate, float[] Mono);
public sealed record InstrumentFrame(InstrumentEpoch Epoch, long EndTicks, double? StartMs, double? EndMs,
    string ProfileId, IReadOnlyDictionary<InstrumentKind, float> Scores);
public sealed record InstrumentTimeline(InstrumentRecordingKey Recording, string ProfileId,
    InstrumentSpan[] Coverage, InstrumentScorePoint[] Scores, InstrumentInterval[] Intervals,
    InstrumentKind[] Roster, bool Complete, long Revision);
public sealed record InstrumentViewState(InstrumentStatus Status, bool Complete,
    IReadOnlyDictionary<InstrumentKind, InstrumentVisualState> Icons, string Message);
public interface IProcessAudioCapture : IAsyncDisposable
{
    IAsyncEnumerable<PcmChunk> ReadAsync(AudioTarget target, CancellationToken token);
}
public interface IInstrumentClassifier : IAsyncDisposable
{
    string ProfileId { get; }
    int SampleRate { get; }
    int WindowSamples { get; }
    int HopSamples { get; }
    ValueTask<IReadOnlyDictionary<InstrumentKind, float>> ClassifyAsync(float[] mono, CancellationToken token);
}
```

`FirstSampleTicks` 与 window ticks 均是 `Stopwatch` 时间域。WASAPI 返回的 QPC 100 ns 值必须按频率换算，不可直接当 Stopwatch ticks。样本数组由消息独占，不允许发送后复用修改。跨声道／采样率的转换只在一个固定边界进行。

额外 API 分属后续任务：

```csharp
// Core
public sealed class InstrumentActivityFilter(ActivityParameters parameters)
{
    public bool Update(float score, double nowMs);
    public void Reset();
}
public sealed class InstrumentCoverage
{
    public IReadOnlyList<InstrumentSpan> Ranges { get; }
    public void Add(double startMs, double endMs);
    public bool IsComplete(double durationMs, double toleranceMs = 50);
}
public sealed class InstrumentEpochGuard
{
    public void Set(InstrumentEpoch value);
    public bool IsCurrent(InstrumentEpoch value);
}
// Infrastructure
public sealed class InstrumentRepository : IDisposable
{
    public InstrumentRepository(string dataRoot);
    public IReadOnlyList<InstrumentRecordingKey> FindRecordings(string source, string persistentId);
    public InstrumentTimeline? Load(InstrumentRecordingKey recording, string profileId);
    public void Save(InstrumentTimeline timeline);
    public void Clear();
    public void Dispose();
}
```

以上是签名清单，不是可直接粘贴的实现类。各任务的行为、算法和测试在下文定义。`InstrumentEpochGuard` 仅处理原子代次；数据库提交与 UI dispatcher 回调还须在各自提交位置核对最新值。

## 统一命令与证据位置

在仓库根目录的 PowerShell 中执行：

```powershell
. .\scripts\common.ps1
$instrumentCheckRoot = Join-Path $env:LOCALAPPDATA ('LumaLyrics-InstrumentChecks\' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $instrumentCheckRoot | Out-Null
Invoke-Dotnet test tests/LumaLyrics.Core.Tests -c Release --filter 'FullyQualifiedName~Instrument'
Invoke-Dotnet test tests/LumaLyrics.Infrastructure.Tests -c Release --filter 'FullyQualifiedName~Instrument'
```

第一条测试命令仅在相应测试已创建后运行。新增 Windows.Tests 后同样用 `Invoke-Dotnet test tests/LumaLyrics.Windows.Tests -c Release`。各任务先运行指定新增失败测试，再实现，再重跑该组；最终阶段统一跑全套。原始报告、录音标注、模型及临时文件写入 `$instrumentCheckRoot`，不提交 Git。公开文档只写测试环境、汇总指标及已验证限制。

### Task 1：建立活动、覆盖与过期规则

**Files:** 新增 Core 契约、`InstrumentActivityFilter.cs`、`InstrumentCoverage.cs`、`InstrumentEpochGuard.cs`；新增 Core.Tests 中的 `InstrumentActivityTests.cs`、`InstrumentCoverageTests.cs`、`InstrumentEpochTests.cs`。

**Interfaces:** 输入为上面的 `ActivityParameters`、`InstrumentEpoch` 和区间；输出为稳定活动状态、合并覆盖区间、当前代次判断。

- [x] **Step 1：先添加以下行为测试。**

```csharp
[Fact]
public void ActivityRequiresAttackAndHonorsRelease()
{
    var filter = new InstrumentActivityFilter(new(.65f, .40f, 2, 750));
    Assert.False(filter.Update(.80f, 0));
    Assert.True(filter.Update(.80f, 480));
    Assert.True(filter.Update(.20f, 960));
    Assert.False(filter.Update(.20f, 1440));
    filter.Reset();
    Assert.False(filter.Update(.90f, 2000));
}
[Fact]
public void ASeekGapDoesNotBecomeCompleteThroughOverlap()
{
    var coverage = new InstrumentCoverage();
    coverage.Add(0, 10000);
    coverage.Add(20000, 30000);
    coverage.Add(0, 10000);
    Assert.False(coverage.IsComplete(30000));
    coverage.Add(10000, 20000);
    Assert.True(coverage.IsComplete(30000));
    Assert.Single(coverage.Ranges);
}
[Fact]
public void SameRecordingAfterSeekRejectsOldEpoch()
{
    var guard = new InstrumentEpochGuard();
    var before = new InstrumentEpoch("AppleMusic", 1, 1, 0);
    guard.Set(before with { Discontinuity = 1 });
    Assert.False(guard.IsCurrent(before));
}
```

- [x] **Step 2：运行 Core 的 Instrument 测试，确认缺少实现导致失败。** 使用统一命令；失败不得来自无关 SDK 或依赖错误。
- [x] **Step 3：实现规则。** `Update` 检查分数有限且在 `[0,1]`、时间单调；连续 `AttackFrames` 个分数 ≥ On 才进入活动。活动期间分数 ≥ Off 更新最后有效时间，低于 Off 达到 ReleaseMs 后熄灭；Reset 清空全部状态。生产阈值从任务 3 的逐类别配置取得，测试数值不作为准确度结论。覆盖按起点排序合并相接／重叠区间，只有首尾采样边界使用 50 ms 容差，内部实际缺口始终保留。

```csharp
// EpochGuard 的并发发布与读取核心。
private InstrumentEpoch? current;
public void Set(InstrumentEpoch value) => Volatile.Write(ref current, value);
public bool IsCurrent(InstrumentEpoch value) => Equals(Volatile.Read(ref current), value);
```

- [x] **Step 4：补齐并运行边界测试。** `NaN`、无限值、负时间、反向区间、零长度、越界分数必须被拒绝；没有任何覆盖不能 Complete；首尾容差不能吞掉内部 1 ms 缺口。不同乐器分别使用 filter，互不排斥。
- [x] **Step 5：检查 diff 并提交。** `feat: define instrument activity and coverage rules`。交付是可独立测试的纯状态逻辑。

### Task 2：验证 Apple Music 进程回环（G1）

**Files:** 新增 Windows 捕获、interop、目标解析、PCM 标准化；新增 `InstrumentAudioProbe.cs`；修改 Probe 命令分发；新建 Windows.Tests 项目与 `PcmNormalizerTests.cs`、`ProcessAudioCaptureTests.cs`，登记 solution。

**Interfaces:** `AppleMusicAudioTargetResolver.ResolveAsync(PlaybackSnapshot expected, CancellationToken token)` 返回 `Task<AudioTarget?>`；`PcmNormalizer.ToFloat(byte[] bytes, int bitsPerSample, bool ieeeFloat)` 返回 `float[]`；捕获实现 `IProcessAudioCapture`。Probe 命令为 `--probe-instrument-audio --data-dir <目录> --seconds 30`。

- [ ] **Step 1：先写 PCM 测试与取消测试并确认失败。**

```csharp
[Fact]
public void SignedPcm16KeepsZeroAndExtremes()
{
    var samples = PcmNormalizer.ToFloat(new byte[] { 0, 128, 0, 0, 255, 127 }, 16, false);
    Assert.Equal(-1f, samples[0]);
    Assert.Equal(0f, samples[1]);
    Assert.InRange(samples[2], .9999f, 1f);
}
[Fact]
public async Task AlreadyCancelledCaptureDoesNotOpenNativeClient()
{
    using var cancel = new CancellationTokenSource();
    cancel.Cancel();
    await using var source = new ProcessAudioCapture();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
    {
        await foreach (var chunk in source.ReadAsync(new(1, DateTime.UnixEpoch, "test"), cancel.Token))
            Assert.Fail("Cancelled capture yielded audio.");
    });
}
```

- [ ] **Step 2：实现目标解析和系统能力判定。** 使用当前媒体会话应用身份核对进程包身份及启动时间；无法唯一确认则返回 null。禁止固定 PID、窗口标题或全系统音频替代。新增 Windows.Tests 项目目标与 Windows 项目一致，并引用 Windows、Core；测试库版本沿用现有 xUnit 项目。
- [ ] **Step 3：按微软官方样例移植进程回环。** 原生顺序固定如下，COM 声明按 Windows SDK 头文件核对布局与 HRESULT；不凭猜测抄写 GUID。

```text
ActivateAudioInterfaceAsync("VAD\\Process_Loopback", IID_IAudioClient,
  VT_BLOB(AUDIOCLIENT_ACTIVATION_PARAMS:
    ActivationType = AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK,
    ProcessLoopbackParams.TargetProcessId = verified PID,
    ProcessLoopbackParams.ProcessLoopbackMode = PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE))
→ ActivateCompleted / GetActivateResult
→ IAudioClient.Initialize(shared, loopback + event callback, supported format)
→ SetEventHandle / GetService(IAudioCaptureClient) / Start
→ GetNextPacketSize / GetBuffer / copy or zero silent packet / ReleaseBuffer
→ cancellation or invalidation → Stop → release handles and COM references
```

实现每次激活／停止的有限等待，启动超时 5 秒；取消后不得有遗留启动回调重新启动客户端。采集回调只复制缓冲并投递；正确处理 float32、PCM16、PCM24、PCM32 及 `WAVEFORMATEXTENSIBLE` 的有效位，无法识别的格式报告不可用。QPC 时间戳错误、断续及丢包标记为 discontinuity。为边界检查增加各格式的合成测试，不能拿正弦波测试当作模型准确度测试。

- [ ] **Step 4：增加私有探针命令。** 校验目录在软件目录外，输出 `instrument-audio-probe.json`，包括系统 build、目标验证结果、采样格式、packet 数、有效帧数、RMS 分位数、掉帧数、启动／释放耗时及错误分类。默认不写 PCM、不在 stdout 打印曲名、PID 或完整路径。

```powershell
Invoke-Dotnet run --project tools/LumaLyrics.Probe -c Release -- --probe-instrument-audio --data-dir $instrumentCheckRoot --seconds 30
```

- [ ] **Step 5：实测并记录 G1。** 实际播放 Apple Music，分别检查最小化、暂停恢复、输出设备切换、目标进程退出；另一个应用播放测试音时，Apple Music 目标捕获不得包含该应用声音。受保护内容、音质及蓝牙组合按实际环境记录，未测试的组合明确标记。全零只记录“未获得可验证有效音频”，不自动归因为 DRM。测试不主动改变用户歌曲或原生窗口状态，必要操作由显式探针参数或用户实际操作触发。
- [ ] **Step 6：提交实现和脱敏汇总。** `feat: add cancellable Apple Music process audio capture`。G1 失败时在 `docs/instrument-evaluation.md` 写明原因及本地 WAV 路径仍可执行的范围。

### Task 3：评估并锁定模型、标签及安装协议（G2 的模型部分）

**Files:** 新增 `tools/instruments/evaluate.py`、`InstrumentBenchmarkCommands.cs`、`InstrumentModelManifest.cs`、`InstrumentModelStore.cs`、`InstrumentModelTests.cs`；新增公开汇总 `docs/instrument-evaluation.md`；更新 notices、licenses、相关项目与 lock 文件。

**Interfaces:** `InstrumentModelStore.EnsureAsync(InstrumentModelManifest manifest, CancellationToken token)` 返回 `Task<string>`（已校验模型路径）；`ValidateAsync(string modelPath, string expectedSha256, CancellationToken token)` 返回 `Task<bool>`。Manifest 保存官方权重和转换权重各自 SHA-256、源代码 revision、许可来源、文件长度、模型输入输出、预处理版本、各类别标签映射、活动阈值、window/hop 和 ProfileId。

- [ ] **Step 1：建立私有评价集。** 至少 30 首，按录音分校准／留出集；标注活动区间和“不确定”区间。不确定部分不进入准确度分母并报告比例。收集密集混音、纯钢琴、弦乐、电子音色、轻伴奏、无目标乐器负例。数据、标签、来源许可记录写入外部目录，不提交歌曲名称和音频。

```json
{
  "recordingId": "self-authored-fixture-01",
  "split": "holdout",
  "audioPath": "audio/self-authored-fixture-01.wav",
  "events": [{ "kind": "Piano", "startMs": 1000, "endMs": 6000 }],
  "uncertain": [{ "startMs": 6000, "endMs": 6500 }]
}
```

这个 JSON 是自写测试样本结构，真实录音 ID 和路径只写私人清单。

- [ ] **Step 2：实现离线评价入口并比较候选。** `evaluate.py` 接受 `--dataset`、`--model yamnet|panns`、`--output`，读取实际模型标签，不杜撰标签；同一窗口对各类别独立打分，不能用 argmax 排除同时演奏的其他乐器。标签到乐器族的映射逐条写入 manifest；校准集选阈值，留出集只评价一次，若再次调参须重新划分留出集。

```python
# evaluate.py 中按类别汇总 0.5 秒格的基本指标；labels 已排除 uncertain 区间。
def class_metrics(labels, predicted):
    if len(labels) != len(predicted):
        raise ValueError("Label and prediction time grids differ")
    tp = sum(a and b for a, b in zip(labels, predicted))
    fp = sum((not a) and b for a, b in zip(labels, predicted))
    fn = sum(a and (not b) for a, b in zip(labels, predicted))
    return {"precision": tp / (tp + fp) if tp + fp else None,
            "recall": tp / (tp + fn) if tp + fn else None,
            "tp": tp, "fp": fp, "fn": fn}
```

先用 `[True, False, True]` 与 `[True, True, False]` 验证结果 precision、recall 均为 0.5；没有正例不能算 100%。评价另外计算进入／退出 P50/P95、每窗口推断耗时和内存。不直接比较文献整体 mAP 来声称本功能准确。

在独立 Python 环境安装候选官方依赖并保存依赖锁定清单后执行；`dataset.json` 为上一步在私人目录生成的录音列表：

```powershell
python tools/instruments/evaluate.py --dataset "$instrumentCheckRoot\dataset.json" --model yamnet --output "$instrumentCheckRoot\yamnet-evaluation.json"
python tools/instruments/evaluate.py --dataset "$instrumentCheckRoot\dataset.json" --model panns --output "$instrumentCheckRoot\panns-evaluation.json"
```

- [ ] **Step 3：选定一个模型并导出 ONNX。** 固定原始实现 revision、Python 依赖及转换命令，记录到公开复现说明。至少 20 个短窗口对比原始模型与 C# ONNX 输出，最大绝对分数差目标 ≤ `1e-4`；超出时定位预处理／算子／精度差异，不静默放宽。模型与标签必须配对，错误标签数应拒绝启动。
- [ ] **Step 4：先写校验失败测试，再实现模型安装。** 测试用固定字节生成哈希，不能将测试哈希填入生产 manifest。

```csharp
[Fact]
public async Task ChangedWeightsAreRejected()
{
    string root = Path.Combine(Path.GetTempPath(), "Luma-model-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        string file = Path.Combine(root, "fixture.onnx");
        await File.WriteAllBytesAsync(file, new byte[] { 1, 2, 3 });
        string expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(new byte[] { 1, 2, 4 }));
        var store = new InstrumentModelStore(root);
        Assert.False(await store.ValidateAsync(file, expected, CancellationToken.None));
    }
    finally { Directory.Delete(root, true); }
}
```

安装只使用发布清单中的 HTTPS 地址；检查重定向后的来源、最大文件长度和 SHA-256；下载到独占 `.partial` 文件，校验通过后原子重命名。取消只清理本次下载文件。损坏权重、网络不可用、磁盘不足分别给出可恢复状态，不能删除仍可用的旧版本。
- [ ] **Step 5：锁定发布材料。** 分别核对代码和权重许可，记录转换来源和再分发条件；选择客户端运行时包后写入精确 PackageReference 版本与 packages.lock.json。公开 manifest 必须含真实固定哈希，不接受任意用户给定地址替换。未找到可合法提供的权重时保留本地模型导入实验路径，不自行托管不明权重。
- [ ] **Step 6：记录 G2 模型结果并提交。** 汇总各类别样本数、precision、recall、时间偏差和设备性能；未达到目标的类别不启用。提交 `feat: add verified instrument model installation and evaluation`。这个阶段只选择候选，不承诺通用乐器识别。

### Task 4：实现有界流式推断与时间窗口

**Files:** 新增 `InstrumentWindowBuilder.cs`、`OnnxInstrumentClassifier.cs`、`InstrumentEngine.cs`、`InstrumentWindowTests.cs`、`InstrumentEngineTests.cs`；完善 `PcmNormalizer.cs` 重采样。

**Interfaces:** `InstrumentWindowBuilder(int sampleRate, int windowSamples, int hopSamples)`；`Push(float[] mono, long firstSampleTicks, InstrumentEpoch epoch, double? firstSampleMs)` 返回 `IReadOnlyList<InstrumentWindow>`；`Reset()`。`InstrumentEngine(IInstrumentClassifier classifier)` 提供 `AnalyzeAsync(InstrumentWindow window, CancellationToken token): Task<InstrumentFrame>` 及 `DisposeAsync()`。

- [ ] **Step 1：写窗口测试并确认失败。**

```csharp
[Fact]
public void ResetPreventsAudioFromTwoRecordingsSharingAWindow()
{
    var windows = new InstrumentWindowBuilder(1000, 1000, 500);
    var a = new InstrumentEpoch("AppleMusic", 1, 1, 0);
    var b = a with { Revision = 2 };
    Assert.Empty(windows.Push(new float[600], 0, a, 0));
    windows.Reset();
    Assert.Empty(windows.Push(new float[600], 0, b, 0));
}
```

- [ ] **Step 2：实现格式标准化与窗口。** 多声道转换使用格式中的声道掩码；未知映射拒绝或显式声明不支持，不猜测 Dolby 通道。先以抗混叠滤波重采样到 manifest 指定采样率，再按固定样本数切窗；维护连续重采样状态，不能每包独立重采样造成边界跳变。测试 44.1/48 kHz 的长度、通带、阻带及分块与整块输出一致性。用与模型官方预处理相同的 STFT、mel、log、padding 规则，全部纳入数值对照。
- [ ] **Step 3：实现串行消费者和有界音频缓冲。** 音频上限 10 秒；用于待推断窗口的 channel 只保留最新窗口，丢弃必须可观测。

```csharp
var pending = System.Threading.Channels.Channel.CreateBounded<InstrumentWindow>(
    new System.Threading.Channels.BoundedChannelOptions(1)
    {
        SingleReader = true,
        SingleWriter = true,
        FullMode = System.Threading.Channels.BoundedChannelFullMode.DropOldest
    });
```

生产实现通过带 drop 回调的创建重载或显式替换队列记录丢弃窗口；不可因为 channel 不报错而把丢弃区间算覆盖。实时和离线任务共享推断调度，实时任务优先；回调不阻塞音频线程。
- [ ] **Step 4：实现分析及取消。** ONNX session 常驻，样本窗口长度必须与 manifest 一致；输出浮点有限、标签维度正确后才返回 frame。取消在取窗口、开始推断、返回结果和发布前均检查；结束时释放 session 和池化缓冲。生产模型不响应取消时允许当前调用结束，但结果丢弃且不得启动下一窗口。
- [ ] **Step 5：运行窗口、流式与模型对照测试。** 验证窗口不是跨曲拼接；积压时分析最新数据；未知时间线时 frame 的 `StartMs/EndMs` 为 null 但实时状态可用；断续后必须重建上下文；零填充不会生成伪覆盖。使用任务 3 的留出数据实测 G2 端到端延迟，计入 attack 与动画。
- [ ] **Step 6：提交。** `feat: stream bounded instrument inference windows`。

### Task 5：建立录音身份和可验证的个人缓存

**Files:** 新增 `src/LumaLyrics.Infrastructure/InstrumentRecordingIdentity.cs`、`InstrumentRepository.cs`、`InstrumentTimelineBuilder.cs`、`tests/LumaLyrics.Infrastructure.Tests/InstrumentStorageTests.cs`；补充 Core 的覆盖测试。

**Interfaces:** 使用公共 `InstrumentRecordingKey`、`InstrumentTimeline` 和 `InstrumentRepository`。`InstrumentRecordingIdentity.KeyId(InstrumentRecordingKey recording): string` 生成存储键；`InstrumentTimelineBuilder(InstrumentRecordingKey recording, string profileId)` 提供 `Add(InstrumentFrame frame, InstrumentInterval[] intervals)`、`Build(): InstrumentTimeline`。

- [ ] **Step 1：写版本隔离及不完整覆盖测试，运行后确认失败。**

```csharp
[Fact]
public void RecordingAndAnalysisVersionsCannotShareResults()
{
    string root = Path.Combine(Path.GetTempPath(), "Luma-instruments-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        using var repository = new InstrumentRepository(root);
        var studio = new InstrumentRecordingKey("fixture-library", "studio-id", 30000, null);
        var live = studio with { PersistentId = "live-id" };
        repository.Save(new(studio, "profile-a", new[] { new InstrumentSpan(0, 10000) },
            Array.Empty<InstrumentScorePoint>(), Array.Empty<InstrumentInterval>(),
            new[] { InstrumentKind.Piano }, false, 1));
        Assert.False(repository.Load(studio, "profile-a")!.Complete);
        Assert.Null(repository.Load(live, "profile-a"));
        Assert.Null(repository.Load(studio, "profile-b"));
    }
    finally { Directory.Delete(root, true); }
}
```

- [ ] **Step 2：实现录音身份门槛与存储键。** `Source` 必须是已确认来源命名空间，`PersistentId` 必须来自已确认的本机录音引用或用户明确文件绑定；名称匹配只生成候选。当前 `PlaybackSnapshot` 不携带持久 ID，必须先检查已确认原生播放交接是否提供该证据；不能假设该字段已经存在。外部换曲后不能沿用上次交接的 ID。暂时无法确认时继续实时展示，不自动读写跨次播放缓存。

```csharp
public static string KeyId(InstrumentRecordingKey recording)
{
    if (string.IsNullOrWhiteSpace(recording.Source) || string.IsNullOrWhiteSpace(recording.PersistentId)
        || recording.DurationMs <= 0) throw new ArgumentException("Recording identity is incomplete.");
    var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(recording);
    return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
}
```

对本地文件，以内容 SHA-256 区分版本，保存用户绑定证据；不能以“时长差不到几秒”代替确认。若现有原生流程不能给出可复用精确身份，将此列为自动缓存复用限制，禁止为凑齐功能偷偷采用 `Track.Fingerprint`。

确认来源与持久 ID 后，先由 `FindRecordings` 读取已存储的不可变 recording key，再校验时长与文件绑定；不能用每次 GSMTC 上报的取整时长重新生成 key。多个不同内容哈希候选且没有当前版本证据时拒绝自动选择。时长只作为一致性检查，容差只能对应已观察到的来源取整误差，不能放宽为版本匹配依据。增加“同一持久 ID 对应多个文件内容”“上报时长取整”“旧绑定在外部切歌后失效”测试。

- [ ] **Step 3：实现数据库事务与验证。** 使用个人根目录 `instruments.db`，WAL、参数化 SQL、`foreign_keys=ON`；版本以 `PRAGMA user_version=1` 管理。一个分析事务同时写入元数据、覆盖、分数和活动区间。最小表结构如下：

```sql
CREATE TABLE Recordings(Id TEXT PRIMARY KEY, IdentityJson TEXT NOT NULL, BindingJson TEXT);
CREATE TABLE Analyses(RecordingId TEXT NOT NULL, ProfileId TEXT NOT NULL,
    Revision INTEGER NOT NULL, Complete INTEGER NOT NULL, RosterJson TEXT NOT NULL,
    PRIMARY KEY(RecordingId, ProfileId), FOREIGN KEY(RecordingId) REFERENCES Recordings(Id));
CREATE TABLE Coverage(RecordingId TEXT NOT NULL, ProfileId TEXT NOT NULL,
    StartMs REAL NOT NULL, EndMs REAL NOT NULL,
    FOREIGN KEY(RecordingId,ProfileId) REFERENCES Analyses(RecordingId,ProfileId) ON DELETE CASCADE);
CREATE TABLE Scores(RecordingId TEXT NOT NULL, ProfileId TEXT NOT NULL,
    StartMs REAL NOT NULL, EndMs REAL NOT NULL, ScoresJson TEXT NOT NULL,
    FOREIGN KEY(RecordingId,ProfileId) REFERENCES Analyses(RecordingId,ProfileId) ON DELETE CASCADE);
CREATE TABLE Intervals(RecordingId TEXT NOT NULL, ProfileId TEXT NOT NULL,
    Kind INTEGER NOT NULL, StartMs REAL NOT NULL, EndMs REAL NOT NULL,
    FOREIGN KEY(RecordingId,ProfileId) REFERENCES Analyses(RecordingId,ProfileId) ON DELETE CASCADE);
CREATE INDEX ScoresByTime ON Scores(RecordingId,ProfileId,StartMs);
CREATE INDEX IntervalsByTime ON Intervals(RecordingId,ProfileId,StartMs);
```

读写时验证有限值、`0 ≤ start < end ≤ duration`、合法枚举、分数范围及分析版本。`Complete` 必须由真实覆盖重算，外部 JSON 中传入 true 不能绕过验证。禁止以最后播放位置作为连续覆盖率。相同 profile 的重播按音乐区间去重，新 revision 事务替换对应范围，不无限追加重复帧；较旧 revision 不覆盖较新数据。
- [ ] **Step 4：实现 timeline 构建。** 只接受时间线与身份均可用的 frame；按实际模型的输出时间约定映射，不以推断完成时刻充当演奏时刻。YAMNet 类窗口输出按窗中心标注并在分析元数据保留半窗时间不确定度；真正逐帧模型按其输出步长映射。状态平滑可以产生估计区间，但不能填补音频覆盖缺口。只把稳定活动的类别加入 roster，已知 roster 本曲内保持固定排序。`Add` 遇到不同 profile 或无有效时间则拒绝持久化。
- [ ] **Step 5：验证事务、掉帧与损坏。** 实测写入中断后旧事务仍完整；重复播放不增加覆盖；seek 缺口不消失；不同模型无法混合；人工篡改 Complete 被拒绝；损坏数据库不导致歌词和播放退出。缓存清理只删除乐器分析，不删除原始文件、模型、手动歌词或用户文件绑定。
- [ ] **Step 6：提交。** `feat: persist recording-scoped instrument timelines`。

### Task 6：完整本地 WAV 的预分析

**Files:** 新增 `src/LumaLyrics.Windows/WaveAudioReader.cs`、`src/LumaLyrics.Infrastructure/InstrumentFileAnalysis.cs`、`tests/LumaLyrics.Windows.Tests/WaveAudioReaderTests.cs`；扩展 `InstrumentEngineTests.cs`、Probe benchmark 命令。

**Interfaces:** `WaveAudioReader.ReadAsync(string path, CancellationToken token): IAsyncEnumerable<PcmChunk>`；`InstrumentFileAnalysis` 提供 `AnalyzeAsync(string path, InstrumentRecordingKey recording, IProgress<double>? progress, CancellationToken token): Task<InstrumentTimeline>`。Infrastructure 不引用 Windows；通过下列构造函数注入音频读取委托，由 App／Probe 传入 `WaveAudioReader.ReadAsync`。

最终构造签名固定为：

```csharp
public InstrumentFileAnalysis(InstrumentEngine engine, InstrumentRepository repository,
    Func<string, CancellationToken, IAsyncEnumerable<PcmChunk>> readAudio);
```

- [ ] **Step 1：先写 WAV 安全解析测试。** 在测试中用 `BinaryWriter` 生成自写的 PCM16 与 float32 WAV；包括 RIFF chunk 奇数字节填充、非 44 字节头、data 截断、越界 chunk 长度、NaN float、零通道及未知编码。合法样本读回的样本数和幅度必须一致，截断文件不能 Complete。

```csharp
// 最小自写 PCM16 WAV 测试夹具内容，不含用户音频。
using var stream = new MemoryStream();
using (var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, true))
{
    writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(40);
    writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
    writer.Write((short)1); writer.Write((short)1); writer.Write(16000);
    writer.Write(32000); writer.Write((short)2); writer.Write((short)16);
    writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(4);
    writer.Write((short)0); writer.Write((short)16384);
}
Assert.Equal(48, stream.Length);
```

- [ ] **Step 2：实现分块读取和稳定内容身份。** 以只读方式打开用户文件，读取前后校验文件身份、长度及修改时间，流式计算 SHA-256；变化则分析失败，不提交新缓存。采样数决定音乐时间，不使用处理耗时决定进度。支持的 WAV 编码及声道限制要在文件选择界面明确显示，其他格式给出可理解的错误。

如果输入 recording 已有 AudioSha256，必须与本次计算值相同；没有时由明确的用户文件绑定建立含哈希的新 key。返回 timeline 使用已确认的实际样本时长和计算哈希，并保存对应绑定，不能将新文件分析写回另一内容哈希的旧 key。
- [ ] **Step 3：执行离线窗口推断。** 复用任务 4 模型预处理及任务 5 的持久化格式；最后不足一窗时可按模型规则 padding，但覆盖只记真实样本范围。短于模型最小有效上下文的音频返回“音频过短，无法可靠识别”，不能因为 padding 生成完整乐器清单。取消保留之前已提交的部分分析，绝不标记 Complete。整曲文件不复制到个人目录。
- [ ] **Step 4：验证预分析与实时一致性。** 同一段自写或许可测试音频，按整块和任意 packet 边界输入，重采样／模型输出应在任务 3 的数值容差内；暂停和恢复离线任务不能影响当前歌曲。先分析后播放，开头可立即取得完整 roster。文件绑定失败或录音不一致时禁止应用该时间线。
- [ ] **Step 5：提交。** `feat: preanalyze local WAV instrument activity`。这一交付即使 G1 不通过也可独立工作，但公开状态必须注明其数据来源。

### Task 7：连接播放生命周期与最新结果（G3）

**Files:** 新增 `src/LumaLyrics.Infrastructure/InstrumentSession.cs`、`src/LumaLyrics.App/PlaybackInstruments.cs`；修改 `PlaybackCoordinator.cs` 的构造、`Receive`、seek 失效路径及 `DisposeAsync`；扩展 `InstrumentEngineTests.cs`。

**Interfaces:** `InstrumentSession(InstrumentEngine engine, Func<InstrumentEpoch?> readCurrent, Action<InstrumentFrame> accept)`；`SubmitAsync(InstrumentWindow window, CancellationToken token): Task`。App partial 提供 `InstrumentViewState Instruments { get; private set; }`、`event Action? InstrumentsChanged`、`SetInstrumentDemand(bool playbackPageVisible, bool backgroundEnabled)`、`RefreshInstrumentSettingsAsync(): Task`。命名只在本功能内部使用。

- [ ] **Step 1：先添加忽略取消仍不能污染新歌的测试。**

```csharp
sealed class DeferredInstrumentClassifier : IInstrumentClassifier
{
    public TaskCompletionSource<IReadOnlyDictionary<InstrumentKind, float>> Pending { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string ProfileId => "fixture";
    public int SampleRate => 1000;
    public int WindowSamples => 1000;
    public int HopSamples => 500;
    public ValueTask<IReadOnlyDictionary<InstrumentKind, float>> ClassifyAsync(float[] mono, CancellationToken token)
        => new(Pending.Task);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
[Fact]
public async Task SlowOldInferenceCannotPublishAfterTrackChange()
{
    var classifier = new DeferredInstrumentClassifier();
    await using var engine = new InstrumentEngine(classifier);
    var old = new InstrumentEpoch("AppleMusic", 1, 1, 0);
    InstrumentEpoch current = old;
    InstrumentFrame? accepted = null;
    var session = new InstrumentSession(engine, () => current, frame => accepted = frame);
    var work = session.SubmitAsync(new(old, 0, 1000, 0, 1000, 1000, new float[1000]), CancellationToken.None);
    current = old with { Revision = 2 };
    classifier.Pending.SetResult(new Dictionary<InstrumentKind, float> { [InstrumentKind.Piano] = .9f });
    await work;
    Assert.Null(accepted);
}
```

- [ ] **Step 2：实现 session 前后核对。**

```csharp
public async Task SubmitAsync(InstrumentWindow window, CancellationToken token)
{
    token.ThrowIfCancellationRequested();
    if (!Equals(readCurrent(), window.Epoch)) return;
    var frame = await engine.AnalyzeAsync(window, token);
    token.ThrowIfCancellationRequested();
    if (!Equals(readCurrent(), frame.Epoch)) return;
    accept(frame);
}
```

其中 `engine/readCurrent/accept` 是构造参数保存的字段。App 的 `accept` 排入 dispatcher 后仍再次检查 `Source.Latest` 和 discontinuity；数据库提交也核对当前 epoch，不能只在发起时检查一次。原始 source 事件到达时立即使旧 epoch 失效，不等待 UI dispatcher 派发 `Receive`。
- [ ] **Step 3：接入状态转移。**

| 事件 | 行为 |
| --- | --- |
| 新歌／新会话 | 立即失效旧代次，取消任务，清除旧窗口和高亮，确认身份后读取兼容缓存 |
| seek／循环回零 | discontinuity +1；重新定位缓存；实时等待一窗新音频；不把越过区间记为覆盖 |
| 暂停／停止 | 停止采集与计时高亮，保留 roster；恢复后新的 discontinuity 和音频窗口 |
| 位置不可用 | 可继续当前会话实时推断；缓存时间线暂停应用，不生成新的可复用时间记录 |
| 缓存完整 | 默认只按播放时钟展示，不持续运行模型；用户另行分析时才重新计算 |
| 缓存部分 | 有覆盖区间用缓存，缺失区间用实时有效结果；二者都没有则 Unknown |
| 页面隐藏／最小化 | 无后台需求时停止采集；有后台需求时只维护缓存，不运行隐藏界面动画 |
| 设置关闭／退出 | 取消所有本功能任务、解绑事件、停止回调、完成安全释放 |
| 模型／设备失败 | 设置状态 Unavailable，不修改歌词状态、不控制 Apple Music 播放 |

总开关与需求使用以下条件；后台选项不能绕过总开关。音频正常但没有足够可信的支持类别时显示“暂未识别出支持的乐器”，不把它改为采集故障或纯音乐判定。

```csharp
bool needsCapture = settings.ShowInstrumentActivity
    && (playbackPageVisible || settings.AnalyzeInstrumentsInBackground)
    && !hasCompleteCompatibleTimeline;
```

`playbackPageVisible` 来自页面、窗口可见及非最小化状态；`hasCompleteCompatibleTimeline` 来自已经核对录音与 profile 的缓存，不能只检查文件存在。

- [ ] **Step 4：完成暂停、seek 与销毁的确定性测试。** 用 `TestTime` 和可控 TaskCompletionSource 依次覆盖 A→B→A、同曲回零、派发前切歌、设备重连、后台关闭、两次 Dispose、进程退出、暂停后零缓冲。对正在运行的推断使用运行时安全终止机制；超时不阻塞 UI，也不强行释放仍被 native 调用使用的内存，记录待完成清理并禁止复用该实例。
- [ ] **Step 5：执行 G3 私有联调。** 检查真实音频与媒体时间的偏差，记录位置质量和延迟；确认乐器功能不会触发 Play、Next、最小化或原生窗口激活，不影响现有队列交接。模糊录音身份只展示临时结果，不声称可自动复用。
- [ ] **Step 6：提交。** `feat: synchronize instrument activity with playback lifecycle`。

### Task 8：加入主播放页图标和 Settings 管理

**Files:** 新增 `InstrumentActivityView.cs`、`InstrumentIcons.cs`、`InstrumentSettingsPanel.cs`、`InstrumentVerification.cs`；修改 `MainWindow.xaml`、`MainWindow.xaml.cs`、`PlaybackLayout.cs`、`SettingsWindow.cs`、`SettingsStore.cs`、`UiThemeVerification.cs`。测试设置默认值及状态呈现。

**Interfaces:** `InstrumentActivityView.Apply(InstrumentViewState state)`；`InstrumentActivityView.Dispose()`；`InstrumentSettingsPanel(PlaybackCoordinator coordinator)`；`InstrumentSettingsPanel.Dispose()`；主窗口 `VerifyInstrumentsAsync(string directory, Dictionary<string,object?> checks): Task`。设置字段如下：

```csharp
public bool ShowInstrumentActivity { get; set; }
public bool AnalyzeInstrumentsInBackground { get; set; }
public string? InstrumentModelProfileId { get; set; }
```

- [ ] **Step 1：添加旧配置与新配置默认关闭的测试。**

```csharp
[Fact]
public void OldSettingsDoNotStartAudioAnalysis()
{
    var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{\"UiTheme\":\"liquid\"}")!;
    Assert.False(settings.ShowInstrumentActivity);
    Assert.False(settings.AnalyzeInstrumentsInBackground);
}
```

- [ ] **Step 2：实现稳定布局与图标。** 在歌曲信息／封面区域下方放置独立 host，关闭时完全折叠。宽布局使用一行，紧凑布局在 host 自身范围内换行或水平滚动；不侵占队列 viewport、不改变封面正方形。图标用自绘 WPF Geometry，固定类别顺序，不用不同平台外观不一致的 emoji。图标资源只加入主窗口应用主题范围。

```csharp
// 单个键盘图标的自绘几何示例；其他类别分别提供同一 24×24 viewbox 的轮廓。
var keyboard = Geometry.Parse("M2,5 L22,5 L22,19 L2,19 Z M7,5 L7,19 M12,5 L12,19 M17,5 L17,19 M5,5 L5,12 M10,5 L10,12 M15,5 L15,12 M20,5 L20,12");
keyboard.Freeze();
```

其余图标分别采用鼓面与鼓槌、贝斯琴身、吉他琴身、弦乐琴身与琴弓、铜管喇叭口、木管管身与键孔、合成器键盘与旋钮；均自绘，不引入未知许可资产。标签文本保证即使图形不易辨认也能理解类别。
- [ ] **Step 3：实现主题、动画和可访问性。** 使用 `ThemeManager` 已有动态画刷和 `ThemeDesign` 尺寸；Liquid/Frosted 随封面调色，Material 固定色板，其余主题按已有资源处理。Unknown 用描边和“当前未识别”，Dim 用弱化实体图形，Active 用清晰实体与强调色。每项 AutomationProperties.Name 包含类别和状态；非交互图标不伪装按钮。渐变从当前值开始，中断不跳变，完成后清理动画时钟；Unload/Dispose 取消订阅。
- [ ] **Step 4：实现 Settings 控件。** 一组功能开关、后台分析开关、模型下载／取消与状态、本地 WAV 选择和录音绑定、分析进度／取消、仅清理乐器缓存。复用 Settings 的嵌入方式，不新增导航或模态管理窗口；事件只在面板 Loaded 期间订阅。功能默认关；缺模型时先显示安装入口，安装失败不偷偷启动其他模型。用户关闭面板后后台工作只按独立后台选项保留。
- [ ] **Step 5：增加隔离验证入口。** `--verify-ui <输出目录> --verify-instruments --data-dir <隔离目录> --exit-after-capture` 使用构造的 `InstrumentViewState`，不启动媒体、联网、模型下载或音频设备。验证 Disabled、Learning、Ready、Paused、Unavailable，完整／部分缓存及全部未知；模拟 rapid A/B 状态更新、字体放大、三档 DPI、队列开关、全部主题和减少动画。图像与坐标检查均限制在控件 viewport 内。
- [ ] **Step 6：验证纯音乐与回归。** 已确认纯音乐仍保持歌词区域空白、浮动文字和空背景隐藏，但主页面乐器条可以显示。歌声返回时歌词恢复；乐器识别结果不得更改 `LyricsDocument.Instrumental`。回归浮动外观、翻译开关、播放快捷键、队列宽度及原生播放交接。
- [ ] **Step 7：提交。** `feat: display themed instrument activity in playback settings`。

### Task 9：个人备份、恢复与软件分发边界

**Files:** 新增 `InstrumentBackup.cs`、`InstrumentBackupTests.cs`；修改 `PersonalLibrarySnapshot.cs`、`PersonalLibraryArchive.cs`、`PersonalLibraryManifest.cs`（仅确有新增字段时）、`.gitignore`、`scripts/verify-distribution.ps1`、`scripts/test-distribution.ps1`、相关个人资料文档。

**Interfaces:** `InstrumentBackup.Export(string dataRoot, string stagingRoot, PersonalLibraryManifest manifest, CancellationToken token)`；`InstrumentBackup.Validate(string backupRoot, PersonalLibraryManifest manifest)`；恢复仍由 `PersonalLibraryArchive.Restore` 统一执行。

- [ ] **Step 1：写并运行备份失败测试。** 在已有 `PersonalLibraryTests` 自写资料夹具中写入一条不完整乐器分析；导出时 live DB 保持 WAL 写入，断言备份数据库与 readable JSON 的 revision、覆盖、分数完全一致。先在无导出实现时确认失败，再实现。

```csharp
// 接入现有 Seed()/PathOf() 夹具，不创建第二套用户资料。
using var source = Seed();
using (var instruments = new InstrumentRepository(PathOf("source")))
{
    var key = new InstrumentRecordingKey("fixture", "recording-1", 30000, null);
    instruments.Save(new(key, "fixture-profile", new[] { new InstrumentSpan(0, 10000) },
        Array.Empty<InstrumentScorePoint>(), Array.Empty<InstrumentInterval>(),
        new[] { InstrumentKind.Piano }, false, 1));
}
var manifest = PersonalLibraryArchive.Export(PathOf("source"), PathOf("collection"));
Assert.Contains(manifest.Files, file => file.Path == "state/instruments.db");
Assert.Contains(manifest.Files, file => file.Kind == "instrument");
PersonalLibraryArchive.Restore(PathOf("collection"), PathOf("restored"));
using var restored = new InstrumentRepository(PathOf("restored"));
Assert.False(restored.Load(new("fixture", "recording-1", 30000, null), "fixture-profile")!.Complete);
```

- [ ] **Step 2：实现一致快照和 readable JSON。** 先用 SQLite backup API 得到 `state/instruments.db`；所有 `instruments/<recording-key>/<profile-hash>.json` 从该快照生成，不再读取 live DB。profile-hash 是 ProfileId UTF-8 文本的 SHA-256，JSON 内保留实际 ProfileId，测试用 fixture 名称也按相同规则处理。manifest 注册 `instrument` kind 和 state 文件白名单；校验 JSON 内容与 DB 的录音、profile、revision、覆盖及哈希一致。文件名使用经过验证的十六进制哈希，拒绝路径穿越和重定向链接。
- [ ] **Step 3：定义兼容与恢复。** 新版本继续接受没有乐器文件的旧备份。带新 kind 的备份要求支持该功能的版本，并在备份 README 明示最低恢复版本；不静默忽略未知 kind。恢复只进入新目录，不覆盖原资料；缺少模型不妨碍恢复已有时间线，显示重新安装模型入口。模型权重和原始音乐文件不打入个人备份，文件绑定保留来源说明；原路径失效后要求重新选择对应内容，不扫描或猜测替代文件。
- [ ] **Step 4：扩充分发拒绝规则并先验证失败。** 在现有分发测试中加入以下自写私有样本；既有 `.onnx` 和 models 目录拦截继续保留。

```powershell
$cases += @('instruments.db', 'instrument-analysis.json', 'instrument-audio-probe.json',
    'instrument-evaluation.json', 'instrument-file-bindings.json',
    'instruments/recording/profile.json', 'models/instruments/model.onnx',
    'instrument-capture.wav', 'instrument-capture.pcm')
```

新增 instruments 私有目录、`instrument-*` 私有报告名和音频后缀检查；软件允许公开说明 Markdown 和经审核的代码内模型清单，不能为模型下载功能放开整个 `.onnx`／models 目录。`.gitignore` 加入对应个人报告、音频、模型及 analysis 目录模式，避免把 docs 的 Markdown 计划一起忽略。
- [ ] **Step 5：运行备份与分发测试并提交。** `Invoke-Dotnet test tests/LumaLyrics.Infrastructure.Tests -c Release --filter 'FullyQualifiedName~PersonalLibrary|FullyQualifiedName~InstrumentBackup'`；`powershell -NoProfile -File scripts/test-distribution.ps1`。提交 `feat: back up instrument timelines and protect distribution boundaries`。

### Task 10：完成验收、说明与 GitHub 同步（G4）

**Files:** 新增 `scripts/verify-instruments.ps1`、`docs/instrument-activity.md`；更新 `docs/instrument-evaluation.md`、`README.md`、`docs/manual-test-checklist.md`、`docs/release-notes.md`；需要随包附带说明时更新 `scripts/publish-ui.ps1` 复制文档列表。

**Interfaces:** 新脚本参数 `-DataDirectory`、`-OutputDirectory`、`-Native`。默认只执行隔离确定性测试；`-Native` 才启用音频探针，输出目录必须在软件目录外。两种结果分别命名 `ui-check.json` 和 `instrument-native-check.json`，不相互替代。

- [ ] **Step 1：运行完整构建与测试。**

```powershell
powershell -NoProfile -File scripts/build.ps1
powershell -NoProfile -File scripts/test.ps1
powershell -NoProfile -File scripts/test-distribution.ps1
```

新增依赖后先有意更新 lock 文件，随后以上构建的 locked restore 必须通过。禁止为掩盖失败删除现有测试或跳过锁文件。
- [ ] **Step 2：运行隔离 UI 验证。**

```powershell
. .\scripts\common.ps1
$instrumentUiRoot = Join-Path $env:LOCALAPPDATA ('LumaLyrics-InstrumentChecks\ui-' + [guid]::NewGuid().ToString('N'))
$instrumentUiProfile = Join-Path $instrumentUiRoot 'profile'
$instrumentUiOutput = Join-Path $instrumentUiRoot 'screens'
Invoke-Dotnet run --project src/LumaLyrics.App -c Release --no-build -- --data-dir $instrumentUiProfile --verify-ui $instrumentUiOutput --verify-instruments --exit-after-capture
```

核对 JSON 中每项布尔断言并检查实际截图。若 apphost 出现已知启动停滞，用已打包目录的 `runtime/dotnet.exe LumaLyrics.dll` 搭配同样的隔离参数启动。不得重复运行一个仍未退出的验证进程，也不触碰用户正常资料目录。
- [ ] **Step 3：执行真实播放矩阵。**

| 用例 | 期待与证据 |
| --- | --- |
| 首次播放，后半首新乐器进入 | 清单逐步增加；开头不显示尚未识别到的类别 |
| 已完整分析的准确录音 | 开头有整曲 roster；活动跟随位置；报告同步误差 |
| 只听一半后重播 | 保持部分标记；另一半为未知，不变 Complete |
| 同名现场版／混音版 | 不复用其他录音的时间线 |
| 连续切歌、seek、循环、暂停恢复 | 没有跨曲高亮、过期写入、异常时间跳动 |
| Apple Music 最小化、输出设备切换 | 支持组合正常；不支持时清晰降级，不激活原生窗口 |
| 无有效音频、目标进程退出 | 不假亮；显示不可用；可恢复；正常释放 |
| 页面隐藏、后台开关、托盘退出 | 按需求停止或继续分析，隐藏页面不保留动画 |
| 密集混音、弱音、电子与合成音色 | 分类别误报／漏报统计，模糊样本单列 |
| 30 分钟连续播放 | 有界延迟、稳定内存，无无限队列和 COM／句柄增长 |

原生窗口状态、媒体快照与音频指标均存在私人输出目录；公开报告只写汇总。逐类验收使用 spec 的门槛，不把“模型成功返回”当成音乐识别正确。
- [ ] **Step 4：补齐使用说明与发布说明。** 明确支持类别、首次播放限制、实时／缓存／文件来源、目标系统能力、资源消耗、默认关闭、模型下载大小及许可、后台选项、清理和备份行为。数值必须来自实测报告，不复制计划目标充当测试结果。未支持的格式和设备组合写明。
- [ ] **Step 5：检查分发输出后再生成软件 ZIP。** 沿用现有 publish 脚本先验证后归档的流程；模型和个人数据零混入。仅说明文档变化不升级版本；实际功能发布时依仓库版本流程更新，不在计划阶段发布软件。
- [ ] **Step 6：完成提交和同步。** 实现任务按其范围显式 `git add`，避免 `git add .` 带入个人文件。最后一笔可用 `docs: document verified instrument activity support`。

```powershell
git status --short
git diff --check
git fetch origin
git log --oneline --left-right HEAD...origin/main
```

若远端有新提交，先整合并重跑受影响检查，再推送；不得 force-push。正常直推使用 `git push origin HEAD:main`；如果保护规则拒绝，保留本地提交，推送功能分支并创建 PR，随后用应用工具 attach_artifact 附加 PR。

```powershell
git rev-parse HEAD
git ls-remote origin refs/heads/main
git fetch origin
git diff --exit-code HEAD origin/main -- src tests scripts docs
```

PR 路径改为验证远端功能分支，并报告 PR 尚未合并。最终报告应包含真实 commit／PR URL、G1–G4 结果、实测支持类别、平台限制和未验证项目。

## 验收追踪与计划自审

| 需求 | 实现任务 | 必须存在的证据 |
| --- | --- | --- |
| 开头显示本曲暗色乐器 | 5、6、7、8 | 完整缓存开头截图；首次无缓存的不同文案 |
| 多乐器同时亮起、避免闪烁 | 1、3、4、8 | 活动状态测试、逐类指标、真实音频演示 |
| 身份、时钟、seek、取消安全 | 1、5、7 | 同名不同录音、忽略取消、dispatcher 延迟测试 |
| 未知与不完整状态真实 | 1、5、6、8 | 掉帧、跳播、截断文件、Unknown 控件检查 |
| Windows 与 Apple Music 可用性 | 2、7、10 | 私人原生探针及播放矩阵；UI 检查不代替 |
| 全部主题与浮动隔离 | 8、10 | 主题矩阵、队列宽度、纯音乐和浮动回归 |
| 生命周期和资源 | 2、4、7、8、10 | 停止释放、后台开关、30 分钟稳定性 |
| 模型许可、下载及数值一致 | 3、4 | 固定版本和哈希、notice、原始模型数值对照 |
| 个人数据备份和分发隔离 | 5、9、10 | 一致快照、恢复到新目录、分发拒绝测试 |
| GitHub 同步 | 每个任务、10 | 测试过的提交、远端 SHA／树或 PR |

执行者完成每个任务后更新相应复选框和公开汇总；不要把本计划的目标当成现有事实。G1/G2 的失败可以构成技术验证的有效结论，但不能把整个用户功能标成完成。

后续扩展不属于第一版：浮动乐器条、逐音符闪烁、任意民族乐器全集、自建训练集、云端音频分析、通用任意源分离，以及未验证的其他音频格式。若基线效果达到目标，再按用户需求另开设计。
