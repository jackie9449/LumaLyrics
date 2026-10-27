# 按歌曲生成乐器清单（实验）

播放页现在按音频证据生成当前录音的清单，不再要求选择摇滚三类。通用实验模型尝试识别下面 19 类；未检出的类别不预先展示，切歌清除上一首的实时清单。亮色表示窗口活动，暗色表示已进入本曲清单但当前不活动。**这不是已通过准确度验收的完整配器识别。**

| 支持尝试的类别 | 能力边界 |
| --- | --- |
| 吉他、贝斯、鼓、镲 | 吉他不细分电／木／古典，不判断数量；鼓与镲允许并存 |
| 钢琴、风琴、合成器、键盘打击乐 | 键盘打击乐对应 mallet percussion；音色与真实录音来源可能混淆 |
| 小提琴、大提琴 | 不将其扩展成全部弦乐种类 |
| 长笛、单簧管、萨克斯、小号、长号 | 本轮管乐混淆明显，见下方失败样例 |
| 手风琴、班卓琴、曼陀林、尤克里里 | 仅为模型词汇，尚无独立类别验收 |

中提琴、双簧管、圆号、竖琴、中国民族乐器等不在本模型词汇内。人声输出不映射为乐器。不能把不显示解释为没有演奏，也不能从歌名或流派补造清单。老弦乐兼容模型仍单独保留；已有具体类别时不重复展示同族的泛化图标。

## 使用与时间

启动独立的 `song-instruments` 预览，打开 **设置 → 乐器识别**，选择「通用多乐器：按歌曲识别（实验）」并开启显示。本机已下载、导出并安装固定模型；其他电脑按下面命令准备。旧摇滚三类设置在新版加载时迁移到通用模型；显式选择的弦乐兼容模型保留。显示与后台开关保持用户值，新资料默认关闭。旧程序需要退出后启动新预览才能载入新代码。

模型使用 **3055 ms** 上下文，每 500 ms 更新。吉他／贝斯／鼓／镲连续两窗达到 0.5 才活动，释放阈值 0.3；其余类别连续三窗达到 0.7，释放阈值 0.45，均保持 750 ms。参数未经独立标注集校准。首次通常至少需要约 3.6–4.1 秒，再加分包、计算和动画；窗口分类不能精确定位短音符。

首次未知流媒体的清单逐步补全，不能在尚未听到后半首时知道后半首才出现的乐器。要从开头显示整曲检出的暗色清单，需要完整单曲 WAV 预分析，并确认与当前 Apple Music 是完全相同的录音版本。**整曲覆盖只表示全部音频已经处理，不表示所有实际乐器已被识别。** 绑定与格式限制见[使用说明](instrument-activity.md)。

## 实测和未通过项目

以下结果不作为准确率、精确进入／退出或所有类别通过的证据。逐窗结果、曲目快照和公开测试音频保留在软件目录外，不随 Git／软件包发布。

- 原始 PyTorch 编码器＋分类头与 ONNX：20 个自生成输入及 27 个实际试听窗口，最大绝对误差 `1.8478e-6`。C# 的 20 个自生成窗口最大误差 `1.8478e-6`，通过既定 `1e-4` 门槛。重复导出得到相同完整 SHA-256。保存的 parity PCM 只有自生成信号。
- 用户恢复播放后的 Apple Music《Paranoid (2009 Remaster)》：观察 30 秒，51 帧、零积压丢窗；补充全零保护后再次观察取得同样帧数与清单，窗口结束到结果 P95 约 **108 ms**（首次约 132 ms）。另需 3055 ms 上下文和活动确认。清单为吉他、贝斯、鼓、镲。未保存原始 PCM，未改变播放器窗口或发送播放命令。
- Apple 官方约 29.976 秒《Paranoid》试听：C# 整文件读取取得 54 个完整窗口，清单为吉他、贝斯、鼓、镲。试听不能绑定整首，也不证明其他摇滚／金属作品的准确度。
- 对 44 首 URMP 混音进行了稀疏窗口观察；另以实际 C# 全文件路径检查以下三首。清单确实变化，但出现明显误报，**通用识别质量未通过**：

| URMP 文件的已知编制 | 模型整文件清单 | 失败观察 |
| --- | --- | --- |
| 01 Jupiter：小提琴、大提琴 | 鼓、吉他、大提琴、小提琴 | 误加鼓与吉他 |
| 03 Dance：长笛、单簧管 | 吉他、钢琴、大提琴、单簧管、长笛、萨克斯、小提琴 | 多个额外类别；局部窗口也会漏掉真实单簧管 |
| 05 Entertainer：两支小号 | 吉他、钢琴、镲、萨克斯、长号、小号、小提琴 | 铜管细分类与其他音色混淆 |

三首合计 478 个完整窗口；完整覆盖不修正这些错误。原模型在全零输入也会给吉他／鼓高分，因此分析引擎将采样值全部为零的窗口输出归零，不改变非零安静音频或原始数值核对。10 秒数字静音 WAV 经实际 C# 整文件路径得到空清单；保护版本进入缓存身份，不复用此前未保护的分析。该保护不能消除非零音乐中的音色混淆。没有把临时调整阈值或同曲适配器当作独立验收。MTG 全部类别直接开放的方案也因负样本误报被放弃，旧 MTG 三类仅保留 CLI 复现。

519 项 .NET 单元测试与 276 项乐器 UI／生命周期断言通过，另通过 259 项应用设计和 124 项滑条回归；六应用主题、820／980／1220 宽度、队列开关、减弱动画与缩放渲染使用原创夹具。这验证界面和隔离规则，不验证音乐识别准确度。分发拒绝 57 种个人文件样例。下一道质量门槛仍是独立多曲风标注、各类别误报／漏报和活动边界验证；目前 `accuracyValidated=false`、`productionReady=false`。

## 模型来源与复现

编码器为 Aurian Quelennec 的 [MATPAC++ Hugging Face 发布](https://huggingface.co/auriankelen/matpac_music)，revision `a7e10eb8d14c764673f4364884fb06c9f9f88887`，Apache-2.0。OpenMIC 分类头来自 [vaclis/AMTFlow](https://huggingface.co/vaclis/amtflow)，revision `65ec90d739b4bd77aece2b47ec4fb5cc6440179c`，CC BY 4.0。模型卡的 10 秒任务结果不等于本机 3.055 秒窗口及活动灯的质量。

`prepare_matpac.py` 固定全部参考代码与权重哈希，在个人目录下载；大文件分段核对 HTTP 范围后仍校验整体 SHA-256。只执行校验后的固定本地代码。前端为 16 kHz、400 点 periodic Hann、160 hop、80 个 Slaney mel、自然对数及原始归一化；采用第 10 层（从零计）平均嵌入和原始分类头。ONNX 的 DFT 使用双精度乘加后转回 float32。原始 PyTorch FFT 路径单独作为数值参考。

```powershell
# 仓库根目录；独立 Python 3.12 环境，不是应用运行时依赖。
python -m pip install -r tools/instruments/requirements-matpac.txt
$checks = Join-Path $env:LOCALAPPDATA 'LumaLyrics-InstrumentChecks'
python tools/instruments/prepare_matpac.py --reference-dir "$checks/matpac-reference" --output-dir "$checks/matpac-export"
. ./scripts/common.ps1
Invoke-Dotnet build tools/LumaLyrics.Probe -c Release
Invoke-Dotnet tools/LumaLyrics.Probe/bin/Release/net10.0-windows10.0.19041.0/LumaLyrics.Probe.dll --verify-instrument-model "$checks/matpac-export"
Invoke-Dotnet tools/LumaLyrics.Probe/bin/Release/net10.0-windows10.0.19041.0/LumaLyrics.Probe.dll --install-instrument-model --model-id matpac-song-instruments-experimental-v1 --file "$checks/matpac-export/matpac-openmic-waveform.onnx"
# reports 为已经存在的软件目录外个人目录；不会发送播放指令。
Invoke-Dotnet tools/LumaLyrics.Probe/bin/Release/net10.0-windows10.0.19041.0/LumaLyrics.Probe.dll --probe-instrument-music --data-dir $reports --model-id matpac-song-instruments-experimental-v1 --model "$checks/matpac-export/matpac-openmic-waveform.onnx" --seconds 30
```

整文件诊断 `--analyze-instrument-file --experimental` 和 `--score-instrument-dataset` 可附加 `--model-id matpac-song-instruments-experimental-v1`，使用与应用完全相同的映射／阈值；不指定时仍保留旧工具的元数据实验阈值，不能据此复现产品清单。

固定导出：`322477291` 字节，SHA-256 **`eb7f7460012bfbf93f029404542faa2e57ffd6ebb4050b18d7bdd6e6b301119f`**。完整来源哈希见脚本。不同依赖导致哈希变化时不得跳过清单验证，需要重新核对。模型约 307.5 MiB，仅安装个人目录，不随软件包或个人备份分发；原始权重、公开音乐和诊断同样不进入软件归档。
