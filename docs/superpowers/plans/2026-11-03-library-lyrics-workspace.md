# Library and Chinese lyrics workspace Implementation Plan

> **For agentic workers:** Execute inline, task-by-task. Follow AGENTS.md and the paired specification. Do not delegate or require an extra skill to interpret this plan. Unchecked steps describe future implementation.

**Goal:** 在「设置 → 资料库与歌词」共用歌曲清单、真实状态、选择与任务，完成原词、中文译文、封面及本地 agent 的连续准备流程。

**Architecture:** 保留现有个人库和准备／接收服务，增加只读公共状态投影与 profile 级操作协调器。Settings 提供总览、歌曲、任务、显示四个内部视图；音乐浏览携带原生 ID 跳转至同一实例。新的管理操作以明确目标执行，不随 ActiveTrack 改变。

**Tech Stack:** 既有 .NET 10 / C# 14、WPF、Microsoft.Data.Sqlite、System.Text.Json、xUnit、PowerShell；不增加翻译模型、网络服务或 UI 框架。

**Spec:** [资料库与中文译文整合设计](../specs/2026-11-03-library-lyrics-workspace.md)。基线 `0df09de007f0b8e3ca7e5d0fb2b5a39beb98fcac`，工作树与远端初始一致。本计划已进入实施；下方原始步骤保留为设计记录，实际交付与差异见下一节。

## 2026-11-03 实施记录

已落地统一入口、四个内部视图、按原生键选择、独立完整／部分状态、选中范围准备与批量 MD、明确目标的导入／恢复／采用／取消、全部批次和单首历史任务、歌曲／专辑管理联动。原词与译文库版本、个人备份和 agent 协议保持不变。

实际文件组织复用 `LibraryPanel`、`TranslationJobListPanel` 和 `ChineseTranslationPanel`，新增 `LibraryWorkspacePanel`、`LibraryTasksPanel`、`PlaybackLibraryWorkspace`、`LibraryWorkspaceModels/Catalog`、`LibrarySongActions`、`LibraryTaskCatalog`。状态投影与类型放在同一个 Models 文件；协调器位于现有 profile 持有的 PlaybackCoordinator partial，沿用原 provider、单实例锁和保存服务，不另建通用持久任务库。导航专项合入增强的 TranslationJobVerification，并注册两个 workspace 参数别名。

实现选择与限制：

- 管理快照在后台批量读取并共享短期读取任务，分页使用请求版本、来源与取消校验；尚未实现逐条增量解析缓存。任务读取在隐藏时取消并解绑。
- 一次冻结原生键集合，仅完整确认快照用于整库同步，封面下载只过滤选中键。原词及来源译文租约忙时逐曲说明后跳过，不并发重复请求；已有部分稿沿用，只有明确重译才替换。
- 多条原生记录共享旧译文槽时，定向写入保守拒绝。尚未提供共享槽整组影响确认界面；不会假装所选一条能独立覆盖共享内容。
- 任务页重试只重新检查失败的补缺目标，重译需要回到歌曲页重新确认。未知／缺失成员文件不猜批次内容；当前会话选择不跨重启执行。
- 音乐浏览提供带身份的管理动作，详细覆盖状态集中在管理页；音乐浏览行未重复显示完整状态列。

实际测试、运行包检查与实机边界见[验证记录](../../verification-library-workspace/README.md)。以下未勾选的历史步骤包含原先的拆文件、额外专项和性能方案，不能作为全部已完成的声明。

## Global Constraints

- 主要入口为「设置 → 资料库与歌词」，合并当前 Settings 的「资料库」与「中文译文」。
- 主窗口的音乐浏览仍负责专辑、歌曲、歌单和实际播放，提供带上下文的管理入口，进入同一个 Settings 实例。
- 部分译文不算完整；来源／手动／AI 是来源属性，不是语义已审。
- 选择以 `(Source, PersistentId)` 保存；开始操作时冻结明确的 ID 集合。
- 原词时间轴、哈希、手动资料、偏移、纯音乐行为及独立译文显示开关保留。
- 只有完整确认清单才能更新活动成员；不能把选择子集传给全库 Sync。
- 页面只在 Loaded 且可见时订阅；Unloaded/Dispose 解绑、取消视图读取并释放动画。
- 数据库、任务、歌词、译文、封面和报告仍在个人目录；沿用 lyrics schema 5、translations schema 2、备份 manifest 2、任务 JSON schema 1。
- 所有后台结果核对 source/generation，写入前核对目标／哈希／修订；忽略不遵守取消的 provider 晚到结果。
- 新版式覆盖六套应用主题与 1220/980/820，保留 Settings 原有 640 最小宽度、减少动画和键盘语义。

## 实施阶段与范围

分四个里程碑交付：A（Task 1–2）统一状态和身份；B（Task 3–4）共同准备与 Settings；C（Task 5–6）任务管理和音乐浏览联动；D（Task 7）回归与交付。每个里程碑须可独立评审，Task 1–2 完成前不迁移 UI。

本次不迁移数据库主键、不引入新的通用任务数据库、不重做 Apple Music 播放、不新增全自动 AI 推理。任务视图遇旧批次成员文件缺失时如实显示不可用；不根据标题／时间猜历史成员。跨页面保留选择，重启不自动执行选择篮，已导出的 agent 批次仍按既有协议续做。

## 文件分工

以下均为相对仓库根目录的路径。新增文件是拟实施文件，并非现有 API。

| 文件 | 职责 |
| --- | --- |
| `src/LumaLyrics.Infrastructure/LibraryWorkspaceModels.cs`（新） | 原生键、独立状态、管理快照、操作目标及进度类型 |
| `src/LumaLyrics.Infrastructure/LibraryWorkspaceProjection.cs`（新） | 纯状态／覆盖统计／筛选口径，不读写数据库 |
| `src/LumaLyrics.Infrastructure/LibraryWorkspaceCatalog.cs`（新） | 后台批量只读投影，详情按需读取和增量缓存 |
| `src/LumaLyrics.Infrastructure/MusicLibraryCatalog.cs`（改） | 返回已确认完整成员快照与缓存回退标志，供浏览和管理共用 |
| `src/LumaLyrics.Infrastructure/LibraryCoverageAudit.cs`（改） | 引用公共覆盖规则，保留既有可读报告及字段兼容 |
| `src/LumaLyrics.Infrastructure/TranslationJobExchange.cs`、`TranslationBatchExchange.cs`、`TranslationRepository.cs`（改） | 精确成员目标 API、原子读取 Content/Revision、旧接口的唯一映射检查 |
| `src/LumaLyrics.Infrastructure/LibraryPreparationCoordinator.cs`、`LibraryPreparationBackend.cs`（新） | 冻结范围、阶段编排、取消、共用 worker、最终批量导出 |
| `src/LumaLyrics.Infrastructure/LibraryPrefetcher.cs`、`LibraryArtworkImporter.cs`、`TranslationPreparation.cs`、`TranslationWorkflow.cs`（改） | 提取共用单项核心和选中范围入口，保留全库同步／原 lease／节流 |
| `src/LumaLyrics.App/LibraryWorkspaceState.cs`、`PlaybackLibraryWorkspace.cs`（新） | UI 的共用状态、选择和 profile 级协调器 partial |
| `src/LumaLyrics.App/LibraryWorkspacePanel.cs`、`LibraryOverviewPanel.cs`、`LibrarySongsPanel.cs`、`LibrarySongDetailsPanel.cs`（新） | 有界管理宿主、总览、唯一管理歌曲表、按所选目标详情 |
| `src/LumaLyrics.App/LibraryTasksPanel.cs`、`LyricsDisplayPanel.cs`（新） | 所有任务与批次、独立显示开关与示例 |
| `src/LumaLyrics.App/SettingsWindow.cs`、`LibraryWorkspaceNavigation.cs`（改／新） | 合并导航与旧入口重定向；复用 Settings 实例 |
| `src/LumaLyrics.App/LibraryPanel.cs`、`ChineseTranslationPanel.cs`、`TranslationJobListPanel.cs`（改／移除） | 迁出可复用控件和导入逻辑，移除重复列表及 ActiveTrack 管理实现 |
| `src/LumaLyrics.App/PlaybackCoordinator.cs`、`PlaybackTranslationJobs.cs`、`PlaybackTranslations.cs`（改） | 生命周期、按明确目标管理的服务入口、逐曲状态失效通知 |
| `src/LumaLyrics.App/MusicLibraryNavigation.cs`、`MusicLibraryView.cs`、`MusicLibraryAlbumNavigation.cs`（改） | 歌曲／专辑的管理跳转、轻量状态；播放命令继续原路 |
| `src/LumaLyrics.Infrastructure/LibraryTaskCatalog.cs`（新） | 任务与批次只读投影、受控分块校验、缺失来源说明 |
| `src/LumaLyrics.App/LibraryWorkspaceVerification.cs`、`LibraryWorkspaceNavigationVerification.cs`（新） | 实际 WPF 操作与选择／来源／生命周期专项 |
| 既有 `TranslationVerification.cs`、`TranslationJobVerification.cs`、`ResponsiveLayoutVerification.cs`、`UiThemeVerification.cs` | 按新导航更新定位，保持原语义断言 |
| 测试和公共指南 | 下面各任务分别列出；行为实现后才更新 release notes 和运行包文档 |

## 共用类型与接口约定

### 身份与状态（Task 1）

```csharp
public readonly record struct LibrarySongKey(string Source, string PersistentId)
{
    public static IEqualityComparer<LibrarySongKey> Comparer { get; } = new KeyComparer();
    sealed class KeyComparer : IEqualityComparer<LibrarySongKey>
    {
        public bool Equals(LibrarySongKey a, LibrarySongKey b) =>
            StringComparer.OrdinalIgnoreCase.Equals(a.Source, b.Source) &&
            StringComparer.Ordinal.Equals(a.PersistentId, b.PersistentId);
        public int GetHashCode(LibrarySongKey key) => HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(key.Source),
            StringComparer.Ordinal.GetHashCode(key.PersistentId));
    }
}
public enum LibraryOriginalState { Missing, Unconfirmed, ManualPlain, Ready, Instrumental }
public enum LibraryChineseState { BlockedOriginal, NotRequired, Missing, Partial, Complete, Outdated }
public enum LibraryCoverState { Missing, Ready, HighResolution, Unreadable }
public sealed record LibraryStatusInput(LyricsDocument? Document,
    bool ConfirmedOriginal, bool ManualOriginal, ChineseTranslation? Translation,
    bool ManualTranslation, bool HasOlderTranslation, string? Revision,
    LibraryCoverState Cover);
public sealed record LibrarySongStatus(LibraryOriginalState Original,
    LibraryChineseState Chinese, int RequiredGroups, int TranslatedGroups,
    bool ManualChinese, bool AiDraft, LibraryCoverState Cover,
    string? SourceHash, string? Revision, string? Reason);
public sealed record LibraryManagedSong(LibrarySongKey Key, Track Track,
    string? BindingId, LibrarySongStatus Status, string? OriginalSource = null,
    string? TranslationSource = null, string? CoverPath = null);
public sealed record LibraryWorkspaceSnapshot(string ProfileRoot, string Source,
    long Generation, bool IsStale, IReadOnlyList<LibraryManagedSong> Songs,
    string? Message = null);
// 新增 static LibraryWorkspaceProjection。
public static LibrarySongStatus Assess(LibraryStatusInput input);
// 新增 LibraryWorkspaceCatalog(string root)。输入必须来自已确认的完整 native/cache 清单。
public Task<LibraryWorkspaceSnapshot> ReadAsync(LibrarySnapshot confirmedMembership,
    long generation, CancellationToken token);
// MusicLibraryResult 新增只读投影来源，不修改持久文件格式。
public LibrarySnapshot? ConfirmedMembership { get; init; }
public bool MembershipIsCached { get; init; }
```

所有以键索引的 Dictionary、HashSet 和集合比较显式使用 LibrarySongKey.Comparer：来源路径 OrdinalIgnoreCase、PersistentId Ordinal，不依赖 positional record 的默认比较，不进行歌名模糊匹配。入口拒绝空 Source/PersistentId 后再加入集合。完整／部分以必要 GroupIndex 覆盖计算，中文主要原词及纯音乐依现有规则标为无需翻译。`IsStale=true` 快照可浏览，写入需重新验证来源，不能据此直接开始动作。

Generation 是 profile／资料库来源的 epoch，切换来源或 profile 时递增，不因本轮自身补齐一首歌而递增。同来源的多次异步读取另外用页面的 RequestVersion 递增核对，防止较早读取覆盖较新状态；这两个版本号不能共用。

### 操作与后台（Task 2–3）

```csharp
public sealed record LibraryPreparationTarget(LibrarySongKey Key, Track Track,
    string? BindingId, string? SourceHash, string? ExpectedTranslationRevision);
public enum LibraryPreparationAction { PrepareMissing, FindExistingChinese, ExportAgent, Retranslate }
public enum LibraryPreparationStage { Original, ChineseSource, AgentExport, Artwork }
public enum LibraryPreparationDisposition { Ready, NeedsAgent, Blocked, Failed, Cancelled, Conflict }
public sealed record LibraryPreparationRequest(Guid OperationId, string ProfileRoot,
    string Source, long Generation, IReadOnlyList<LibraryPreparationTarget> Targets,
    LibraryPreparationAction Action, bool OnlineLookup, bool IncludeArtwork);
public sealed record LibraryPreparationStep(LibraryPreparationTarget Target,
    LibraryPreparationStage Stage, LibraryPreparationDisposition Disposition,
    string Message);
public sealed record LibraryTranslationBatchResult(TranslationBatchTicket? Batch,
    IReadOnlyDictionary<LibrarySongKey, TranslationJobTicket> Jobs,
    IReadOnlyDictionary<LibrarySongKey, string> Skipped);
public sealed record LibraryPreparationResult(Guid OperationId,
    IReadOnlyList<LibraryPreparationStep> Outcomes, LibraryTranslationBatchResult? AgentBatch);
public interface ILibraryPreparationBackend
{
    Task<LibraryPreparationStep> InspectAsync(LibraryPreparationTarget target, CancellationToken token);
    Task<LibraryPreparationStep> EnsureOriginalAsync(LibraryPreparationTarget target, bool onlineLookup, CancellationToken token);
    Task<LibraryPreparationStep> FindChineseAsync(LibraryPreparationTarget target, bool onlineLookup, CancellationToken token);
    Task<LibraryPreparationStep> EnsureArtworkAsync(LibraryPreparationTarget target, bool onlineLookup, CancellationToken token);
    Task<LibraryTranslationBatchResult> ExportAsync(IReadOnlyList<LibraryPreparationTarget> targets,
        TranslationJobMode mode, CancellationToken token);
}
// 新增 LibraryPreparationCoordinator(ILibraryPreparationBackend backend)。
public event Action<LibraryPreparationStep>? Progress;
public Task<LibraryPreparationResult> RunAsync(LibraryPreparationRequest request,
    CancellationToken token);
// TranslationJobExchange 的新入口，不改变持久请求格式。
public static TranslationJobTicket CreateForLibrarySong(string root,
    LibraryPreparationTarget expected, TranslationJobMode mode);
// TranslationBatchExchange 的新入口；旧 Track 入口只接受唯一映射。
public static LibraryTranslationBatchResult CreateForLibrarySelection(string root,
    IReadOnlyList<LibraryPreparationTarget> targets, TranslationJobMode mode,
    int chunkSize = 5, CancellationToken token = default);
```

`Targets` 在提交时复制并冻结。`PrepareMissing` 每首先 EnsureOriginal，再 FindChinese；已完整或无需中文直接结束，只有 NeedsAgent 集合进行一次 Export。其余动作先 Inspect；FindExistingChinese 只查译文、ExportAgent 不联网、Retranslate 不下载新原词或查来源。封面是可选且独立的阶段，失败不阻断已有原词／译文成果。

`LibraryPreparationBackend(string root, ILyricsProvider originals, ITranslationProvider translations, Func<bool> onlineLookup, Func<(string Source, long Generation)> currentScope)` 通过现有 worker 实现接口，函数用于校验实时联网开关和来源 epoch。生产实例由 PlaybackCoordinator 构造；测试注入原创 provider。封面测试沿用 LibraryArtworkImporter 已有 HttpMessageHandler 注入。

Outcomes 按原生键输出最终结果，完整进度可多阶段，但统计按键只计一次。允许一份实际 Job 被多条明确关联记录引用时，Jobs 字典可以多个键指向同一 ticket，工作份数按不同 JobId 统计。禁止按 Track.Distinct 丢掉歌曲行。

### 页面与导航（Task 4–6）

```csharp
public enum LibraryWorkspacePage { Overview, Songs, Tasks, Display }
public sealed record LibraryWorkspaceNavigation(LibraryWorkspacePage Page,
    string? Source = null, IReadOnlyList<LibrarySongKey>? Songs = null);
// 新增 MainWindow partial，复用 SettingsClicked 建立的唯一 SettingsWindow。
internal void OpenLibraryWorkspace(LibraryWorkspaceNavigation request);
// SettingsWindow 新增；旧 SelectLibraryTab/SelectTranslationTab 保留包装入口。
internal void SelectLibraryWorkspace(LibraryWorkspaceNavigation request);
// 新增 LibraryWorkspaceState，属于当前 profile，不是静态全局状态。
public void ApplySnapshot(LibraryWorkspaceSnapshot snapshot);
public void SetSelected(IEnumerable<LibrarySongKey> keys);
public IReadOnlyList<LibraryPreparationTarget> CaptureSelection();
public int SelectedCount { get; }
// LibraryWorkspacePanel 暴露当前可见视图是否正在观察，不属于 State 的歌曲选择。
public bool IsSubscribed { get; }
```

`CaptureSelection` 返回当前已确认快照中实际存在的键、Track、绑定／哈希和 Revision；过时或歧义则明确拒绝。管理动作始终使用这个快照目标，不在完成回调中重新读取 ActiveTrack。

兼容定位可暂保留 `SettingsWindow.Library => LibraryWorkspace`、`SettingsWindow.ChineseTranslations => LibraryWorkspace.Display` 和 `LibraryWorkspace.SourcePath => Overview.SourcePath`，不创建额外控件实例；Display 保留测试使用的 SetVisibility 方法。源位置和来源详情改位置的旧断言要验证新实际目标。

## Task 1：统一身份、覆盖状态与只读查询

**Files:** 新增 Models、Projection、Catalog；修改 MusicLibraryCatalog、LibraryCoverageAudit；新增 `tests/LumaLyrics.Infrastructure.Tests/LibraryWorkspaceProjectionTests.cs`、`LibraryWorkspaceCatalogTests.cs`。

**Interfaces:** 输入确认成员与现有原词／译文／封面记录；产出上述 Assess 和 ReadAsync。此任务不迁移导航，不写个人数据。

- [ ] **Step 1：先写状态失败测试。** 至少覆盖完整、部分、旧哈希、手动静态、原词缺失、纯音乐与无需翻译；下面测试必须先由不存在的新投影导致失败，不能删弱覆盖断言。

```csharp
[Fact]
public void ExistingPartialTranslationDoesNotCountAsComplete()
{
    var doc = new LrcParser().Parse("[00:01]We sail home\n[00:04]Across the bay\n[00:08]Under the stars");
    var translation = new ChineseTranslation(TranslationAlignment.Hash(doc.Raw),
        [new(0, "我们驶向家乡")], "Own manual", "manual", "Own review", DateTimeOffset.UtcNow);
    var status = LibraryWorkspaceProjection.Assess(new(doc, true, true,
        translation, true, false, "own-revision", LibraryCoverState.Ready));
    Assert.Equal(LibraryChineseState.Partial, status.Chinese);
    Assert.Equal(3, status.RequiredGroups);
    Assert.Equal(1, status.TranslatedGroups);
    Assert.True(status.ManualChinese);
}
```

- [ ] **Step 2：实现纯投影。** 从 Document 的实际必要组计算 R；原词不可靠／静态则 BlockedOriginal，纯音乐和沿用现有中文判定则 NotRequired，旧哈希不计当前覆盖，覆盖零／部分／全量分别 Missing／Partial／Complete。把这段计算同时交给 LibraryCoverageAudit 调用，保持旧报告字段名称和原生重复计数。
- [ ] **Step 3：实现共用成员来源及后台批读。** MusicLibraryResult 暴露 Reader 已核验的 ConfirmedMembership；离线只能由同来源已确认的缓存成员构造，并设 MembershipIsCached=true，来源不可唯一确定时不猜第一个。Playlist.Tracks 不能扩大 Membership.Tracks。profile 所属协调器合并浏览／管理并发刷新，复用该完整快照；缓存回退显示“离线核验清单”，不冒充新读取的 native 总数。按快照的 Source/PersistentId 批量读取绑定、Content+Manual+Revision、封面索引与轻量任务字段，初始每库至多 6 个批量查询，不按每行开连接；只解析变化的原词绑定，详情才加载完整文本。原词质量仍用 QualityOf，不能用 cache.State 独自证明完整。
- [ ] **Step 4：加入实际只读／身份测试。** 一个原创 snapshot 含相同 Track 的两个 PersistentId、另一个不同来源、一个非成员历史缓存；断言前两行均保留，非成员不入表，原词字节和数据库内容不变，旧 Source/generation 的结果不能替换新快照。异常保留上次同来源快照并设 IsStale。
- [ ] **Step 5：运行并提交。**

```powershell
. ./scripts/common.ps1
Invoke-Dotnet test tests/LumaLyrics.Infrastructure.Tests -c Release --filter 'FullyQualifiedName~LibraryWorkspaceProjectionTests|FullyQualifiedName~LibraryWorkspaceCatalogTests|FullyQualifiedName~LibraryCoverageAuditTests'
git add src/LumaLyrics.Infrastructure/LibraryWorkspaceModels.cs src/LumaLyrics.Infrastructure/LibraryWorkspaceProjection.cs src/LumaLyrics.Infrastructure/LibraryWorkspaceCatalog.cs src/LumaLyrics.Infrastructure/MusicLibraryCatalog.cs src/LumaLyrics.Infrastructure/LibraryCoverageAudit.cs tests/LumaLyrics.Infrastructure.Tests/LibraryWorkspaceProjectionTests.cs tests/LumaLyrics.Infrastructure.Tests/LibraryWorkspaceCatalogTests.cs
git commit -m "feat: unify library lyric and translation status projection"
```

**完成条件：** 同一首的 UI 和报告使用一致口径；完整／部分不混淆，原生身份与只读性有实际测试。

## Task 2：精确目标贯穿导出、导入、恢复和部分采用

**Files:** 修改 TranslationJobExchange、TranslationBatchExchange、TranslationRepository；新增 `LibraryTranslationTargetTests.cs`；复用已有 TranslationJobTests 的原创数据模式。

**Interfaces:** 新增两个按 LibraryPreparationTarget 的入口；原 JSON 格式不变，明确写入目标 Source/PersistentId。旧 Track API 对唯一匹配保持兼容，歧义返回具体错误。

- [ ] **Step 1：先写身份与修订失败测试。** 建立两个相同 Track／不同 PersistentId 的确认成员，分别传入新 API；断言请求里的 PersistentId 精确匹配所选键，没有 FirstOrDefault。另用相同标题／不同艺人、来源或时长的行，断言不能跨录音关联。
- [ ] **Step 2：修正原子读取与注册。** FindActive 一次 SELECT 同时返回 Content、Manual、Revision，再检查当前原词与可用分组；不能先读正文再用另一次查询拼一个更新后的修订号。CreateForLibrarySong 新增短事务内的注册复查，复用现有接收的 ATTACH + BEGIN IMMEDIATE 思路核对 expected 的成员、绑定、哈希、Revision，仅写译文库；不能悄悄重取新版本授权，文件导出置于事务外。
- [ ] **Step 3：处理旧存储槽复用。** 保留每条原生行；完全相同绑定的补缺可共用工作，返回按键映射。只选部分关联记录进行替换时返回明确的关联冲突，UI列出关联项后由用户选择完整范围再提交；不变更数据库主键。单首入口遇共享槽的定向替换直接拒绝；批量入口先验证整个组，只有 BindingId、SourceHash、当前 Revision 一致且关联范围完整时才通过内部组注册、产生一个真实 JobId 并映射所有关联键，不能逐首绕过单首拒绝。不同绑定／录音／时长的旧键碰撞仍拒绝，即使全选也不允许写入。当前播放的非成员单曲保留旧功能，但不伪造 LibrarySource/PersistentId。
- [ ] **Step 4：抽取按明确目标的管理入口。** LRC导入、历史读取／恢复、部分采用、取消按捕获目标执行；文件选择期间切歌、后台更新原词和用户手动保存均触发对应复查。既有 partial result hash 和修订 CAS 检查保留；不能仅用中文文件的时间戳确认目标。
- [ ] **Step 5：运行新身份测试与原有任务／手动保护测试后提交。**

```powershell
. ./scripts/common.ps1
Invoke-Dotnet test tests/LumaLyrics.Infrastructure.Tests -c Release --filter 'FullyQualifiedName~LibraryTranslationTargetTests|FullyQualifiedName~TranslationJobTests|FullyQualifiedName~TranslationStorageTests|FullyQualifiedName~TranslationDraftTests'
git add src/LumaLyrics.Infrastructure/TranslationJobExchange.cs src/LumaLyrics.Infrastructure/TranslationBatchExchange.cs src/LumaLyrics.Infrastructure/TranslationRepository.cs tests/LumaLyrics.Infrastructure.Tests/LibraryTranslationTargetTests.cs
git commit -m "fix: retain native song identity in lyric management actions"
```

**完成条件：** 所选 B 与正在播放 A 可完全分离；重复记录不丢失，任何重译都不能借新建任务覆盖期间更新的版本。

## Task 3：共同准备阶段与应用生命周期

**Files:** 新增 LibraryPreparationCoordinator、LibraryPreparationBackend；修改既有四个 worker、PlaybackCoordinator；新增 `PlaybackLibraryWorkspace.cs`、`LibraryPreparationCoordinatorTests.cs`、`LibraryPreparationScopeTests.cs`。

**Interfaces:** 实现共用 ILibraryPreparationBackend、RunAsync；应用为当前 profile 持有一个协调器，页面只观察。原 worker 的全库入口继续存在并复用同一单项核心。

- [ ] **Step 1：写集成失败场景。** 原创三首 A/B/C：A缺原词，B原词就绪但无译文，C已有完整手动译文；仅选择 A/B。Fake ILyricsProvider/ITranslationProvider 返回原创、通过既有标准的配对，实际执行后断言 C 的 Raw、译文和 Revision 不变，A/B进入正确阶段，最终只有一个批量入口。沿用已有 provider 测试的签名，不调用真实网络。
- [ ] **Step 2：实现按请求冻结的流程。** PrepareMissing 顺序为 EnsureOriginal → FindChinese → 汇总 NeedsAgent 后一次 Export；原词阶段取得可靠新绑定后捕获新的 hash供本轮下游使用；原词仍受阻则结束该首。FindExistingChinese／ExportAgent／Retranslate先 Inspect，按接口约定跳过不适用的阶段。可选 Artwork独立执行，不能拖掉已成功译文。

```csharp
// RunAsync 的主流程；backend 为构造时注入的 ILibraryPreparationBackend。
if (request.Action != LibraryPreparationAction.PrepareMissing && request.IncludeArtwork)
    throw new ArgumentException("快捷动作不同时准备封面，请使用准备所选。");
var targets = request.Targets.ToArray();
var outcomes = new List<LibraryPreparationStep>();
var exports = new List<LibraryPreparationTarget>();
foreach (var target in targets)
{
    token.ThrowIfCancellationRequested();
    var inspected = request.Action == LibraryPreparationAction.PrepareMissing
        ? await backend.EnsureOriginalAsync(target, request.OnlineLookup, token)
        : await backend.InspectAsync(target, token);
    if (request.IncludeArtwork)
        Progress?.Invoke(await backend.EnsureArtworkAsync(target, request.OnlineLookup, token));
    if (inspected.Disposition != LibraryPreparationDisposition.Ready)
    {
        outcomes.Add(inspected);
        Progress?.Invoke(inspected);
        continue;
    }
    if (request.Action is LibraryPreparationAction.ExportAgent or LibraryPreparationAction.Retranslate)
    {
        exports.Add(inspected.Target);
        continue;
    }
    var chinese = await backend.FindChineseAsync(inspected.Target, request.OnlineLookup, token);
    outcomes.Add(chinese);
    Progress?.Invoke(chinese);
    if (chinese.Disposition == LibraryPreparationDisposition.NeedsAgent &&
        request.Action == LibraryPreparationAction.PrepareMissing)
        exports.Add(chinese.Target);
}
token.ThrowIfCancellationRequested();
var batch = exports.Count == 0 ? null : await backend.ExportAsync(exports,
    request.Action == LibraryPreparationAction.Retranslate
        ? TranslationJobMode.ReplaceExpected : TranslationJobMode.FillMissing, token);
if (batch is not null)
{
    foreach (var target in exports)
    {
        bool created = batch.Jobs.ContainsKey(target.Key);
        var step = new LibraryPreparationStep(target, LibraryPreparationStage.AgentExport,
            created ? LibraryPreparationDisposition.NeedsAgent : LibraryPreparationDisposition.Blocked,
            created ? "已导出，等待 agent" : batch.Skipped.GetValueOrDefault(target.Key) ?? "未能导出任务");
        outcomes.RemoveAll(old => LibrarySongKey.Comparer.Equals(old.Target.Key, target.Key));
        outcomes.Add(step);
        Progress?.Invoke(step);
    }
}
return new LibraryPreparationResult(request.OperationId, outcomes, batch);
```

backend 的网络许可为请求冻结值与当前全局联网设置的交集；每个方法返回前都进行目标／取消复查，并将可预期单项失败转换为 Step，不让单项错误吞掉全批结果。页面统计等待 agent 的数量以 batch.Jobs 为准，不把 NeedsAgent 候选直接当成已导出任务。Artwork进度独立呈现，Outcomes的中文结果不因封面失败被覆盖。

- [ ] **Step 3：加入选中范围入口而不重置整库。** 原词 worker 先同步确认的完整 membership，选中入口只过滤候选键；封面同理仅筛队列但保留全部 Active。现有 lease若被全库任务占用，按可取消的最多 15 秒等待／共享已有结果，仍忙则返回受阻原因，不能发第二套重复请求。封面仅按已确认 ImageKey 共享缓存，不据相似专辑名推断。
- [ ] **Step 4：处理暂停、联网和晚到结果。** 同 profile 操作串行；前台查词继续优先。每次 provider 返回后及写库／注册任务之前检查 token、当前来源、generation和绑定／Revision。关闭联网立即取消相关查询，网络设置重新开启不恢复旧请求。连续来源错误暂停该阶段，并用真实原因输出可导出的安全子集。
- [ ] **Step 5：验证生命周期和范围。** 用实际 SQLite fixture 检查未选行 Active 不变、provider调用仅落在已选集合、忽略取消的 provider不写库；页面卸载仍能从后台收到完成结果，显式取消只作用于对应操作，已接收 agent任务不撤销。应用退出取消并等待，不遗留 watcher／timer／token订阅。
- [ ] **Step 6：运行并提交。**

```powershell
. ./scripts/common.ps1
Invoke-Dotnet test tests/LumaLyrics.Infrastructure.Tests -c Release --filter 'FullyQualifiedName~LibraryPreparationCoordinatorTests|FullyQualifiedName~LibraryPreparationScopeTests|FullyQualifiedName~LibraryMembershipTests|FullyQualifiedName~TranslationPreparationTests'
git add src/LumaLyrics.Infrastructure/LibraryPreparationCoordinator.cs src/LumaLyrics.Infrastructure/LibraryPreparationBackend.cs src/LumaLyrics.Infrastructure/LibraryPrefetcher.cs src/LumaLyrics.Infrastructure/LibraryArtworkImporter.cs src/LumaLyrics.Infrastructure/TranslationPreparation.cs src/LumaLyrics.Infrastructure/TranslationWorkflow.cs src/LumaLyrics.App/PlaybackCoordinator.cs src/LumaLyrics.App/PlaybackLibraryWorkspace.cs tests/LumaLyrics.Infrastructure.Tests/LibraryPreparationCoordinatorTests.cs tests/LumaLyrics.Infrastructure.Tests/LibraryPreparationScopeTests.cs
git commit -m "feat: prepare selected original and Chinese lyrics in one workflow"
```

**完成条件：** 无原词的选中歌曲可继续到来源译文或一份 MD；缩小范围不影响整库，不在隐藏页面上重复启动 worker。

## Task 4：合并 Settings，提供唯一管理表和所选详情

**Files:** 新增 LibraryWorkspaceState、LibraryWorkspacePanel、LibraryOverviewPanel、LibrarySongsPanel、LibrarySongDetailsPanel、LibraryTasksPanel、LyricsDisplayPanel、LibraryWorkspaceNavigation；修改 SettingsWindow 和现有三个 panel；新增 LibraryWorkspaceVerification。

**Interfaces:** 实现共用页面／导航接口；旧 `SelectLibraryTab` → Overview，`SelectTranslationTab` → Display。Settings 内持有唯一 workspace；旧测试直接访问显示控件的包装入口可暂保留，不能再创建旧重复列表。

- [ ] **Step 1：新增 `--verify-library-workspace` 实际 WPF 专项。** 验证导航仅一个合并项、四个内部视图；通过实际 checkbox/按钮生成范围，断言旧两个顶级项不再存在。先运行看到未实现失败。
- [ ] **Step 2：实现有界布局和虚拟化。** 使用 Grid 分配工具栏、表格视口、所选详情／窄窗折叠区；表格不包进无限高度父滚动。3003 首原创记录下生成容器不超过 120 个，排序和状态更新不清空有效选择或滚动位置。显示已选 N／可见 M、查看所选、全选当前完整筛选快照。
- [ ] **Step 3：总览、任务与显示分别工作。** 资料库路径选择、原词／封面准备和概要迁入 Overview；原有两显示开关和小示例迁入 Display，仍调用 SetTranslationVisibility。库覆盖与本轮处理分开，错误／缺失不计为可用完成；中文分母说明原词受阻数。任务视图本任务先展示现有服务读出的真实逐首任务、所选任务原因及受控的最近批量 MD，不能放空白占位页；Task 5 再增加全部批次分组和批次级操作。
- [ ] **Step 4：管理所选目标。** 所选详情打开 B 的原词来源、译文覆盖、历史和任务；LRC选择、恢复、采用、取消使用捕获的 B。当前播放变化只更新高亮；定位播放行由用户主动触发。普通补齐一次操作，重译一次摘要确认，关联旧存储槽影响需说明。
- [ ] **Step 5：接入共用状态和生命周期。** Loaded/可见时订阅，合并变化后后台更新投影；Unloaded解绑且取消旧视图查询，后台工作不随页面 Dispose被取消。旧来源结果不得覆盖新来源；切库或读取失败保留上下文并禁用不能核验的动作，不自动改为默认个人目录。
- [ ] **Step 6：验证选择与当前播放分离。** 选 B、播放 A，再开始管理动作、切 C、刷新和切内部视图；实际断言如下。new 专项中的变量在操作前捕获，按 fixture结果读取，不以更改标签文字代替行为检查。

```csharp
checks["workspace.targetRemainsSelectedB"] = completedTarget.Key == selectedB.Key;
checks["workspace.playbackUnaffected"] = coordinator.ActiveTrack == playingC;
checks["workspace.selectionSurvivesRefresh"] = state.CaptureSelection()
    .Select(t => t.Key).ToHashSet(LibrarySongKey.Comparer).SetEquals(capturedSelection);
checks["workspace.noDuplicateSettings"] = Application.Current.Windows
    .OfType<SettingsWindow>().Count() == 1;
checks["workspace.hiddenDetaches"] = !workspace.IsSubscribed;
```

- [ ] **Step 7：更新旧验证的定位并提交。** 响应式检查保留“嵌入 Settings／关闭解绑／同实例复用”，原来恰好四个按钮及固定 TabIndex 的断言按新结构改为实际功能检查；检查 ApplicationDesignVerification 的 Library.SourcePath 定位和 QueueReorderingVerification 的旧导航调用。译文原词保持、开关、待校对隐藏、原生播放等语义断言不放宽。提交仅包含上述列明 UI 文件和相应测试定位。

```powershell
git diff --check
git add src/LumaLyrics.App/LibraryWorkspaceState.cs src/LumaLyrics.App/LibraryWorkspacePanel.cs src/LumaLyrics.App/LibraryOverviewPanel.cs src/LumaLyrics.App/LibrarySongsPanel.cs src/LumaLyrics.App/LibrarySongDetailsPanel.cs src/LumaLyrics.App/LibraryTasksPanel.cs src/LumaLyrics.App/LyricsDisplayPanel.cs src/LumaLyrics.App/LibraryWorkspaceNavigation.cs src/LumaLyrics.App/SettingsWindow.cs src/LumaLyrics.App/LibraryPanel.cs src/LumaLyrics.App/ChineseTranslationPanel.cs src/LumaLyrics.App/TranslationJobListPanel.cs src/LumaLyrics.App/LibraryWorkspaceVerification.cs src/LumaLyrics.App/TranslationVerification.cs src/LumaLyrics.App/TranslationJobVerification.cs src/LumaLyrics.App/ResponsiveLayoutVerification.cs src/LumaLyrics.App/ApplicationDesignVerification.cs src/LumaLyrics.App/QueueReorderingVerification.cs
git commit -m "feat: merge library and Chinese lyric management in Settings"
```

**完成条件：** 用户只维护一份管理选择，不必通过播放来定位待管理的歌；开关与实际播放不受批量动作影响。

## Task 5：全部批次、逐首原因与目标明确的重试

**Files:** 新增 LibraryTaskCatalog；扩展 Task 4 已可工作的 LibraryTasksPanel；修改 PlaybackLibraryWorkspace、PlaybackTranslationJobs、TranslationJobExchange 的轻量查询入口；新增 `LibraryTaskCatalogTests.cs`。

**Interfaces:** `LibraryTaskCatalog(string root)` 只读当前资料库／全部个人任务，提供 `Task<LibraryTaskSnapshot> ReadAsync(string? source,CancellationToken token)`。新增 `LibraryTaskSnapshot(IReadOnlyList<LibraryTaskBatch> Batches,IReadOnlyList<TranslationJobSnapshot> Jobs)` 与 `LibraryTaskBatch(string Id,string Source,string? EntryPath,bool MembershipAvailable,IReadOnlyList<string> JobIds,string? Reason)`，放在 LibraryWorkspaceModels；任务操作仍使用原 Import/Cancel/ApprovePartial/RestoreHistory 服务。

- [ ] **Step 1：先写任务投影测试。** 用现有导出服务生成两个原创批次，断言都列出；提交一个结果但不接收，状态仅为已提交；导入后才已接收。删除一个登记分块，断言该批 MembershipAvailable=false，原因明确且不猜成员。
- [ ] **Step 2：校验旧文件与身份。** 仅从数据库登记 Manifest读取；按个人目录边界核对 batch/chunk路径及 SHA，逐项验证 JobId/RequestHash/Source/PID。迁移 profile后不读旧路径，显示历史任务与重新生成入口。数据库任务终态优先于磁盘陈旧回执，agent-progress不提升任务状态。
- [ ] **Step 3：实现批次与单首操作。** 有效批次可打开／复制 MD，筛出真实失败项重新检查；共享 JobId取消影响需展示。重译的重试重新展示当前版本，不复用旧授权；已接收成果保留。缺失成员批次仅查看可确认记录，不做不明确的整批取消或自动重试。
- [ ] **Step 4：减少高频读取。** 总览读汇总、歌曲页读轻量按键状态，任务页需要时才读完整请求／文本。只对可见视图订阅，变更合并；原 TranslationJobMessage 不再作为所有来源唯一状态，不让另一首的错误覆盖当前选中详情。
- [ ] **Step 5：运行并提交。**

```powershell
. ./scripts/common.ps1
Invoke-Dotnet test tests/LumaLyrics.Infrastructure.Tests -c Release --filter 'FullyQualifiedName~LibraryTaskCatalogTests|FullyQualifiedName~TranslationJobTests|FullyQualifiedName~PersonalLibraryTests'
git add src/LumaLyrics.Infrastructure/LibraryTaskCatalog.cs src/LumaLyrics.Infrastructure/LibraryWorkspaceModels.cs src/LumaLyrics.App/LibraryTasksPanel.cs src/LumaLyrics.App/PlaybackLibraryWorkspace.cs src/LumaLyrics.App/PlaybackTranslationJobs.cs src/LumaLyrics.Infrastructure/TranslationJobExchange.cs tests/LumaLyrics.Infrastructure.Tests/LibraryTaskCatalogTests.cs
git commit -m "feat: show library preparation and agent batches in one task view"
```

**完成条件：** 所有可核验批次可继续，失败原因能定位到歌，历史／缺文件不伪装成当前可执行任务。

## Task 6：音乐浏览带上下文进入同一管理页

**Files:** 修改 MusicLibraryNavigation、MusicLibraryView、MusicLibraryAlbumNavigation、LibraryWorkspaceNavigation；新增 LibraryWorkspaceNavigationVerification；必要状态绑定位于 MusicLibraryArtworkRefresh 的既有可见性合并机制中。

**Interfaces:** 歌曲／专辑操作调用 `OpenLibraryWorkspace(new(Songs, source, keys))`，只传原生 ID，不传歌词全文或播放命令。旧缓存管理快捷入口进入 Overview。

- [ ] **Step 1：实际 WPF 导航失败测试。** 在已有音乐浏览的原创 3003 首fixture上打开专辑、设置搜索与滚动，点击管理其中一首／该专辑；断言 workspace目标键相同、Settings实例唯一，返回后浏览上下文不变。
- [ ] **Step 2：管理按钮与状态。** 添加歌曲行管理按钮／专辑管理动作，沿用 IsSongAction 防止事件冒泡为 BeginPlay；简短原词／译文状态引用同一投影。专辑分组和虚拟化不改变，暂不增加另一个管理歌曲表。
- [ ] **Step 3：键盘和播放隔离。** 歌曲行 Enter／双击仍走原生播放；焦点位于管理按钮时 Enter只开管理页。管理动作不调用 Play、Play Next、queue read，不恢复 Apple Music窗口，也不修改搜索 FilterTextBox。实际播放最新请求取消与媒体确认通道原样保留。
- [ ] **Step 4：回归并提交。** 跑本专项和既有音乐浏览／资料库成员／AlbumPlayback／Playlist／queue专项；记录真实 UI和原生命令边界，不能从这次只读管理 UI声称已重测真实 Apple Music。

```powershell
git add src/LumaLyrics.App/MusicLibraryNavigation.cs src/LumaLyrics.App/MusicLibraryView.cs src/LumaLyrics.App/MusicLibraryAlbumNavigation.cs src/LumaLyrics.App/MusicLibraryArtworkRefresh.cs src/LumaLyrics.App/LibraryWorkspaceNavigation.cs src/LumaLyrics.App/LibraryWorkspaceNavigationVerification.cs
git commit -m "feat: open lyric management for browsed songs and albums"
```

**完成条件：** 选歌时能直接进入准确的管理范围，播放和原生队列完全保留既有语义。

## Task 7：完整验收、文档、预览与同步

**Files:** 更新 README、`docs/chinese-translations.md`、`docs/library-assets.md`、`docs/music-library.md`、`docs/personal-library.md`、`docs/manual-test-checklist.md`、release notes；新增公开 `docs/verification-library-workspace/README.md`；修改 UiThemeVerification 和 publish-ui 的公共文档复制列表。

**Interfaces:** 注册 `--verify-library-workspace` 与 `--verify-library-workspace-navigation`。沿用 bundled SDK、隔离 --data-dir 和 Microsoft host，不为本次规划运行真实个人库。

- [ ] **Step 1：构建、完整单元测试与分发检查。**

```powershell
. ./scripts/common.ps1
Invoke-Dotnet restore LumaLyrics.slnx --locked-mode
Invoke-Dotnet build LumaLyrics.slnx -c Release --no-restore
Invoke-Dotnet test LumaLyrics.slnx -c Release --no-build --no-restore
./scripts/test-distribution.ps1
./scripts/publish-ui.ps1 -PreviewLabel library-lyrics-workspace
```

- [ ] **Step 2：运行包实际 UI 专项。** 从当前 Version读包名，每项新建独立目录；运行新两项、既有 translations、translation-jobs、responsive-layout、theme-collection。新增专项覆盖六主题和 1220/980/820/640、减少动画／系统动画关闭、焦点、筛选选择、3003首虚拟化、取消和来源变化，逐项核对 JSON而非只看进程退出码。

```powershell
. ./scripts/common.ps1
$workspaceVersion = ([xml](Get-Content -LiteralPath './Directory.Build.props' -Raw)).Project.PropertyGroup.Version
$workspacePackage = Join-Path $projectRoot "artifacts/LumaLyrics-$workspaceVersion-ui-preview-library-lyrics-workspace-win-x64"
$workspaceCheck = Join-Path $env:LOCALAPPDATA ('LumaLyrics-WorkspaceChecks/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $workspaceCheck | Out-Null
& (Join-Path $workspacePackage 'runtime/dotnet.exe') exec (Join-Path $workspacePackage 'LumaLyrics.dll') --data-dir (Join-Path $workspaceCheck 'data') --verify-ui (Join-Path $workspaceCheck 'report') --verify-library-workspace --exit-after-capture
```

- [ ] **Step 3：核验用户路径和兼容性。** 一次选择混合缺失→补原词→来源译文→一份MD→接收刷新；所选非播放歌的导入／恢复；部分稿不降级完整旧版；切库／切页／窗口关闭；重复元数据及共享存储槽；旧任务／旧个人备份恢复；旧快捷入口复用同 Settings。接收前后验证原词、偏移、歌词滚动、双显示开关、独立悬浮主题不变。
- [ ] **Step 4：文档和可检查预览。** 指南围绕新入口重写最短流程，区分“准备处理”“已有可用内容”“等待 agent”。记录实际通过数和原有失败；私人元数据、原词和完整现场截图不进 Git。ZIP逐文件散列匹配输出目录，软件边界检查先于压缩，既有备份／分发测试继续通过。
- [ ] **Step 5：同步并验证远端。** 提交前检查 working tree 和 origin/main，仅暂存本次文件；整合任何并发更新再重测受影响部分。普通提交／push；若保护阻止直推则 `codex/library-lyrics-workspace` 分支与 PR，不强推。GitHub工具更新 ref须 expected SHA。最后重新 fetch并比较远端 commit与source tree，报告实际链接及验证限制。

```powershell
git diff --check
git fetch origin main
git log --oneline HEAD..origin/main
git add README.md docs/chinese-translations.md docs/library-assets.md docs/music-library.md docs/personal-library.md docs/manual-test-checklist.md docs/release-notes.md docs/verification-library-workspace/README.md src/LumaLyrics.App/UiThemeVerification.cs scripts/publish-ui.ps1
git diff --cached --name-only
git commit -m "docs: explain integrated library and lyric preparation"
git push origin main
git ls-remote origin refs/heads/main
git fetch origin main
git diff HEAD origin/main --name-status
```

**完成条件：** 所有计划内实现、实际 UI、兼容检查和可复现预览均交付；未做的真实客户端／agent语义检查如实列出，不宣称全通过。

## 需求追踪与规划自检

| 目标 | 对应任务 |
| --- | --- |
| 两个 Settings 入口合并、可读而不堆成长页面 | Task 4 |
| 一份歌曲清单、真实完整／部分状态 | Task 1、4 |
| 选择一次，补原词→现有译文→批量MD | Task 2、3、4 |
| 所选歌曲无需播放，切歌不改变操作目标 | Task 2、4、6 |
| 全部任务和批次、失败重试、历史恢复 | Task 2、5 |
| 只选子集不改变整库／重复记录不丢 | Task 1、2、3 |
| 旧数据／开关／悬浮／播放和快捷入口 | Task 2、4、6、7 |
| 隐藏解绑、后台继续、取消／旧结果丢弃 | Task 1、3、4、5 |
| 六主题、窄窗、虚拟化、隐私、GitHub | Task 4、6、7 |

实现前先读完整规格与本计划；接口变化先同步类型和所有消费方。每完成任务记录实际验证并提交，不能把预期测试结果当成已通过，也不能用新的计数或命名掩盖原有语义回归。
