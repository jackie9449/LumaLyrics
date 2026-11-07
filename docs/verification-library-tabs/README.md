# 资料库页签裁切验证 · 2026-11-07

原版 WPF TabPanel 中，页签实际宽 52 DIP，裁切区域宽 46 DIP，右侧 6 DIP 的圆角、背景及焦点框被截断。独立横向 StackPanel 按完整 Margin 测量排列，状态层内缩 1 DIP；不再使用侧栏的左侧选中标记。资源仅应用于 LibraryWorkspacePanel，其它导航及悬浮外观编辑器继续使用各自原有样式。

专项 `--verify-library-tabs` 在六主题、640/820/980/1220 窗宽下逐一选择并聚焦四个页签，比较完整视觉范围与实际裁切区域，保存 24 张页签渲染；检查方向焦点遍历、Home/End 选择及减少动画下的切换。左右遍历通过 WPF MoveFocus 执行；Home/End 使用路由键事件，不将这些检查表述为物理键盘输入测试。截图及现场检查 JSON 保留在程序目录外的个人 QA 目录。

Release 构建零警告、零错误；Core 92、Infrastructure 368，共 460 项单元测试通过。源码及最终运行包页签专项均为 289/289，运行包工作区回归 100/100。已逐一查看六主题页签渲染，完整边缘与主题形状一致；分发检查通过 2 个软件样例与 69 类私人文件拒绝案例。

使用 scripts/common.ps1 选择 SDK，执行 scripts/test.ps1；以全新隔离 --data-dir 运行包内 runtime/dotnet.exe exec LumaLyrics.dll --verify-ui <report> --verify-library-tabs --exit-after-capture，读取 ui-check.json 全部布尔结果并检查截图。工作区回归使用 --verify-library-workspace。

交付脚本：scripts/publish-ui.ps1 -PreviewLabel library-tabs。软件分发检查、ZIP 内容散列及 SHA-256 在生成后核对。本轮不验证真实 Apple Music 同步或第三方 agent 翻译，也不替换用户运行中的旧实例。
