# 第三方组件和数据来源

本项目使用下列组件。各原始许可文本随包附在 `licenses/`；运行时原始第三方说明一并保留。组件的商标和名称归其各自所有人。源码和运行时依赖不授予第三方歌词、音乐或封面的所有权。

| 组件 | 本次版本 | 来源与许可 |
|---|---|---|
| .NET Runtime / Windows Desktop Runtime / WPF | 10.0.13 | [dotnet/runtime](https://github.com/dotnet/runtime)、[dotnet/wpf](https://github.com/dotnet/wpf)，保留发行版原始许可与 notices |
| Microsoft.Data.Sqlite / Core | 10.0.13 | [dotnet/efcore](https://github.com/dotnet/efcore)，MIT |
| Microsoft.ML.OnnxRuntime / Managed | 1.31.0 | [microsoft/onnxruntime](https://github.com/microsoft/onnxruntime)，MIT；原始许可和完整 notices 见 `licenses/ONNX-Runtime-*` |
| SQLitePCLRaw core / bundle / provider / native bundle | 2.1.13 | [SQLitePCL.raw](https://github.com/ericsink/SQLitePCL.raw)，Apache-2.0（以具体包的许可说明为准） |
| SQLite | 包内 e_sqlite3 | [SQLite copyright](https://sqlite.org/copyright.html)，SQLite 核心 public domain；保留包装组件自身许可 |
| Windows SDK .NET 投影 / WinRT.Runtime | SDK 按 net10.0-windows10.0.19041.0 解析 | Microsoft Windows SDK / [CsWinRT](https://github.com/microsoft/CsWinRT)，保留相应许可 |
| xUnit | 2.9.3 | 测试依赖，Apache-2.0，不随运行程序加载 |
| xunit.runner.visualstudio | 2.8.2 | 测试依赖，Apache-2.0 |
| Microsoft.NET.Test.Sdk | 17.14.1 | 测试依赖，MIT |
| FlaUI.Core / FlaUI.UIA3 | 5.0.0 | [FlaUI](https://github.com/FlaUI/FlaUI)，MIT，原始许可见 licenses/FlaUI-LICENSE.txt |
| Interop.UIAutomationClient | 10.19041.0 | [UIAutomation-Interop](https://github.com/Roemer/UIAutomation-Interop)，MIT，原始许可随包附带 |
| System.Management | 8.0.0 | [dotnet/runtime](https://github.com/dotnet/runtime)，MIT，原始许可与 notices 随包附带 |
| Inno Setup | 6.7.3 | 安装程序由 [Inno Setup](https://jrsoftware.org/) 生成；保留安装引擎版权及 [原始许可](licenses/Inno-Setup-LICENSE.txt)。安装界面中文增补文案由本项目编写。编译器不随应用分发。 |

直接与传递 NuGet 版本和完整内容哈希固定在各项目 `packages.lock.json`，SDK 在 `global.json`。本次 SDK 为 10.0.402，仅用于开发，不把整个 SDK 分发给用户。附带的备用 dotnet host 和 hostfxr 来自相同微软发行版。

歌词查询使用 [LRCLIB](https://lrclib.net/docs)，带 `LumaLyrics/0.1.0` 客户端标识。只调用查询接口，串行节流并遵守 Retry-After。歌词与封面按实际来源在用户本地获取；发布包不包含批量歌词库或音乐。新增公开截图使用原创几何封面与自写歌词，不含个人资料库内容。

图标为本项目用几何图形生成的原生资产；未使用外部照片、字体文件或商业图标包。显示字体使用系统已有字体，不捆绑分发。

乐器识别提供默认关闭、限制类别的实验入口。`prepare_yamnet.py` 在软件目录外按固定哈希下载 Google YAMNet 原始权重及 TensorFlow Models 参考代码；软件和源码仓库不含这些文件或转换权重。参考代码 revision、文件哈希和模型许可来源见该脚本与 [评估说明](docs/instrument-evaluation.md)。数值对照不代表音乐识别准确度或进入／退出时间已通过，当前没有已发布的生产模型下载清单。Python 评估依赖独立安装，版本清单不构成这些依赖的分发包。

公开评价使用 Li、Liu、Dinesh、Duan、Sharma 的 [URMP 数据集发布记录](https://zenodo.org/records/5034983)，发布记录许可 CC0；混音／音符文件来自固定版本的镜像，来源与校验记录保留在个人评估目录，不随软件分发。镜像不被描述为原发布者，作者提供的音符标注不冒充用户人工审核。

通用多乐器实验采用 Aurian Quelennec 的 [MATPAC++ 编码器发布](https://huggingface.co/auriankelen/matpac_music)（Apache-2.0）与 [vaclis/AMTFlow 的 OpenMIC 分类头](https://huggingface.co/vaclis/amtflow)（CC BY 4.0）。本项目将固定版本组合并转换为包含波形前端的 ONNX，修改与数值核对见[通用清单说明](docs/instrument-roster.md)。参考代码及原始／转换权重全部保留在个人目录，不随 Git 或软件 ZIP 分发。固定 revision、来源、SHA-256 见 `tools/instruments/prepare_matpac.py`；模型许可不改写成应用源码许可，作者的原任务指标不代表本机实时准确度已通过。

摇滚三类实验使用 MTG/UPF 的 Discogs Effnet 和 MTG Jamendo instrument 模型资产，[官方来源及许可](https://essentia.upf.edu/models.html)为 CC BY-NC-SA 4.0。原始文件及转换权重只在个人目录下载／生成，不随仓库或软件包分发；须遵守模型的署名、非商业和相同方式共享条件。自行实现的前端没有复制 Essentia 源码；参考原始模型、哈希与验证范围见 [摇滚说明](docs/instrument-rock.md)。Black Sabbath《Paranoid》的 Apple 官方试听仅供本机验证，不随软件分发。另有 EfficientAT MN10（Florian Schmid，固定源码 MIT）候选工具，2 秒候选因静音／负样本误报被拒绝，未加入应用清单；其参考代码和权重同样只保存于个人目录。

候选工具 `prepare_panns.py` 下载 Qiuqiang Kong 等人的 PANNs Cnn6 权重（[Zenodo 3987831](https://zenodo.org/records/3987831)，CC BY 4.0）和固定 revision 的[参考代码](https://github.com/qiuqiangkong/audioset_tagging_cnn/tree/d2f4b8c18eab44737fcc0de1248ae21eb43f6aa4)（`LICENSE.MIT`）。代码、许可和权重只下载到个人开发目录，软件中未采用该模型；本轮转换未通过数值门槛。两套模型、公开音乐与逐曲结果都不包含在 Git 或便携包中。

资料库只读解析器由本项目实现，格式兼容性参考 Gary Vollink 和 rinsuki 发布的字段研究：[musicdb 格式](https://home.vollink.com/gary/playlister/musicdb.html)、[musicdb2sqlite](https://github.com/rinsuki/musicdb2sqlite)。使用 .NET 自带 AES/zlib 处理本地曲目元数据。

原生 Apple Music 的无障碍播放命令研究参考 [windows-apple-music-remote](https://github.com/ahmetberber/windows-apple-music-remote) 的公开说明。浮光独立实现本地歌曲列表适配与录音核对，不启动远程服务或网络监听。
