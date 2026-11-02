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
| Inno Setup | 6.7.3 | 安装程序由 [Inno Setup](https://jrsoftware.org/) 生成；保留安装引擎版权及 [原始许可](licenses/Inno-Setup-LICENSE.txt)。安装界面中文增补文案由本项目编写。编译器不随应用分发。 |

直接与传递 NuGet 版本和完整内容哈希固定在各项目 `packages.lock.json`，SDK 在 `global.json`。本次 SDK 为 10.0.402，仅用于开发，不把整个 SDK 分发给用户。附带的备用 dotnet host 和 hostfxr 来自相同微软发行版。

歌词查询使用 [LRCLIB](https://lrclib.net/docs)，带 `LumaLyrics/0.1.0` 客户端标识。只调用查询接口，串行节流并遵守 Retry-After。歌词与封面按实际来源在用户本地获取；发布包不包含批量歌词库或音乐。新增公开截图使用原创几何封面与自写歌词，不含个人资料库内容。

图标为本项目用几何图形生成的原生资产；未使用外部照片、字体文件或商业图标包。显示字体使用系统已有字体，不捆绑分发。

资料库只读解析器由本项目实现，格式兼容性参考 Gary Vollink 和 rinsuki 发布的字段研究：[musicdb 格式](https://home.vollink.com/gary/playlister/musicdb.html)、[musicdb2sqlite](https://github.com/rinsuki/musicdb2sqlite)。使用 .NET 自带 AES/zlib 处理本地曲目元数据。

原生 Apple Music 的无障碍播放命令研究参考 [windows-apple-music-remote](https://github.com/ahmetberber/windows-apple-music-remote) 的公开说明。浮光独立实现本地歌曲列表适配与录音核对，不启动远程服务或网络监听。
