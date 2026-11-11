# 浮光歌词 · 从这里继续

这是 2026-11-11 的完整开发迁移包，包含本轮全部成果。准确提交和散列见 manifest.json；当前功能基线为 441c37d，产品版本 1.1.0。

## 新电脑操作

1. 对外层 ZIP 运行 `Get-FileHash <ZIP路径> -Algorithm SHA256`，与旁边 .sha256 比较，再完整解压。
2. 准备 Windows x64、PowerShell 和 Git。在解压目录根部运行：

```powershell
./scripts/Verify-Handoff.ps1
./scripts/Restore-Workspace.ps1 -Destination 'D:\Work\LumaLyrics' -Build
```

3. 用 Codex 打开恢复后的 `D:\Work\LumaLyrics`，阅读其中 AGENTS.md、CODEX_HANDOFF.md、docs/updates-2026-11-11.md、docs/development-log.md。
4. 运行软件时解压 app 中的 ZIP，打开 浮光歌词.exe。完整软件目录必须保留，不要只取 EXE。要测试真实播放，在新机安装 Apple Music 并自行登录。

本包带 .NET SDK 10.0.402 和项目所需原始 NuGet 包，可离线恢复、编译和测试；Git 本身不随包携带。首次恢复已使用新目录和独立依赖缓存验证，详见 MIGRATION_CHECKS.md。SDK 和 NuGet 原有许可保留。普通开发无需 Inno Setup。

## 包内内容

| 路径 | 用途 |
| --- | --- |
| source/ | 全部 Git 跟踪源码、测试、脚本、文档、许可和历史公开验证资料；阅读快照，没有 .git |
| history/LumaLyrics.bundle | 全部本地分支和版本标签的完整可达 Git 历史，可离线克隆 |
| history/commits.txt | 全部提交说明、日期与逐次文件修改统计 |
| app/ | 当前已经测试的自包含软件 ZIP 和原始校验文件 |
| toolchain/ | SDK、离线 NuGet feed、语言审计辅助源码 |
| scripts/ | 逐文件校验、恢复新仓库和离线构建测试 |
| CODEX_HANDOFF.md | 接手说明，含当前成果、代码地图、限制和新 Codex 开场提示词 |
| manifest.json | 源码树、提交、运行包对应提交、SDK、逐文件大小与 SHA-256 |

恢复脚本只克隆到不存在的新目录，核验提交及 Git 对象并移除 origin，不连接 GitHub。源码快照与 Git 历史都包含完整更新记录；当前协作以本地 Git 为准。顶层接手说明中的 docs 链接从 source/ 内阅读时可直接使用。

个人歌词、译文、封面、设置、音乐库、登录及私人诊断不在本包内。若要迁移这些内容，在旧机软件“设置 → 个人资料”导出，在新机恢复到新目录并选择该资料位置；见 source/docs/personal-library.md。
