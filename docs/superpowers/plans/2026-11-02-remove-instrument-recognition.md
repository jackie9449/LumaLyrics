# Remove instrument recognition Implementation Plan

> **For agentic workers:** Execute inline in this session. Do not delegate.

**Goal:** 按用户“不要识别乐器、回退版本”的要求，恢复到乐器实验之前的应用功能。

**Architecture:** 当前 HEAD `5370769` 与远端一致且工作区原本干净。`4cc273f` 之后连续九个提交均属于乐器实验，使用逆向提交恢复原有应用／测试／依赖，不重写 Git 历史。保留分发隐私防护、Git 忽略规则和媒体源退出取消修复；不打开或删除个人资料及模型。

**Tech Stack:** .NET 10、WPF、PowerShell、Git revert。

**Spec:** 用户 2026-11-02 明确撤销乐器识别需求；`AGENTS.md` 的资料保护、隔离验证和 GitHub 同步规则继续适用。

## Global Constraints

- 不强推，不重置远端历史，保留与乐器无关的现有功能。
- 不修改个人歌词、封面、设置、数据库、历史分析或模型文件。
- 便携包使用全新目录，排除旧构建残留、音频采集辅助代码和 ONNX 运行时。
- “纯音乐”歌词处理不是乐器识别，保持原有纯音乐判定与歌词绑定。

### Task 1: 回退代码和依赖

**Files:** 逆向恢复 `4cc273f..5370769` 的功能文件；保留 `.gitignore`、`scripts/verify-distribution.ps1`、`scripts/test-distribution.ps1`、`src/LumaLyrics.Windows/GsmtcPlaybackSource.cs` 的已修复版本。

- [x] 应用九个反向变更，不创建逐个回退提交，不改个人目录。

```powershell
git revert --no-commit 4cc273f..5370769
git restore --source=5370769 --staged --worktree -- .gitignore scripts/verify-distribution.ps1 scripts/test-distribution.ps1 src/LumaLyrics.Windows/GsmtcPlaybackSource.cs
```

- [x] 核对 `src`、`tests`、`tools`、solution 与基线差异只剩媒体源取消修复；更新 release notes，说明已移除功能和个人资料不受影响。打包脚本保留 UTF-8 BOM 兼容性。

```powershell
git diff --name-only 4cc273f -- src tests tools LumaLyrics.slnx
```

### Task 2: 验证并交付回退版本

**Files:** `docs/release-notes.md`、独立 `pre-instruments` 便携预览，验证结果放在个人检查目录。

- [x] 使用 bundled SDK 完成 locked restore、Release 构建零警告、428 项单元测试通过；59 种个人文件拒绝检查通过。

```powershell
. ./scripts/common.ps1
Invoke-Dotnet restore LumaLyrics.slnx --locked-mode
Invoke-Dotnet build LumaLyrics.slnx -c Release --no-restore
Invoke-Dotnet test LumaLyrics.slnx -c Release --no-build --no-restore
./scripts/test-distribution.ps1
./scripts/publish-ui.ps1 -PreviewLabel pre-instruments
```

- [x] 包内 Microsoft host、隔离 `--data-dir` 验证通过：应用设计 253 项、响应式布局／设置 307 项、进度滑条 124 项，共 684 项布尔断言。已检查播放页与六页 Settings 截图；实际 Core／App／Infrastructure／Windows／Probe 中无乐器相关类型，包与 deps 均无 ONNX 依赖。
- [x] ZIP 845 个文件逐项散列一致，SHA-256 `c569ac007585afb4fcd5659000e5e0bb6a88f280a14512640e29615f8f5051d3`。重新读取远端，普通提交／推送并核对远端 SHA 和树；实际提交链接随交付回复报告。未额外进行 Apple Music 实播操作。
