# Argos Multilingual Translation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** 扩展离线中文机翻，覆盖英、法、德、西、日、韩语。

**Architecture:** .NET 保存原词语言选择并在确切原词工作流中识别，Python Argos worker 使用固定模型按组翻译。英语直接译中，其他五语按同组经英语中转；仅保留英语→中文和当前源语言两个已加载模型。按所选语言准备，自动识别准备全部；保持英文基础锁和环境路径不变，新增模型独立校验/原子安装，不复制基础运行环境。

**Tech Stack:** WPF/.NET 10、Argos Translate 1.11.0 Package/tokenizer、CTranslate2 4.6.0 CPU int8、Windows ELS、本机 embedded Python 3.12.10。

**Spec:** `docs/superpowers/specs/2026-11-17-argos-multilingual.md`

## Global Constraints

- 缺词兜底；保留原词 SHA-256、组索引、时间轴、空白、偏移及已有译文保护。
- 五个新增方向经英语中转，明确写入 provenance，机器稿不冒充人工审核。
- 组件固定 HTTPS/字节数/SHA-256；模型及私人任务不进 Git/软件 ZIP/个人备份。
- 四语、六主题；只本地提交，不控制真实 Apple Music，不使用用户歌词验证。
- 用户已授权实施，本会话内联完成；不另外启动并行 agent。

### Task 1: 语言路由、固定模型和 worker

**Files:** Create `src/LumaLyrics.Infrastructure/ArgosLanguages.cs`; modify `ArgosEngine.cs`, `Resources/argos-worker.py`, `Resources/argos-model-lock.json`, `ArgosInstallation.cs`, `scripts/argos/refresh-lock.py`; test `tests/LumaLyrics.Infrastructure.Tests/OfflineLanguageTests.cs`.

**Interfaces:** `ArgosLanguages.Resolve(string text,string selection,Func<string,string?>? detector)` returns one of en/fr/de/es/ja/ko or throws InvalidDataException. `IOfflineTranslationEngine.TranslateAsync(IReadOnlyList<string> texts,string sourceLanguage,CancellationToken token)` preserves ordered output; English convenience overload remains.

- [x] Add route tests with actual cases: French `Le vent traverse la vallée`, German `Der Wind zieht durch das Tal`, Spanish `El viento cruza el valle`, Japanese `風が谷を渡る`, Korean `바람이 계곡을 건넌다`; manual choice, regional tags, unknown language and incompatible scripts. Run `Invoke-Dotnet test tests/LumaLyrics.Infrastructure.Tests -c Release --no-restore --filter FullyQualifiedName~OfflineLanguageTests`.
- [x] Implement central supported choices and exact machine provenance URL whitelist. Keep legacy `local:argos/en-zh`; add `local:argos/{fr,de,es,ja,ko}-en-zh`. Route auto with reliable detector/script signals, refuse unknown detector results; keep old no-detector English test/API behavior.
- [x] Download official fr_en1.9/de_en1.3/es_en1.9/ja_en1.1/ko_en1.1 packages only into ignored .tools; inspect metadata/tokenizers/licenses and pin actual bytes/SHA-256. Keep the base lock/runtime unchanged; install extra packages independently under a hash-addressed private model directory.
- [x] Pass SourceLanguage through JSONL. Translate each original group with source→en then en→zh; bound both tokenizations and output, never expose/save intermediate text. Cache by language and text, release previous source translator when changing language; retain two-thread CPU/network block/timeout/cancellation.
- [x] Run the above tests and actual original-fixture engine checks for all six languages, switching source directions within one worker and then cancelling; record real time/memory and disposal.

### Task 2: Queue, exact-source protection and upgrades

**Files:** Modify `OfflineTranslationWorkflow.cs`, `TranslationRepository.cs`, `TranslationJobExchange.cs`; test `OfflineTranslationTests.cs`, `OfflineLanguageTests.cs`.

**Interfaces:** `OfflineTranslationItem(...,string SourceLanguage="auto")` appended optional field; legacy JSON restores auto. The workflow resolves source per song and calls the new engine overload with the exact chosen language. Central machine URL SQL uses only the same static whitelist as IsMachine.

- [x] Test queue JSON old/new round-trip, invalid language rejection, pending task explicit reselection, Japanese Kanji groups with Kana context, mixed Chinese/Latin blank preservation, and unchanged hash/group indexes.
- [x] Capture selected language in each queued song. New explicit selection replaces that song's pending parameters; Continue uses persisted values. Resolve language only after existing-translation early exit, preserve original/member/revision revalidation and cancellation before commit.
- [x] Save each route in SourceUrl and pivot notice in Review; generalize exact whitelisted machine-upgrade checks. Test source and agent upgrades for new routes, preserve history/manual safeguards and partial approval. Use existing backup path, add multilingual queued-choice restoration test.
- [x] Run Core and Infrastructure unit suites using scripts/common.ps1, no private/live data.

### Task 3: Theme UI, docs, distribution and local commit

**Files:** Modify `SettingsStore.cs`, `PlaybackOfflineTranslation.cs`, `OfflineTranslationPanel.cs`, `TranslationJobListPanel.cs`, `OfflineTranslationVerification.cs`, `Localization/offline.*.json`, `docs/offline-translation.md`, release/development notes, README, THIRD-PARTY-NOTICES and publish script.

**Interfaces:** persistent `AppSettings.OfflineTranslationLanguage="auto"`; coordinator getter/setter validates selections and saves immediately. ComboBox uses theme selector styling and localized language names with stable language-code tags; disabled while busy.

- [x] Add auto plus six source choices, explain five-language English pivot and dynamic download size. Rename English-only actions/status; capture source choice at enqueue. Prepare self-test covers all requested routes with fixed original text and no personal data.
- [x] Extend --verify-offline-translation: selector choices/persistence, busy disable, new source capture, explicit pause/resume, exact originals, coverage refresh, six themes at 1220/820, four-language catalogs. Run packaged --verify-ui with isolated --data-dir; inspect screenshots.
- [x] Update guide to list paths/model sizes/hashes and licenses, migration/preparation, auto/manual behavior, pivot/quality limitations, backup and upgrade behavior. Record actual results/failures/remaining platform limits.
- [x] Publish a separate local multilingual preview, run distribution rejection tests and hash every ZIP entry against package contents. Stage only code/public docs, commit conventional message, report SHA and final status; leave user handoff archives untouched.


## 完成记录（2026-11-17）

- 按选择准备：英语保持原基础锁与 RuntimePath，五个额外模型独立原子安装；自动识别准备全部。额外模型共 738.3 MB，完整六语约 856.5 MB。公开模型锁包含逐个官方 URL、字节数和 SHA-256，模型正文/私人文件不进入软件或 Git。
- 已实现并测试自动/指定语言、入队选择持久化、旧队列读取、明确重选更新待办、日韩分组、日文々与韩文分解字母、六条机翻白名单、来源/agent 先后顺序升级及部分稿保护。语言切换屏蔽旧报告/就绪回调，子进程退出完成后另行恢复按钮。
- Core 157、Infrastructure 550 项通过，共 707 项。真实六语原创建模流程与本机 ELS 识别通过，核对逐组/空白/重复和原词 SHA-256、中转来源；取消释放子进程与租约。2 句样例的额外语言首切换约 0.34–0.89 秒，先前含模型加载与切换的采样峰值约 474.4 MiB；这些是缓存条件下的夹具结果，不代表整库均值或语义验收。
- 最终包内四语与六主题结果见同次 [开发记录](../../development-log.md)。UI 可选择真实已准备工具目录，已验证法语实际按钮贯通 Argos/数据库/渲染和完成后的控件恢复；其他暂停/批量场景使用可控引擎夹具。
- 用法、下载表、迁移及不同模型的原始许可/credits 见 [离线翻译指南](../../offline-translation.md)。未操作用户歌词/真实 Apple Music，未覆盖全新机器、全部 CPU/DPI/弱网/单首混合语种，也未做全机防火墙断网验收。只在本地提交，保留原交接 ZIP，不同步 GitHub。
