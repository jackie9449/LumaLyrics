# 第三方组件和数据来源

本项目使用下列组件。各原始许可文本随包附在 `licenses/`；运行时原始第三方说明一并保留。组件的商标和名称归其各自所有人。源码和运行时依赖不授予第三方歌词、音乐或封面的所有权。

| 组件 | 本次版本 | 来源与许可 |
|---|---|---|
| .NET Runtime / Windows Desktop Runtime / WPF | 10.0.13 | [dotnet/runtime](https://github.com/dotnet/runtime)、[dotnet/wpf](https://github.com/dotnet/wpf)，保留发行版原始许可与 notices |
| Microsoft.Data.Sqlite / Core | 10.0.13 | [dotnet/efcore](https://github.com/dotnet/efcore)，MIT |
| SQLitePCLRaw core / bundle / provider / native bundle | 2.1.13 | [SQLitePCL.raw](https://github.com/ericsink/SQLitePCL.raw)，Apache-2.0（以具体包的许可说明为准） |
| SQLite | 包内 e_sqlite3 | [SQLite copyright](https://sqlite.org/copyright.html)，SQLite 核心 public domain；保留包装组件自身许可 |
| Windows SDK .NET 投影 / WinRT.Runtime | SDK 按 net10.0-windows10.0.19041.0 解析 | Microsoft Windows SDK / [CsWinRT](https://github.com/microsoft/CsWinRT)，保留相应许可 |
| xUnit | 2.9.3 | 测试依赖，Apache-2.0，不随运行程序加载 |
| xunit.runner.visualstudio | 2.8.2 | 测试依赖，Apache-2.0 |
| Microsoft.NET.Test.Sdk | 17.14.1 | 测试依赖，MIT |
| FlaUI.Core / FlaUI.UIA3 | 5.0.0 | [FlaUI](https://github.com/FlaUI/FlaUI)，MIT，原始许可见 licenses/FlaUI-LICENSE.txt |
| Interop.UIAutomationClient | 10.19041.0 | [UIAutomation-Interop](https://github.com/Roemer/UIAutomation-Interop)，MIT，原始许可随包附带 |
| System.Management | 8.0.0 | [dotnet/runtime](https://github.com/dotnet/runtime)，MIT，原始许可与 notices 随包附带 |
| OpenccNetLib | 1.7.0 | [OpenccNet](https://github.com/laisuk/OpenccNet)，MIT，原始许可与第三方说明见 licenses/OpenccNetLib-* |
| OpenCC 内置简繁字词词典 | OpenccNetLib 1.7.0 内嵌版本 | [OpenCC](https://github.com/BYVoid/OpenCC)，Apache-2.0，原始许可见 licenses/OpenCC-Dictionary-LICENSE.txt；仅在本地转换显示文字 |
| ZstdSharp.Port | 0.8.8 | [ZstdSharp](https://github.com/oleg-st/ZstdSharp)，MIT，原始许可见 licenses/ZstdSharp-LICENSE.txt；托管代码解压内置词典 |
| System.Formats.Cbor | 8.0.2 | [dotnet/runtime](https://github.com/dotnet/runtime)，MIT，原始许可见 licenses/Runtime-LICENSE.txt；显式使用已修复安全公告的 8.0.2 |
| Inno Setup | 6.7.3 | 安装程序由 [Inno Setup](https://jrsoftware.org/) 生成；保留安装引擎版权及 [原始许可](licenses/Inno-Setup-LICENSE.txt)。安装界面中文增补文案由本项目编写。编译器不随应用分发。 |

直接与传递 NuGet 版本和完整内容哈希固定在各项目 `packages.lock.json`，SDK 在 `global.json`。本次 SDK 为 10.0.402，仅用于开发，不把整个 SDK 分发给用户。附带的备用 dotnet host 和 hostfxr 来自相同微软发行版。

歌词查询使用 [LRCLIB](https://lrclib.net/docs)，带 `LumaLyrics/0.1.0` 客户端标识。只调用查询接口，串行节流并遵守 Retry-After。歌词与封面按实际来源在用户本地获取；发布包不包含批量歌词库或音乐。新增公开截图使用原创几何封面与自写歌词，不含个人资料库内容。

图标为本项目用几何图形生成的原生资产；未使用外部照片或商业图标包。

悬浮歌词按语言提供本机字体和随程序分发的 Noto 字体：Noto Sans CJK Regular（SC/TC/JP/KR，共享 TTC，Sans 2.004）以及 Noto Sans、Arabic、Hebrew、Devanagari、Thai、Bengali、Tamil 的 Regular 字体，均以 SIL Open Font License 1.1 分发。保留原始字体名称与版权，不修改字体文件，不安装到系统字体目录。原始许可随软件放在 `licenses/OFL-CJK.txt` 和 `licenses/OFL-Noto.txt`，源码副本位于 `src/LumaLyrics.App/Assets/Fonts/`。每个资产的固定上游提交 URL、大小和 SHA-256 记录在 `Assets/Fonts/sources.json`（源码路径前加 `src/LumaLyrics.App/`）。来源：[notofonts/noto-cjk](https://github.com/notofonts/noto-cjk)、[notofonts/noto-fonts](https://github.com/notofonts/noto-fonts)。

共享字母的语言通过 Windows 本地 ELS 服务识别，文字不会提交到在线服务；[Microsoft Language Detection](https://learn.microsoft.com/en-us/windows/win32/intl/microsoft-language-detection) 输出按相关性排序的语言，短句和混合语言仍可能存在歧义。

资料库只读解析器由本项目实现，格式兼容性参考 Gary Vollink 和 rinsuki 发布的字段研究：[musicdb 格式](https://home.vollink.com/gary/playlister/musicdb.html)、[musicdb2sqlite](https://github.com/rinsuki/musicdb2sqlite)。使用 .NET 自带 AES/zlib 处理本地曲目元数据。

原生 Apple Music 的无障碍播放命令研究参考 [windows-apple-music-remote](https://github.com/ahmetberber/windows-apple-music-remote) 的公开说明。浮光独立实现本地歌曲列表适配与录音核对，不启动远程服务或网络监听。

## 可选 Argos 离线翻译组件（2026-11-17）

用户主动准备时从上游下载，软件 ZIP 不携带 Python、wheel 或模型。Argos Translate 1.11.0（MIT / CC0，采用 MIT）、CTranslate2 4.6.0（MIT）、SentencePiece 0.2.1（Apache-2.0）、Python 3.12.10 embedded（PSF）及辅助依赖均保留原始许可和 notices。固定来源、SHA-256、依赖版本与安装目录见 [离线翻译指南](docs/offline-translation.md)。

英→中 1.9 Argos 模型包原始 OPUS 模型为 CC BY 4.0，署名 Jörg Tiedemann 和 Santhosh Thottingal，*OPUS-MT — Building open translation services for the World*，EAMT 2020，Lisbon, Portugal。使用原始包进行 CPU int8 推理，不重新训练或分发修改权重；输出为机器翻译。模型 README 随安装保留，软件指南同时提供署名与许可链接。
