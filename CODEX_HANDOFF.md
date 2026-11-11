# 浮光歌词 · 换机开发接手说明

更新：2026-11-11。当前应用版本为 **1.2.0**，基于 **b0736aa** 的完整交接成果准备正式发布；此前功能提交为 **441c37d**。发布结果与验证以 [1.2.0 记录](docs/verification-v120/README.md)和对应 v1.2.0 标签为准。旧迁移包 manifest 仍记录该次源码及 1.1.0 预览运行包，不会随此版本发布改变。

## 1. 记录与协作方式

先读 [AGENTS.md](AGENTS.md)、[全部更新索引](docs/updates-2026-11-11.md)、[开发记录](docs/development-log.md)、[发布记录](docs/release-notes.md)。更早设计、截图和验证材料仍在 docs 中。工作包 history/commits.txt 和 history/LumaLyrics.bundle 保存完整本地提交历史及 v1.0.0、v1.1.0 标签。

**常规开发使用本地 Git。** 仅在用户对当前任务明确提出时进行 GitHub 同步或发布；本次用户已明确要求同步最新代码并发布 1.2.0。其它任务继续遵循 AGENTS.md，不覆盖旧正式发布资产。原 571ba0e 交接包只作历史保留，不能代替本次成果。

## 2. 新电脑恢复

包内包含 source（全部跟踪文件）、history（完整 Git bundle）、app（已测运行包）、toolchain（SDK 与离线 NuGet 包）、scripts（校验与恢复）和 manifest.json。个人资料继续独立保存。

1. 将 ZIP 和 .sha256 一起复制到新机，核对 SHA-256，再完整解压到新目录。
2. 使用 Windows x64、PowerShell 和 Git。开发 SDK 10.0.402 随包提供，不需要更改 global.json 或升级依赖。Git 本身不随包提供。普通开发不需要 Visual Studio 或 Inno Setup；制作安装程序才需要 Inno Setup。
3. 在**解压后的工作包根目录**运行：

```powershell
./scripts/Verify-Handoff.ps1
./scripts/Restore-Workspace.ps1 -Destination 'D:\Work\LumaLyrics' -Build
```

恢复脚本拒绝已有目录；只从本地 bundle 克隆，核验 HEAD、树和 Git 对象，移除克隆自动生成的 origin。SDK、离线依赖及隔离 NuGet 缓存放入新仓库被忽略的 .tools。-Build 仅使用本地包源执行锁定恢复、Release 构建和测试，不会启动软件或音乐。source/ 是阅读快照，没有 .git；应在恢复后的仓库继续开发。

## 3. 本轮完成的全部工作

| 提交 | 内容 |
| --- | --- |
| 936da33 | 接手三个文件，改用本地 Git 和开发记录 |
| b506832 | 设置／诊断内嵌主窗口；媒体快照投递延迟补偿；右侧播放列表封面匹配和刷新 |
| a28061d | 合并悬浮工具栏，播放三键居中，整个工具栏统一避让屏幕边缘 |
| 281eae0 | 悬浮工具栏跟随独立主题、颜色、材质、圆角与透明度 |
| bdf4668 | 当前歌词按原文／简体／繁体显示，离线转换并保留原始数据 |
| 611d852 | 四种界面语言；去除主播放页和设置页顶部标题、小英文，保留标题栏 |
| bfcc9ad | 修复进度跳转录音校验；简繁选择移至底部并按条件显示；八项全局快捷键和冲突处理 |
| 441c37d | 软件内空格播放／暂停，保护输入框和控件原有操作 |

详细行为、用户请求与验证逐项对应见[全部更新索引](docs/updates-2026-11-11.md)。此前已有的资料库工作区、翻译任务、歌曲／专辑／歌单、实际队列、个人备份、宽歌词及 Apple Music 新导航适配全部保留。旧对话中已完成的请求不是新待办。

## 4. 构建与运行

普通联网开发：scripts/build.ps1、scripts/test.ps1；预览打包：scripts/publish-ui.ps1 -PreviewLabel next-fix。scripts/common.ps1 优先使用仓库 .tools/dotnet，否则使用 PATH 中的 dotnet。

首次离线恢复后，后续离线开发可用：

```powershell
. ./scripts/common.ps1
Invoke-Dotnet restore --locked-mode --source ./.tools/nuget-feed --packages ./.tools/nuget-packages -p:NuGetAudit=false
Invoke-Dotnet build -c Release --no-restore
Invoke-Dotnet test -c Release --no-build --no-restore
```

离线 NuGetAudit=false 仅避免联网审计请求，不改变锁文件，也不代表进行了最新漏洞审计。SDK、nupkg 保留各自许可；旧机用户 NuGet/Git 配置、凭据和 hooks 不随包复制。

旧交接包中的 app/LumaLyrics-1.1.0-ui-preview-space-shortcut-win-x64.zip 是当时已测预览。当前发布使用 LumaLyrics-1.2.0-portable-win-x64.zip 和 LumaLyrics-1.2.0-setup-win-x64.exe。完整解压后运行浮光歌词.exe，无需开发 SDK；旧版从托盘完全退出后再开新版。用版本、包名、提交与散列辨认，保留 v1.1.0 标签及原安装程序。

源 apphost 曾有启动停顿，保留 Microsoft host 回退：.tools/dotnet/dotnet.exe exec src/LumaLyrics.App/bin/Release/net10.0-windows10.0.19041.0/LumaLyrics.dll。运行包 runtime/dotnet.exe 只有启动用途，不是开发 SDK。

再次交接可运行 scripts/export-handoff.ps1，显式提供已测软件包、对应应用提交和新的输出目录。导出后先校验，再从 bundle 恢复验证。

## 5. 当前验证与保留限制

功能基线 Release 构建 0 警告／0 错误，单元测试 **558**（Core 134、Infrastructure 424）。空格真实输入和路由 **22**，运行包路由 **19**。此前快捷键提交四语运行包各 **111**（合计 444）、交互 66、译文／简繁 145、滑条与动效 124。各数字属于对应提交的实际验证，不能当作当前新版全部历史专项重跑结果。交接恢复检查另记在开发记录和包内 MIGRATION_CHECKS.md。

- 歌词程序时钟的排队延迟已修复并实机采样；“大部分歌曲”的逐句音频／人声对照尚未完成，不能保证所有歌词文件时间戳准确。
- Apple Music 播放中前跳 15 秒和回跳已获真实时间轴确认；**暂停时客户端可能延迟或回退进度**。保留未确认提示，不伪造进度、不自动播放、不重复提交。
- 应用四语已测，Apple Music 分别切成四种语言后的真实账户操作尚未逐一验证；Windows 文件对话框仍使用系统语言。
- 多屏跨 DPI、真实玻璃背景及新版本原生队列／专辑／歌单操作仍需现场核对。隔离 UI 和名称样例不证明真实播放成功。
- 无假名的纯汉字日文存在中文识别边界，可选“按原文”。转换只影响显示，不改原词、哈希、时间轴、偏移或译文来源。
- 空格真实输入采用隔离计数接收器，未在此项重复操作真实音乐或做长按现场测试；长按抑制由重复事件和并发保护实现。

新机隔离 UI 示例（已解锁交互桌面，数据在软件外）：

```powershell
. ./scripts/common.ps1
$lumaCheck = Join-Path $env:LOCALAPPDATA ('LumaLyrics-Checks/' + [guid]::NewGuid().ToString('N'))
$lumaDll = (Resolve-Path ./src/LumaLyrics.App/bin/Release/net10.0-windows10.0.19041.0/LumaLyrics.dll).Path
Invoke-Dotnet exec $lumaDll --data-dir "$lumaCheck/data" --verify-ui "$lumaCheck/report" --verify-local-space --exit-after-capture
Get-Content -LiteralPath "$lumaCheck/report/ui-check.json"
```

检查所有布尔结果及相关截图，不能仅看退出码。可按改动选用 --verify-interaction-updates、--verify-localization、--verify-translations、--verify-seek-sliders、--verify-overlay-transport、--verify-library-layout。带 --verify-local-space-input、--verify-seek-input、--verify-shortcut-input 的模式需要实际按键／鼠标输入，会等待完成。原生现场检查会操作真实客户端，应另行核对录音与队列，不为接手随意执行。

## 6. 代码地图与维护

主要入口位于 src/LumaLyrics.App/MainWindow.xaml*、PlaybackCoordinator.cs、SettingsWindow*。计时在 Core/PlaybackClock.cs；跳转相关为 Core/PlaybackSeeking.cs 和 App/SeekSlider.cs、AppleMusicPlaybackBridge*。悬浮相关是 App/OverlayWindow*、OverlaySurface*、GlassMaterialSurface.cs、DesktopGlassHost.cs。队列／封面见 App/PlaybackQueue*、AppleMusicQueue* 和 Infrastructure/Artwork*。

歌词简繁见 App/ChineseScriptDisplay.cs、Core/ChineseScriptVisibility.cs、Windows/ChineseScriptConverter.cs。界面语言见 Core/UiText.cs、Localization/*.json、App/LocExtension.cs。快捷键见 Core/KeyboardShortcuts.cs、ShortcutRegistry.cs、Infrastructure/ShortcutSettings.cs、App/Hotkeys.cs、MainWindowShortcuts.cs、HotkeySettingsPanel.cs、LocalPlaybackShortcut.cs。

资料库与任务见 LibraryWorkspace*、TranslationJob*、TranslationBatchExchange.cs；个人资料见 PersonalLibrary*、DataDirectoryConfiguration.cs。完整产品约束在 AGENTS.md，原生精确匹配、订阅释放、应用与悬浮主题独立、手动绑定和来源保护均须保留。

语言审计辅助源码随 toolchain/locale-audit-tool 提供，恢复后在 .tools/locale-audit-tool。执行 .tools/dotnet/dotnet.exe run --project .tools/locale-audit-tool/Tool.csproj -- --hant 可审计英日缺键并重建繁体资源；检查生成差异后提交。该工具不是产品运行依赖。

## 7. 个人资料另行迁移

沿用此前“只迁移开发工作，个人资料另行备份”的约定。本包不扫描或包含真实歌词、agent 译文、封面、个人设置、音乐库、私人诊断截图、Apple 登录或账号凭据。

需要时按[个人资料说明](docs/personal-library.md)在设置导出一致备份，不能只复制 SQLite 主文件遗漏 WAL。新机恢复到全新目录，再选择“下次启动使用其他资料目录”，完全退出后重启。核对绑定、偏移、译文、外观和封面；旧绝对路径关联的未完成翻译任务需重新生成入口。Apple Music 由用户在新机安装、登录并定位自己的资料库。

## 8. 给新电脑 Codex 的开场提示词

> 请接手浮光歌词 Windows 项目。先读 AGENTS.md、CODEX_HANDOFF.md、docs/updates-2026-11-11.md、docs/development-log.md 和 release-notes.md；核对 manifest、HEAD、工作区和 SDK。只有迁移包时先校验，再用 Restore-Workspace.ps1 离线恢复到新目录。当前功能基线为 441c37d，本轮界面、悬浮、简繁／四语、进度和快捷键请求已实现。保留已记录限制，不把旧机结果当作新机结果。按当前任务继续开发；相关验证使用隔离目录，完成后记录实际结果并提交本地 Git，不进行 GitHub 同步。个人资料另行迁移。
