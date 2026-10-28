# 乐器快速响应实验 — 2026-10-28

针对“亮灭慢、长时间常亮”，通用实验模型更新为 `matpac-song-instruments-responsive-v2`。仍按歌曲音频尝试同样的 19 类，不按歌曲名或流派预设清单。模型、较强的清单准入与快速活动判断分别调整；尚未通过全面准确度或逐音符／逐鼓点验收。

## 本机使用和变化

启动 `responsive-instruments` 便携预览，设置 → 乐器识别中选择「通用多乐器：快速响应（实验）」。本机新模型已安装到当前个人资料目录。旧通用 v1 或旧摇滚三类设置在新版加载时迁移到 v2；显式弦乐兼容选择保留，用户开关值不变，新资料仍默认关闭。旧程序需要退出后启动新预览才会加载新代码。

| 项目 | 旧通用 v1 | 快速响应 v2 |
| --- | --- | --- |
| 音频上下文 | 3055 ms | 1455 ms |
| 更新间隔 | 500 ms | 200 ms |
| 已知乐器活动确认 | 连续 2／3 窗 | 1 窗达到活动阈值 |
| 活动释放保持 | 750 ms | 200 ms，且提高弱分数保持门槛 |
| 新类别加入本曲 | 活动确认即永久加入 | 独立的较高置信度、连续 4／5 窗确认 |
| 音频结果过时 | 最长 2 秒后未知 | 新模型超过 750 ms 后未知 |

清单准入：贝斯 `0.6`、吉他 `0.9`、鼓／镲 `0.85` 连续 4 窗；钢琴／合成器 `0.95`、其余类别 `0.9` 连续 5 窗。确认后不再重复等待这段准入时间，活动亮灭使用原有 `0.5`／`0.7` 开启门槛，关闭门槛分别为 `0.4`／`0.6`。因此首次列出新类别通常仍需约 2.1–2.3 秒音频与确认时间，另加连接、分包、计算及图标动画。短暂乐器可能因严格准入而漏掉。

最后 80 ms **已观察采样全部为零**时，撤销模型对旧声音的残留活动分数；不是按响度认定具体乐器，也不对安静的非零音乐使用这个规则。文件尾部给模型补的零不算实际观察，不能借补零提前宣告静音。该策略及清单参数进入缓存身份，旧分析不混用。

首次流媒体仍逐步完善清单；从开头显示整曲检出的暗色清单仍需[完整且相同录音的 WAV 分析与绑定](instrument-activity.md)。完整覆盖不等于所有实际乐器都已正确识别。持续演奏时可以持续亮起，本功能不承诺每次拨弦／鼓点都独立闪动。

## 响应验证

使用 Apple 官方《Paranoid》试听前 6 秒构造个人测试文件：6 秒音乐、4 秒数字静音、同段 6 秒音乐、4 秒静音。两套固定模型使用相同 WAV，经实际 C# 解码／重采样／ONNX 路径计分，再由原始 `InstrumentActivityFilter` 在因果窗口结束时刻判断转换。音乐与逐窗报告不分发。

| 判定延迟 | 旧 v1 | 新 v2 |
| --- | --- | --- |
| 两次整段停奏，吉他／贝斯／鼓／镲共 8 项 | 均 3555 ms | 均 255 ms |
| 恢复：吉他／鼓 | 555 ms | 55 ms |
| 恢复：贝斯 | 1055 ms | 455 ms |
| 恢复：镲 | 1555 ms | 455 ms |

12 项转换均观察到前态和后态，没有把原本已经熄灭的状态当作零延迟。**这只测整段声音停止／恢复的窗口和滤波延迟**；还需计算、分包、UI 刷新及 120 ms 渐变，不能把 255 ms 宣称为所有真实乐器的端到端退出延迟，也不代表混音中单件乐器退出已通过。

用户恢复 Apple Music 后，实际《Paranoid (2009 Remaster)》30 秒探针取得 **138 帧、零积压丢窗**，帧结束间隔均为 200 ms，窗口结束到结果 P95 约 **45.12 ms**。确认后的吉他有 129 帧亮／6 帧暗，贝斯 126 帧亮／7 帧暗，各有 4 次亮暗变化；鼓和镲各 135 帧保持模型活动。其余启动帧尚未加入清单。这证明新链路实际更新与状态变化，不能代替人工演奏时刻标注。没有操纵 Apple Music 窗口／播放，也没有保存 PCM。

另外 30 秒有节奏的文件重放取得 137 帧、零丢窗，窗口结束到结果 P95 约 132.22 ms；当时并行运行 UI 验证，CPU 约单核的 73.7%，工作集约 390 MiB。重放不产生声音，不是原生采集验证。

## 误报和漏报

直接缩短窗口以及仅改最近时间片池化的候选在 calibration 数据上变差，因此未直接采用。新版本在固定编码器和原分类头之后加入小型线性抑制器：只允许降低小提琴、大提琴、长笛、单簧管、萨克斯、小号、长号七类的原始分数，不提高任何类别分数；其余类别沿用原头。

训练只用 URMP 的 26 首 calibration 录音，按 composition 分组进行三折选择，选择冻结后评价其余 18 首保留组。每 2 秒采样，使用发布者的音符范围在**结果时刻**评价，容许边界前后 150 ms；不是回移到窗口中点的离线结果，也不是用户人工审核。此公开保留组已在早期研究中使用，不描述为从未看过的新数据。以下是 19 类原始逐窗活动门槛的微平均，未计入新的严格清单过滤和 UI 动画：

| 18 首保留组 | v1 3.055 秒 | 仅缩短到 1.455 秒 | v2 短窗＋抑制 |
| --- | --- | --- | --- |
| 真阳性 / 误报 / 漏报 | 1695 / 1422 / 697 | 1655 / 1437 / 737 | 1603 / 726 / 789 |
| 精确率 | 54.38% | 53.53% | 68.83% |
| 召回率 | 70.86% | 69.19% | 67.02% |
| F1 | 61.54% | 60.36% | 67.91% |

误报约减少 49%，但漏报增加，不能称为“全部乐器准确度通过”。没有使用保留组重新调参数；也没有验证这种抑制在各种摇滚／金属编制上的迁移质量。

实际 C# 整文件路径及清单规则对照：

| 真实编制 | v1 清单 | v2 清单 |
| --- | --- | --- |
| URMP 01：小提琴、大提琴 | 鼓、吉他、大提琴、小提琴 | 吉他、大提琴、小提琴，仍误加吉他 |
| URMP 03：长笛、单簧管 | 吉他、钢琴、大提琴、单簧管、长笛、萨克斯、小提琴 | 长笛、萨克斯，仍误报萨克斯并漏掉单簧管 |
| URMP 05：两支小号 | 吉他、钢琴、镲、萨克斯、长号、小号、小提琴 | 萨克斯、小号，仍有管乐混淆 |
| 官方 Paranoid 试听 | 吉他、贝斯、鼓、镲 | 相同四类 |
| 10 秒数字静音 | 空 | 空 |

这三个管弦乐示例的额外类别从 13 个降到 3 个，同时少识别了一个真实类别；它们不是独立于上述公开集的另一套准确度验收。继续保留 `accuracyValidated=false`、`productionReady=false`。19 类词汇的其他边界见[旧版类别说明](instrument-roster.md)。

## 数值、来源和复现

参考来源仍为 [Aurian Quelennec / MATPAC++](https://huggingface.co/auriankelen/matpac_music)（Apache-2.0）及 [vaclis / AMTFlow OpenMIC 头](https://huggingface.co/vaclis/amtflow)（CC BY 4.0），固定 revision 与全部参考资产哈希沿用 `prepare_matpac.py`。新增抑制器由本项目在 [URMP 发布者数据](https://zenodo.org/records/5034983)（CC0）的 calibration 部分训练；不是发布者原始分类头的官方更新。

- 上下文 144 完整 mel 帧、9 个时间片，使用原第 10 层全时间平均嵌入。抑制器为 3840 → 7 线性 sigmoid，经 `min(1, probability / 0.5)` 仅衰减指定分数。
- composition SHA-256 决定三折；seed `42028`、180 步 Adam、学习率 `0.02`、L2 `0.01`，正例权重为负／正比例平方根并限制 1–5，训练特征标准差下限 `0.05`。特征、模型与报告只在个人目录。
- 20 个自生成输入及 29 个实际试听窗口：原始 PyTorch 路径与 ONNX 最大误差 `1.2517e-6`；C# 20 个自生成输入最大误差 `1.6094e-6`，保留 `1e-4` 门槛。独立重复导出 SHA-256 相同。保存的 parity PCM 不含实际音乐。
- 固定抑制器 NPZ SHA-256：`69921e2865f972c545e72b7b3a0a4327ab1a906042530f6cde135793a107582b`。固定 ONNX 大小 `322341960` 字节，SHA-256：`11a7162cf36efe2bd81506d8bddf8e9f96e60583c9d5f2f5f691ccd0ab214a8e`。

```powershell
# 仓库根目录，独立 Python 3.12 环境；所有输出在软件目录外。
python -m pip install -r tools/instruments/requirements-matpac.txt
$checks = Join-Path $env:LOCALAPPDATA 'LumaLyrics-InstrumentChecks'
python tools/instruments/prepare_urmp.py --output-dir "$checks/urmp-public"
python tools/instruments/compare_matpac_windows.py --reference-dir "$checks/matpac-reference" --dataset "$checks/urmp-public/dataset.json" --output-dir "$checks/response-features" --split all --contexts 19,9 --save-embeddings
python tools/instruments/train_matpac_gate.py --features-dir "$checks/response-features" --baseline-dir "$checks/response-features" --output-dir "$checks/response-gate"
python tools/instruments/prepare_matpac.py --reference-dir "$checks/matpac-reference" --output-dir "$checks/matpac-responsive-export" --response-gate "$checks/response-gate/matpac-gate.npz" --gate-sha256 69921e2865f972c545e72b7b3a0a4327ab1a906042530f6cde135793a107582b
. ./scripts/common.ps1
Invoke-Dotnet build tools/LumaLyrics.Probe -c Release
Invoke-Dotnet tools/LumaLyrics.Probe/bin/Release/net10.0-windows10.0.19041.0/LumaLyrics.Probe.dll --verify-instrument-model "$checks/matpac-responsive-export"
Invoke-Dotnet tools/LumaLyrics.Probe/bin/Release/net10.0-windows10.0.19041.0/LumaLyrics.Probe.dll --install-instrument-model --model-id matpac-song-instruments-responsive-v2 --file "$checks/matpac-responsive-export/matpac-responsive-waveform.onnx"
```

训练或依赖差异导致哈希变化时必须重新验证，不能绕过固定清单。保留 v1 的显式 CLI ID 用于历史复现。模型、NPZ／NPY 特征、实际音频及逐窗报告全部排除出 Git／软件包／个人备份；软件包只含程序和文档。524 项 .NET 测试与 277 项乐器 UI／生命周期断言通过，分发拒绝 59 种个人文件样例。UI 验证使用独立资料目录和原创夹具，不证明音乐语义准确度。
