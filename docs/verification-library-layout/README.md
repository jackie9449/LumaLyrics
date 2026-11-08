# 资料库内容宽度与滚动验证 · 2026-11-07

## 原因与修复

上次页签样式将 TabItem.HorizontalContentAlignment / VerticalContentAlignment 设为 Center。WPF TabControl 同时将这些值用于选中页面，而不只用于页签标题。导致四页内容按自身期望尺寸居中；歌曲列表虚拟行回收重新测量后，页面期望宽度随行内容变化。

修复前专项复现：Liquid、1220 窗宽下，可用内容宽 978 DIP，歌曲页初始宽 424.43 DIP，滚动后 426.73 DIP。337 项断言中 151 项失败，包括页面铺满、滚动宽度稳定及返回页面的布局。

修复仅调整 LibraryWorkspaceTab：内容横纵向 Stretch，标题 ContentPresenter 独立 Center。保留原有页签圆角、焦点框、键盘导航、主题资源及动画；列表继续获得有限视口并保留虚拟化。

## 验收与复现

使用 --verify-library-layout 专项，隔离资料生成 3,003 首原创测试歌曲，初始与滚动后的行标题长度不同。覆盖全部四页、六主题和 640/820/980/1220 宽度，核对实际内容边界与可用布局槽；实际滚动后核对页面及视口宽度，检查列表确实移动、虚拟行数量、返回页面和减少动画设置下的布局。保存宽窄窗四页的 48 张截图，绝对现场路径、数据库及截图保留在个人 QA 目录。

2026-11-08 完成运行包验收：布局专项 337/337、页签回归 289/289、工作区回归 100/100，共 726 项布尔检查通过。源码布局专项同样为 337/337；上述 978 DIP 可用区域内，修复后歌曲页进入和滚动后均为 978 DIP。已查看四页宽窄截图，内容铺满且视口有界。Release 构建零警告、零错误，Core 92、Infrastructure 368，共 460 项单元测试通过；分发检查通过 2 个软件样例及 69 类私人文件拒绝案例。

SDK 使用 scripts/common.ps1。Release 构建后，以程序目录外新建 --data-dir 运行 runtime/dotnet.exe exec LumaLyrics.dll --verify-ui <report> --verify-library-layout --exit-after-capture。检查 ui-check.json 中所有布尔值和视觉结果。回归专项为 --verify-library-tabs 和 --verify-library-workspace。

运行包由 scripts/publish-ui.ps1 -PreviewLabel library-layout 生成。验证分发隐私边界、ZIP 内容及 SHA-256。此检查不连接真实 Apple Music、不修改用户音乐资料，也不替换正在运行的旧实例。
