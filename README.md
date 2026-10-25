# 浮光歌词 LumaLyrics

**让音乐，有迹可循。** 为 Windows Apple Music 设计的桌面歌词应用，支持动态原词、独立悬浮歌词、中文译文与本机音乐库。

[下载 1.0.0](https://github.com/jackie9449/LumaLyrics/releases/tag/v1.0.0) · [更新记录](docs/release-notes.md) · [使用与故障反馈](https://github.com/jackie9449/LumaLyrics/issues)

![六套主应用主题](docs/screenshots-ui-v0180/theme-collection.png)

## 下载与安装

支持 **Windows 10 2004 / build 19041 及以上、Windows 11，x64**。请先安装 Apple Music for Windows，并在 Apple Music 中登录和播放音乐。

| 版本 | 下载 | 使用方式 |
| --- | --- | --- |
| 安装版 | [LumaLyrics-1.0.0-setup-win-x64.exe](https://github.com/jackie9449/LumaLyrics/releases/download/v1.0.0/LumaLyrics-1.0.0-setup-win-x64.exe) | 双击安装，默认安装到当前用户目录，并创建名为 **浮光歌词** 的桌面快捷方式与开始菜单入口。无需管理员权限。 |
| 便携版 | [LumaLyrics-1.0.0-portable-win-x64.zip](https://github.com/jackie9449/LumaLyrics/releases/download/v1.0.0/LumaLyrics-1.0.0-portable-win-x64.zip) | 完整解压后双击 **浮光歌词.exe**。程序不写入系统安装目录，个人资料仍独立存储。 |

两种版本使用同一程序与随包 .NET 运行时，无需另装 SDK 或填写 Apple API 密钥。启动器使用 Windows 自带的 .NET Framework，保留 `Launch.vbs` 作为兼容入口。发布页同时提供 SHA-256 校验文件。程序与安装包暂未进行代码签名。

升级前从旧版托盘菜单选择 **退出**，再运行安装程序或解压新版。安装、升级与卸载均保留原有个人歌词、译文、封面和设置；关闭主窗口后程序可继续留在托盘，卸载前请先退出。

## 1.0.0 带来什么

- **悬停即可控制播放。** 悬浮歌词未锁定时，指针移到歌词上，下方出现上一曲、播放/暂停、下一曲；移入按钮可继续操作，离开后收起。锁定后保持点击穿透，外观编辑、拖动和纯音乐状态不显示控制条。
- **六套完整主应用设计。** 液态玻璃、雾白、柔塑、纸间、夜航、构成，各有排版、字体、面板形状、封面装帧和导航风格。音乐库、设置与弹窗使用同一套设计语言。
- **安装与便携两种分发。** 安装版自动创建桌面入口，并支持 Windows“已安装的应用”卸载；便携版完整解压即可运行。

![未锁定时的悬浮播放控制](docs/verification-v100/floating-playback-controls.png)

## 歌词与中文译文

播放时优先使用已保存的个人绑定与缓存；联网查找会核对歌曲、艺人、专辑和时长，通过匹配检查的动态原词才自动导入。支持 LRCLIB、网易云等独立来源，以及 Apple Music 本机动态歌词参考；也可自行导入 LRC 或文本、选择候选、调整歌词偏移。不会为静态文字编造时间戳，也不会把缺失歌词当作纯音乐。

Apple Music 原词通道只读取已经展开的原生动态歌词，不会在后台自动打开窗口或面板。自动原生读取有超时和进程级暂停保护；不能读取时仍可使用缓存与其他来源。不同版本的 Apple Music 可能限制原生访问，不能保证每首歌都有可获取的动态原词。[原词获取说明](docs/apple-original-lyrics.md)

中文译文可在主窗口右下角、设置页以及悬浮上方工具栏分别开关，主窗口和悬浮歌词独立记忆，默认关闭。译文绑定准确的原词 SHA-256 与分组索引；原文变化后不会串用旧译文，手动译文受保护。来源译文与个人 AI 译稿保留真实来源，现有功能不代表全资料库已有人工核验翻译。[中文译文指南](docs/chinese-translations.md)

## 悬浮歌词

在 **设置 → 悬浮歌词** 调整八套独立外观、字体、文字颜色、透明度、圆角、留白、对齐和动效。每套悬浮主题分别记住修改；保存提交本次各主题调整，取消撤销未保存预览。设置页本身始终跟随主应用主题，悬浮主题只改变预览和实际歌词。

- 未锁定时可拖动、调整大小，悬停显示上方设置工具栏与下方播放控制；播放按钮根据实际媒体状态显示播放或暂停，不支持的操作自动禁用。
- 锁定后鼠标穿透。`Ctrl+Alt+U` 解锁，`Ctrl+Alt+L` 显示或隐藏；快捷键可在设置中修改。
- 已确认纯音乐时歌词区留白，悬浮文字和空卡片一起隐藏。
- 液态玻璃与毛玻璃使用独立背景合成层；系统不支持时使用兼容材质。关闭系统动画或开启“减少界面动画”后保留直接反馈。

[悬浮主题记忆](docs/theme-memory.md) · [玻璃材质说明](docs/glass-materials.md)

## 主应用与音乐库

在 **设置 → 应用界面** 选择六套主题。液态玻璃与雾白随当前封面取色；柔塑、纸间、夜航、构成保留各自固定配色。窗口缩放时实时调整正方形封面、歌词、播放控件与右侧队列；主题选择不占用歌词页。

音乐库默认显示本机 Apple Music 资料库的专辑卡片，支持专辑详情、全部歌曲、搜索、单曲与整张专辑播放。成员按原生资料库清单确认，后台封面和歌词准备使用相同范围。资料库管理统一位于 **设置 → 资料库**。

右侧播放列表读取 Apple Music 实际暴露的当前曲目与待播队列，保留重复歌曲和插播顺序；支持双击/Enter 跳到对应位置、插播和拖动排序。操作会核对实际录音与顺序，读不到完整原生状态时明确提示。Apple Music 的部分主动操作仍可能短暂恢复窗口，不能保证完全后台点播；后台读取不会自动展开播放列表。

[六主题页面实图](docs/screenshots-ui-v0180/README.md) · [主题说明](docs/ui-themes.md) · [音乐库与队列](docs/music-library.md)

## 个人资料与隐私

个人歌词、译文、原始封面、数据库、设置与诊断资料保存在程序之外，默认位于 `%LOCALAPPDATA%\LumaLyrics`；自定义位置会继续沿用。安装版与便携版不会各自创建一套互不相通的资料库。

在 **设置 → 个人资料** 可导出文件夹或 ZIP，包含可直接阅读的歌词、封面与一致数据库快照；恢复到新目录，保留现有个人资料。选定位置不可用时会提示重新选择，不会悄悄创建空库。软件安装包和便携包均排除个人库、下载歌词、译文、媒体缓存和模型。[个人资料指南](docs/personal-library.md)

联网查词需要向相应歌词服务发送录音元数据；本机歌词校对资料保存在个人目录。首次资料库准备时间取决于库大小和服务状态，扫描完成不等于每首歌都有歌词；已缓存内容可离线使用。

## 构建与验证

C# / WPF，.NET 10，Windows x64。使用仓库的 SDK 选择脚本（有 `.tools/dotnet` 时优先使用），依赖锁定。

```powershell
./scripts/build.ps1
./scripts/test.ps1
./scripts/publish.ps1
./scripts/publish-installer.ps1 -Compiler 'C:/Program Files (x86)/Inno Setup 6/ISCC.exe'
```

`publish.ps1` 生成正式便携包，`publish-installer.ps1` 用相同文件生成安装 EXE，需要 Inno Setup 6.7+。安装界面中文文案保存在 `installer/Languages`。仅调试界面时仍可使用 `publish-ui.ps1` 生成预览包。

UI 验证须通过 `--data-dir` 使用软件目录外的隔离目录，不启动真实媒体和网络服务；这些检查不能替代实际 Apple Music 版本上的原生同步验证。[1.0.0 验证记录](docs/verification-v100/README.md) · [既有六主题验证](docs/verification-v0180/README.md)

[资料库导入工具](docs/library-assets.md) · [原词校对](docs/lyrics-review.md) · [第三方组件](THIRD-PARTY-NOTICES.md) · [协作规范](AGENTS.md) · [Codex 换机接手](CODEX_HANDOFF.md)
