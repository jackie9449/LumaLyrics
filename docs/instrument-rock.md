# 吉他、贝斯与鼓：摇滚实验

> 本文保留旧 MTG 三类模型的历史验证与复现。新的 `song-instruments` 预览已改用[按歌曲生成清单的通用实验模型](instrument-roster.md)，产品选择中不再提供固定三类，旧设置自动迁移。以下设置步骤仅适用于旧 `rock-instruments` 预览；CLI 仍可指定旧模型 ID 复现。

在 **设置 → 乐器识别 → 乐器识别模型** 选择「摇滚／金属：吉他、贝斯、鼓（实验）」，点「准备本机已下载模型」，再开启显示。当前电脑已经生成并安装固定模型；开关仍默认关闭。古典室内乐弦乐组是独立选项，切换立即取消旧分析并清除本次录音绑定，不混用缓存。

需要实际启动 `rock-instruments` 预览。先从托盘菜单选择「退出」关闭旧 `instrument-activity` 版本，再启动新版；仅关窗口可能只是进入托盘。旧版只支持弦乐，不会因为下载了新模型就出现新类别。新版继续读取原个人资料，仍需在设置选择摇滚模型并开启显示。

三个图标独立判断，允许同时亮起。吉他合并电吉他、木吉他和通用 guitar 标签，贝斯使用 bass 标签，鼓使用 drums 标签；没有把低频、音量、歌曲名或 other 音轨直接当作乐器。其他乐器尚未在此 profile 开放。

当前是窗口分类实验，**尚未通过广泛摇滚／金属准确度与进入／退出验收**。它不能区分贝斯型号、吉他数量或演奏者，也不提供逐音符定位。输入上下文 2064 ms，每 500 ms 更新；亮起需连续两窗达到 0.5，释放阈值 0.3 并保持 750 ms。首次结果还需采集分包、推断与渐变时间，通常至少约 2.6 秒；短暂进入／退出可能被合并。这些参数未经独立摇滚标注集校准。

首次听到一首歌时，清单随识别逐步补全。若要开头就显示完整暗色清单，仍需[完整单曲 WAV 预分析并确认完全相同的录音版本](instrument-activity.md)。本次试听片段不能绑定整首 Apple Music 歌曲，也不能据此宣称开头全曲清单已经验证。

## 2026-10-27 验证

使用 MTG/UPF 的 Discogs Effnet 特征提取器与 MTG Jamendo instrument 40 类分类头。参考资产逐项固定 SHA-256；软件内仅使用吉他／贝斯／鼓三个组。ONNX 包含自行实现的 MusicNN 参数前端：16 kHz、512 点对称 Hann、256 hop、96 个 Slaney 单位面积 mel 三角带、功率谱、`log10(1 + 10000 * mel)`。每窗截取 128 个完整内部帧，不补左右边界。独立 NumPy FFT 前端加原始 TensorFlow 编码器／分类头用作数值参考；没有声称在 Windows 上运行了 Essentia 原生算法。

- 原始参考与导出 ONNX：20 个自生成输入，另加 56 个实际试听窗口，最大绝对误差 `2.8611e-6`；C# 20 个自生成输入最大误差 `1.9372e-6`，通过原定 `1e-4` 门槛。保存的 parity 向量均为自生成信号。
- Black Sabbath《Paranoid》：使用 [Apple 官方目录的这首录音](https://music.apple.com/us/album/paranoid/785232473?i=785232521)提供的约 **29.976 秒试听**，完整目录时长约 168.406 秒。试听只在个人验证目录下载、解码，不进入 Git 或软件包。C# 原有 WAV 读取／连续重采样／ONNX 路径取得 56 个完整窗口，吉他／贝斯／鼓分数中位数分别约 `0.772 / 0.609 / 0.625`，超过 0.5 的窗口比例分别为 `100% / 80.4% / 94.6%`。这是片段检测观察，**不是准确率、召回率或完整歌曲边界验证**。
- 负样本：[URMP 公开室内乐](instrument-evaluation-urmp.md)44 首、9467 个完整窗口，吉他超过 0.5 的窗口为 0；鼓 30 窗超过 0.5（约 0.317%，涉及 4 首），说明仍有误报。Bass 最大值约 0.475，但数据中存在 double bass，不能把全部 Bass 分数都算成无贝斯负例。这套数据也不能代表金属乐中的失真和密集鼓点。
- Apple Music 实际采集：20 秒检查时实际为 Black Sabbath 的其他录音，取得 36 帧、零丢窗；窗口结束到结果 P95 约 20 ms，另需上述 2064 ms 上下文和活动平滑。最终固定图重复验证同样取得 36 帧、零丢窗。真实曲目快照和逐窗诊断只保留在个人目录，没有保存原始 PCM，没有切歌或改变 Apple Music 窗口。它证明采集与推断链路，不冒充《Paranoid》整首测试。
- 另一个 EfficientAT MN10 的 2 秒候选虽通过数值对照，却对静音和没有吉他／鼓的室内乐大量误报，已拒绝，**不在应用模型清单中**。其准备工具新增静音拒绝门槛，数值一致不能单独授权安装。

当前仍缺少独立、带精确进入／退出标注的摇滚／金属集和完整 Apple Music 播放矩阵，因此保留实验标识，不报告通用 G2 通过。UI 原创夹具检查不作为音乐识别证据。

## 用户开启单曲循环后的补充验证

同日随后核对了指定的《Paranoid (2009 Remaster)》实际 Apple Music 媒体会话，时长约 168 秒，连续观察 210 秒。631 个媒体快照均为 Playing，记录到两次有效位置从 167 秒返回 0 秒；每次回绕之间出现一个短暂不可读的位置样本，前后有效样本间隔分别约 0.38／0.45 秒，没有将空位置当成停止或其他歌曲。一次回绕发生在推断启动前，另一次被完整的实时输出覆盖。

固定模型取得 415 帧，零积压丢窗，窗口结束到结果 P95 约 11.84 ms，最大帧间隔约 500 ms；完整观察到的回绕边界附近也保持该间隔。另需 2064 ms 音频上下文、连续两窗活动确认和图标动画，不能把 11.84 ms 描述成用户感受到的总延迟。吉他／贝斯／鼓分数中位数分别约 0.808／0.532／0.567；对应模型活动状态分别为 410／410／390 帧，暗色状态分别为 4／4／24 帧，其余各 1 帧尚未进入已知清单。分数和活动比例均不代表识别准确率。

22 次只读窗口检查均确认 Apple Music 没有前台焦点、窗口可见且未最小化；这次验证覆盖后台播放，不能据此宣称最小化状态也已经通过。没有发送播放／跳转命令、改变窗口或保存 PCM。完整逐窗结果、原生位置和窗口记录只保留在个人诊断目录。此结果补足指定录音的播放流及循环连续性验证，广泛摇滚准确度和精确乐器进入／退出仍未通过。

## 模型、许可与复现

原始模型来源：[MTG 模型目录](https://essentia.upf.edu/models.html)。模型资产标注 **CC BY-NC-SA 4.0**，需遵守其署名、非商业和相同方式共享条件；原始资产、转换权重、参考下载和音乐都在个人目录，不随源码／ZIP 分发。软件不将模型许可描述成 MIT。模型来源、哈希与工具参数见 `tools/instruments/prepare_mtg.py`；依赖独立安装，不是应用运行时依赖。

```powershell
# 在仓库根目录；使用独立 Python 3.12 虚拟环境。
python -m pip install -r tools/instruments/requirements-mtg-lock.txt
$checks = Join-Path $env:LOCALAPPDATA 'LumaLyrics-InstrumentChecks'
python tools/instruments/prepare_mtg.py --reference-dir "$checks/mtg-reference" --output-dir "$checks/mtg-export-official"
# 若修改工具或依赖导致导出哈希改变，不能直接替换固定清单；须重新核对。
. ./scripts/common.ps1
Invoke-Dotnet build tools/LumaLyrics.Probe -c Release
& $script:Dotnet tools/LumaLyrics.Probe/bin/Release/net10.0-windows10.0.19041.0/LumaLyrics.Probe.dll --verify-instrument-model "$checks/mtg-export-official"
& $script:Dotnet tools/LumaLyrics.Probe/bin/Release/net10.0-windows10.0.19041.0/LumaLyrics.Probe.dll --install-instrument-model --model-id mtg-rock-three-experimental-v1 --file "$checks/mtg-export-official/mtg-jamendo-waveform.onnx"
```

固定转换文件大小 `20374087` 字节，SHA-256 `dbbd8c9371dc11d77ec40e82dedcbcffe922ebce618ea7973342c393d9f73a35`。组合发布者固定的动态 batch ONNX 与分类头，原始 batch-one TensorFlow 图仅作数值参考，避免重复 TF 转换的内部命名使哈希变化。模型仍保存于当前选定个人目录的 `models/instruments/`；不会覆盖旧弦乐模型。

## 播放器与采集诊断

`--probe-instrument-audio` 与新增 `--probe-instrument-music` 独立报告 AppleMusic 进程、AMPLibraryAgent 渲染进程和媒体会话。**有进程、有媒体会话、取得有效音频是三个不同检查**；不能把无音频或 PowerShell 表格的空白行解释成播放器未启动。

源码诊断的 `--seconds` 现支持 3–300 秒，私有报告额外保留带单调时钟的原生位置样本与每帧 epoch，可复核循环；旧 `rock-instruments` 预览包内的诊断仍为最长 120 秒，应用实时识别本身没有这个诊断时长限制。

```powershell
# reports 必须是已经存在的软件目录外个人文件夹，诊断不会发送 Play。
& $script:Dotnet tools/LumaLyrics.Probe/bin/Release/net10.0-windows10.0.19041.0/LumaLyrics.Probe.dll --probe-instrument-music --data-dir $reports --model-id mtg-rock-three-experimental-v1 --model "$checks/mtg-export-official/mtg-jamendo-waveform.onnx" --expect-title Paranoid --expect-artist 'Black Sabbath' --seconds 30
```

实际歌曲不匹配时返回 `RequestedRecordingMismatch`，并把真实快照写入个人诊断文件，停止采集；不会把《Paranoid》专辑的其他歌曲当成同名歌曲通过。实际媒体会话在开始采集前再次检查，切歌或暂停即取消。诊断文件不含 PCM，包含私人曲目元数据和逐窗数值，不能加入软件归档。
