# Lyric Tail Acceptance and Failure Reassessment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** 修正长尾段歌词误拒，展示逐曲拒用原因、更正匹配度名称，并重评旧失败。

**Architecture:** Core 统一提供含歌曲信息/歌词质量的自动判定；Infrastructure 按策略修订管理负缓存和逐曲状态；WPF 仅在显示边界本地化诊断，复用现有搜索和列表虚拟化。针对明确授权的个人库在隔离测试后单独执行有范围、可取消的重评，原始响应不进软件。

**Tech Stack:** .NET 10、WPF、SQLite 5版现有个人库、现有受限速来源。

**Spec:** `docs/superpowers/specs/2026-11-17-lyric-tail-reassessment.md`

## Global Constraints

- 不修改原词、时间戳、偏移、哈希和译文，不伪造尾段/器乐语义。
- 保留版本/艺人/时长、格式及正文检查，来源全文一致不等于音频人工核对。
- 只读仓库不迁移；负缓存两列旧接口保持兼容；手工写入及库成员变化阻止迟到自动写。
- 保留用户联网/自动预缓存/暂停设置；只重评相关已确认成员。四语六主题；本地提交，私人数据不分发。

### Task 1: 统一候选决定与长尾例外

Files: `src/LumaLyrics.Core/AutomaticLyricsMatcher.cs`, `MatchRules.cs`; `tests/LumaLyrics.Core.Tests/AutomaticMatchingTests.cs`.
Interfaces: `AutomaticLyricsMatcher.PolicyVersion=2`, `Decide(Track,LyricsCandidate,IReadOnlyList<LyricsCandidate>)` returns accepted flag/score/assessment/reason; Select uses that same decision.

- [x] Add original fixtures with 479s duration, six or more timed groups over >60s and a matching full source text ending at 250s. Verify acceptance; reject missing/reordered full text, compressed <60s cues, insufficient content, incorrect version/artist/duration, out-of-range time and section-only tail markers. Run the Core test project via scripts/common.ps1.
- [x] Compute existing text coverage before long-tail assessment. Tail exception requires ordered normalized full equality, >=6 lyric groups, >=80 normalized characters and >=60s timestamp span; retain 55% as risk condition and all other checks. Return a factual text/range explanation, not instrumental or semantic verification.
- [x] Centralize candidate rejection reasons, preserve ranking and conflicting instrumental safeguard; metadata mismatch reasons explicitly identify title/artist/version/duration/album problems.

### Task 2: Persistent reasons, negative-cache revision and safe re-evaluation

Files: `LyricsRepository.cs`, `LibraryPrefetcher.cs`, new `LyricsLookupDiagnosis.cs`, `LibraryWorkspaceCatalog.cs`, `tests/LumaLyrics.Infrastructure.Tests/LyricsPolicyTests.cs`.
Interfaces: per-failure policy sidecar includes fingerprint+expiry+PolicyVersion without changing legacy LookupFailures columns; singleton policy marker requeues related unfinished active members once. `Save(...,LibraryQueueItem? expectedLibrary=null)` atomically validates current member and preserves manual binding.

- [x] Test old failure invalidation/current failure retention/reopen idempotence/old writer expiry mismatch, read-only nonmigration, only related queue reset, manual/offset/translation preservation, and stale library target rejection.
- [x] Add policy marker and sidecar tables; clear only legacy negative suppression and requeue unfinished matching failures. Preserve network backoff and established manual/cached records; prioritize policy re-evaluation pending entries. Do not overwrite original documents to re-assess cache.
- [x] Persist the shared diagnostic in per-song LibraryTracks.Detail from foreground, batch and background paths, distinguishing source unavailable from actual no candidates. Include reason in workspace assessment cache identity and hide outdated failure reasons once original is ready.
- [x] Exercise source/format/late-manual/cancellation cases and run Core/Infrastructure suites. Private live scope is captured read-only before migration; take an online SQLite snapshot before applying it and use exact-member automatic writes for the user-authorized re-evaluation.

### Task 3: UI, actual retry and deliverable

Files: `PlaybackCoordinator.cs`, `PlaybackLibraryWorkspace.cs`, `DialogWindows.cs`, `LibraryWorkspaceUi.cs`, `TranslationJobListPanel.cs`, localization catalogs, verification helpers, public guide/release/development notes.

- [x] Show localized per-song reason with bounded rows/tooltips and selected detail; display 歌曲信息匹配度 plus independent auto-check explanation in candidate preview. Keep manual confirmation available and existing data/controls intact.
- [x] Verify actual batch buttons and dialog across all six themes/four UI languages using original fixtures. Validate rejection becomes visible, refresh clears stale reason, 100% metadata can fail content, long-tail success preserves timeline/hash, and no confidence label remains.
- [x] After tests, re-evaluate only the private captured related scope, respecting current membership/manual data/cancellation/provider pacing. Persist counts and exact refusal reasons privately; verify example and manual offsets/translation hashes unchanged. Report unresolved source failures truthfully.
- [x] Produce a separate local preview, verify distribution privacy and ZIP hashes, update actual development results, commit conventionally and report SHA; no GitHub synchronization.

## Actual results · 2026-11-17

- Core 161/161, Infrastructure 557/557; Release App/Probe built successfully.
- Packaged acquisition UI: 51/51 per language (four languages, six themes); localization: 143/143 per language. Related jobs 115/115, translations 145/145, search hints 84/84. Total selected UI checks: 1,120/1,120.
- Private related scope: 171/171 processed; 11 saved, 24 existing reliable caches passed, 1 existing unconfirmed binding retained with its refusal reason, 135 rejected; no source errors or stale members. Snapshot comparison preserved all existing original documents, bindings and offsets. Translation storage was not written.
- Privacy fixtures: 2 software + 74 private cases passed. Separate local lyric-tail preview; no GitHub action. Final archive integrity and local commit are recorded with completion in the development log.
- Limits: no live playback/timeline verification or semantic/audio certification; unconfirmed candidates retain manual review. Existing unrelated handoff archives remain untouched.
