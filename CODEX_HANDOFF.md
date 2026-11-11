# 浮光歌词 · 换机开发接手说明

更新日期：2026-11-11。适用于另一台 Windows 电脑上的 Codex 或其他本地开发 agent。本文替换旧的 2026-10-25 接手状态；旧说明仍可从 Git 历史查看。

## 1. 当前状态与本次交接范围

| 项目 | 交接基线 |
| --- | --- |
| 仓库 | https://github.com/jackie9449/LumaLyrics |
| 分支 | main |
| 本文更新前的应用源码基线 | 2a5524269abda90fb040569d4465e1e16cad0e96 |
| 远端核对 | 编写前已 fetch；本地与 origin/main 同为上述提交，工作区干净 |
| 当前应用版本 | Directory.Build.props 中为 1.1.0 |
| 最近正式版本 | v1.1.0；正式发布的源码基线为 5b711a0 |
| 最新已测运行包 | LumaLyrics-1.1.0-ui-preview-overlay-native-update-win-x64.zip |
| 运行包对应应用源码 | 2a5524269abda90fb040569d4465e1e16cad0e96；当前交接仅更新文档 |
| 技术栈 | C# 14、.NET 10、WPF、WinForms 托盘、SQLite、GSMTC、FlaUI/UI Automation |
| 系统目标 | Windows 10 2004 / build 19041 及以上或 Windows 11，x64 |
| 开发 SDK | global.json 锁定 10.0.402，rollForward 为 latestPatch |

本次用户明确选择：**只迁移开发工作，个人资料另行备份。** 工作包不含个人歌词、agent 译文、封面、设置、真实音乐库、账号凭据或私人现场诊断。无需为了接手扫描用户个人资料。

本文会产生一个新的文档提交，工作包 manifest.json 记录其准确 SHA；恢复时保留此文档提交，不要把仓库退回上面的应用源码 SHA。GitHub 在交接后可能继续更新，开始工作前重新核对。

当前没有本轮尚未完成的产品开发指令。本轮完成文档和打包后，下一位 agent 应等待用户下一项具体需求；下文“尚未验证”表示验证边界，不是自动改功能或发布新版本的授权。

## 2. 工作包包含什么

工作包顶层 START_HERE.md 是使用入口，说明适用于该次实际打包的文件和校验信息：

- source/：交接提交的完整 Git 跟踪文件快照，含源码、锁文件、脚本、AGENTS.md、本文件、说明、许可与公开验证材料。它不含 .git，适合先阅读；推荐通过 bundle 恢复 Git 仓库后开发。
- history/LumaLyrics.bundle：main 的完整可达提交历史及 v1.0.0、v1.1.0 标签，可离线克隆。不是复制旧机 .git/config，不携带凭据、用户配置、hooks 或无关远端分支。
- app/：最新已测自包含程序 ZIP 及原 SHA-256 文件。完整解压后运行浮光歌词.exe，不需要开发 SDK；不要只取一个 EXE 或 DLL。
- scripts/：工作包校验、离线恢复 Git 仓库及隔离 UI 验证助手。先读脚本再执行。
- manifest.json：源码提交、树、运行包基线、文件尺寸和 SHA-256。外层 ZIP 另有 .sha256。

不携带 .tools、bin、obj、TestResults、NuGet 缓存、旧预览、私人 QA、Apple 登录或 GitHub 凭据。**离线可以恢复仓库和运行现成程序；首次重新编译仍需另装匹配 SDK、联网恢复 NuGet，或自行准备对应离线开发依赖。** 不要把“带了运行时”误认为“带了 SDK”。

## 3. 新电脑先做这几步

1. 把整个外层 ZIP 和旁边的 .sha256 带到新电脑，校验 ZIP 散列，再完整解压到新目录。
2. 阅读 START_HERE.md，运行 scripts/Verify-Handoff.ps1 核对工作包内所有文件。
3. 使用 scripts/Restore-Repository.ps1 恢复到一个尚不存在的开发目录。不要覆盖已有仓库。助手把 origin 指回 GitHub，不自动联网拉取、不执行个人数据恢复。
4. 打开恢复后的仓库，先读 AGENTS.md，再读本文件、README.md 和 docs/release-notes.md。
5. 检查工作区和远端，安装 SDK，先构建与单元测试，再按下一项任务选择界面验证。

手动恢复的等价示例（先在解压后的工作包顶层打开 PowerShell；D: 路径可自行更换）：

~~~powershell
$handoffRoot = (Get-Location).Path
$lumaDestination = 'D:\Work\LumaLyrics'
if (Test-Path -LiteralPath $lumaDestination) { throw '目标已存在，请选择新的目录。' }
git clone --branch main (Join-Path $handoffRoot 'history/LumaLyrics.bundle') $lumaDestination
if ($LASTEXITCODE -ne 0) { throw '克隆失败，保留现场，不要覆盖其它目录。' }
git -C $lumaDestination remote set-url origin https://github.com/jackie9449/LumaLyrics.git
Set-Location -LiteralPath $lumaDestination
git status --short
git log -5 --oneline
git fsck --full
~~~

如果不使用工作包，可直接从 GitHub 克隆，但必须确保已取得不早于本次交接提交的源码。联网后按下列顺序检查：

~~~powershell
git remote -v
git branch --show-current
git status --short
git fetch origin --prune
git log --oneline HEAD..origin/main
git log --oneline origin/main..HEAD
~~~

远端领先且本地无修改、无分叉时，可 git pull --ff-only。出现分叉或用户改动时先检查再整合；不得 reset --hard、force-push 或覆盖新提交。离线时记录无法 fetch，继续本地阅读和验证，不把离线快照称作当前远端最新。

GitHub 登录在新机重新配置，不复制旧机令牌或凭据存储。AGENTS.md 已持续授权请求范围内的普通提交与推送；直推受保护时走功能分支和 PR，不重复询问同一授权。

## 4. 开发环境、构建与启动

安装 Windows x64 Git、PowerShell（5.1 或 7）和与 global.json 匹配的 .NET SDK 10.0.402 / 同 feature band 允许的补丁。只有 Runtime 不能编译。普通开发不要求 Visual Studio IDE。

所有项目命令先经过 scripts/common.ps1：优先使用 .tools/dotnet/dotnet.exe，没有时使用 PATH 中的 dotnet。新电脑没有旧机 .tools 是正常情况。

~~~powershell
. ./scripts/common.ps1
Invoke-Dotnet --version
./scripts/build.ps1
./scripts/test.ps1
~~~

build.ps1 执行 locked-mode restore 和 Release build；test.ps1 执行测试并写入被 Git 排除的 TestResults。不要仅为绕过环境问题升级 SDK、依赖、重写 packages.lock.json 或改 global.json。SDK 不匹配先修环境。

编译后主程序路径为 src/LumaLyrics.App/bin/Release/net10.0-windows10.0.19041.0/LumaLyrics.dll。普通启动会按设置连接媒体和进行后台准备，不能用它冒充离线 UI 检查。

现成运行包使用浮光歌词.exe；Launch.vbs 是兼容入口。源 apphost 曾有启动停顿，保留 Microsoft host 回退：

~~~powershell
# 在恢复的源码仓库内；仅在确实要普通运行时使用
. ./scripts/common.ps1
$lumaProfile = Join-Path $env:LOCALAPPDATA ('LumaLyrics-Dev/' + [guid]::NewGuid().ToString('N'))
Invoke-Dotnet exec ./src/LumaLyrics.App/bin/Release/net10.0-windows10.0.19041.0/LumaLyrics.dll --data-dir $lumaProfile
~~~

真正测试音乐同步还需在新机安装 Apple Music，由用户自行登录和准备资料库。记录实际客户端版本、系统和缩放比例，不假设与旧机相同。不要为接手而自动终止音乐客户端或改变用户正在播放的内容。

## 5. 已完成的近期变化

| 应用提交 | 已完成行为 |
| --- | --- |
| 5b711a0 / v1.1.0 | 资料库与歌词管理、批量本地 agent 翻译、原词搜索改进、歌单及进度控制、安装与便携分发 |
| 360b6b8 | 最大化及隐藏队列后歌词使用面板可用宽度，取消固定 820 的正文上限 |
| 35b7978 | “有中文译文”筛选；“重新生成翻译任务…”入口，旧译文继续使用，新结果按预期修订校验后接收 |
| 2a55242 | 悬浮横向居中、独立主题配色、底部播放条位置、高 DPI 弹窗排列、工作区约束；新版 Apple Music 折叠资料库导航适配 |

1.1.0 正式资产早于最后三项修复，不能用 GitHub 原 v1.1.0 安装包代替当前交接运行包，也不要擅自覆盖其标签或发布资产。当前尚未为这些后续改动递增产品版本。

资料库与歌词统一在设置内，总览、歌曲、任务、显示四页。音乐库选歌/专辑管理进入同一设置面板，不能重建第二套窗口。新专辑同步与大库操作、页签及宽度修复已经完成，旧对话中的这些请求不是未实现待办。

## 6. 代码与文档地图

以下路径相对仓库根目录：

| 责任 | 主要文件 |
| --- | --- |
| 主窗口与状态协调 | src/LumaLyrics.App/MainWindow.xaml*、PlaybackCoordinator.cs、App.xaml.cs |
| 设置资料库四页 | App/LibraryWorkspacePanel.cs、LibraryWorkspaceUi.cs、PlaybackLibraryWorkspace.cs；Infrastructure/LibraryWorkspaceCatalog.cs、LibraryWorkspaceModels.cs |
| 音乐库/专辑浏览 | App/MusicLibraryView.cs、MusicLibraryAlbumView.cs、MusicLibraryNavigation.cs；Infrastructure/MusicLibraryCatalog.cs、MusicLibraryAlbums.cs |
| 本地翻译任务/批次 | Infrastructure/TranslationJob*.cs、TranslationBatchExchange.cs、Resources/LocalAgentTranslationBatch.zh-CN.md；App/TranslationJobListPanel.cs |
| 原词及译文 | Infrastructure/LibraryPrefetcher.cs、TranslationPreparation.cs、TranslationRepository.cs、TranslationDraftExchange.cs；Core 的时间轴及对齐逻辑 |
| 悬浮工具栏与边界 | App/OverlayWindow.xaml*、OverlayWindow.Geometry.cs；Infrastructure/OverlayScreenLayout.cs；tests/LumaLyrics.Infrastructure.Tests/OverlayScreenLayoutTests.cs |
| 悬浮材质与独立外观 | App/OverlaySurface.xaml*、DesktopGlassHost.cs、GlassMaterialSurface.cs、ThemeManager.cs；Infrastructure/OverlayAppearance.cs、OverlayThemeSession.cs |
| 原生选歌、专辑、窗口归属 | App/AppleMusicLibraryHandoff.cs、AppleMusicAlbumHandoff.cs、AppleMusicHandoffWindow.cs、AppleMusicPlaybackBridge.cs |
| 歌单及实际队列 | App/AppleMusicPlaylistHandoff.cs、AppleMusicQueue*.cs、MusicLibraryPlaylist*.cs、PlaybackQueue*.cs |
| Apple Music 本机原词 | App/AppleOriginalLyrics.cs；Windows/AppleMusicMemoryLyrics.cs；Core/AppleTimedLyrics.cs |
| 个人导出及位置 | Infrastructure/PersonalLibrary*.cs、DataDirectoryConfiguration.cs；App/PersonalLibraryPanel.cs |
| 测试、原生诊断 | tests/；App/*Verification.cs；tools/LumaLyrics.Probe |
| 启动与分发 | launcher/、installer/、scripts/publish*.ps1、scripts/verify-distribution.ps1、licenses/ |

表内 App/、Infrastructure/、Core/、Windows/ 分别指 src/LumaLyrics.App/、src/LumaLyrics.Infrastructure/、src/LumaLyrics.Core/、src/LumaLyrics.Windows/。

先读 AGENTS.md 中完整约束，尤其保留：
- 手动绑定、中文原词哈希与分组、历史修订、真实 AI 来源；不伪造时间戳、不借另一录音时间轴、不把缺词认作纯音乐。
- 应用主题与实际悬浮外观独立，外观编辑器 chrome 跟随应用；主窗与悬浮译文开关分别记忆。
- 浮窗圆角比例、玻璃宿主、点击穿透、锁定/纯音乐/编辑时控件抑制，以及动效、订阅和时钟清理。
- 原生选歌仅操作准确的 FilterTextBox；最新请求取消、一次命令、录音和队列顺序确认、拥有者最小化/恢复均不能删除。
- 后台读取不恢复原生窗口；UIA 超时不代表底层调用已经结束，不能堆叠失控读取。
- 乐器识别实验已撤销。Git 历史存在旧实现和部分工具说明，不据此恢复为当前产品功能。

## 7. 验证基线与在新机复核

以下是先前旧电脑的结果，**不是本次交接重新跑出的产品验收，也不能当作新电脑结果**：

| 范围 | 已记录结果 |
| --- | --- |
| 最新应用单元测试 | 469/469：Core 92、Infrastructure 377 |
| 最新独立运行包专项 | 535/535：悬浮控制 45、外观调整 45、移动 8、隔离 156、玻璃真实帧 62、侧栏/原生就绪 104、资料库工作区 115 |
| v1.1.0 正式版历史全量 WPF | 1707/1707；它是发布时版本结果，不是当前修复版全量重跑 |
| 分发边界 | 2 个软件样例和 69 类私人文件拒绝案例 |
| 最新运行包 ZIP | 866 个文件集合、逐文件散列与 SHA-256 sidecar 已核对 |
| 实机单曲选播 | Apple Music 1.1792.39682.0，150% 缩放；展开准确资料库组、选本地歌曲、唯一录音确认、原筛选恢复、确认后最小化、恢复暂停 |

详细依据：[最新悬浮与原生适配](docs/verification-overlay-native-update/README.md)、[已有中文筛选](docs/verification-existing-chinese/README.md)、[宽歌词](docs/verification-wide-lyrics/README.md)、[1.1.0 发布](docs/verification-v110/README.md)。更早验证有失败记录，不能用最近专项通过数字抹去历史或声称全量全绿。

本次新版 Apple Music 尚未逐项实测整张专辑、歌单续播、插播、队列跳转及拖动。一次被动队列读取没有得到可用基线，因此没有为追加测试改动未知队列。不能把单曲成功推广到所有原生操作。跨 DPI、多显示器及新机玻璃效果仍须现场确认，原生调用成功本身不证明实际模糊和裁角正确。

新机 UI 验证必须在已解锁、可交互桌面，用软件目录外新建的 --data-dir；不要复用真实个人库，不启动原生实机模式。示例：

~~~powershell
. ./scripts/common.ps1
$lumaCheckRoot = Join-Path $env:LOCALAPPDATA ('LumaLyrics-Checks/' + [guid]::NewGuid().ToString('N'))
$lumaAppDll = (Resolve-Path ./src/LumaLyrics.App/bin/Release/net10.0-windows10.0.19041.0/LumaLyrics.dll).Path
Invoke-Dotnet exec $lumaAppDll --data-dir "$lumaCheckRoot/data" --verify-ui "$lumaCheckRoot/report" --verify-overlay-transport --exit-after-capture
Get-Content -LiteralPath "$lumaCheckRoot/report/ui-check.json"
~~~

必须检查报告中布尔失败项和截图，不仅检查退出码。工作包 scripts/Verify-Ui.ps1 会逐项解析结果。相关专项参数：
- --verify-overlay-transport、--verify-overlay-adjustments、--verify-overlay-motion、--verify-overlay-isolation、--verify-glass-backdrop。
- --verify-playback-sidebar、--verify-library-workspace、--verify-library-layout、--verify-translations。
- 完整回归不加专项参数，仅保留 --verify-ui 和 --exit-after-capture；选择与改动相关的验证，不为纯文档修改重复全套 UI。

--verify-ui 使用原创隔离样例，不证明真实 Apple Music。显式 --verify-native-library-playback 可能操作真实客户端，仅针对用户实际任务、有准确录音和队列基线时执行；报告留在个人目录，不写入仓库、公开 Issue 或软件包。

## 8. 个人资料另行迁移

此工作包不会恢复个人歌词和译文。若之后迁移，按 [个人资料说明](docs/personal-library.md) 操作：
1. 旧机设置 → 个人资料，核对实际使用目录，导出新的个人备份 ZIP；不要只复制 SQLite 主文件而遗漏 WAL。
2. 新机使用当前新版恢复到一个全新目录。
3. 仅“从 ZIP 恢复”不等于启用：再点“下次启动使用其他资料目录”，选恢复结果，从托盘完全退出后重新启动。
4. 复核已有 agent 中文译文、手动绑定、偏移、外观和封面；旧绝对路径的未完成翻译任务需重新生成入口。
5. Apple Music 音乐资料库由新机重新定位，Git 克隆不会带回收藏和媒体文件。

不要把个人备份放入仓库、软件目录、GitHub 或本开发工作包。不要在旧路径不可达时静默创建空资料，也不要覆盖已有个人目录。

## 9. 后续打包、提交与授权边界

~~~powershell
# 在新仓库，采用一个新的预览标签，不覆盖正式资产
./scripts/publish-ui.ps1 -PreviewLabel next-fix
./scripts/test-distribution.ps1
./scripts/verify-distribution.ps1 -PackageDirectory ./artifacts/LumaLyrics-1.1.0-ui-preview-next-fix-win-x64
~~~

正式便携包入口为 scripts/publish.ps1；只有用户提出发布需求时执行正式发布。安装包需 Inno Setup 6.7+ 和 scripts/publish-installer.ps1；普通开发无需安装该工具。打包脚本依赖 SDK 的 host/fxr、Roslyn 和 Windows 的 .NET Framework reference 文件，不能拿现成运行包 runtime/dotnet.exe 当开发 SDK。

产品变更后按范围构建、测试、更新说明，排除 .tools、生成物和个人内容，使用 conventional commit 并同步 jackie9449/LumaLyrics；不 force-push。复核远端提交和源码树，报告实际 SHA 或 PR 链接。

先前合规讨论只是历史分析和待决策建议，没有授权本次停用原词来源、自动下架 Release、改变项目许可或宣称整改已经完成。如用户另提该任务，再结合当时现行资料专项核查；旧记录可从 Git 历史中的 2026-10-25 CODEX_HANDOFF.md 查看。

## 10. 给新电脑 agent 的开场提示词

> 请接手浮光歌词 Windows 项目。完整阅读 AGENTS.md、CODEX_HANDOFF.md、README.md 和最新 release notes；若从工作包开始，先读 START_HERE.md、校验文件，并从 Git bundle 恢复到新目录。核对 manifest 中提交、本地改动、origin/main、global.json 和 1.1.0 后续修复，不回退、不覆盖用户文件。配置 scripts/common.ps1 选择的 SDK，构建与测试；UI 检查使用软件目录外全新 --data-dir。旧机验证数字只作基线，原生单曲成功不代表专辑或队列均已实测。个人资料不在开发包中，不自动扫描或迁移。此次交接没有新增产品任务，请根据我接下来的具体需求继续；按 AGENTS.md 保存行为、记录真实验证和限制，提交同步 GitHub 并核对远端。不得自动发布、恢复已撤销的乐器实验或实施旧合规建议。
