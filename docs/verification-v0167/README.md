# 0.16.7 验证

验证通过 Microsoft host 启动时，托盘和主窗口使用内嵌浮光图标；隐藏后恢复、退出清理与重新创建继续可用。使用独立个人目录，不启动真实音乐同步或网络准备，不改变运行中的个人应用。

[托盘专项检查](tray-development.json) 15/15 通过：内嵌资源、原生图标像素对应、透明边缘、深浅背景可见性、主窗口冻结图片、系统托盘矩形、关闭保留恢复入口、恢复菜单、双击处理、资源释放、系统注册删除及重新创建。[尺寸预览](../screenshots-ui-v0167/README.md)来自实际图标句柄。

[首轮 14/15](tray-first.json)与[后续 12/15](tray-before-hresult-fix.json)保留。新检查最初要求系统返回恰好为 0，但本机返回 HRESULT 1 且提供有效的 24×24 图标矩形；修正为成功 HRESULT 与有效矩形同时满足，删除后则必须返回失败 HRESULT。依据 [HRESULT 成功判定](https://learn.microsoft.com/en-us/windows/win32/api/winerror/nf-winerror-succeeded)与[托盘矩形接口](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shell_notifyicongetrect)，不把 NotifyIcon.Visible 独自当作系统注册证明。

恢复菜单和双击是应用命令／事件回归。当前自动化窗口接口没有返回可操作的任务栏窗口，因此未声称完成真实鼠标点击托盘或肉眼确认固定显示；系统注册矩形与程序图标像素不等同于任务栏截图。

Release 构建零警告、零错误；297 项单元测试通过（Core 57、Infrastructure 240）。[源码完整 WPF 回归](source-ui-check.json)与[自包含包完整复验](packaged-ui-check.json)均为 970/970；运行包使用随包 Microsoft host，SDK 列表为空。

[运行包首轮](packaged-first-ui-check.json)为 968/970，未通过的是既有 overlayCorners.liquid.fadeDoesNotActivate 与 motion.liquid.inputFocusVisible 检查，15 项托盘检查均通过。同一批二进制在新隔离目录完成 970/970 完整复验，没有改动焦点断言、阈值或等待条件；首轮原因不作确定归因。

[程序集校验值](binary-checksums.json)记录复验的五个项目程序集。最终文档打包后复核程序集保持一致，ZIP 与目录逐项比较。分发边界检查及两类软件样例／32 类私人文件拒绝检查通过。个人数据与真实桌面诊断不进入软件包或 Git。
