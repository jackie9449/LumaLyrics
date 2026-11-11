# 2026-11-11 换机包验证记录

这是本机执行的换机模拟，不是另一台电脑的现场验收。功能源码为 `441c37d`；独立恢复测试使用交接准备提交 `4686bdd`，后续只补充交接记录和辅助工具锁文件。最终准确源码提交和树由包内 manifest.json 标明，应用源码保持不变。

## 实际执行结果

| 检查 | 结果 |
| --- | --- |
| 预检包逐文件大小和 SHA-256 | 6,451 个文件全部通过；最终包的准确集合以其 manifest 为准 |
| 现成程序 ZIP | 原始 SHA-256 相符；完整解压并通过软件分发边界检查 |
| Git bundle | bundle verify 成功，完整可达历史，main 与 v1.0.0、v1.1.0 标签齐全 |
| 新目录恢复 | 从本地 bundle 克隆；HEAD 和树与 manifest 一致，fsck --full 通过 |
| 远端与工作区 | 恢复后无 remote；编译测试后工作区仍干净；已有目标目录被拒绝，未覆盖 |
| 开发 SDK | 使用包内 Windows x64 .NET SDK 10.0.402 |
| 依赖隔离 | 30 个原始 nupkg；恢复的 assets 只引用包内 feed 和新建的 NuGet 缓存，没有使用旧全局缓存 |
| 依赖恢复 | 7 个项目 locked-mode restore 成功，只使用本地包源；为离线运行关闭该次 NuGet 在线审计 |
| 编译 | 新恢复仓库 Release build 成功，0 警告、0 错误 |
| 单元测试 | 558 项通过：Core 134、Infrastructure 424；失败及跳过均为 0 |
| 恢复后的软件启动 | 通过新复制的 SDK 启动刚编译的 DLL；隔离 UI 空格专项 19 项全部通过 |
| 校验脚本异常检查 | 正常清单通过；修改内容及额外文件均被拒绝 |

最终导出脚本再次校验全部目录文件，并生成外层 ZIP 及 SHA-256 sidecar。交付前另读回 ZIP 的每个条目，核对文件集合、大小和散列；外层最终 SHA-256 在旁边 .sha256 中，避免自引用散列。

## 包含与未包含

源码、测试、脚本、许可、全部开发／发布记录及历史公开验证材料随完整 Git 快照和 bundle 保留。语言审计辅助源码及其锁文件随 toolchain 提供。SDK 和 nupkg 保留其原有许可。运行包直接使用已经验证的 space-shortcut 包，不重新标成正式发布，也不更改 v1.1.0 标签。

不复制旧机 .git/config、hooks、NuGet 用户配置、账号凭据、私人诊断和个人资料。原始迁移过程日志、TRX、隔离 UI 数据留在旧机忽略目录或 LocalAppData；本文件只记录结果。新电脑仍需 Windows x64、PowerShell、Git；真正播放需安装并登录 Apple Music。产品层面的尚未验证事项完整保留在 CODEX_HANDOFF.md 与 development-log.md。
