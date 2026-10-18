# Apple Music 兼容性报告

日期：2026-10-18（香港时间）。**当前暂定 B 级，完整现场验收尚未通过。**

## 环境

| 项目 | 实测信息 |
|---|---|
| 系统 | Windows 10 专业版 22H2，10.0.19045，x64 |
| CPU | Intel Core i5-12600KF |
| Apple Music | Microsoft Store 包 AppleInc.AppleMusicWin，1.1538.24067.0，原生 Windows 应用 |
| 来源标识 | `AppleMusic.exe` |
| 开发环境 | 项目本地 SDK 10.0.402；.NET 10.0.13；WPF；Windows API 目标 19041 |
| 显示环境 | 本次运行报告单屏 2560×1440，96 DPI / 100%；其他缩放与多屏未测 |

## 真实轨迹

`probe-redacted.jsonl` 由实际 GSMTC API 生成。采集约 12:16:32–12:19:34；曲名、歌手和专辑默认隐藏，`track` 只是本次采集内的匿名序号。未上传轨迹。

| 操作 / 观察 | 证据和结果 |
|---|---|
| 未播放启动 | 初始 Opened，时长 0，时间轴更新时间为 1601 默认值；程序将其视为不可用 |
| 开始播放 | 12:16:44 收到 Playing；随后时长与时间轴到达，位置从 0 增加 |
| 暂停 | 12:16:51 位置约 4 秒进入 Paused；暂停期间未持续推进 |
| 恢复 | 12:16:59 Playing 先附带旧时间轴，随后新时间戳到达；不可外推整个暂停时段 |
| 前拖 | 12:17:06 位置从约 11 秒跳至 32 秒；约 16 ms 后短暂返回 11 秒，再回到 32 秒 |
| 后拖 | 12:17:13 从约 36 秒跳至 12 秒；同样出现一次瞬时旧位置 |
| 切歌 | 12:17:19 元数据先变、时间轴仍是上一首；之后位置归零、时长先 0 后为 263 秒 |
| 时间分辨率 | 位置值按整秒发布，间隔中可能重复同一位置但更换更新时间 |
| 封面 | 会话提供缩略图，WPF 主窗口已实际读取并显示 |
| 控制能力 | 播放/暂停、上一首、下一首可用；`IsPlaybackPositionEnabled=false`，应用内跳转禁用 |
| 真实歌曲数量 | 当前轨迹包含 2 首有元数据歌曲，未达到方案要求的至少 3 首全动作测试 |

媒体字段还观察到 `Artist="歌手 — 专辑"`、`AlbumTitle=""`。0.2.0 已按本机验证的字段格式拆分，自动检查录音及歌词后导入，不再要求逐首确认；以下时间轴证据仍适用。

## 已采取的适配

- 通过 Apple Music 来源识别并保持已有连接，不用全局当前会话替代来源选择。
- 切歌清空旧歌词并取消旧查询；新结果同时检查请求、会话代次及曲目修订。
- 新曲目等待属于它的新时间轴，避免沿用上一首时长/位置。
- 时间轴事件合并等待约 120 ms 后读取，过滤实测中的短暂旧值；每秒补漏一次。
- 可信锚点加单调时间推进；整秒重复值不反复重置插值；暂停冻结，过期证据停止无限推进。
- 空时间轴、不可用位置、没有歌词、纯音乐、静态歌词分别降级显示。

## 启动与运行验证

用户于本次会话明确确认双击 `artifacts/validated-win-x64/LumaLyrics.exe` 能打开主窗口。自动化直接启动新 apphost 曾停在运行时加载之前；没有系统报错证据，原因未确定，不能归因于安全软件或系统兼容性。

最终 WPF 代码通过自包含目录及附带的独立 Microsoft dotnet host 启动，使用单独数据目录完成运行验证；该 host 的 SDK 列表为空，证明这一验证不依赖 SDK。真实会话连接、本地导入、缓存绑定、偏移、托盘和 HWND 穿透样式均有记录。截图为运行中的 WPF 客户区实图；歌词注明为自写 QA fixture，避免把测试歌词误当作实际歌曲歌词。

重启相同独立数据目录，仍能读取 7 个歌词时间点及手动绑定。主程序正在运行时第二个 QA 实例注册相同快捷键失败，属于真实的快捷键占用情形；已记录，原实例的快捷键保持有效。

## 未验证与结论边界

本机能够提供歌词同步所需的元数据、状态、跳转位置及更新时间，适合继续使用本地插值；**尚未证明达到 ≤250 ms 行切换目标**，整秒源数据本身也限制了精度判断。暂定 B 级，不宣称 A 级或稳定版验收完成。

待验收：单曲循环、中途播放启动、多次重启和最小化、竞争浏览器会话、20 次/快速 10 次切歌、跨进程实际点击穿透、多屏/负坐标/150%–200% DPI、睡眠恢复、30 首曲目覆盖、两小时稳定性及性能。

## 核对来源

- [微软：桌面 .NET 项目调用 WinRT](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/winrt-apis-desktop-apps)
- [微软：GSMTC 会话接口](https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssession)
- [微软：时间轴属性定义](https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssessiontimelineproperties)
- [LRCLIB 当前 API 文档](https://lrclib.net/docs)：客户端标识、串行请求、限流与 Retry-After 已在接入时重新核对。
- [Microsoft .NET 10 发布页面](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)：本次使用 SDK 10.0.402 / runtime 10.0.13。
