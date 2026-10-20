# Three UI Themes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** 下载浮光歌词源码，将液态玻璃、毛玻璃、谷歌平面扩展为完整、可切换的应用主题，并完成调试。

**Architecture:** 使用独立的应用主题设置和共享动态资源。主窗口保留播放器与歌词逻辑，重做导航、封面、歌词舞台和底部工具。外观与业务弹窗使用相同控件系统；桌面歌词继续使用已有独立材质设置。

**Tech Stack:** WPF, C# 14, .NET SDK 10.0.402, xUnit.

**Spec:** 本次用户要求：多个高审美可选主题，首先实现液态玻璃、毛玻璃、谷歌平面，完成 debug。

## Global Constraints

- 不改变媒体控制、歌词匹配、资料库读取与现有用户绑定行为。
- 三种主题可即时切换、重启恢复；未识别设置回退到液态玻璃。
- 液态玻璃：深海蓝、通透渐变、精细边缘与反光。
- 毛玻璃：浅矿物绿、柔雾层次、低饱和绿色强调。
- 谷歌平面：浅丁香底色、紫色强调、宽松圆角与纯色层次。
- 保留悬浮歌词八种配色和外观保存/取消语义。
- 在独立数据目录进行视觉验证，避免改动日常资料库和设置。

### Task 1: 可持久化的主题状态

**Files:** `src/LumaLyrics.Infrastructure/AppThemes.cs`, `SettingsStore.cs`, `tests/LumaLyrics.Infrastructure.Tests/AppThemeTests.cs`。

**Interfaces:** `AppThemes.All`, `AppThemes.Find(string?)`, `AppSettings.UiTheme`；与 `OverlayAppearance` 独立。

- [x] 添加三个主题 ID、名称、说明和经过对比度检查的配色。
- [x] 测试旧设置默认值、未知 ID 回退、全部主题保存重载和悬浮窗设置独立性。
- [x] 运行 Infrastructure 测试验证持久化行为。

### Task 2: 主窗口与全局控件

**Files:** `App.xaml`, `ThemeManager.cs`, `UiStyles.xaml`, `MainWindow.xaml`, `MainWindow.xaml.cs`, `SliderStyles.xaml`, `AppearanceStyles.xaml`。

**Interfaces:** `ThemeManager.Apply(string)`, 动态资源 `TextBrush` / `MutedBrush` / `AccentBrush` / `WindowBrush` / `PanelBrush` / `BorderBrush`。

- [x] 用动态资源重做按钮、输入、下拉菜单、复选框、滚动条、焦点状态与选中状态。
- [x] 重做主窗口结构，增加三个主题切换入口和主题说明。
- [x] 使用现有命名控件和事件，保留进度条、播放、导入、校准和资料库入口。
- [x] 用共享资源更新运行时创建的歌词，避免切换后仍保留旧颜色。
- [x] 低宽度时改成紧凑双行布局，保留播放控件。

### Task 3: 外观页和业务弹窗统一

**Files:** `AppearanceWindow.cs`, `DialogWindows.cs`。

- [x] 去除固定深色，跟随应用主题。
- [x] 保持八个悬浮歌词主题、实时预览、保存/取消和颜色校验功能。
- [x] 下拉、标签、选中项、禁用态均使用主题动态资源。

### Task 4: 调试与交付

**Files:** `UiThemeVerification.cs`, `docs/ui-themes.md`, `scripts/publish-ui.ps1`。

- [x] Release 构建和全部已有测试通过。
- [x] 对三种主题、最小窗口、外观页和弹窗生成真实 WPF 视觉截图并检查。
- [x] 验证主题设置保存重载、预览取消、歌词颜色切换和窗口缩放。
- [x] 实际运行应用检查交互与崩溃日志。
- [x] 生成含运行时的 Windows x64 可运行包，记录已验证范围与未完成的外部播放器验收。

执行方式：本次用户已要求直接完成开发与调试，在当前会话逐项执行，不另行等待计划审批。
