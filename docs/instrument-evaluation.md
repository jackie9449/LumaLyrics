# 乐器活动显示：执行与验证记录

当前处于计划的 G2 模型校准准备阶段，主播放页尚未加入乐器识别控件。任务 1 完成，任务 2 的实际采集和任务 3–4 的模型／窗口基础已实现；任务 5–10 尚未实施。正式准确度、乐器 UI 和完整播放矩阵尚未验收。

## 已有证据

- Core 已有独立的活动迟滞、覆盖区间、代次与样本窗口测试；覆盖缺口不能因重播、重叠或 seek 被填平。
- Windows 进程回环已实现在 Windows build 26100 上实际取得 Apple Music 音乐信号。一次 10 秒检查取得 995 个缓冲、992 个有效信号块，无原始音频落盘。
- 实际音频渲染者是同包的独立 `AMPLibraryAgent`，不是 AppleMusic UI 进程的子进程。现在先核对媒体会话 AUMID，再查同包活动音频会话；不按进程名猜测渲染者，也不按音量挑选多个候选。
- 未找到唯一同包活动渲染进程时拒绝采集，不降级到系统混音或麦克风。所有实际进程、端点和逐曲信息仅保留在个人诊断目录。
- 静音回环可能存在 PCM16 的低幅抖动；仅有缓冲、非零样本或成功 HRESULT 均不算音乐验证通过。当前探针同时检查 RMS ≥ 0.0005、peak ≥ 0.002；这只是信号强度检查，不是乐器准确度检查。
- 探针的 Microsoft dotnet host 入口已验证可用；独立脚本具有父进程截止时间，避免已知 apphost 启动停滞拖住验证。
- 一次原生 `Stop` 曾长时间等待，因此采集现在运行在受控的独立辅助进程，以匿名管道传递有界缓冲。取消先请求正常释放，超过期限只终止本次拥有的辅助进程树，不终止 Apple Music。辅助进程版本的一轮检查取得 1010 个缓冲、1007 个有效块，取消到释放约 22 ms，未触发强制停止。后续无媒体会话时复检明确返回 `NoAppleMusicSession`，未将此前成功当作本轮成功。
- YAMNet 原始权重 SHA-256 为 `13c3308955bbfaef262f175ac9c40e47b134573a93984f009220dd7cc12a1744`，源码固定于 `c14bf9ad91962cf189f9f58db2132c06247fcd53`。
- Python 原始 FFT 图与 ONNX 对照已在 20 个自生成数值窗口和 30 个真实录音短窗口上通过，最大绝对分数差约 `8.96e-5`。复跑单窗推断 P95 约 1.80 ms。
- C# `Microsoft.ML.OnnxRuntime 1.31.0` CPU 对照同一批 20 个合成输入通过，最大绝对差约 `9.02e-5`，单窗 P95 约 1.33 ms。这些数字不包含采集、重采样、窗口积累、迟滞和动画，不代表实际图标响应延迟。
- TFLite DFT 变体的首轮误差约 `1.57e-4`，未达到 `1e-4` 门槛，因此没有采用它，也没有放宽数值标准。
- 已从用户指定的 WAV 目录建立 30 个 20 秒待复核片段条目。整张 CD 文件只作为音频来源，没有自动认定为独立录音；未复制原文件。私有清单全部保持 `reviewed=false`，实际运行评估返回 `RequiresAnnotation`、`accuracyValidated=false`。
- 476 项 .NET 测试、8 项 Python 评估规则测试通过；Release App／Probe 构建零警告。现有播放滑条专项的六套主题隔离 WPF 回归 124/124 通过，检查了 Liquid、Material 的实际渲染；这不是新增乐器 UI 验收。分发隔离测试拒绝全部 53 种个人文件样例。

## 尚未通过的检查

- 各乐器类别的人工时间标注、独立校准集和留出集准确度；不公布支持类别或“识别所有乐器”。
- 完整流式重采样／调度、资源与实际进入／退出延迟；评估工具尚未实现人工边界误差汇总。
- 其他应用声音隔离的实际对照、最小化／暂停恢复／输出设备切换、不同音质及保护状态，以及 30 分钟稳定性矩阵。
- 精确录音时间线缓存、本地完整文件分析、主页面 UI、个人备份与最终分发集成。
- PANNs 候选尚未实现；传入 `--model panns` 会明确记录 `CandidateNotImplemented`。不能据此声称已经完成模型比较或锁定生产类别。

## 运行方式

使用独立的个人检查目录：

```powershell
powershell -NoProfile -File scripts/verify-instruments.ps1 -DataDirectory "$env:LOCALAPPDATA/LumaLyrics-InstrumentChecks"
powershell -NoProfile -File scripts/verify-instruments.ps1 -DataDirectory "$env:LOCALAPPDATA/LumaLyrics-InstrumentChecks" -Native -Seconds 30
```

默认只跑确定性测试。`-Native` 仅采集经确认的当前 Apple Music 渲染进程；请在正在播放时运行。没有有效信号则检查失败并保留私人诊断，而不是报告乐器为空或归因为 DRM。

模型数值复现使用独立 Python 3.12 环境；直接依赖见 `requirements-yamnet.txt`，本机实际安装的完整精确版本见 `requirements-yamnet-lock.txt`。从项目根目录运行以下命令。该工具只在指定的软件目录外下载固定哈希资源、生成实验 ONNX 和私有评估报告；30 个真实窗口的原始 PCM 只在内存中使用，保留的数值输入夹具全部为自生成信号。

```powershell
python -m pip install -r tools/instruments/requirements-yamnet-lock.txt
python tools/instruments/prepare_yamnet.py --reference-dir "$env:LOCALAPPDATA/LumaLyrics-InstrumentChecks/yamnet-reference" --output-dir "$env:LOCALAPPDATA/LumaLyrics-InstrumentChecks/yamnet-fft-export"
. ./scripts/common.ps1
Invoke-Dotnet build tools/LumaLyrics.Probe -c Release
Invoke-Dotnet tools/LumaLyrics.Probe/bin/Release/net10.0-windows10.0.19041.0/LumaLyrics.Probe.dll --verify-instrument-model "$env:LOCALAPPDATA/LumaLyrics-InstrumentChecks/yamnet-fft-export"
```

`--music-root` 可指向用户明确指定的 WAV 目录；只分块读取每个文件的短窗口，不修改、复制或散列整份音乐库。该参数生成的目录清单与分数仍为私人资料。模型准确度不会因数值对照通过而自动标为已验证。

## 私有标注和评价

```powershell
python tools/instruments/evaluate.py --prepare --music-root $authorizedMusicRoot --dataset "$env:LOCALAPPDATA/LumaLyrics-InstrumentChecks/instrument-review-dataset.json"
python tools/instruments/evaluate.py --dataset "$env:LOCALAPPDATA/LumaLyrics-InstrumentChecks/instrument-review-dataset.json" --model yamnet --model-dir "$env:LOCALAPPDATA/LumaLyrics-InstrumentChecks/yamnet-fft-export" --output "$env:LOCALAPPDATA/LumaLyrics-InstrumentChecks/instrument-evaluation-01.json"
python -m unittest discover -s tools/instruments -p 'test_*.py' -v
```

`$authorizedMusicRoot` 必须是用户明确授权读取的音乐目录。准备命令拒绝覆盖已有清单。每个条目的 `startMs` 是原文件起点；`events` 和 `uncertain` 的半开区间均相对于该 20 秒片段。逐个试听并核对全部八类后，填写 `reviewedKinds`、独立录音身份 `verifiedRecordingId`、用途／来源 `rightsNotes`，再设置 `reviewed=true`。多个片段或版本属于同一录音时必须使用同一身份和 split；同一源文件也不能跨校准／留出集。全局不确定区间与某个 500 ms 格有任意交集时，整格从所有类别的分母排除。

当前候选中 `Piano` 实际为键盘族，包含钢琴、管风琴和羽管键琴，不能把它解释为精确钢琴识别；其他族同样按原始标签映射。模型独立输出多类别，分数取族内标签最大值，不使用 argmax 排除同时演奏的乐器。阈值、attack 和 release 只从校准集选择；留出集只用于报告，应在下一轮调参前重新划分。无正例时 recall 为 null，不能报 100%。图表级音乐时间按窗口中心对齐，同时保留窗口结束的可用时间；进入／退出及端到端延迟仍待实测。

评价报告使用独占创建，避免覆盖之前的留出结果。少于 30 个确认独立的录音，或缺少密集混音、钢琴、弦乐、电子、轻伴奏和负例场景时，只报告 `InsufficientCorpus`。即便某个类别达到 precision ≥ 0.85、recall ≥ 0.65，也不自动发布生产模型；完整 G2、生命周期、缓存和 UI 验收仍须继续。当前没有人工已审标签，正式识别 UI 按原计划的 G2 条件暂不推进。
