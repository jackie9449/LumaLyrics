# 浮光歌词 0.4.1 · 应用界面与独立悬浮歌词

主界面已移除主题切换条。点击左下角设置，在「应用界面」中选择「液态玻璃」「毛玻璃」或「谷歌平面」，立即切换并自动保存。应用主题只影响主窗口和应用设置、导入等业务弹窗。

| 主题 | 设计 |
| --- | --- |
| 液态玻璃 | 深海蓝、冰蓝强调色、半透明渐层、细腻高光与轮廓 |
| 毛玻璃 | 浅矿物绿、柔和雾面、低饱和绿色、舒展留白 |
| 谷歌平面 | 浅丁香底色、紫色强调、纯色层次、大圆角 |

左侧依次提供当前歌词、资料库、桌面歌词外观、设置和诊断。顶部「桌面歌词」控制悬浮窗；底部保留校准、导入、更换歌词和重新搜索。窗口缩小时转为上下布局，进度与播放控制仍可使用。

设置提供「应用界面」「悬浮歌词」「常规」三个页签。「悬浮歌词」页可查看独立预览，点击「打开悬浮歌词独立外观」进入专用编辑器。原有八种悬浮主题、字体、配色、透明度和保存 / 取消继续可用。悬浮窗、悬停工具栏和外观编辑器使用本地资源，均不随应用主题变化；编辑悬浮外观也不覆盖应用主题。

应用主题选择后立即保存；悬浮外观在独立编辑器中保存或取消；常规表单修改后点击「保存常规设置」。三类操作分别生效。

主窗口的玻璃效果通过应用内的半透明表面、高光和背景光晕实现。悬浮窗继续尝试 Windows 原生背景模糊；系统不支持时回退为半透明材质。没有实现真实光学折射。

## 运行

解压完整运行包后双击 Launch.vbs，或使用 LumaLyrics.exe。运行包自带 .NET 运行时；Launch.vbs 使用随包 Microsoft host，不需要安装 SDK。原本的数据目录保持为 %LocalAppData%/LumaLyrics；新 UiTheme 字段默认液态玻璃，旧外观参数继续保留。

## 构建与验证

Windows x64，SDK 10.0.402。scripts/common.ps1 优先使用仓库内 .tools/dotnet。

```powershell
./scripts/build.ps1
./scripts/test.ps1
./scripts/publish-ui.ps1
```

独立数据目录的 UI 验证（不会启动媒体连接和网络预缓存）：

```powershell
./.tools/dotnet/dotnet.exe ./src/LumaLyrics.App/bin/Release/net10.0-windows10.0.19041.0/LumaLyrics.dll --data-dir ./.tools/qa-ui --verify-ui ./docs/screenshots-ui-v041 --exit-after-capture
```

验证涵盖三种主题的保存、歌词颜色继承、封面裁切、外观控件、保存/取消、导入弹窗，以及 1220×820、980×700、820×640 的布局和播放控件。另运行原有歌词渐变、快速切换、关闭动画、圆角与滑块回归检查。示例歌词为自写文字，测试数据位于独立目录。

本次结果：61 项单元测试、84 项 UI 检查全部通过。检查通过真实设置控件选择应用主题，并确认主界面无选择器、悬浮工具栏与编辑器不随应用主题变化。Release 编译无警告。

本次 UI 调试不代表已完成 Apple Music 实际播放同步、多显示器跨屏、两小时稳定性及全库网络缓存验收。

本机自动化直接启动 apphost EXE 会在运行时加载前停顿，与原仓库兼容报告中的记录一致；本次未确认其原因。随包 Microsoft host（SDK 列表为空）已完成整套 UI 检查，建议使用 Launch.vbs 启动。

实图：[主界面](screenshots-ui-v041/main-liquid.png)、[应用界面设置](screenshots-ui-v041/settings-liquid.png)、[悬浮歌词设置](screenshots-ui-v041/settings-overlay-liquid.png)、[独立外观编辑器](screenshots-ui-v041/appearance-liquid.png)。

机器可读结果见 [ui-check.json](screenshots-ui-v041/ui-check.json)。后续提交、验证与 GitHub 同步规范记录在仓库根目录 [AGENTS.md](../AGENTS.md)。
