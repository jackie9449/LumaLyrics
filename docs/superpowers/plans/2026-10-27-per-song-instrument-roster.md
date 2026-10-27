# 按歌曲生成多乐器清单 Implementation Plan

> **For agentic workers:** Execute inline in this session. Do not delegate. Track each validated task below.

**Goal:** 改掉摇滚固定三类入口，统一尝试模型支持的具体乐器，按当前录音的证据显示本曲清单；保留未知与整曲分析边界，不声称识别到世界上所有乐器。

**Architecture:** 采用固定 MATPAC++ 编码器与 OpenMIC 分类头，导出完整波形 ONNX，将 20 个输出的前 19 类映射为乐器，排除人声。Core 增加稳定枚举和词汇／父级去重，Presentation 的实时与文件清单采用相同规则。新通用 profile 默认跨曲风，旧三类设置迁移到通用；保持弦乐兼容模式与旧数字枚举／缓存含义。原定直接开放 MTG 全部乐器因负样本误报被否决；新模型也存在明显误报，只以默认关闭的实验交付，不标记准确度通过。

**Tech Stack:** .NET 10、WPF、ONNX Runtime；个人目录中的固定 MATPAC++ / OpenMIC 权重、URMP 公开混音与音符标注、Python 3.12 / PyTorch 原始数值参考。

**Spec:** 用户要求“根据歌曲不同，显示不同的完整乐器”，继承 `docs/superpowers/specs/2026-10-27-instrument-activity-design.md` 的音频、身份与存储约束；不再将三类功能作为完成目标。

## Global Constraints

- 不预置歌曲乐器，不把全部可支持图标当作本曲清单，不通过曲名或流派点亮。
- 仅新增枚举值，保留原 0–7、旧模型 profile 和个人缓存的原含义。
- 全曲 WAV 分析表示完整音频覆盖，不能改写成“所有实际乐器已确认”。首次未知流媒体仍明确逐步补全。
- 具体与族级标签重叠时，已识别具体类别优先；原始分数不删除，活动与清单规则不等同于人工真值。
- 个人模型／音乐／逐窗报告留在软件目录外。模型许可证与固定哈希保持原约束。
- 切歌、模型切换、暂停、跳播、取消、界面与浮动外观隔离保持既有约束。

### Task 1：词汇与按曲清单

**Files:** `src/LumaLyrics.Core/InstrumentModels.cs`、新增 `InstrumentVocabulary.cs`、`InstrumentPresentation.cs`；`tests/LumaLyrics.Core.Tests/InstrumentPresentationTests.cs`。

**Interfaces:** `InstrumentVocabulary.Name(kind)` 提供精确中文名称，`VisibleKinds(kinds)` 返回去重后的清单；实时和绑定文件两条路径共享，状态仍为 Active/Dim/Unknown。

- [x] 测试旧数字稳定、不同录音清单不混用、未检出类别不出现、具体／父级重复去除、时间线开头暗色清单与覆盖缺口；Core 122 项通过。
- [x] 添加 14 类：手风琴、班卓琴、大提琴、单簧管、镲、长笛、键盘打击乐、曼陀林、风琴、萨克斯、长号、小号、尤克里里、小提琴；原吉他不臆造细分类。
- [x] 在两条显示路径去除已有具体类别的泛化父图标，保留细分类并存与未确定类别的族级后备。

### Task 2：通用模型与设置

**Files:** `InstrumentModelCatalog.cs`、`SettingsStore.cs`、`InstrumentController.cs`、`InstrumentSettingsPanel.cs`；`InstrumentModelTests.cs`、`InstrumentVerification.cs`。

**Interfaces:** 新 `GeneralId`／`GeneralExperimental` 包含 19 类；新图 322477291 字节，固定 SHA-256；映射、上下文、预处理与阈值进入新 profile ID；旧三类选择迁移到通用，用户开关值保留，新资料默认关闭。

- [x] 核对发布者类别、排除人声标签，默认通用；旧三类模型保留诊断重现但从产品选择中移除。
- [x] 自行下载固定权重，安装在当前个人目录；准备和 WAV 分析使用当前 profile。新图不复用旧三类权重。
- [x] 原 PyTorch / ONNX 自生成 20 窗加实际 27 窗，C# 自生成 20 窗通过 `1e-4` 门槛，最大误差 `1.8478e-6`；重复导出哈希相同。
- [x] 设置不要求每首歌手工选择曲风，明确本曲音频决定清单与有限覆盖能力。
- [x] 不同具体类别有可区分的原生几何图标；中文名称不误用旧默认合成器。

### Task 3：实际多曲验证与交付

**Files:** `InstrumentActivityView.cs`、`docs/instrument-activity.md`、`docs/instrument-roster.md`、`docs/release-notes.md`、`scripts/publish-ui.ps1`。

- [x] 实际 Paranoid 实播 30 秒取得 51 帧、零丢窗；C# 整文件检查官方试听及三种 URMP 编制，44 首稀疏观察。识别清单变化但管弦乐明显误报／漏报，记录失败，不宣布 19 类准确度通过。
- [x] 补充全零采样保护；数字静音实际文件为空清单，安静非零音频回归保留得分；缓存身份隔离保护版本。519 项单元测试、locked Release 构建、276 项六主题及 820/980/1220 宽度与生命周期检查通过；全部图标在滚动视口内。
- [x] 发布独立 `song-instruments` 预览并核对分发边界、文件内容及 Microsoft host 实际 UI；模型仅安装个人目录。另通过 259 项应用设计与 124 项滑条回归，57 种个人文件分发拒绝样例。
- [x] 重新检查远端，提交并普通推送，核对远端提交／树；明确首播完整清单依赖完整预分析、有限词汇与未通过准确度的限制。实际远端提交链接随交付回复报告。

**未完成质量目标：** 独立的广泛多曲风／19 类准确度与精确进出验收；没有将功能集成完成写成完整配器识别成功。来源、失败样例和复现见 `docs/instrument-roster.md`。
