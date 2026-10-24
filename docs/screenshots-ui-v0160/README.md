# 0.16.0 应用界面与交互设计

主应用的播放、音乐库、设置与工具窗口使用同一组主题资源和交互组件。三套主题分别呈现深色流光、浅色柔雾和丁香平面的氛围；悬浮歌词、工具栏与独立外观编辑器继续使用自己的样式和配色。

- 应用按钮、专辑卡片、开关、列表、下拉选项与输入焦点有柔和的状态反馈。悬停与选择约 150–190 ms，按压约 85 ms，页面进入约 210–270 ms；快速反向操作保留当前可见值。
- 设置采用左侧分类、右侧内容卡片；应用设置与导入、缓存、诊断和校对窗口共享标题栏、边框及控件。主播放页仍保留专辑封面、逐行歌词和右侧实际队列。
- 保存“减少界面动画”或由系统关闭动画后直接呈现状态，停用菜单淡入和界面过渡。动画不延迟播放命令，不改变歌曲、队列或个人资料。
- 动效结束、内容卸载或隐藏时释放相应时钟与交互状态。队列入场只移动卡片内部内容，保留外围布局和可见区域边界。

验证使用原创文字和几何封面，以及位于软件目录之外的隔离个人目录；没有对用户的 Apple Music 发送播放、插播或排序命令。

本机验证为 Windows 10 22H2、100% 缩放。Release 构建零警告、零错误；280 项单元测试通过。[源码完整 WPF 检查](ui-check.json)与[自包含运行包检查](ui-check.packaged.json)继续运行原有 613 项回归，新增 88 项全部通过，覆盖三套主题的动效、固定点击区域、开关实际位移、可见焦点、状态中断、减少动画、静态忙碌提示、导航唯一选中、关闭清理、设置五个页面、840/640 布局和弹窗样式。

完整回归尚未全绿：本机的原生悬浮玻璃颜色采样仍有失败。以提交 `2d04d70df465acb3e40b5fc69f8ab48821431ec9` 的未修改 0.15.0 做隔离对照，[旧版结果](ui-check.baseline-v0150.json)为 609/613；同样出现三个 `overlayCorners.*.backdropKeepsSceneColor` 失败，另有旧的专辑搜索固定等待检查失败。本次没有修改悬浮原生合成实现，也没有降低玻璃颜色或模糊阈值；此项限制继续保留，不能仅凭窗口创建成功宣称原生玻璃验收通过。

源码完整回归为 699/701，未通过的两项是 `overlayCorners.liquid.backdropKeepsSceneColor` 与 `overlayCorners.frost-roundtrip.backdropKeepsSceneColor`。随后将导航检查改为有上限地等待实际选中反馈完成，替代固定 250 ms 延迟；[最终源码队列专项](ui-check.queue.json)为 44/44，覆盖三套主题的往返导航与原有队列功能。产品中的选中状态、时长和动画完成判定未放宽；最终运行包再次执行完整检查。

最终运行包为 699/701，新增 88 项全部通过；未通过的两项为 `overlayCorners.liquid.backdropKeepsSceneColor` 与 `overlayCorners.liquid-roundtrip.backdropKeepsSceneColor`。报告保留这些失败，没有将其改写为通过。

| 验证对象 | 通过 / 总数 | 未通过 |
| --- | --- | --- |
| 0.16.0 源码完整检查 | 699 / 701 | 2 项悬浮玻璃颜色采样 |
| 0.16.0 最终源码队列专项 | 44 / 44 | 0 |
| 0.16.0 最终自包含运行包 | 699 / 701 | 2 项悬浮玻璃颜色采样 |
| 未修改的 0.15.0 对照 | 609 / 613 | 3 项玻璃采样、1 项固定搜索等待 |

| 液态玻璃 | 毛玻璃 | 谷歌平面 |
| --- | --- | --- |
| [主播放页](main-liquid.png) | [主播放页](main-frost.png) | [主播放页](main-material.png) |
| [音乐库专辑](albums-liquid.png) | [音乐库专辑](albums-frost.png) | [音乐库专辑](albums-material.png) |
| [应用设置](settings-liquid-0.png) | [应用设置](settings-frost-0.png) | [应用设置](settings-material-0.png) |
| [交互组件](interaction-liquid.png) | [交互组件](interaction-frost.png) | [交互组件](interaction-material.png) |

[中文译文设置](settings-material-4.png) · [导入窗口](import-liquid.png) · [实际队列视图的原创样例](playback-sidebar-liquid.png)。

源码验证中的搜索检查采用有上限的防抖完成等待；所有原有结果数量、选中项和滚动位置断言保留。运行包通过随包 Microsoft host 执行相同完整检查，该 host 的 SDK 列表为空；[已测程序集 SHA-256](binary-checksums.json)记录随包的五个项目程序集。软件分发边界检查通过，另有 2 个软件样例与 32 个私人文件拒绝样例通过。界面回归不能代替真实 Apple Music 集成、多屏和长期稳定性验收。
