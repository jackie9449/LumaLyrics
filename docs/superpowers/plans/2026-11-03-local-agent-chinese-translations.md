# Existing Chinese lyrics and local-agent translation Implementation Plan

> **For agentic workers:** Execute this plan task-by-task, inline. Follow the repository AGENTS.md. Do not delegate or require an additional skill to interpret the implementation steps.

**Goal:** 优先使用可确认的已有中文译文；缺失或用户不满意时，以单首自足 MD 或一份批量入口 MD 交给具备本机读写能力的 agent，支持批量续做、校验接收及历史恢复。

**Architecture:** 沿用当前来源查询、原词 SHA-256、GroupIndex、个人译文库与显示通道。在个人目录增加逐曲任务和必需的批量入口／分块清单，agent逐曲读取、提交及保存进度；应用单消费者服务精确校验目标并按修订条件保存。保持“选用本地 agent”与具体模型供应商解耦。

**Tech Stack:** 当前 .NET 10 / C# 14、WPF、Microsoft.Data.Sqlite、System.Text.Json、xUnit、PowerShell；不增加翻译 SDK、模型依赖或在线翻译服务。

**Spec:** [2026-11-03-local-agent-chinese-translations.md](../specs/2026-11-03-local-agent-chinese-translations.md)。执行前完整阅读此规格、根目录 `AGENTS.md` 和本计划；规格与计划同时交付，不能只把执行计划交给翻译 agent。

## Global Constraints

- 能取得现有中文歌词时，直接使用。
- 找不到现有中文歌词时，提供足够详细的 MD；用户只需把该文件交给能读写本机文件的 agent，即可完成翻译并放入正确位置。
- 已有中文歌词不满意时，允许用户主动选择由本地 agent 重新制作。
- 批量只交接一份入口 MD；agent 按清单连续处理、每首保存进度并可续做，用户不逐首发送或导入。
- 译文绑定精确原词 UTF-8 SHA-256 和分组索引；不改变原词、时间轴、偏移、重复句、空白间奏或纯音乐状态。
- 主窗口／悬浮译文开关继续独立且首次关闭；不得增加可见“待校对”标志，不将 AI 生成改成语义已审或伪装成手动导入。
- 设置内管理本地任务，界面跟随应用主题；不影响悬浮歌词、工具栏、预览的独立外观。
- 个人原词、译文、任务、历史、回执和模型不得进入 Git 或软件包；生成目录不可用时不能创建默认空个人库。
- 旧中文 LRC 导入与旧 `--export-translation-drafts` / `--import-translation-drafts` 行为兼容，自动路径仍不能替换已有译文。
- 测试使用原创样例及软件目录外隔离资料；通过 `scripts/common.ps1` 选择 SDK；UI 检查不证明真实来源或 Apple Music 已验证。
- 检查远端后普通提交／推送到 `jackie9449/LumaLyrics`，不强推，不覆盖用户或并发工作；完成后核验远端提交和文件树。

## 当前交付状态与实施顺序

2026-11-03 已实现首版：协议、修订与历史、单首／批量 MD、严格接收、Inbox、Probe命令、Settings多选入口和个人备份。实现将批次渲染与导出合并于 TranslationBatchExchange，将任务持久化 helper 与 schema迁移置于 TranslationJobRepository；未新增依赖。Task 1–8 的基础交付已完成，下面逐步复选框保留为原计划和后续扩展参考，不将未逐项执行的检查勾为已通过。

实际验证：439 项单元测试通过（Core 92、Infrastructure 347，其中新增任务 11）；运行包新增 WPF 专项 21/21，既有译文专项 55/55；分发拒绝 69 类私人文件。生成独立 `local-agent-translations` 预览包。第三方 agent 的跨会话实测和语义质量尚未完成，Task 9 的这部分仍需使用实际选定的 agent 验收；没有宣称跨 agent 或整库语义验证完成。

实现取舍：个人备份从同一 SQLite 快照导出当前译文、完整请求／批次和历史记录；不读取正在变化的 agent临时文件／进度，也不将这些文件作为已完成成果。恢复后旧请求的路径资格失效，通过原词与记录重新生成新入口；未提交的临时翻译不属于已接收备份。

依赖顺序：`1 → 2 → 3 → 4 → 5 → 6 → 7 → 8 → 9`。每项有独立可评审结果和验证；先稳定协议与原子保存，再开放会自动替换内容的 UI。预计分 4 个里程碑：协议／存储、MD／接收、Settings／备份、完整验收；实际耗时以竞态和 UI 验证结果为准，不预先承诺模型语义质量。

### 项目调查结论

已有来源查找、精确哈希绑定、缺失译稿 JSON 交换、个人备份和双显示开关；无需重建翻译系统。主要缺口是自足任务 MD、已有译文的明确重译授权、修订／历史、应用自动回收和跨生命周期状态。

必须修正的实现细节：`TranslationRepository.Save` 目前可覆盖非手动译文；`TranslationDraftExchange.Import` 的“先 Find 再 Save”存在竞态；当前记录没有修订号。不能只给旧导入函数增加 `overwrite=true`，也不能把新版 AI 结果保存为 `manual=true` 来绕过保护。

原词读取还有一个需特别处理的细节：当前 `LyricsRepository.Find` 在提供时长时会更新 Tracks.Duration。任务的只读校验和受锁接收必须使用新增的纯读绑定快照方法，不直接复用这个有写入副作用的入口。

## 文件职责与最终接口

以下路径相对仓库根目录；标记“新增”的类型、文件和命令是本计划拟新增的接口，并非当前已存在功能。新生产代码按职责拆分，避免进一步膨胀 `PlaybackTranslations.cs`。

| 文件 | 操作与职责 |
| --- | --- |
| `src/LumaLyrics.Infrastructure/TranslationJobs.cs` | 新增；请求、结果、状态、回执和修订类型，协议限制 |
| `src/LumaLyrics.Infrastructure/TranslationJobRepository.cs` | 新增；登记任务、状态、授权、幂等记录；附加原词库后事务内接收与历史恢复 |
| `src/LumaLyrics.Infrastructure/TranslationJobExchange.cs` | 新增；创建／导出、严格结果校验、导入、部分采用／恢复的公共服务 |
| `src/LumaLyrics.Infrastructure/TranslationJobMarkdown.cs` | 新增；无模型依赖的完整 MD 渲染及绝对路径／围栏转义 |
| `src/LumaLyrics.Infrastructure/Resources/LocalAgentTranslationTask.zh-CN.md` | 新增；规格第 5 节完整正文模板，嵌入程序集 |
| `src/LumaLyrics.Infrastructure/TranslationBatchExchange.cs`、`TranslationBatchMarkdown.cs` | 新增；批次登记、分块清单、单一入口渲染和进度汇总 |
| `src/LumaLyrics.Infrastructure/Resources/LocalAgentTranslationBatch.zh-CN.md` | 新增；连续处理／恢复的完整批量说明模板，嵌入程序集 |
| `src/LumaLyrics.Infrastructure/TranslationJobInbox.cs` | 新增；应用生命周期文件通知、合并、串行接收、启动补扫和取消 |
| `src/LumaLyrics.Infrastructure/TranslationJobCommandLine.cs` | 新增；两个新 Probe 命令的纯参数解析，便于在现有跨平台 Infrastructure 测试项目验证 |
| `src/LumaLyrics.Infrastructure/TranslationRepository.cs` | 修改；schema 1→2、Revision、历史只读接口、全部保存更新修订、自动仅补缺 |
| `src/LumaLyrics.Infrastructure/LyricsRepository.cs` | 修改；抽出可在既有附加数据库事务连接中读取绑定快照的内部方法，重用当前匹配规则 |
| `src/LumaLyrics.Infrastructure/TranslationPreparation.cs` | 修改；复用来源查询为单曲／批量结果，写入时不覆盖新增译文 |
| `src/LumaLyrics.Infrastructure/TranslationLookupResult.cs`、`TranslationWorkflow.cs` | 新增；来源结果分类，以及来源优先／离线生成任务的编排 |
| `src/LumaLyrics.Infrastructure/TranslationDraftExchange.cs` | 修改；旧格式保持不变，最终保存改用原子补缺 |
| `src/LumaLyrics.Infrastructure/TranslationCacheReconciliation.cs`、`TranslationDraftReuse.cs` | 核对并保持仅补缺，不引入新覆盖入口 |
| `src/LumaLyrics.Infrastructure/LumaLyrics.Infrastructure.csproj` | 修改；嵌入静态模板资源 |
| `src/LumaLyrics.App/PlaybackTranslationJobs.cs` | 新增；协调器 partial，捕获目标、创建／查看／取消／恢复任务，分派状态 |
| `src/LumaLyrics.App/TranslationJobListPanel.cs` | 新增；任务列表和当前状态控件，页面订阅及释放 |
| `src/LumaLyrics.App/ChineseTranslationPanel.cs` | 修改；当前歌曲／批量缺失的任务入口、状态与历史，不显示 JSON 实现细节 |
| `src/LumaLyrics.App/PlaybackCoordinator.cs` | 修改；创建／关闭 inbox，与已有生命周期整合 |
| `src/LumaLyrics.App/PlaybackTranslations.cs` | 修改；单曲来源查询与只对匹配目标的刷新，不改显示逻辑 |
| `src/LumaLyrics.App/TranslationJobVerification.cs` | 新增；原创数据下实际 WPF 任务交互、六主题与关闭清理验证 |
| `src/LumaLyrics.App/UiThemeVerification.cs` | 修改；在现有验证分派处注册 `--verify-translation-jobs` 分支 |
| `tools/LumaLyrics.Probe/TranslationJobCommands.cs`、`Program.cs` | 新增／修改；两个明确命令分支，避免落入默认媒体 probe |
| `src/LumaLyrics.Infrastructure/PersonalLibrarySnapshot.cs`、`PersonalLibraryArchive.cs`、`PersonalLibraryManifest.cs` | 修改；快照、schema／manifest 兼容、任务／历史可读 JSON、安全恢复 |
| `.gitignore`、`scripts/verify-distribution.ps1`、`scripts/test-distribution.ps1` | 修改；新增任务目录及专用私人文件拒绝规则 |
| `scripts/publish-ui.ps1` | 核对静态模板随程序集存在，只复制公共指南，不复制任务 |
| `docs/chinese-translations.md`、`docs/personal-library.md`、`docs/manual-test-checklist.md`、`docs/release-notes.md`、`README.md` | 实施时更新使用入口、迁移边界和实际验证结果 |

### 共用协议类型（Task 1 定义）

以下类型位于 `LumaLyrics.Infrastructure`。`Track`、`ChineseTranslation`、`LyricsDocument` 和 `StoredTranslation` 沿用当前定义。未列出的未知 JSON 字段一律拒绝，不让 agent 传入替换权限或路径。

```csharp
public enum TranslationJobMode { FillMissing, ReplaceExpected }
public enum TranslationJobState
{
    Exporting, AwaitingResult, Applied, AppliedPartial, PartialReady,
    Rejected, Stale, Conflict, Cancelled
}
public sealed record TranslationJobGroup(int GroupIndex, string Original, bool NeedsTranslation);
public sealed record TranslationJobTarget(Track Track, string BindingId,
    string SourceHash, string OriginalRaw, string? LibrarySource,
    string? PersistentId, IReadOnlyList<TranslationJobGroup> Groups);
public sealed record TranslationJobRequest(int SchemaVersion, string JobId,
    TranslationJobMode Mode, string ProfileRoot, TranslationJobTarget Target,
    string? ExpectedRevision, DateTimeOffset CreatedUtc, string OriginalSource,
    string? UserInstructions, IReadOnlyList<TranslationLine>? ExistingTranslation);
public sealed record TranslationJobResultLine(int GroupIndex, string Original,
    string? Chinese, string? UntranslatedReason);
public sealed record TranslationJobResult(string Format, int SchemaVersion,
    string JobId, string RequestSha256, string SourceHash, string DraftEngine,
    IReadOnlyList<TranslationJobResultLine> Lines);
public sealed record TranslationJobTicket(TranslationJobRequest Request,
    string RequestSha256, string TaskPath, string ResultPath);
public sealed record TranslationJobSkip(Track Track, string Code, string Message);
public sealed record TranslationBatchSelection(Track Track, TranslationJobMode Mode,
    string? UserInstructions = null, bool IncludeExistingTranslation = false);
public sealed record TranslationBatchItem(string JobId, string RequestSha256,
    string TaskPath, string ResultPath, string ReceiptPath);
public sealed record TranslationBatchChunk(string Format, int SchemaVersion,
    string BatchId, int ChunkIndex, IReadOnlyList<TranslationBatchItem> Items);
public sealed record TranslationBatchChunkReference(int ChunkIndex, string Path,
    string Sha256, int Count);
public sealed record TranslationBatchManifest(string Format, int SchemaVersion,
    string BatchId, string ProfileRoot, DateTimeOffset CreatedUtc, int Total,
    int ChunkSize, IReadOnlyList<TranslationBatchChunkReference> Chunks);
public sealed record TranslationBatchTicket(string BatchId, string EntryPath,
    string ManifestPath, string ProgressPath, IReadOnlyList<string> ChunkPaths);
public sealed record TranslationJobBatchResult(TranslationBatchTicket? Batch,
    IReadOnlyList<TranslationJobTicket> Tickets,
    IReadOnlyList<TranslationJobSkip> Skipped);
public sealed record TranslationJobValidation(TranslationJobRequest Request,
    TranslationJobResult Result, string ResultSha256,
    int TranslatedCount, int RequiredCount);
public sealed record TranslationJobReceipt(string JobId, TranslationJobState State,
    string Code, string Message, int TranslatedCount, int RequiredCount,
    string? AppliedRevision, DateTimeOffset UpdatedUtc);
public sealed record TranslationJobSnapshot(TranslationJobTicket Ticket,
    TranslationJobReceipt Receipt);
public sealed record ActiveTranslationRevision(string Revision, StoredTranslation Record);
public sealed record TranslationHistoryEntry(string Revision, StoredTranslation Record,
    string Reason, DateTimeOffset ReplacedUtc, string? JobId);
```

`TranslationJobResult` 使用 PascalCase 字段，与现有交换工具风格一致；内部请求枚举输出为字符串。request 的哈希按已写入的实际字节计算，不能依赖重新序列化恰巧有相同属性顺序。

### 公共服务边界

```csharp
// TranslationRepository 的新增方法；既有 Save / SaveMissing 等 API 保留。
ActiveTranslationRevision? FindActive(Track track, LyricsDocument document);
IReadOnlyList<TranslationHistoryEntry> ReadHistory(Track track, string sourceHash);

// 新增 static TranslationJobExchange；下述每个方法均要求既有个人库可用。
TranslationJobTicket Create(string root, Track track, TranslationJobMode mode,
    string? userInstructions = null, bool includeExistingTranslation = false);
TranslationJobBatchResult CreateMissingBatch(string root,
    IReadOnlyList<Track> selectedTracks);
TranslationJobValidation Validate(string root, string resultPath);
Task<TranslationJobReceipt> ImportAsync(string root, string resultPath,
    CancellationToken token = default);
Task<TranslationJobReceipt> ApprovePartialAsync(string root, string jobId,
    string expectedRevision, CancellationToken token = default);
Task<bool> RestoreHistoryAsync(string root, Track track, string sourceHash,
    string historyRevision, string expectedRevision, CancellationToken token = default);
IReadOnlyList<TranslationJobSnapshot> ReadJobs(string root);
void Cancel(string root, string jobId);

// 新增 static TranslationBatchExchange；批量明确授权和缺失任务共用同一协议。
TranslationJobBatchResult Create(string root,
    IReadOnlyList<TranslationBatchSelection> selections, int chunkSize = 5);
// 新增 static TranslationBatchMarkdown；模板内给出逐首循环及断点恢复规则。
string Render(TranslationBatchTicket ticket, TranslationBatchManifest manifest);

// 新增 TranslationJobMarkdown，纯渲染，不查资料库、不联网、不写文件。
string Render(TranslationJobRequest request, string requestSha256,
    string temporaryResultPath, string finalResultPath, string? validationCommand);

// 新增 TranslationJobInbox : IAsyncDisposable。
// 构造函数 TranslationJobInbox(string root)。
event Action<TranslationJobReceipt>? Received;
Task RunAsync(CancellationToken token);
void RequestScan();
ValueTask DisposeAsync();
```

`ApprovePartialAsync` / `RestoreHistoryAsync` 只绑定 UI 中的明确用户动作，不暴露为 agent 结果字段或自动 CLI 参数。JSON 校验失败用 `InvalidDataException` 返回具体组与原因；可展示的消息不含整首原词或私有路径以外的无关信息。

## Task 1：建立任务协议和可复用原创样例

**Files:** 新增 `TranslationJobs.cs`；新增 `tests/LumaLyrics.Infrastructure.Tests/TranslationJobProtocolTests.cs`、`TranslationJobFixture.cs`。

**Interfaces:** 消费既有 Track／LrcParser／TranslationAlignment；产出上面的共用类型与 `TranslationJobProtocol.ValidateShape(TranslationJobRequest request, TranslationJobResult result)` 静态方法，返回 `(int TranslatedCount, int RequiredCount)`，不读写库。

- [ ] **Step 1：编写协议失败测试。** 用完整逐组比对覆盖重复句、空白、多行、引号、反斜线、中文和未译原因。下列 shared fixture 在本任务一并实现，后续测试统一使用。

```csharp
// TranslationJobFixture.cs 的固定输入与构造／清理逻辑。
public sealed class TranslationJobFixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(),
        "Luma-agent-" + Guid.NewGuid().ToString("N"));
    public static readonly Track Track = new("Own song", "Own artist", "Own album", 10000);
    public const string Original = "[00:01]We sail home\n[00:03]\n" +
        "[00:04]Across the quiet bay\n[00:08]The stars will guide us";
    public TranslationJobFixture()
    {
        Directory.CreateDirectory(Root);
        using var lyrics = new LyricsRepository(Path.Combine(Root, "lyrics.db"));
        lyrics.SyncLibrary(new("own-library", 1, [new("1", Track)], "own"));
        lyrics.Save(Track, Original, "Own source", true);
    }
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(Root, true);
    }
}
```

```csharp
[Fact]
public void ShapeValidationRejectsChangedOriginalBeforeAnyStorage()
{
    const string originalRaw = "[00:01]We sail home";
    var groups = new[] { new TranslationJobGroup(0, "We sail home", true) };
    var target = new TranslationJobTarget(TranslationJobFixture.Track, "binding",
        TranslationAlignment.Hash(originalRaw), originalRaw, "own-library", "1", groups);
    var request = new TranslationJobRequest(1, new string('1', 32),
        TranslationJobMode.FillMissing, Path.GetTempPath(), target, null,
        DateTimeOffset.UtcNow, "Own source", null, null);
    var result = new TranslationJobResult("LumaLyrics.TranslationJobResult", 1,
        request.JobId, new string('a', 64), target.SourceHash, "Own fixture",
        [new(0, "We sail home", "我们驶向家乡", null)]);
    Assert.Equal((1, 1), TranslationJobProtocol.ValidateShape(request, result));
    var changed = result with { Lines = [new(0, "Changed original", "我们驶向家乡", null)] };
    Assert.Throws<InvalidDataException>(() =>
        TranslationJobProtocol.ValidateShape(request, changed));
}
```

- [ ] **Step 2：先运行新测试确认尚无类型／方法，再按共用定义实现类型和纯校验。** 不在此任务接触数据库迁移或 UI。纯校验须拒绝 null 中文、索引增删／重排、非必要组有译文、无译原因缺失、超长内容和全曲零译文。

```powershell
. ./scripts/common.ps1
Invoke-Dotnet test tests/LumaLyrics.Infrastructure.Tests -c Release --filter 'FullyQualifiedName~TranslationJobProtocolTests'
```

- [ ] **Step 3：加入理论测试。** 用 `[Theory]` / `[InlineData]` 覆盖 4,000 字译文、1,000 字原因、20,000 组及 600 字引擎标识的边界前后。8 MiB 文件边界、无效 JSON／重复属性由 Task 4 解析层验证，文件大小以实际编码字节计，不能用默认反序列化的“最后一个属性胜出”。
- [ ] **Step 4：新测试全过后提交。**

```powershell
git add src/LumaLyrics.Infrastructure/TranslationJobs.cs tests/LumaLyrics.Infrastructure.Tests/TranslationJobProtocolTests.cs tests/LumaLyrics.Infrastructure.Tests/TranslationJobFixture.cs
git commit -m "feat: define local-agent translation job protocol"
```

**完成条件：** 翻译任务的字段、可写范围和部分结果规则可独立验证；未改变任何现有行为。

## Task 2：增加译文修订和历史，关闭自动覆盖竞态

**Files:** 修改 `TranslationRepository.cs`、`TranslationPreparation.cs`、`TranslationDraftExchange.cs`；新增 `TranslationJobRepository.cs` 基础 schema；扩展 `TranslationStorageTests.cs`、`TranslationDraftTests.cs`、`TranslationPreparationTests.cs`。

**Interfaces:** 消费 Task 1 的修订／历史类型；产出 `FindActive`、`ReadHistory` 和 schema 2。所有成功写入产生新 Revision，自动路径继续只补缺。

- [ ] **Step 1：使用真实 schema 1 fixture 写迁移测试。** 不通过新版构造器伪造旧库。用旧建表 SQL 与旧 Content 插入社区、AI、手动三类原创记录；验证迁移不改任何 `ChineseTranslation` 字段或 Manual，迁移后再次打开 Revision 不变，未来版本拒绝且不写库。
- [ ] **Step 2：加入修订测试并先运行。**

```csharp
[Fact]
public void ManualSaveChangesRevisionAndMissingWriteCannotReplaceIt()
{
    using var fixture = new TranslationJobFixture();
    using var repo = new TranslationRepository(fixture.Root);
    var document = new LrcParser().Parse(TranslationJobFixture.Original);
    var first = new ChineseTranslation(TranslationAlignment.Hash(document.Raw),
        [new(0, "我们驶向家乡")], "Own manual", "manual", "Own review",
        DateTimeOffset.UtcNow);
    repo.Save(TranslationJobFixture.Track, first, true);
    var revision = repo.FindActive(TranslationJobFixture.Track, document)!.Revision;
    Assert.False(repo.SaveMissing(TranslationJobFixture.Track, first with { Source = "Automatic" }));
    Assert.Equal(revision, repo.FindActive(TranslationJobFixture.Track, document)!.Revision);
    repo.Save(TranslationJobFixture.Track, first with { Lines = [new(0, "我们归航")] }, true);
    Assert.NotEqual(revision, repo.FindActive(TranslationJobFixture.Track, document)!.Revision);
    Assert.Equal("我们驶向家乡", Assert.Single(repo.ReadHistory(
        TranslationJobFixture.Track, first.SourceHash)).Record.Translation.TextAt(0));
}
```

- [ ] **Step 3：事务迁移和保存。** 当前表新增 Revision，新增 TranslationHistory、TranslationJobs 和规格第 4.4 节的 TranslationBatches；保存当前译文、旧版本入历史和修订变化必须同事务，保存无效／受保护时不得产生历史或改变 Revision。数据库只读模式接受受支持的 1／2 并保持旧库不变，写模式迁移 1→2；具体迁移测试同时检查失败回滚。只读 v1 可用原有 Find／ReadAll 和空历史，FindActive 明确要求先在可写的新目录升级，不伪造旧库 Revision。
- [ ] **Step 4：自动路径改为 `SaveMissing`，按真实保存结果更新进度。** 来源在网络等待期间若用户保存译文，必须读取并保留新值；旧 AI 导入返回实际插入数，不能明明被跳过仍标成 Draft。现有复用／修复核实同样语义；不要重写来源匹配算法。
- [ ] **Step 5：运行迁移与已有翻译测试，提交。**

```powershell
. ./scripts/common.ps1
Invoke-Dotnet test LumaLyrics.slnx -c Release --filter 'FullyQualifiedName~Translation'
git add src/LumaLyrics.Infrastructure/TranslationRepository.cs src/LumaLyrics.Infrastructure/TranslationJobRepository.cs src/LumaLyrics.Infrastructure/TranslationPreparation.cs src/LumaLyrics.Infrastructure/TranslationDraftExchange.cs tests/LumaLyrics.Infrastructure.Tests/TranslationStorageTests.cs tests/LumaLyrics.Infrastructure.Tests/TranslationDraftTests.cs tests/LumaLyrics.Infrastructure.Tests/TranslationPreparationTests.cs
git commit -m "feat: retain translation revisions and protect automatic writes"
```

**完成条件：** 旧资料无损升级，自动译文不覆盖期间新增的任何有效译文；历史恢复所需信息完整。

## Task 3：生成单首自足 MD 与一次交接的批量入口

**Files:** 新增 `TranslationJobMarkdown.cs`、`Resources/LocalAgentTranslationTask.zh-CN.md`、`TranslationJobExchange.cs` 创建部分、`TranslationBatchExchange.cs`、`TranslationBatchMarkdown.cs`、`Resources/LocalAgentTranslationBatch.zh-CN.md`；修改 `LumaLyrics.Infrastructure.csproj`；新增 `TranslationJobMarkdownTests.cs`、`TranslationJobExportTests.cs`、`TranslationBatchTests.cs`。

**Interfaces:** 消费 Task 1/2 协议与修订；产出 `TranslationJobExchange.Create` / `CreateMissingBatch`、`TranslationJobMarkdown.Render`、`TranslationBatchExchange.Create`、`TranslationBatchMarkdown.Render`。单首 Create 捕获授权后返回任务 ticket；批量 Create 返回唯一 EntryPath、分块路径和逐曲跳过原因；CreateMissingBatch 是仅含 FillMissing 的便利入口，委托批量服务。

- [ ] **Step 1：编写自足性测试并先运行。**

```csharp
[Fact]
public void SingleMarkdownContainsEveryGroupIdentityAndFinalPath()
{
    using var fixture = new TranslationJobFixture();
    var ticket = TranslationJobExchange.Create(fixture.Root,
        TranslationJobFixture.Track, TranslationJobMode.FillMissing);
    var markdown = File.ReadAllText(ticket.TaskPath);
    Assert.Contains(ticket.ResultPath, markdown);
    Assert.Contains(ticket.Request.JobId, markdown);
    Assert.Contains(ticket.Request.Target.SourceHash, markdown);
    Assert.Contains(ticket.RequestSha256, markdown);
    Assert.Contains("We sail home", markdown);
    Assert.Contains("Across the quiet bay", markdown);
    Assert.Contains("The stars will guide us", markdown);
    Assert.DoesNotContain("{{", markdown);
    Assert.All(ticket.Request.Target.Groups, group =>
        Assert.Contains($"\"GroupIndex\": {group.GroupIndex}", markdown));
}
```

- [ ] **Step 2：实现模板嵌入与严格渲染。** 模板逐条覆盖规格第 5 节的 17 项，不做只有简短 prompt 的壳。原词数据用 JSON 序列化再放围栏，动态选择长于内容最长反引号串的围栏；路径用实际绝对值，命令由受控路径参数生成。渲染后检查未替换变量，不执行歌词／用户修订说明内的指令。
- [ ] **Step 3：实现创建流程与限制。** `FillMissing` 遇到已有译文立即拒绝，不能偷偷升级为 ReplaceExpected；重译捕获当前 Revision。按 `store.Root` 校验原有资料和路径，拒绝源代码／软件目录、重解析点、不可靠原词、纯音乐。任务目录按随机 JobId `CreateNew`，登记 Exporting → 原子写 request/MD → AwaitingResult；导出失败留可诊断状态，不放出半份 MD。
- [ ] **Step 4：加入实际路径与上下文测试。** 在中文＋空格＋单引号路径下验证所有绝对路径和可选 PowerShell 命令；测试 `\n`、JSON 引号、反斜线、包含反引号的原句、多行组、重复副歌、已有中文、原生 TTML 的既有解析组；确认结果骨架含全部组且 agent无需自己生成哈希。MD／结果超限不静默截断。
- [ ] **Step 5：批量只选确认成员。** `CreateMissingBatch` 从 `LyricsRepository.GetLibraryQueue(includeDeferred:true)` 的已确认成员求交集；完全缺失才能自动 FillMissing；部分译文单独列出供用户重译，不自动覆盖。返回实际生成任务和逐曲拒绝原因，保留已成功任务，不回滚其他首。
- [ ] **Step 6：批量入口和断点规则。** 新模板逐条落实规格第 3.3 / 4.4 节：一份入口、绝对路径清单、默认 5 首一块（支持 1–10）、每次读一首完整任务、逐首提交并更新自己的进度、已提交不覆盖、失败继续、自动进入下一块、限额前保存并可同入口恢复。全部索引／分块写完才公开 EntryPath，空批次返回 Batch=null，不能给用户空入口。进度不是接收授权；总数来自真实成功 ticket。增加以下测试及 100 首／20 个默认分块、混合 FillMissing 与 ReplaceExpected、重复选择、失败跳过和中断恢复测试；恢复不能重生成 JobId 或重新授权。

```csharp
[Fact]
public void SixSongsRequireOneEntryAndTwoDefaultChunks()
{
    using var fixture = new TranslationJobFixture();
    var tracks = Enumerable.Range(0, 6).Select(i =>
        TranslationJobFixture.Track with { Title = $"Own song {i}" }).ToArray();
    using (var lyrics = new LyricsRepository(Path.Combine(fixture.Root, "lyrics.db")))
    {
        lyrics.SyncLibrary(new("own-library", 6,
            tracks.Select((track, i) => new LibraryTrack(i.ToString(), track)).ToArray(), "own"));
        foreach (var track in tracks)
            lyrics.Save(track, TranslationJobFixture.Original, "Own source", true);
    }
    var result = TranslationJobExchange.CreateMissingBatch(fixture.Root, tracks);
    Assert.Equal(6, result.Tickets.Count);
    Assert.Empty(result.Skipped);
    var batch = Assert.IsType<TranslationBatchTicket>(result.Batch);
    Assert.True(File.Exists(batch.EntryPath));
    Assert.Equal(2, batch.ChunkPaths.Count);
    var entry = File.ReadAllText(batch.EntryPath);
    Assert.Contains(batch.ManifestPath, entry);
    Assert.Contains(batch.ProgressPath, entry);
}
```

`LibraryTrack` 是当前 `AppleMusicLibraryReader.cs` 中的既有类型，不新增或改动资料库模型。

- [ ] **Step 7：运行并提交。**

```powershell
. ./scripts/common.ps1
Invoke-Dotnet test tests/LumaLyrics.Infrastructure.Tests -c Release --filter 'FullyQualifiedName~TranslationJobMarkdownTests|FullyQualifiedName~TranslationJobExportTests|FullyQualifiedName~TranslationBatchTests'
git add src/LumaLyrics.Infrastructure/TranslationJobMarkdown.cs src/LumaLyrics.Infrastructure/Resources/LocalAgentTranslationTask.zh-CN.md src/LumaLyrics.Infrastructure/TranslationJobExchange.cs src/LumaLyrics.Infrastructure/TranslationBatchExchange.cs src/LumaLyrics.Infrastructure/TranslationBatchMarkdown.cs src/LumaLyrics.Infrastructure/Resources/LocalAgentTranslationBatch.zh-CN.md src/LumaLyrics.Infrastructure/LumaLyrics.Infrastructure.csproj tests/LumaLyrics.Infrastructure.Tests/TranslationJobMarkdownTests.cs tests/LumaLyrics.Infrastructure.Tests/TranslationJobExportTests.cs tests/LumaLyrics.Infrastructure.Tests/TranslationBatchTests.cs
git commit -m "feat: export self-contained local-agent lyric tasks"
```

**完成条件：** 单首 task.md 包含完整原词和唯一输出路径；批量只交一个 batch-task.md，agent自行逐曲读取和续做，用户不逐首发送；两者均不依赖仓库知识。

## Task 4：严格校验并原子接收，支持明确重译和恢复

**Files:** 完成 `TranslationJobExchange.cs`、`TranslationJobRepository.cs`；修改 `LyricsRepository.cs`；新增 `TranslationJobImportTests.cs`、`TranslationJobConcurrencyTests.cs`、`TranslationJobHistoryTests.cs`。

**Interfaces:** 消费任务、修订与完整组；产出 `Validate`、`ImportAsync`、`ApprovePartialAsync`、`RestoreHistoryAsync`、`ReadJobs`、`Cancel`，签名见公共服务边界。`Validate` 只读且可检查 `.tmp`；Import 仅接收登记的最终路径。

- [ ] **Step 1：先写完整接收和幂等测试。** 新增到 shared fixture 的 helper 如下：

```csharp
public static string WriteCompletedResult(TranslationJobTicket ticket)
{
    var result = new TranslationJobResult("LumaLyrics.TranslationJobResult", 1,
        ticket.Request.JobId, ticket.RequestSha256, ticket.Request.Target.SourceHash,
        "Own agent fixture", ticket.Request.Target.Groups.Select(g =>
            new TranslationJobResultLine(g.GroupIndex, g.Original,
                g.NeedsTranslation ? "我们循着星光归家" : "", null)).ToArray());
    File.WriteAllText(ticket.ResultPath, System.Text.Json.JsonSerializer.Serialize(result),
        new System.Text.UTF8Encoding(false));
    return ticket.ResultPath;
}
```

```csharp
[Fact]
public async Task FillMissingIsHashBoundAndDuplicateImportKeepsSameRevision()
{
    using var fixture = new TranslationJobFixture();
    var ticket = TranslationJobExchange.Create(fixture.Root,
        TranslationJobFixture.Track, TranslationJobMode.FillMissing);
    var path = TranslationJobFixture.WriteCompletedResult(ticket);
    var first = await TranslationJobExchange.ImportAsync(fixture.Root, path);
    var second = await TranslationJobExchange.ImportAsync(fixture.Root, path);
    Assert.Equal(TranslationJobState.Applied, first.State);
    Assert.Equal(first.AppliedRevision, second.AppliedRevision);
    using var repo = new TranslationRepository(fixture.Root, true);
    var record = repo.FindActive(TranslationJobFixture.Track,
        new LrcParser().Parse(TranslationJobFixture.Original))!;
    Assert.True(record.Record.Translation.Draft);
    Assert.False(record.Record.Manual);
    Assert.Empty(repo.ReadHistory(TranslationJobFixture.Track, ticket.Request.Target.SourceHash));
}
```

- [ ] **Step 2：严格解析和全字段验证。** 用 `Utf8JsonReader` 检查每层重复属性、未知属性、字段类型和深度；再解码有限大小的 UTF-8 文件。测试 8 MiB 文件字节边界，校验 Format／SchemaVersion／JobId／RequestSha256／SourceHash、全部原文、顺序与 GroupIndex。客户端缺陷不能由模型重写 metadata “修好”；不给 agent 自行覆盖路径或授权字段。
- [ ] **Step 3：实现受锁的目标再验证。** 接收连接 ATTACH 同目录 `lyrics.db` 后 `BEGIN IMMEDIATE`，共用 LyricsRepository 抽出的纯读绑定／规范化匹配，事务内复查源哈希、BindingId、录音、任务授权和 Revision，仅写 translations；不得调用有更新 Duration 副作用的现有 Find。数据库占用总等待上限 2 秒、至多三次可取消尝试，不能把三次各 2 秒当成同一个上限。跨库 WAL 不做双库写入；用第二连接测试原词更新在锁释放前不能插入检查／保存之间。
- [ ] **Step 4：条件接收、历史和冲突。** FillMissing 使用 SQL `INSERT ... ON CONFLICT DO NOTHING`；ReplaceExpected 使用 `UPDATE ... WHERE Revision=$expected` 并检查实际影响行数。历史插入、当前版本／状态更新同事务；无效、取消、冲突无内容更新。部分替换进入 PartialReady，旧版继续显示；UI 显式 ApprovePartial 重新检查当前 Revision、原词和登记结果哈希后才应用。恢复记录生成新 Revision，保留当前版本历史。
- [ ] **Step 5：增加真实并发测试。** 两个进程／连接同时接收同一文件只产生一个 Revision；同一预期旧版的两个不同任务只有一个成功；来源先到／手动 LRC 先到／原词更新／原词同字但不同录音／取消先到均拒绝过期任务。用屏障控制到达顺序，不依赖固定睡眠碰运气。回执写失败后再导入仍幂等，进程在事务提交前后中止可恢复。
- [ ] **Step 6：运行并提交。**

```powershell
. ./scripts/common.ps1
Invoke-Dotnet test tests/LumaLyrics.Infrastructure.Tests -c Release --filter 'FullyQualifiedName~TranslationJobImportTests|FullyQualifiedName~TranslationJobConcurrencyTests|FullyQualifiedName~TranslationJobHistoryTests'
git add src/LumaLyrics.Infrastructure/TranslationJobExchange.cs src/LumaLyrics.Infrastructure/TranslationJobRepository.cs src/LumaLyrics.Infrastructure/LyricsRepository.cs tests/LumaLyrics.Infrastructure.Tests/TranslationJobFixture.cs tests/LumaLyrics.Infrastructure.Tests/TranslationJobImportTests.cs tests/LumaLyrics.Infrastructure.Tests/TranslationJobConcurrencyTests.cs tests/LumaLyrics.Infrastructure.Tests/TranslationJobHistoryTests.cs
git commit -m "feat: validate and apply revision-bound agent translations"
```

**完成条件：** agent写好的结果只能影响已授权的目标版本；从失败或并发中不能产生覆盖、串歌或重复历史。

## Task 5：应用自动回收与可选 Probe 校验

**Files:** 新增 `TranslationJobInbox.cs`、`TranslationJobCommandLine.cs`、`tools/LumaLyrics.Probe/TranslationJobCommands.cs`；修改 `Program.cs`；新增 `TranslationJobInboxTests.cs`、`TranslationJobCommandTests.cs`。

**Interfaces:** 消费 Task 4 接收服务；产出 Inbox 接口及新命令：`--validate-translation-job-result <root> <file>` / `--import-translation-job-result <root> <file>`。不改变旧译稿命令，也不接受强制覆盖参数。

参数类型在 Infrastructure 内定义，避免为了测纯参数解析让 `net10.0` 测试工程引用 Windows Probe 可执行工程：

```csharp
public enum TranslationJobCommandKind { Validate, Import }
public sealed record TranslationJobCommand(TranslationJobCommandKind Kind,
    string Root, string File);
// 新增 static TranslationJobCommandLine。
public static bool TryParse(string[] args, out TranslationJobCommand? command);
```

不属于这两个命令时返回 false；命中命令却缺参数／多参数则抛 `ArgumentException`。Probe 命令包装器只负责调用服务、输出摘要与退出码，不包含媒体或 UI 逻辑。

- [ ] **Step 1：先写临时文件／重启补扫测试。**

```csharp
[Fact]
public async Task InboxRescansReadyFilesCreatedBeforeStartup()
{
    using var fixture = new TranslationJobFixture();
    var ticket = TranslationJobExchange.Create(fixture.Root,
        TranslationJobFixture.Track, TranslationJobMode.FillMissing);
    TranslationJobFixture.WriteCompletedResult(ticket);
    var received = new TaskCompletionSource<TranslationJobReceipt>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    await using var inbox = new TranslationJobInbox(fixture.Root);
    inbox.Received += receipt => received.TrySetResult(receipt);
    using var cancellation = new CancellationTokenSource();
    var running = inbox.RunAsync(cancellation.Token);
    var receipt = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.Equal(TranslationJobState.Applied, receipt.State);
    cancellation.Cancel();
    try { await running; } catch (OperationCanceledException) { }
}
```

- [ ] **Step 2：实现单消费者与有界扫描。** FileSystemWatcher 只监听本 profile 的登记任务目录，事件入队并去重；启动、前台恢复、手动刷新补扫。候选只来自 AwaitingResult 的任务，每次批次至多 100 首，取消后不继续入库。临时文件、junction、其他 profile 与未登记文件不进入队列。文件占用按规格退避，不能占用 dispatcher。
- [ ] **Step 3：实现 CLI 的只读校验与显式接收。** 命令返回 JSON 小摘要，不输出全歌词；退出码 `0` 成功、`2` 格式／任务不合法、`3` 过期／冲突／取消、`4` IO／资料不可用。Validate 不创建库／任务／回执；允许本任务临时输出路径。Import 与应用走同一个原子服务，不能重复实现检查。未知参数立刻报错并退出，不能落入 Program 的默认媒体读取分支。
- [ ] **Step 4：测试关闭与竞态。** 占用文件锁、通知合并、重复 rename、取消、重启、ProfileRoot 迁移、watcher overflow 和回执故障；Dispose 后没有事件／时钟／文件句柄残留。App 与 Probe 同时接收仍幂等。CLI 参数测试使用上述 TryParse，进程级验证在 Task 9 使用发布包和原创结果，不为测试启动真实媒体会话。
- [ ] **Step 5：运行并提交。**

```powershell
. ./scripts/common.ps1
Invoke-Dotnet test tests/LumaLyrics.Infrastructure.Tests -c Release --filter 'FullyQualifiedName~TranslationJobInboxTests|FullyQualifiedName~TranslationJobCommandTests'
git add src/LumaLyrics.Infrastructure/TranslationJobInbox.cs src/LumaLyrics.Infrastructure/TranslationJobCommandLine.cs tools/LumaLyrics.Probe/TranslationJobCommands.cs tools/LumaLyrics.Probe/Program.cs tests/LumaLyrics.Infrastructure.Tests/TranslationJobInboxTests.cs tests/LumaLyrics.Infrastructure.Tests/TranslationJobCommandTests.cs
git commit -m "feat: receive local-agent lyric results across app restarts"
```

**完成条件：** agent不运行任何额外命令也可放置结果，应用运行或下次启动时接收；只读校验是可选辅助。

## Task 6：来源优先的单曲与批量编排

**Files:** 修改 `TranslationPreparation.cs`；新增 `TranslationLookupResult.cs`、`TranslationWorkflow.cs`；新增 `TranslationWorkflowTests.cs`；修改 `TranslationPreparationTests.cs`。

**Interfaces:** `TranslationLookupState` 定义为 `Found, NotFound, Mismatch, Offline, TransientError, MissingOriginal, NotRequired`；`TranslationLookupResult(Track Track, TranslationLookupState State, string Message)`。`TranslationPreparation.PrepareOneAsync(Track track, CancellationToken token)` 复用批量核心。新增 `TranslationWorkflow(string root, ITranslationProvider provider)`，提供 `Task<TranslationWorkflowResult> PrepareAsync(Track track, bool onlineLookup, bool createTaskIfMissing, CancellationToken token)`；`TranslationWorkflowResult(TranslationLookupResult Lookup, TranslationJobTicket? Ticket)`。批量使用相同单曲流程，沿用来源节流与连续失败暂停。

- [ ] **Step 1：先写离线任务生成与来源成功不生成任务测试。**

```csharp
sealed class MustNotCallProvider : ITranslationProvider
{
    public Task<IReadOnlyList<TranslationCandidate>> LookupAsync(Track track,
        CancellationToken token) => throw new InvalidOperationException("Unexpected network");
}

[Fact]
public async Task OfflinePreparationCreatesTaskWithoutContactingProvider()
{
    using var fixture = new TranslationJobFixture();
    var workflow = new TranslationWorkflow(fixture.Root, new MustNotCallProvider());
    var result = await workflow.PrepareAsync(TranslationJobFixture.Track,
        onlineLookup: false, createTaskIfMissing: true, CancellationToken.None);
    Assert.Equal(TranslationLookupState.Offline, result.Lookup.State);
    Assert.NotNull(result.Ticket);
    Assert.True(File.Exists(result.Ticket!.TaskPath));
}
```

- [ ] **Step 2：抽取单曲来源核心。** 对有效缓存直接返回 Found；无缓存并允许联网时沿用既有录音／全文／逐行验证，保存成功即结束，不生成 MD。无结果且用户点击的“准备中文译文”允许生成任务时自动创建；独立“生成本地任务”按钮可跳过联网。未允许创建时只显示原因，不暗中生成全资料库任务。
- [ ] **Step 3：准确处理失败。** Offline／TransientError 保留真实状态，不记为确定无译文；用户已选“无可用结果时准备本地任务”仍可生成。MissingOriginal／NotRequired 无任务；重试不重复创建相同未完成任务，已有任务返回原 ticket。ReplaceExpected 由明确 UI 入口直接创建，不绕回来源搜索、不抹掉用户重译意图。
- [ ] **Step 4：补来源优先测试。** Fake provider 返回当前原创三行配对→Found 且无 MD；返回错误录音→Mismatch；网络异常→TransientError；已缓存手动／来源／AI→不请求 provider；provider忽略取消晚到时仍不保存或生成任务。批量进度明确区分已有译文、来源新取得、已生成任务、原词不适用和网络问题。
- [ ] **Step 5：运行并提交。**

```powershell
. ./scripts/common.ps1
Invoke-Dotnet test tests/LumaLyrics.Infrastructure.Tests -c Release --filter 'FullyQualifiedName~TranslationWorkflowTests|FullyQualifiedName~TranslationPreparationTests'
git add src/LumaLyrics.Infrastructure/TranslationLookupResult.cs src/LumaLyrics.Infrastructure/TranslationWorkflow.cs src/LumaLyrics.Infrastructure/TranslationPreparation.cs tests/LumaLyrics.Infrastructure.Tests/TranslationWorkflowTests.cs tests/LumaLyrics.Infrastructure.Tests/TranslationPreparationTests.cs
git commit -m "feat: prefer existing lyric translations before agent handoff"
```

**完成条件：** “有现成的直接用”“没找到生成 MD”“不满意主动重译”三个入口有明确而可测试的选择逻辑。

## Task 7：Settings 中开放当前歌曲、批量任务与历史

**Files:** 新增 `PlaybackTranslationJobs.cs`、`TranslationJobVerification.cs`、`TranslationJobListPanel.cs`；修改 `ChineseTranslationPanel.cs`、`PlaybackCoordinator.cs`、`PlaybackTranslations.cs`、`UiThemeVerification.cs`。任务列表的独立 panel 仅负责控件和按钮绑定，不复制导入／替换规则。

**Interfaces:** 协调器新增 `Task<TranslationJobTicket?> PrepareCurrentChineseAsync(bool preferExisting, CancellationToken token)`、`Task<TranslationJobTicket?> RetranslateCurrentChineseAsync(string? userInstructions, bool includeExisting, CancellationToken token)`、`IReadOnlyList<TranslationJobSnapshot> CurrentTranslationJobs`、`void RefreshTranslationJobs()`；批量界面选定确认成员后调用 Task 3/6 服务。历史／取消／部分采用直接使用 Task 4 服务，不复制保存规则。

- [ ] **Step 1：新增 `--verify-translation-jobs` 和原创交互断言。** 验证必须真实调用按钮／automation peer，经设置页执行，不只测 view model。先确认新增检查失败，再实现控件和事件。

```csharp
// TranslationJobVerification.cs 中沿用既有 checks 字典、原创 fixture 和实际 WPF 元素。
checks["translationJobs.originalUnchanged"] =
    TranslationAlignment.Hash(coordinator.Document!.Raw) == originalHash;
checks["translationJobs.switchesIndependent"] =
    settings.ShowChineseTranslation == savedMain &&
    settings.OverlayShowChineseTranslation == savedFloating;
checks["translationJobs.noReviewBadge"] =
    !Descendants<TextBlock>(preferences.ChineseTranslations)
        .Any(b => b.Text.Contains("待校对"));
// originalHash / savedMain / savedFloating 在操作前捕获；preferences 使用既有 SettingsWindow。
```

- [ ] **Step 2：当前歌曲卡片。** 显示来源、实际覆盖数及“准备中文译文…”／“生成本地翻译任务…”／“用本地 agent 重新制作…”；生成后提供“打开任务 MD”“复制任务路径”“打开任务文件夹”。“打开”只通过 Shell 打开已登记文件，不从结果／歌词执行命令。任务内容包含一键交接所需全部信息。
- [ ] **Step 3：明确重译授权。** 创建界面显示目标和旧来源，可选简短风格说明及参考旧译文；按钮清楚写明完成后替换并保留历史。任务期间旧版可用；PartialReady提供“查看部分稿／采用此部分译稿／重新生成”，采用时捕获当前修订。历史卡可查看来源和恢复；保留 AI provenance，不添加待校对徽标。
- [ ] **Step 4：批量任务与状态。** Settings 内复用已确认资料库筛选，支持“全部缺失”与勾选多首重译，创建页一次列明各首模式和替换授权。生成后主要按钮为“打开批量入口 MD／复制批量任务路径”，不让用户逐首发送文件；支持取消剩余导出、同入口续做和只对失败项重新生成。汇总从真实任务状态显示选中数、来源已取得、待制作、已提交待接收、已接收、需处理和失败，不能信任 agent进度把内容标为已接收。列表使用有界／虚拟化容器，不在 playback主页面塞入管理控件。
- [ ] **Step 5：生命周期与过期结果。** Coordinator 持有单个 inbox，创建／查找捕获 Track＋原词哈希及请求代次；切歌或新请求后不得将旧 UI 结果显示为新歌曲。inbox 按登记目标保存，切歌不取消已授权的独立翻译任务。页面 Loaded订阅、Unloaded解绑；应用 DisposeAsync取消回收并按既有关闭流程等待释放。收件完成仅刷新匹配目标和列表，不抢焦点、不跳滚动、不改偏移。
- [ ] **Step 6：运行实际 UI 专项。** 覆盖 1220／980／820 宽度、六套应用主题（liquid/frost/material/paper/studio/bauhaus）、键盘 Tab/Enter/Escape、长路径换行、减少动画及系统动画关闭。确认悬浮主题、预览、锁定、两个开关、下一句原文与间奏保持。自动保存完成时不弹抢焦点窗口。
- [ ] **Step 7：提交通过专项的 UI。**

```powershell
git add src/LumaLyrics.App/PlaybackTranslationJobs.cs src/LumaLyrics.App/TranslationJobVerification.cs src/LumaLyrics.App/TranslationJobListPanel.cs src/LumaLyrics.App/ChineseTranslationPanel.cs src/LumaLyrics.App/PlaybackCoordinator.cs src/LumaLyrics.App/PlaybackTranslations.cs src/LumaLyrics.App/UiThemeVerification.cs
git commit -m "feat: manage local-agent lyric tasks in translation settings"
```

**完成条件：** 用户不需要操作数据库、手工对行或找个人库；任务在 Settings 关闭后也能接收，旧译文可明确恢复。

## Task 8：个人备份、恢复与软件分发边界

**Files:** 修改 `PersonalLibrarySnapshot.cs`、`PersonalLibraryArchive.cs`、`PersonalLibraryManifest.cs`、`.gitignore`、`verify-distribution.ps1`、`test-distribution.ps1`；新增 `TranslationJobBackupTests.cs`；扩展 `PersonalLibraryTests.cs`。

**Interfaces:** 沿用 PersonalLibraryArchive 的 Export／Restore API；新 manifest 写 version 2，读取支持 1/2；新 Job 资料按规格第 7 节白名单导出。当前译文和历史／授权 JSON 来自同一 translations.db快照，不读取活库补齐。

- [ ] **Step 1：先写恢复原创任务／历史测试。** 场景：先有手动译文→用户重译接收→导出快照→恢复到全新目录。验证当前 Draft、旧 Manual、Review、哈希、历史和 Revision 全部保留；恢复后旧任务暂停，不扫描原目录，不自动写旧路径；新任务生成新 JobId和新绝对路径。
- [ ] **Step 2：增加旧格式兼容测试。** 使用真实 v1 manifest和 translations schema 1 fixture，读取只做验证，恢复目标中迁移；不修改原备份、不将当前新库当成旧库样例。未来版本清楚拒绝且不留下空库。
- [ ] **Step 3：实现已登记文件白名单。** manifest 新增明确 job/history/batch 类型，校验 ID、文件名、大小、SHA-256、相对路径、来源记录和受支持状态；包含登记的批量入口和分块，不复制临时文件、未登记 result或外链文件。agent-progress 只作可选且经校验的恢复提示，损坏则跳过并记录，不作为译文接收事实。已接收结果按数据库中记录哈希验证；在途、被占用、导出中任务的缺失须明确写入 warnings，不把不一致文件标为成功。
- [ ] **Step 4：调整恢复路径。** 只恢复新目录；恢复译文和历史可直接使用，旧未完成任务标记 `Stale`、原因 `ProfileRelocated` 并保留可读记录，用户重新生成后才启用新路径。恢复一致性检查同时覆盖数据库快照与可读 JSON 内容，不只验证 ZIP 能解压。
- [ ] **Step 5：增加泄漏拒绝样例。** 目录 `translation-jobs`／`translation-batches` 及根级 `task.md`、`batch-task.md`、`batch.json`、`agent-progress.json`、`request.json`、`translation-result.json`、`receipt.json` 与专用任务／历史 JSON都拒绝；静态模板资源／公共指南允许。Git 忽略规则涵盖生成任务但不能忽略仓库 `docs` 中的规格计划或源码模板。新增隐私测试使用原创样例，检查打包前拒绝，不实际打入真实个人内容。
- [ ] **Step 6：运行并提交。**

```powershell
. ./scripts/common.ps1
Invoke-Dotnet test tests/LumaLyrics.Infrastructure.Tests -c Release --filter 'FullyQualifiedName~TranslationJobBackupTests|FullyQualifiedName~PersonalLibraryTests'
./scripts/test-distribution.ps1
git add src/LumaLyrics.Infrastructure/PersonalLibrarySnapshot.cs src/LumaLyrics.Infrastructure/PersonalLibraryArchive.cs src/LumaLyrics.Infrastructure/PersonalLibraryManifest.cs tests/LumaLyrics.Infrastructure.Tests/TranslationJobBackupTests.cs tests/LumaLyrics.Infrastructure.Tests/PersonalLibraryTests.cs .gitignore scripts/verify-distribution.ps1 scripts/test-distribution.ps1
git commit -m "feat: back up agent translation history without leaking personal tasks"
```

**完成条件：** 备份包含完整个人成果、来源与历史；恢复不执行旧路径；软件包和 Git不能夹带个人歌词任务。

## Task 9：端到端验收、文档与 GitHub 交付

**Files:** 更新使用指南、README、release notes、manual checklist；需要公开验收记录时新增 `docs/verification-translation-jobs/README.md`，只记录原创界面和非私人计数。

**Interfaces:** 消费全部前序成果；产出可使用版本、真实验证记录和 GitHub提交／PR链接。

- [ ] **Step 1：构建和回归。** 使用 bundled SDK；若有不相关既有失败，记录实际失败，不能放宽断言后宣称全过。

```powershell
. ./scripts/common.ps1
Invoke-Dotnet restore LumaLyrics.slnx --locked-mode
Invoke-Dotnet build LumaLyrics.slnx -c Release --no-restore
Invoke-Dotnet test LumaLyrics.slnx -c Release --no-build --no-restore
./scripts/test-distribution.ps1
./scripts/publish-ui.ps1 -PreviewLabel local-agent-translations
```

- [ ] **Step 2：在软件之外的隔离资料目录执行 UI。** 用新 GUID 避免覆盖已有验证数据；包名从 Directory.Build.props读取，不能硬编码实施时可能改变的版本。UI回归时不使用当前真实 profile。

```powershell
. ./scripts/common.ps1
$translationVersion = ([xml](Get-Content -LiteralPath './Directory.Build.props' -Raw)).Project.PropertyGroup.Version
$translationPackage = Join-Path $projectRoot "artifacts/LumaLyrics-$translationVersion-ui-preview-local-agent-translations-win-x64"
$translationChecks = Join-Path $env:LOCALAPPDATA ('LumaLyrics-AgentChecks/' + [guid]::NewGuid().ToString('N'))
$translationCheckData = Join-Path $translationChecks 'data'
$translationCheckReport = Join-Path $translationChecks 'report'
New-Item -ItemType Directory -Path $translationCheckData,$translationCheckReport | Out-Null
& (Join-Path $translationPackage 'runtime/dotnet.exe') exec (Join-Path $translationPackage 'LumaLyrics.dll') --data-dir $translationCheckData --verify-ui $translationCheckReport --verify-translation-jobs --exit-after-capture
```

保留并核对专项 JSON 的每条结果，实际打开六主题的关键截图；同时运行已有翻译／Settings专项或完整 UI 回归，报告各自实际通过数。Microsoft host是既有 apphost启动卡顿的可用后备。图像保留原创样例，路径含个人用户名的私人报告留本机。

- [ ] **Step 3：验证 agent 交接，不以 mock替代可用性。** 至少使用一种较弱模型和一种常用本地 agent，分别仅提供单首 MD 和批量 batch-task.md，不提供仓库／request／额外口头规则。用原创英语、重复副歌＋空白、多行组、日语／混合语言、中文路径各一首；另做至少 10 首跨两块的连续处理、完成 3 首后强制结束并以同一入口续做、应用关闭时连续提交、单首失败后继续等实测。核对是否一次放到正确路径、是否跳过已提交项、结构校验／接收是否通过、有没有假称完成。若当前没有可用 agent环境，保留未完成状态并列明待用户实测项，不声称已经跨 agent通过。
- [ ] **Step 4：人工评估翻译内容。** 检查含义、否定、人物指代、习语、前后呼应及每组边界；记录样例和具体问题，修正文案或模板后只重测受影响交接。结构通过仍记录 Draft，不能改为已语义核实。无需在此验收中操作真实 Apple Music播放。
- [ ] **Step 5：走通用户三条完整路径。** 可靠来源→直接使用且无新任务；无来源→准备动作生成MD→agent落盘→自动显示；不满意→明确重译→旧版继续→新版本替换→历史恢复。另走切歌、退出重启、目录不可用、原词变化、手动修订／双任务竞争、部分替换拒绝路径。
- [ ] **Step 6：更新文档并发布普通提交。** 指南写清楚最短操作、agent能力前提、当前个人目录、部分结果、失败原因、历史恢复和备份；release notes仅写实际实现和实际验证，不夹带个人翻译或未验证宣传。必要的版本号只在开始打包前按仓库版本约定更新，并与包内一致。
- [ ] **Step 7：重新检查远端，提交并同步。**

```powershell
git status --short
git diff --check
git fetch origin main
git log --oneline HEAD..origin/main
```

如果远端有新增提交，先在保留本地改动的前提下整合并重测受影响部分。只暂存本次明确文件；用 `git diff --cached --name-only` 逐一确认没有个人数据、.tools或build输出。

```powershell
git add README.md docs/chinese-translations.md docs/personal-library.md docs/manual-test-checklist.md docs/release-notes.md
git commit -m "docs: explain existing translations and local-agent handoff"
git push origin main
git ls-remote origin refs/heads/main
git fetch origin main
git diff HEAD origin/main --name-status
```

若 direct push被分支保护拒绝，建立 `codex/local-agent-chinese-translations`，普通推送并创建PR，不强推或改保护。通过GitHub工具更新ref时必须提交检查过的 expected SHA。交付报告实际 commit或PR URL、已通过检查、未完成的实机／agent／语义验证。

**完成条件：** 三条需求均有可使用入口；MD单文件交接可复现；数据保护与恢复通过；远端内容与验证后的本地源码一致。

## 需求追踪与实施自检

| 要求／风险 | 实施与验收 |
| --- | --- |
| 有现成中文歌词直接用 | Task 2 / 6 / 9 |
| 缺失生成一份足够详细的 MD | Task 1 / 3；规格第 5 节逐条覆盖；Task 9 仅交 MD试用 |
| 一次选多首、只交一份批量 MD、可中断续做 | Task 3 / 7 / 8 / 9；规格第 3.3 / 4.4 节；100 首分块测试和跨会话实测 |
| 不满意时选择本地重译 | Task 2 / 4 / 7，含手动旧译文、明确授权、保留历史 |
| 任意 agent无数据库／项目知识也可交接 | Task 3完整骨架、绝对路径、自检；Task 5命令可选 |
| 原词、时间、重复句和空白不被改 | Task 1 / 3 / 4 / 7 |
| 晚到结果／切歌／并发不能串歌覆盖 | Task 4 / 5 / 7 |
| 部分输出不降级完整旧译文 | Task 1 / 4 / 7 |
| 主题、显示开关、来源和 AI provenance | Task 7；沿用原有翻译专项 |
| 备份含一致快照和可读资料，软件不带私人内容 | Task 8 / 9 |
| 老资料与老工具兼容 | Task 2 / 5 / 8 |
| 避免模型能力不足靠猜 | 完整规格第 5节模板＋Task 9跨 agent实测；失败保留真实未完成项 |

执行者完成每个任务后勾选步骤、记录实际测试输出并提交；不能把本计划的预计结果写成已经通过。若实现时发现基线接口发生变化，先更新规格、接口和相关测试片段，再继续依赖该接口的任务。
