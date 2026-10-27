# 摇滚／金属乐具体乐器识别 Implementation Plan

> **For agentic workers:** Execute the tasks inline in this session; do not delegate. Steps use checkbox syntax for tracking.

**Goal:** 独立检测混音中的吉他、贝斯与鼓，并用用户指定的 Black Sabbath《Paranoid》检查实际输出；不根据曲名或流派预置活动状态。

**Architecture:** 保留现有采集、连续重采样、有界队列与代次拦截。EfficientAT MN10 的 2 秒候选因静音／负样本误报被拒绝；改用固定的 MTG Discogs Effnet + Jamendo instrument ONNX，自行实现前端，与原始 TensorFlow 模型及独立 FFT 参考对照。新增可切换的摇滚实验 profile 与诊断入口，支持集与未通过的准确度检查明确展示。

**Tech Stack:** .NET 10／WPF／ONNX Runtime，Python 3.12、TensorFlow 2.16.1、ONNX 1.16.2。最终 MTG 资产及组合哈希固定于 prepare_mtg.py 与 InstrumentModelCatalog；另保留被拒绝候选的可复现工具，不加入应用清单。

**Spec:** 用户 2026-10-27 最新需求：具体吉他／贝斯／鼓，适配摇滚与金属，用 Black Sabbath 的 Paranoid 试播；继承 instrument-activity-design 中的身份、取消、个人数据与分发约束。

## Global Constraints

- 不能用歌曲类别、固定名单或分离模型的 other 通道冒充吉他检测。
- 原始权重、参考代码、测试音频与逐窗结果位于个人目录，不进入 Git 或软件包。
- 原始模型与 ONNX 对照最大误差目标仍为 1e-4，不静默放宽；失败候选不安装。
- 实际媒体录音先核对曲名、艺人、专辑与新鲜快照；没有音频信号与没有进程分别报告。
- 检测结果是模型判断；单曲试听、片段级标签与精确进入／退出验收分别记录。
- 默认关闭、主题资源、浮动歌词隔离与现有队列交接行为保持原约束。

### Task 1：固定摇滚候选和数值验证

**Files:** Create `tools/instruments/prepare_efficientat.py`、`tools/instruments/requirements-efficientat.txt`；更新 `tools/LumaLyrics.Probe/InstrumentBenchmarkCommands.cs`。

**Interfaces:** 导出仍为一个 float waveform 向量和一个 sigmoid scores 向量；`instrument-model-export.json` 保留 sampleRate/windowSamples/hopSamples/outputClassCount、源版本、原权重哈希、标签映射与数值对照。

- [x] 核对、固定候选许可与原始资产哈希，保留私有来源；最终 MTG 模型许可为 CC BY-NC-SA 4.0，不分发权重。
- [x] 拒绝 EfficientAT 的 2 秒语义结果，并加入静音拒绝门槛；最终输入为 16 kHz、2064 ms、128 个完整内部帧，组合固定的发布者 ONNX，避免重复转换命名影响哈希。
- [x] 20 个自生成输入、56 个实际试听窗口的原始 TF／独立 FFT 与 Python ONNX 对照、C# 20 窗数值门槛通过；仅自生成 PCM 保存为 parity 向量。重复生成最终文件哈希相同。
- [x] 完成 44 首 URMP 负样本和 Paranoid 官方试听的输出观察；保留少量鼓误报、未校准阈值和进入／退出准确度未通过状态，详见 instrument-rock.md。

### Task 2：三类 profile 和应用入口

**Files:** Modify `src/LumaLyrics.Infrastructure/InstrumentModelCatalog.cs`、`SettingsStore.cs`、`src/LumaLyrics.App/InstrumentController.cs`、`InstrumentSettingsPanel.cs`、`InstrumentActivityView.cs`；Test `tests/LumaLyrics.Infrastructure.Tests/InstrumentModelTests.cs`。

**Interfaces:** `InstrumentModelCatalog.Get(string? id)` 返回固定清单的 profile 与安装参数；Settings 保存当前 profile ID。Controller 使用配置的标签集合创建 presentation，切换模型立即取消旧 owner，文案使用当前模型范围。

- [x] 为旧配置保留原 profile 和关闭默认值；新摇滚选项独立包含 Guitar、Bass、Drums，键盘等不混入。
- [x] 在设置提供模型选择和固定模型准备，名称使用产品文案；model import 仍核对尺寸与 SHA-256，缺文件明确降级。
- [x] 参数、模型哈希、预处理和标签索引都进入 profile 身份，旧弦乐缓存不被摇滚模型读取。
- [x] 覆盖三个类别独立活动、同时亮、暗色／未知／暂停，以及切模型后旧回调拒绝；六主题模型选择、边界与原有歌词／浮动外观隔离通过。

### Task 3：Paranoid 验证、分发与同步

**Files:** Create `tools/LumaLyrics.Probe/InstrumentMusicProbe.cs`、`docs/instrument-rock.md`；更新 `Program.cs`、`docs/release-notes.md` 与本计划。

**Interfaces:** `--probe-instrument-music --data-dir <private> --model-dir <verified> --seconds <bounded>` 使用当前经核对的 Apple Music renderer，保存数值结果与录音确认到个人目录，原始 PCM 不落盘。

- [x] 本机 native 查询未得到唯一精确 Paranoid 匹配，在 Play 前拒绝；改用 Apple 官方约 30 秒试听。实际流检查独立记录真实曲目到个人目录，不把专辑名当作歌曲名。首轮未完成完整 Paranoid 实机测试；用户随后开启指定录音循环后的补充结果见下文。
- [x] 对实际流统计分数、有效音频、丢窗、可用延迟与活动状态；不保存原始音频，未知边界不伪造真值。另分别报告 AppleMusic／AMPLibraryAgent 进程和 GSMTC 媒体会话，修复立即取消 poll 的释放异常。
- [x] 513 项 .NET、12 项 Python、274 项乐器 UI、259 项应用设计、124 项滑条检查通过；Release locked restore 零警告，57 个分发私有样例被拒绝。便携包使用 Microsoft host 再验 274 项通过，857 个 ZIP 文件内容逐项匹配。

交付流程：提交前重新检查远端，常规 push 后核对远端 SHA 与源树；最终提交链接由任务交付报告记录。独立预览为 `rock-instruments`，ZIP SHA-256 `4f101182a5508a482e6813145e97f46927ebca1f45a1b42a68ff047acf346d43`。首轮交付时完整指定录音尚未试播，随后流验证见补充；广泛摇滚／金属准确度与进入／退出仍明确未验收，不据此将质量门槛标记为通过。

### 补充：用户已开启单曲循环后的实机验证

用户已将指定录音设为后台单曲循环，执行本计划的实际流复核。仅扩展 `InstrumentMusicProbe.cs` 的诊断能力：最长允许 300 秒，私有报告记录新鲜媒体时间轴样本（单调时钟、原生位置、状态），每帧保留 epoch，以核对重复播放边界；不改播放器状态、不保存音频。运行固定模型的 210 秒检查，比较原生位置回绕、逐窗分数／状态和丢窗／延迟，并读取实际窗口状态。运行构建与相关检查后，把汇总写入说明及 release notes，所有曲目快照和逐窗结果保留在个人目录；同步经验证的诊断更新。循环完成不等于乐器准确度或逐音符边界通过。

执行结果：210 秒、415 帧、零积压丢窗，631 个 Playing 快照及两次位置回绕；原生位置在两次回绕时各有一个短暂无效样本，前后位置可核对，不能忽略后宣称时间轴始终可读。22 次窗口样本均后台、可见、未最小化。完整采样覆盖的循环边界保持约 500 ms 结果间隔。相关 31 项 Core、41 项 Infrastructure、27 项 Windows 测试通过；探针构建零警告。该诊断更新不改变应用 UI 或之前的便携包。所有原始报告在个人目录，公开汇总见 instrument-rock.md。
