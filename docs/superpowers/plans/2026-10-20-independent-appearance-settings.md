# Independent Appearance Settings Implementation Plan

> **For agentic workers:** Execute the authorized tasks in this session, tracking each task to completion.

**Goal:** 应用主题只在设置中切换；悬浮歌词拥有独立的外观设置与资源；将全部已授权变更同步到 GitHub。

**Architecture:** 主窗口保持播放与阅读布局。SettingsWindow 提供应用界面、悬浮歌词、常规三个页签；应用主题仍即时保存，常规表单单独保存。悬浮歌词和外观编辑器使用窗口级配色资源，不继承全局主题。

**Tech Stack:** WPF / C# / .NET 10 / xUnit / GitHub Git data API。

**Spec:** 本轮用户的三点要求。

## Tasks

- [x] 将主题选择从 MainWindow 移入独立 SettingsWindow，保留设置页实时主题切换。
- [x] 为悬浮窗、工具栏和外观编辑器提供独立窗口资源；保存/取消不覆盖应用主题。
- [x] 更新 UI 验证，通过真实设置页选择主题，检查双向外观隔离、布局和旧动效。
- [x] 升级至 0.4.1，更新说明与实图，运行测试和发布检查。
- GitHub 同步作为本地验证后的最终交付动作：校验期望 SHA，提交并核对远端文件树，交付时报告实际提交链接。

## Acceptance

主界面无主题选择条；设置页能保存三种应用主题；悬浮歌词文字、工具栏和外观面板不会随应用主题变化；悬浮外观变化也不会改变应用主题。全部单元测试和 UI 检查通过后执行非强制 GitHub 同步。
