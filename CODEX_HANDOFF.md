# 浮光歌词 · Codex 换机接手说明

更新日期：2026-10-25。供另一台 Windows 电脑上的 Codex 接手项目使用。

本文记录当前代码事实、历史验证结果及尚未实施的建议，不代表全功能验收或正式法律意见。开始工作前必须重新检查仓库和目标电脑，不能把旧电脑上的通过结果当作新电脑的结果。

## 1. 先了解当前状态

| 项目 | 状态 |
| --- | --- |
| 项目 | 浮光歌词 / LumaLyrics，Windows Apple Music 桌面歌词应用 |
| GitHub | https://github.com/jackie9449/LumaLyrics |
| 当前分支 | `main` |
| 本文编写前的远端与本地基线 | `8fa8a8cb3290533293a65928c53ca346d80c747d`，已 fetch 核对一致，工作区干净 |
| 1.0.0 应用代码提交 | `9e12ffa3f5cb3f9cce571d7e15b05a255b0ba17b` |
| 最近公开版本 | [v1.0.0](https://github.com/jackie9449/LumaLyrics/releases/tag/v1.0.0)，含安装版、便携版与校验文件 |
| 技术栈 | C# 14、WPF / WinForms 托盘、.NET 10、SQLite、Windows GSMTC、UI Automation |
| 系统目标 | Windows 10 2004 / build 19041 及以上、Windows 11，x64 |
| 开发 SDK | `global.json` 锁定 `10.0.402`，`latestPatch` |

注意：本文自身会另有一个文档提交；上面的 SHA 是本次交接前的基线，接手时以 `git log` 和远端实况为准。不要回退到该 SHA，也不要覆盖后续提交。

用户最后完成的功能需求是：未锁定悬浮歌词悬停显示下方上一曲 / 播放暂停 / 下一曲按钮；制作自动安装及中文桌面快捷方式；发布 1.0.0 安装版、便携版；更新 README。上述已实现并发布，验证限制见第 6 节。

随后用户要求分析合规风险，已经完成代码与官方资料的初步分析，**没有修改功能、下架 Release 或实施合规整改**。本次请求仅是生成换机接手说明。新的 Codex 应等待用户的后续开发指令，不把风险建议当成已经授权执行的整改方案。

## 2. 第一轮接手顺序

1. 完整阅读根目录 [AGENTS.md](AGENTS.md)，它包含持续有效的产品约束和 GitHub 同步要求。
2. 阅读 [README](README.md)、[更新记录](docs/release-notes.md)、[1.0.0 验证记录](docs/verification-v100/README.md)。`docs/progress.md` 主要是早期历史，不是最新待办清单。
3. 检查工作区、分支、远端及新提交，保留用户已有改动：

   ```powershell
   git status --short
   git remote -v
   git branch --show-current
   git fetch origin --prune
   git log -5 --oneline
   git log --oneline HEAD..origin/main
   git log --oneline origin/main..HEAD
   ```

4. 配置第 3 节的环境，先构建和运行单元测试，再以隔离数据目录运行必要的界面检查。
5. 如果要处理个人歌词，先完成第 4 节的数据迁移和备份。不要对真实个人库运行界面验证。
6. 做用户当前要求的工作，记录实际验证结果，按 AGENTS.md 提交、同步并复核远端。不要 force-push；远端有新提交先整合，分支保护不允许直推时创建 PR。

## 3. 新电脑准备环境与运行方法

### 开发环境

- Windows x64、Git、可访问 NuGet 的网络环境，以及 .NET SDK 10.0.402。
- `.tools/` 不在 Git 中，克隆后没有旧电脑的 SDK、Inno 编译器和其他本地工具是正常现象。
- 所有开发命令优先使用 `scripts/common.ps1`：有 `.tools/dotnet/dotnet.exe` 时使用本地 SDK，否则使用 PATH 中的 dotnet。
- 构建不要求 Visual Studio IDE；WPF 图形验证要求 Windows 上可交互且已解锁的桌面。
- 测试真实音乐同步时，另装 Apple Music for Windows，由用户自行登录并播放；记录该机的客户端版本、系统版本和 DPI。GitHub 凭据和 Apple 登录状态不能从本文恢复，不要复制旧机凭据。
- 只有生成安装包才需要 Inno Setup 6.7+；1.0.0 使用过 6.7.3。无需为普通编译先安装整个打包环境。

如果尚未克隆，可执行下列示例；目标目录已存在时先检查，不要覆盖：

```powershell
git clone https://github.com/jackie9449/LumaLyrics.git D:/LumaLyrics
Set-Location D:/LumaLyrics
. ./scripts/common.ps1
Invoke-Dotnet --info
./scripts/build.ps1
./scripts/test.ps1
```

`build.ps1` 运行 locked-mode restore，再构建 Release。不要仅为绕过环境问题随意升级 SDK、NuGet 依赖或重写锁文件。SDK 未找到时先配置实际工具路径。

### 使用隔离目录运行 UI 验证

下面的路径不依赖旧机用户名，也不使用真实个人资料：

```powershell
. ./scripts/common.ps1
$lumaCheckRoot = Join-Path $env:LOCALAPPDATA ('LumaLyrics-Checks/' + [guid]::NewGuid().ToString('N'))
$lumaAppDll = './src/LumaLyrics.App/bin/Release/net10.0-windows10.0.19041.0/LumaLyrics.dll'

# 完整界面回归；应检查输出报告，不能只看进程是否退出。
Invoke-Dotnet exec $lumaAppDll --data-dir "$lumaCheckRoot/data" --verify-ui "$lumaCheckRoot/report" --exit-after-capture
```

若本轮仅修改悬浮播放控件，可在同类命令中增加 `--verify-overlay-transport`；六主题专项使用 `--verify-theme-collection`。每次验证建立新的外部目录，按改动选择相关检查，无需为纯文档修改运行全套图形测试。

`--verify-ui` 使用原创样例，不启动真实媒体连接或网络预缓存。通过它不能证明真实 Apple Music 点播、队列、TTML 或玻璃合成在新电脑上可用。

普通启动会依照当前设置连接媒体及执行后台准备。若仅需离线 UI 检查，应使用验证模式，不要把普通启动当成隔离测试。此前自动化启动原生 apphost 曾有停顿，未确认根因；可使用上面的 `dotnet exec`，或完整运行包中的 Microsoft host：

```powershell
# 在完整解压的运行包目录中执行
./runtime/dotnet.exe exec ./LumaLyrics.dll --data-dir 'D:/LumaLyrics-Private/TestProfile'
```

发行版首选 `浮光歌词.exe`，它通过随包 Microsoft host 启动；`Launch.vbs` 是兼容入口。不要只拷贝一个 EXE 或 DLL，运行包依赖同目录的完整文件。

### 打包入口

```powershell
./scripts/publish.ps1
./scripts/publish-installer.ps1 -Compiler 'C:/Program Files (x86)/Inno Setup 6/ISCC.exe'
```

版本由 `Directory.Build.props` 决定，目前为 1.0.0。输出位于 `artifacts/`，不提交 Git。`publish.ps1` 生成正式便携包，`publish-ui.ps1` 默认生成预览包，安装脚本复用同一便携目录。

相关验证脚本：`scripts/test-distribution.ps1`、`verify-distribution.ps1`、`verify-release.ps1`、`test-installer.ps1`。运行前阅读参数；安装测试会操作当前用户的安装注册和快捷方式，仅在干净用户环境运行，它会拒绝覆盖现有正式安装。`-FullUi` 执行完整图形检查，`-SkipUi` 仅检查安装及文件。不要为完成交接重新发布或覆盖已有 1.0.0 资产。

## 4. 换电脑必须单独迁移个人资料

**Git 克隆和下载安装包都不会带回个人歌词、译文、封面、手动绑定、偏移及准备队列。**

旧机默认数据目录是 `%LOCALAPPDATA%/LumaLyrics`，但用户可能选了其他位置。先在旧机「设置 → 个人资料」查看当前实际目录，不能只凭默认路径推断。

推荐流程：

1. 在旧机「设置 → 个人资料」导出新的个人备份 ZIP（`.lumalibrary.zip`）或个人文件夹，单独放到软件目录之外。
2. 通过用户自己的私人传输方式带到新机，不上传 GitHub、Release、公开 Issue 或公共网盘。
3. 在新机通过同一页面恢复到一个新的目录，确认备份校验通过，再选择「下次启动使用其他资料目录」，退出并重启。
4. 检查代表歌曲的手动绑定、中文译文、偏移、主题及封面。旧机器的绝对来源路径可能失效，Apple Music 资料库应重新定位并核验成员范围，不覆盖旧的手动内容。
5. 确认恢复成功前保留旧机原始目录和导出文件。不覆盖已有个人目录，也不要在位置失效时静默新建空库。

导出含一致数据库快照和可读 `lyrics/`、`covers/`、`translations/`、`originals/`。包含个人原创或第三方版权内容，不能混入软件分发。机器本地的 `data-location.json` 指针不随备份迁移，需要在新机重新设置。

不要直接在运行中只复制 SQLite 主 `.db` 而忽略 WAL。优先用应用的在线快照导出入口。详细命令与恢复格式见 [个人资料说明](docs/personal-library.md)。

旧机另有一份私人《歌词补全接手说明》（2026-10-24 版本，保存在桌面，非 Git 文件）。如需接着补词，须由用户另行携带该文档和最新个人备份；本文不复制其中的私人曲目、进度数字或歌词。旧清单可能落后于当前库，恢复后重新统计。

## 5. 当前功能和代码地图

| 区域 | 入口与说明 |
| --- | --- |
| 播放、窗口、状态协调 | `src/LumaLyrics.App/MainWindow.xaml*`、`PlaybackCoordinator.cs`、`App.xaml.cs` |
| 主应用六主题与交互 | `ThemeManager.cs`、`ThemeDesign.cs`、`ThemeIdentity.cs`、`AppMotion.cs`、`SettingsWindow.cs`；[主题说明](docs/ui-themes.md) |
| 悬浮歌词与玻璃 | `OverlayWindow.xaml*`、`OverlaySurface.xaml*`、`DesktopGlassHost.cs`、`GlassMaterialSurface.cs`；[材质说明](docs/glass-materials.md) |
| 独立悬浮主题草稿 | Infrastructure 中 `OverlayAppearance.cs`、`OverlayThemeSession.cs`；[主题记忆](docs/theme-memory.md) |
| Apple Music 原词 | App 中 `AppleOriginalLyrics.cs`，Windows 中 `AppleMusicMemoryLyrics.cs`，Core 中 `AppleTimedLyrics.cs`；[原词通道说明](docs/apple-original-lyrics.md) |
| 网络原词与译文 | Infrastructure 中 `LrclibProvider.cs`、`NetEaseTranslationProvider.cs`、`LibraryPrefetcher.cs`、`TranslationPreparation.cs`、`TranslationRepository.cs`、`TranslationDraftExchange.cs` |
| 个人资料库与封面 | Infrastructure 中 `AppleMusicLibraryReader.cs`、`MusicLibraryCatalog.cs`、`LibraryArtworkImporter.cs`；[音乐库说明](docs/music-library.md) |
| 原生点播和队列 | App 中 `AppleMusicLibraryHandoff.cs`、`AppleMusicAlbumHandoff.cs`、`AppleMusicQueue*.cs`、`PlaybackQueue*.cs` |
| 个人导出与路径 | Infrastructure 中 `PersonalLibraryArchive.cs`、`PersonalLibrarySnapshot.cs`、`DataDirectoryConfiguration.cs` |
| 调试工具与测试 | `tools/LumaLyrics.Probe`、`tests/LumaLyrics.Core.Tests`、`tests/LumaLyrics.Infrastructure.Tests`，App 内各 `*Verification.cs` |
| 分发 | `launcher/`、`installer/`、`scripts/publish*.ps1`、`THIRD-PARTY-NOTICES.md`、`licenses/` |

### 不要破坏的已确认需求

- 主应用现有六主题：液态玻璃、雾白、柔塑、纸间、夜航、构成，差异包括排版、形状、封面装帧及导航，不能退回只改颜色。主题选择只在设置里。
- 主窗口、音乐库、设置、嵌入外观编辑器的控件及业务弹窗跟随主应用主题；实际悬浮窗、上下工具栏、歌词预览独立。切换悬浮主题不能使设置页出现明暗割裂。
- 左栏仅保留当前歌词、音乐浏览、设置、诊断；资料库缓存和悬浮外观统一在设置内，不能再弹第二套管理窗口。
- 主窗布局实时适应窗口大小，宽布局封面保持正方形，队列限制在滚动视口内。保留右下角队列及中文译文开关。
- 主窗与悬浮译文分别记忆，默认均关闭。悬浮上方工具栏同步悬浮译文开关。
- 未锁定悬浮悬停显示下方播放按钮；锁定、拖动、隐藏、外观编辑和纯音乐时关闭快捷条。锁定保持点击穿透。
- 原词手动绑定优先。只有检查通过的动态歌词可自动导入，不能伪造时间戳或把缺词认作纯音乐。
- 译文绑定精确原词 SHA-256 及分组索引，不能借其他录音的时间轴。保留手动译文和 AI 来源；不显示待审徽标，不等于把草稿标成已审核。
- 资料库只包含原生主清单确认的成员，不能将整个本地曲目缓存表当成用户收藏。
- 原生选歌、整专辑、插播、队列跳转及拖动须维持请求归属、取消、实际录音与顺序确认。不要用重复 Next 或普通库播放冒充精确队列操作。
- 后台读取不恢复 Apple Music 窗口，不自动打开歌词或播放列表；主动操作按已有确认与最小化规则处理。
- 托盘从嵌入资源加载图标，检查实际 Shell 注册；不能将 NotifyIcon.Visible 当成已固定到通知区域。
- 动效须兼容减少动画和系统偏好，保留中断连续性、焦点语义、时钟与订阅清理。更完整的约束以 AGENTS.md 为准。

### 歌词获取现状

通常先保留手动绑定和可靠缓存；需要获取时按偏好尝试 Apple Music 本机原词，再使用网络来源。手动重新查词仍不能覆盖锁定的手动绑定。

本机通道要求用户已打开当前歌曲的动态歌词，核对原生文字及录音后，才只读扫描 Apple Music 内存中的 TTML，再次核对当前状态并验证时间模型；不是官方全库接口，不保证每首可用。现有限额为 384 MiB / 10 秒 / 100000 个区域。完整来源保存在个人库，不能提交 Git。

自动原词和队列读取共用受限的 MTA UIA 连接。RPC 超时不等于底层调用终止；实际读取结束前不叠加自动读取，超时或无响应会暂停该进程生命周期内的自动访问。重启 Apple Music 后重新判断，不能宣称卡顿根因已经彻底解决。用户曾允许旧机诊断时重启 Apple Music，新机操作前仍应检查当前工作及会话状态，不盲目杀进程。

网络来源主要为 LRCLIB 原词、网易云原词与已有社区译文，含元数据匹配、限流和回退。AI 译稿交换入口本身不执行云端翻译，也不代表全库译文已完成语义审核。继续私人补词时读 [中文译文说明](docs/chinese-translations.md)，先核对原词版本，再进行准确、自然、符合创作语境的翻译；不确定处留待确认，不覆盖手动译文。

## 6. 验证基线与已知限制

以下均是旧电脑的历史结果，本次文档编写没有重跑产品测试：

| 项目 | 1.0.0 记录 |
| --- | --- |
| Release 构建 | 零警告、零错误 |
| 单元测试 | 337 通过：Core 78、Infrastructure 259 |
| 悬浮播放专项 | 最终源码 28/28；最终安装后完整检查中的专项同为 28/28 |
| 完整源码 WPF 回归 | 1494/1535，41 项未通过 |
| 完整安装后 WPF 回归 | 1496/1535，39 项未通过 |
| 安装及分发 | 文件一致、启动、重复安装、中文桌面快捷方式、卸载、保留外部个人样例及 ZIP/校验文件核对通过；完整安装检查整体因 UI 未全过而返回失败 |

未通过项主要是动画插值、完成时钟、键盘焦点、玻璃淡出；安装后另有既有减少动画检查失败。旧 0.18.0 对照也出现同类问题，但不能据此全部归因于环境、忽略断言或宣称全绿。

旧机图像捕获曾连续超时，用户解锁并保持亮屏后复验仍有失败。新机应在可交互桌面上复现，区分真实产品缺陷、测试采样和环境问题；保留失败报告，不放宽阈值掩盖问题。玻璃效果须观察连续可见帧、保留场景色与四角裁切，不能仅以原生 API 返回成功验收。

旧机测试环境为 Windows 10、100% 缩放；不同 DPI、多显示器、Windows 11 及新 Apple Music 版本需要各自确认。专项播放使用可注入发送委托，不等于已向真实 Apple Music 逐项发送并验证播放命令。完整材料见 [1.0.0 验证记录](docs/verification-v100/README.md)，不要用更早版本的通过数字替代。

## 7. 最新合规分析：建议尚未实施

用户问过“这个软件有没有合规性风险”。当前结论是存在需优先处理的风险，不能宣称已经完全合规；没有因此改变程序或发布状态。

| 事项 | 已确认事实 | 后续建议 / 判断边界 |
| --- | --- | --- |
| 内存取词，优先级高 | `AppleMusicMemoryLyrics.cs` 调用 OpenProcess / ReadProcessMemory 取 TTML，非单纯系统媒体控制 | 公开版是否保留应先专项审查；只读、无注入不等于获得授权，用户同意也不能代替平台和版权方授权 |
| 本地资料库解码 | `AppleMusicLibraryReader.cs` 用固定 AES 密钥解码 musicdb 元数据 | 不是音频 DRM 解密；是否涉及受保护技术措施、合同限制及研究例外，需要依据实际用途判断，不能直接定性违法 |
| 原词、译文及封面 | 网络取得、缓存并可导出；未从代码证明完整内容许可 | 接口可访问不等于有复制、翻译或公开传播许可；个人欣赏与公开发布批量内容包应分别评估 |
| 隐私及默认行为 | `SettingsStore.cs` 的 OnlineLookup、AppleMusicOriginalLyrics、AutoPrefetchLibrary 默认开启；启动会按设置准备整库 | 建议网络与整库预取分别主动选择，明确服务方、发送数据、用途、保存和删除方式。README 已有简要说明，但未找到完整隐私政策和首次选择流程 |
| 软件自身许可 | 已有第三方 notices 和许可文本，未找到根目录项目 LICENSE | 由权利人决定授权方式并核实贡献、依赖许可；不要擅自替用户选择不可撤回的开源许可 |
| 分发可信度 | 最终 EXE 和安装器未代码签名，有 SHA-256 | 哈希不是发布者签名；签名是发行可信度事项，不能当作版权或隐私合规认证 |

本轮审查未发现上述路径上传 Apple 登录凭据、下载解密音频或调用开发者自建遥测后端；这只是所查代码范围内的发现，不是完整安全认证。歌曲与网络信息是否构成个人信息、谁是处理者及是否跨境，需结合实际数据流判断。

建议的处理顺序为：审查内存取词及 musicdb 解码；重新设计默认联网与整库预取；补隐私、授权及投诉文档；继续隔离个人内容，扩大推广或收费前核实内容许可。**这是一组待决策建议，不是已完成任务，也不是本交接文档授权自动下架或停用功能。**

已核对的官方来源（后续整改前重新确认现行版本、适用地区与实际协议）：

- [Apple 中国区媒体服务条款](https://www.apple.com.cn/legal/internet-services/itunes/cn/terms.html)：F 节限制自动抓取、拷贝、分析等，G 节规定终止或暂停。平台协议风险与刑事违法不是同一结论。
- [国家版权局《著作权法》2020 修正版](https://www.ncac.gov.cn/xxfb/flfg/flfg_532/202103/t20210309_50530.html)：第十、十三、二十四条涉及权利、译作及法定例外；第四十九、五十条涉及技术措施及例外，不能把研究例外直接扩展为公开分发工具。
- [国家网信办《个人信息保护法》](https://www.cac.gov.cn/2021-08/20/c_1631050028355286.htm)：结合合法基础、告知、最小必要和实际跨境情况分析，不自动推断所有本地处理或每次请求都触发同一种义务。
- [GitHub 仓库许可说明](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/licensing-a-repository)：公开仓库不等于授予完整开源许可。

免费、开源、本地保存或“仅供学习”说明均不自动解决授权问题。正式商业化前宜由熟悉软件与版权的律师专项评估，本文不能代替法律意见。

## 8. 给新 Codex 的开场提示词

可把下面一段连同本文件交给新电脑的 Codex：

> 请接手这个 Windows 浮光歌词项目。先阅读 AGENTS.md 和 CODEX_HANDOFF.md，检查本地改动、远端 main 和当前版本，再配置 scripts/common.ps1 选择的 SDK，构建并运行相关验证。保留手动歌词、译文、个人库和现有播放行为，UI 验证必须用软件目录之外的隔离 --data-dir。不要把交接中的历史测试数字当成此机结果，也不要声称所有歌词已补齐或合规整改已完成。最新合规分析只是待决策建议，请按我接下来提出的具体任务推进。完成授权的代码或文档变更后按仓库规则提交并同步 GitHub，复核远端；报告真实验证结果和剩余限制。
