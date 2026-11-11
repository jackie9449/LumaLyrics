# 本地开发记录

每次完成改动，在此记录日期、改动范围、实际验证结果和仍待确认的事项。记录与改动一起提交到本地 Git。用 `git log --oneline -- docs/development-log.md` 查找对应提交。

## 2026-11-11 · 接手开发与切换本地记录

- 接手基线为 `main` 的 `571ba0e4f825ac5f953ff563ff5a14d06030a5c6`，应用源码基线为 `2a5524269abda90fb040569d4465e1e16cad0e96`，版本为 1.1.0。
- 外层 ZIP 的 SHA-256 与校验文件一致。归档清单所列 781 个文件的大小与散列全部通过校验。清单的提交和源码树与接手前的本地仓库一致，bundle 含完整 `main` 历史及 `v1.0.0`、`v1.1.0` 标签。
- 在临时目录验证了从原包离线恢复。恢复后的提交、源码树和版本标签与清单及 bundle 一致，`git fsck --full` 通过，仓库无远端配置。
- 更新协作规范、接手说明及文档入口，后续交付使用本地提交和本记录。当前开发仓库已移除 GitHub `origin`，原交接 ZIP 和校验文件保留用于离线恢复。
- 校验并安装了项目锁定的 Windows x64 .NET SDK 10.0.402。`scripts/common.ps1` 选择仓库内 `.tools/dotnet` 中的 SDK。
- 本机执行 `scripts/build.ps1`，锁定依赖恢复和 Release 构建通过，0 警告、0 错误。执行 `scripts/test.ps1`，469 项单元测试全部通过（Core 92、Infrastructure 377）。
- 本次提交仅涉及交接文档和开发记录。应用未启动，旧机的界面和 Apple Music 实测结果见[接手说明](../CODEX_HANDOFF.md#7-验证基线与在新机复核)。
