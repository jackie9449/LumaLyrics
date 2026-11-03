# 资料库与歌词整合验证 · 2026-11-03

隔离个人目录、原创曲目／歌词 fixture 和 Microsoft host 执行。数据库、任务、日志、现场路径和完整截图留在个人验证目录，不提交 Git。

## 自动检查

- Release locked restore／build：零警告、零错误。
- 单元测试：Core 92、Infrastructure 356，共 448 通过。新投影与目标测试覆盖完整／部分、静态／旧哈希、原生重复项、排除孤立记录、非播放歌曲导入、旧修订冲突、所有批次及缺分块、原生键比较和覆盖报告统一口径。
- 增强 WPF workspace 专项 68 项，覆盖真实生成六首／一入口／两分块、自动接收、六主题、640／820／980／1220 的有界滚动视口、管理 B 而播放 C、单 Settings 实例和隐藏解绑。菜单按应用主题绘制并检查键盘焦点；混合批次验证补原词→现有中文→仅剩余项导出，已有手动译文保留，暂停拒绝不遵守取消的 provider 晚到原词。
- 运行包中文显示 55/55、音乐浏览 108/108、响应式布局 307/307、主题集合 43/43 通过；加上 workspace 68/68，共 581 项布尔断言通过。音乐浏览包含实际歌曲管理按钮、Enter 不播放和专辑完整范围交接。
- 分发隐私检查：2 个软件 fixture、69 类私人文件拒绝通过；软件包 ZIP 与输出目录逐文件散列核对。

## 复现

先用 `scripts/common.ps1` 的 Invoke-Dotnet 构建，或执行 `scripts/publish-ui.ps1 -PreviewLabel library-lyrics-workspace`。每项新建程序目录以外的 `--data-dir`，运行包内：

```powershell
./runtime/dotnet.exe exec ./LumaLyrics.dll --data-dir 'D:/隔离验证/data' --verify-ui 'D:/隔离验证/report' --verify-library-workspace --exit-after-capture
```

`--verify-library-workspace-navigation` 和 `--verify-translation-jobs` 运行同一增强专项。其它专项使用 `--verify-translations`、`--verify-library-browser`、`--verify-responsive-layout`、`--verify-theme-collection`。检查 ui-check.json 全部布尔值并查看截图，不能只看退出码。

## 边界

本次未对真实个人 Apple Music 资料库发起批量联网准备，未重新确认原生播放／窗口状态，也未调用第三方 agent 评估真实翻译语义。程序校验仅证明目标、原词、逐组结构与修订一致；不提升 AI 稿或社区译文为人工已审。窗口边界与焦点检查不等于完整辅助技术验收；新大库管理页的长时间性能和共享译文槽整组确认未完成，现有共享槽写入会拒绝。
