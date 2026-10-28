# Instrument response and quality Implementation Plan

> **For agentic workers:** Execute inline in this session. Do not delegate. Track measured outcomes below.

**Goal:** 缩短乐器亮灭滞后并减少误报，用相同真实音频比较改进前后，不将流畅动画或降低阈值冒充准确度提升。

**Architecture:** 先拆分 3055 ms 模型上下文、500 ms 更新、连续确认、750 ms 释放与 33 ms UI 刷新的贡献。对固定 MATPAC 编码器测试更短上下文和最近时间片池化；参数只在 URMP calibration 组选择，冻结后报告保留组与摇滚试听表现。清单准入与已知乐器的活动状态分别处理，保留切歌／跳播／暂停的取消和个人数据边界。没有测得改善的模型不替换现有模型。

**执行决定：** 直接缩窗变差，采用 9 时间片（1455 ms）全局平均并增加七类线性误报抑制。26 首 calibration 按 composition 三折选参数后冻结；18 首保留组误报 1422→726、漏报 697→789，F1 61.54%→67.91%。保留组早期研究已使用，不描述为全新数据，仍不标记全面准确度通过。详细来源及参数见 `docs/instrument-response.md`。

**Tech Stack:** .NET 10、WPF、ONNX Runtime；私有固定 MATPAC++ / OpenMIC 资产，Python 3.12 / PyTorch；URMP 已有公开音符标注和 Apple 官方试听。

**Spec:** `docs/superpowers/specs/2026-10-27-instrument-activity-design.md`，用户反馈“同步不够灵敏，不够准确”；有限词汇与已知误报见 `docs/instrument-roster.md`。

## Global Constraints

- 根据实际音频识别，不按歌曲名称、流派或能量直接点亮具体乐器；默认不保存实时 PCM。
- 仅固定校验的模型；原模型数值误差门槛 `1e-4` 不放宽，新预处理／池化／活动参数隔离缓存身份。
- 首次流媒体不能提前知道未播放部分；完整文件覆盖不等于所有实际乐器正确识别。
- 保留 19 类词汇和旧枚举含义；不以退回固定三类伪装准确率提升。
- 个人权重、真实歌曲、逐窗分数和诊断只在个人目录，软件包和 Git 不包含。
- 普通提交并同步 GitHub，先检查远端，禁止强推；便携预览独立发布。

### Task 1: 分解延迟并比较候选

**Files:** 新增 `tools/instruments/compare_matpac_windows.py`、`train_matpac_gate.py`；个人目录产生候选比较报告、冻结特征和抑制器。

**Interfaces:** 读取既有 URMP `dataset.json`、固定参考资产；输出每个候选的因果时刻分数、calibration 与保留组指标和推断耗时。模型保留时间轴的原始第 10 层嵌入供不同池化比较。

- [x] 记录当前模型窗口、更新与活动规则，确认 UI 并非低频刷新瓶颈。
- [x] 比较 19 时间片全局池化、最近 4／8 片，以及 9 时间片短上下文；拒绝直接缩窗退化候选，采用经 calibration 分组选择的七类衰减器。
- [x] 用 calibration 组选择候选并冻结，再评价保留组；准入／活动参数为产品实验规则，不冒充独立准确度验收。

```powershell
$checks = Join-Path $env:LOCALAPPDATA 'LumaLyrics-InstrumentChecks'
python tools/instruments/compare_matpac_windows.py --reference-dir "$checks/matpac-reference" --dataset "$checks/urmp-public/dataset.json" --output-dir "$checks/response-features" --split all --contexts 19,9 --save-embeddings
python tools/instruments/train_matpac_gate.py --features-dir "$checks/response-features" --baseline-dir "$checks/response-features" --output-dir "$checks/response-gate"
```

### Task 2: 活动与清单分离并接入

**Files:** `InstrumentPresentation.cs`、`InstrumentActivityFilter.cs`、`InstrumentModelCatalog.cs`、`InstrumentFileAnalysis.cs`、相应 Core／Infrastructure 测试；如候选通过则扩展 `prepare_matpac.py` 并导出新的固定图。

**Interfaces:** `InstrumentPresentation.Apply` 接收每窗分数，清单只吸收充分证据，活动亮灭采用更快且有滞回的规则；实时与整文件使用同一准入定义，不混用旧缓存。

- [x] 测试较弱证据不永久加入清单、已确认类别快速恢复／熄灭、缺帧／缺少分数中断连续准入、取消旧结果；整文件与实时使用同一清单准入。
- [x] 采用 200 ms 更新／200 ms 释放，80 ms 已观察数字静音撤销残留，750 ms 过时结果转未知；正确忽略离线尾部补零。
- [x] 原参考／Python ONNX 20 自生成加 29 试听窗误差 `1.2517e-6`；C# 20 自生成窗 `1.6094e-6`，均低于 `1e-4`。固定图重复导出一致。
- [x] 实际 Apple Music 恢复后 138 帧、零丢窗、200 ms 间隔、窗口结束到结果 P95 45.12 ms；保留鼓／镲持续活动的观察，不宣称逐鼓点完成。

```csharp
var profile = InstrumentModelCatalog.GeneralExperimental;
var presentation = new InstrumentPresentation(profile.Thresholds, profile.RosterThresholds, freshnessMs: 750);
```

### Task 3: 验证和交付

**Files:** 新增 `docs/instrument-response.md`；更新 `docs/instrument-roster.md`、`docs/instrument-activity.md`、`docs/release-notes.md`、分发脚本；独立便携预览。

- [x] locked Release 构建零警告、524 项单元测试和 277 项六主题／窗口尺寸／生命周期 UI 断言通过。
- [x] 同一个受控停奏／恢复文件、两模型、12 项转换均确认前后态；停奏判定 3555→255 ms，计算和显示时间另计。文件重放零丢窗；不把单曲或原创夹具作为全面准确度通过。
- [x] `responsive-instruments` 包通过 59 类个人文件拒绝检查及包内 277 项 UI 断言；ZIP 859 个文件逐项哈希相同。检查远端后普通提交／推送并核对远端 SHA／树，实际链接随交付回复报告。

```powershell
. ./scripts/common.ps1
Invoke-Dotnet restore LumaLyrics.slnx --locked-mode
Invoke-Dotnet test LumaLyrics.slnx -c Release --no-restore
./scripts/publish-ui.ps1 -PreviewLabel responsive-instruments
```

**剩余质量边界：** 仍有单簧管漏检及管乐混淆；未通过各种摇滚／金属编制的独立迁移验证、混音中单件乐器精确退出和逐音符／鼓点同步。产品集成与响应改善不等于这些目标完成。
