# 乐器活动显示：执行与验证记录

最新状态：已加入默认关闭、仅启用古典室内乐弦乐组的实验图标与设置入口，完成公开标注评价和有界实时调度。通用 G2 与完整实机矩阵仍未通过。请先阅读[本轮 URMP、模型比较与 UI 结果](instrument-evaluation-urmp.md)及[使用说明](instrument-activity.md)。下文保留前两轮基础证据和开发命令，不将早期验证替代本轮结果。

## 前两轮基础证据（截至 9160557）

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
- 已实现录音／模型版本隔离、WAL 事务、分数替换、真实覆盖合并和修订保护。篡改 Complete、越过分析缺口的活动区间、无效分数均被拒绝；模拟插入中断保留旧事务。缓存不提供按歌名查询，当前仍无可自动复用的 Apple Music 精确录音身份连接。
- C# WAV 实验入口已在用户授权目录中的一个约 43.4 分钟文件上完成全文件分析：5426 个窗口、完整实际覆盖、约 14.3 秒处理时间、约 2.63 MiB 数据库。只读检查前后长度与修改时间相同，内容哈希在分析前后均核对；没有复制音频。整个 CD WAV 按一个文件处理，尚未按 CUE 分曲，不能自动应用到当前 Apple Music 单曲。
- 重采样使用有界、保持相位的 Blackman sinc 滤波，版本 `mono-blackman-sinc-24-r094-v1`，在引擎分析 ProfileId 中同时固定声道平均、采样／窗口配置和实际活动参数。分块边界不改变转换结果，降采样混叠测试通过；尾部 padding 不延长实际覆盖，短于一个有效模型窗口时拒绝分析。
- 个人备份包含 `state/instruments.db` 一致快照和从同一快照生成的可阅读 JSON。恢复只到新目录；校验 JSON 的录音、profile、修订、覆盖和绑定与 DB 一致，修改 JSON 及其清单哈希仍不能冒充快照。旧备份继续兼容，源音乐和模型权重不打入备份。
- 当前 500 项 .NET 测试、8 项 Python 评估规则测试通过；locked restore／Release App／Probe 构建零警告。本次既有应用／Settings／导入界面的六主题隔离检查 253/253 通过，并检查 Liquid、Material 页面实际渲染；此前播放滑条专项为 124/124。这不是新增乐器 UI 验收。分发隔离测试拒绝全部 53 种个人文件样例。

## 前两轮遗留检查与本轮进度

- 通用场景的独立人工声学复核与逐类准确度仍不足；本轮发布者标注评价只支持室内乐弦乐实验，不宣称“识别所有乐器”。
- 有界窗口、积压、取消与短时回放已经实现；长时资源和真实进入／退出延迟仍待验证。名义音符边界汇总已实现，保留未匹配数量。
- 其他应用声音隔离的实际对照、最小化／暂停恢复／输出设备切换、不同音质及保护状态，以及 30 分钟稳定性矩阵。
- 当前媒体会话到持久精确录音身份的自动连接仍未完成；本轮已加入主页面、Settings 与仅限本次会话的明确文件绑定。
- PANNs 已实现下载、导出及独立标签映射，但数值一致性失败，没有进行虚假的准确率比较或锁定生产类别。

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

评价报告使用独占创建，避免覆盖之前的留出结果。少于 30 个确认独立录音，或缺少密集混音、钢琴、弦乐、电子、轻伴奏和负例场景时，只报告 `InsufficientCorpus`。即便某类达到 precision ≥ 0.85、recall ≥ 0.65，也不自动发布生产模型。用户要求继续实现后，已加入明确限制类别与场景、默认关闭的实验 UI；正式通用 G2 与完整实机验收仍保持未通过。

公开资料核对：[OpenMIC 官方仓库](https://github.com/cosmir/openmic-2018)提供片段级聚合乐器标签，不能直接充当进入／退出时间真值。[MedleyDB 官方仓库](https://github.com/marl/medleydb)提供活动标注与工具，但此次尚未取得对应音频并核对其使用条款；代码仓库的许可不能自动代表音频许可。因此两者目前都没有计入本功能的准确度通过证据。

## 本地 WAV 实验入口

使用上方已经通过数值对照的本地模型导出，以及一个已存在的软件目录外个人目录：

```powershell
. ./scripts/common.ps1
Invoke-Dotnet build tools/LumaLyrics.Probe -c Release
Invoke-Dotnet tools/LumaLyrics.Probe/bin/Release/net10.0-windows10.0.19041.0/LumaLyrics.Probe.dll --analyze-instrument-file --experimental --file $authorizedWaveFile --model-dir "$env:LOCALAPPDATA/LumaLyrics-InstrumentChecks/yamnet-fft-export" --data-dir $instrumentPrivateDirectory
```

支持 RIFF PCM16／PCM24、IEEE float32（含对应 extensible 子格式）、1–8 声道、8–192 kHz；少数不常见采样率比例超过相位表上限时明确拒绝。RF64、压缩 WAV 和重复 data 区块尚不支持。原文件以只读共享方式保持打开，分析前后校验内容；结果写入指定个人目录的 `instruments.db` 和唯一的 `instrument-file-analysis-*.json`。取消或失败时保留先前已提交的分析，不保存当前未完成的新事务，也不提供断点续算。

`--experimental` 是明确的未校准实验入口，固定默认阈值没有得到人工验证，报告始终保留 `accuracyValidated=false`、`productionReady=false`。这里只建立用户选中文件的内容身份，不以文件名、歌名或相近时长确认它就是当前媒体会话的录音。
