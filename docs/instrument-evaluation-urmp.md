# 2026-10-27：公开标注评价与实验 UI

本轮已接入默认关闭的主播放页图标、Settings 控件、有界实时调度、WAV 预分析和仅限本次播放版本的明确绑定。[使用说明](instrument-activity.md)列出实际能力。正式通用 G2 仍未通过；只把通过室内乐分组评价的弦乐作为明确标示的实验能力启用，其他族保持关闭。有限公开数据的结果没有升级为通用生产准确度。

## 数据与方法

已自行下载并校验 [URMP 原发布记录](https://zenodo.org/records/5034983)及说明 PDF：44 个混音 WAV、149 个音符标注，共 693,676,005 字节，不下载视频与分轨。原发布者许可 CC0；逐文件下载的镜像为 `Eredis02/URMP`，固定 revision `58177a0f0f816e621b3d5d304c5fd1a03c035a86`。LFS 文件用 SHA-256、普通 Git 文件用 blob SHA-1 核对，另存逐文件 SHA-256。PDF 的 MD5 为 `6b29bc5744a7b013f79f3baf57b4913a`，已提取并渲染核对第 2 页：Notes 列为起点秒、音高 Hz、持续秒；音频为 48 kHz／24 bit／mono。

清单保留 `reviewed=false`，另记 `publishedAnnotationsVerified=true` 和来源，不冒充人工复核。使用发布者演奏音符的同族区间并集，边界前后 75 ms 为该族不确定区间，500 ms 格有交叠则排除并报告数量。在看分数前按作品分组；同作品不同编配不跨集，得到 26 个校准录音和 18 个留出录音。阈值只在校准集选择，之后保持不变；Python 与实际 C# PCM24 解码／连续 FIR／ONNX 链路分别报告，没有根据留出分数重新调参。

## 实际 C# 链路结果

44 文件评分耗时约 25.3 秒。固定弦乐参数为 on 0.1、off 0.065、attack 1 窗、release 500 ms。

| 类别 | 校准 precision / recall | 留出 precision / recall | 当前决策 |
| --- | --- | --- | --- |
| 弦乐 | 0.9542 / 0.8914 | 0.9903 / 0.8893 | 仅在室内乐范围实验启用 |
| 铜管 | 0.9228 / 0.8586 | 0.7057 / 0.7506 | 不启用 |
| 木管 | 0.5031 / 0.5702 | 0.8555 / 0.7611 | 校准未通过，不启用 |
| 贝斯 | 无校准正例 | 约 0.17 / 0.44 | 不启用 |
| 打击乐、吉他、键盘、合成器 | 正例不足 | 正例不足 | 不启用 |

弦乐留出集 TP 2049、FP 20、FN 255，评价 4307 格、排除 698 格（约 13.9%）；12 个录音有弦乐正例。校准集评价 3704 格、排除 898 格（约 19.5%）。同时要求校准与留出 precision ≥ 0.85、recall ≥ 0.65，不能因只在留出集表现好而启用木管。数据缺少密集流行混音、键盘和电子场景，`status=InsufficientCorpus`、`accuracyValidated=false`、`productionReady=false` 保持不变。

名义音符边界比较保留未匹配数量：弦乐留出 765 个参考进入边界只匹配 84 个，763 个退出边界只匹配 79 个；已匹配边界的绝对误差中位数约 1.52／1.42 秒。算法按每录音同类边界的 2 秒范围寻找未使用的最近边界，删去首尾被截断状态。短音符经 975 ms 窗口及活动平滑合并，因此**没有通过逐音符或精确进入／退出验收**，更不能把它作为端到端延迟。报告不只展示已匹配的误差。

## 第二模型候选

已下载 [PANNs Cnn6](https://zenodo.org/records/3987831)（CC BY 4.0）及 MIT 参考代码，固定于 `d2f4b8c18eab44737fcc0de1248ae21eb43f6aa4`。原权重 23,698,321 字节、MD5 `e25e26b84585b14c7754c91e48efc9be`，使用自己的 527 类映射，没有复用 YAMNet 的 521 类索引。20 个自生成输入的 Torch／ONNX 最大误差约 `2.35e-4`（关闭 MKLDNN 复跑；首轮约 `2.23e-4`），超过 `1e-4`，没有进入准确率比较或应用清单；工具明确失败，不放宽门槛。

## UI、运行与未完成验证

253 项乐器 UI／所有权断言通过，覆盖六主题、三档窗口宽度、队列开关、状态可访问名称、快速变化和动画释放、隐藏页面／后台开关、暂停与过期回调。96／144／192 DPI 是不同像素密度的离屏渲染，不能当作物理显示器切换通过。已检查设置和播放页截图，修复图标文字受滚动条遮挡及宽布局越界。状态夹具包含全部八类图标，不代表实际八类都启用。

60 秒真实速度文件回放取得 123 帧、零积压丢窗，窗口结束到结果 P95 约 95.8 ms（含 100 ms 文件分包等待），观察窗 975 ms。进程峰值工作集约 97.5 MiB，约占一核 9.25% CPU；只代表无声音输出的解码／FIR／ONNX 回放探针，不含 WPF 或 native capture，不代表长时间稳定性或实际进入延迟。本轮原生复检为 `NoAppleMusicSession`；完整最小化／设备切换／其他声音排除／30 分钟播放矩阵仍待实际会话验证。

延长到约 300 秒的复跑取得 617 帧、零积压丢窗，窗口结束到结果 P95 约 97.6 ms、CPU 约一核 8.81%。工作集 10 秒时约 88.4 MiB、结束约 98.3 MiB；私有内存约 54.2 → 62.3 MiB，句柄 312 → 326。这只是短时资源观测，没有据此判定 30 分钟无增长。511 项 .NET 测试（Core 118／Infrastructure 366／Windows 27）、12 项 Python 规则测试及 56 个私有文件分发拒绝样例通过，Release locked restore 零警告。

完整通用语料、独立声学边界复核、持久原生录音身份连接、其他格式、跨设备及长时实机验证仍未完成。当前实验 UI 不改变这些门槛的结论。

## 复现

所有目标目录都必须在软件目录外，评分目录必须已存在。报告使用新名称保留，避免反复覆盖留出结果。

```powershell
python tools/instruments/prepare_urmp.py --output-dir $urmpPrivateRoot
. ./scripts/common.ps1
Invoke-Dotnet build tools/LumaLyrics.Probe -c Release
Invoke-Dotnet tools/LumaLyrics.Probe/bin/Release/net10.0-windows10.0.19041.0/LumaLyrics.Probe.dll --score-instrument-dataset --dataset "$urmpPrivateRoot/dataset.json" --model-dir $verifiedModelExport --data-dir $newPrivateScoreDirectory
python tools/instruments/evaluate.py --dataset "$urmpPrivateRoot/dataset.json" --model-dir $verifiedModelExport --scores "$newPrivateScoreDirectory/instrument-csharp-dataset-scores.json" --output $newPrivateEvaluationReport
```

`--parameters-from <之前的报告>` 可冻结之前的校准参数，不再次搜索；工具核对模型与源文件哈希。`--score-instrument-dataset` 只评分整文件，`--scores` 不接受片段起点不为零的清单。边界误差与未匹配量同时输出。

```powershell
python -m pip install -r tools/instruments/requirements-panns.txt
python tools/instruments/prepare_panns.py --reference-dir $privatePannsReference --output-dir $newPrivatePannsExport
```

PANNs 命令本轮会保存未通过数值门槛的报告并以失败退出；失败权重不会装入主程序。

```powershell
Invoke-Dotnet tools/LumaLyrics.Probe/bin/Release/net10.0-windows10.0.19041.0/LumaLyrics.Probe.dll --replay-instrument-file --file $licensedWaveFile --model $verifiedOnnxFile --data-dir $existingPrivateReplayDirectory --seconds 60
```

回放按真实速度在内存中处理，没有声音输出；不要拿它替代原生音频验证。
