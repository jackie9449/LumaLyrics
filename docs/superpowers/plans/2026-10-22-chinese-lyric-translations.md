# Chinese Lyric Translations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking. This session follows the user's continuing implementation authorization with inline execution; no delegation or execution-choice pause.

**Goal:** Provide independently switchable Chinese lyric translations and a conservative, resumable preparation pass over the personal Apple Music library.

**Architecture:** Core alignment maps a provider's original/translated lyric pair to the exact local original. Infrastructure owns public requests, a separate personal SQLite repository, and a leased batch worker. WPF renders subordinate translation text and exposes visibility, preparation, provenance and import in Settings.

**Tech Stack:** C# 14, .NET 10, WPF, Microsoft.Data.Sqlite, xUnit, PowerShell.

**Spec:** `docs/superpowers/specs/2026-10-22-chinese-lyric-translations.md`

## Global Constraints

- Main-window and floating lyric visibility are independent, saved immediately, initially off.
- Never replace the next original lyric with a translation.
- Store translations in a separate personal `translations.db`, tied to exact original text by SHA-256 and conservative recording identity.
- Preserve original/manual lyrics.
- No AI drafts without the user's choice to allow them.
- Do not call a missing lyric instrumental or claim every translation is correct.
- Software archives must exclude all private translations and reports.
- Synchronize tested updates to jackie9449/LumaLyrics with an expected-head lease and verify the resulting tree.

---

### Task 1: Exact original alignment

**Files:** Create `src/LumaLyrics.Core/ChineseTranslations.cs`; test `tests/LumaLyrics.Core.Tests/TranslationTests.cs`.

**Interfaces:** `TranslationLine(int GroupIndex,string Text)`, `ChineseTranslation(string SourceHash,IReadOnlyList<TranslationLine> Lines,string Source,string SourceUrl,string Review,DateTimeOffset PreparedUtc)`. `TranslationAlignment.Align(LyricsDocument local,LyricsDocument original,LyricsDocument translated,string source,string url)` returns a nullable `ChineseTranslation`. `TranslationAlignment.Hash(string raw)` binds the source.

- [x] Test independently timed translation pairs, duplicate refrains, blank intervals, Unicode scripts, changed text, provider offsets, Japanese kanji, missing lines and timing ambiguity. The minimal fixture is `[00:01]We sail home\n[00:03]Across the quiet bay\n[00:05]The stars will guide us` with Chinese counterpart `我们驶向家乡 / 穿过宁静海湾 / 星光为我们引路`.
- [x] Implement complete ordered original-text comparison and a one-to-one translation timestamp mapping, then retime by local group index. Reject uncertain maps; ignore only explicit metadata/credit rows. Preserve blank original groups.
- [x] Run `Invoke-Dotnet test tests/LumaLyrics.Core.Tests -c Release --no-restore` and inspect all results.

### Task 2: Personal storage, provider and resumable preparation

**Files:** Create `src/LumaLyrics.Infrastructure/TranslationRepository.cs`, `NetEaseTranslationProvider.cs`, `TranslationPreparation.cs`; modify `tools/LumaLyrics.Probe/Program.cs`; tests `TranslationStorageTests.cs`, `TranslationProviderTests.cs`, `TranslationPreparationTests.cs`.

**Interfaces:** Repository `Find(Track,LyricsDocument)`, `Save(Track,ChineseTranslation)`, preparation queue/status and failure records. Provider `LookupAsync(Track,CancellationToken)` returns recording candidates containing original/translated LRC and attribution. Worker `RunAsync(string libraryPath,CancellationToken,int? maximum)` exposes progress and uses a file lease. Probe command `--translations <root> [maximum] [--retry]` invokes the same worker.

- [x] Test wrong versions/artists/duration, malformed/oversized responses, cancellation and rate limits with an injected HttpMessageHandler. Public endpoints are `/api/search/get/web` and `/api/song/lyric`, with an honest product user agent and serial pacing.
- [x] Save source-hash keyed records transactionally; refuse mismatched hashes, invalid indexes and duplicate lines. Track retry states separately from translation content and retain existing translations on errors.
- [x] Read active library and existing originals; prioritize known foreign songs. Preserve manual bindings, quality-check automatic originals, and never replace a usable original to force alignment. Only import a provider original after the existing conservative recording/quality checks pass.
- [x] Test resumable queue, source changes, instrumental/Chinese classification, absent originals/translations, shared worker exclusion and cancellation. Run infrastructure tests and start the actual personal batch using the Probe command after passing tests.

### Task 3: Optional WPF rendering and settings

**Files:** Create `src/LumaLyrics.App/ChineseTranslationPanel.cs`, `PlaybackTranslations.cs`, `TranslationVerification.cs`; modify `PlaybackCoordinator.cs`, `MainWindow.xaml.cs`, `PlayerViewModel.cs`, `OverlaySurface.xaml`, `OverlaySurface.xaml.cs`, `SettingsWindow.cs`, `SettingsStore.cs`, `UiThemeVerification.cs`.

**Interfaces:** Coordinator exposes `Translation`, `TranslationStatus`, `TranslationProgress`, `RefreshTranslation()`, batch start/pause and manual translation import. Settings stores `ShowChineseTranslation` and `OverlayShowChineseTranslation`. View model exposes `CurrentTranslation` and `NextTranslation`.

- [x] Load only hash-matching cached translation when applying an original. Clear on track/document changes; observe external worker progress with the existing monitor.
- [x] Append a Settings tab without changing existing tab indices. Save independent visibility toggles immediately, show original-text/translation provenance, local aligned import, progress and prepare/pause/retry actions. Explain public provider metadata disclosure where the action occurs.
- [x] Render the smaller translated line beneath each original; preserve styling/scroll/selection and main/floating isolation. Collapse absent translation elements. Keep instrumentals and empty intervals blank.
- [x] Add explicit `--verify-translations` fixtures and include them in full UI verification. Check visibility persistence, independent toggles, source invalidation, current/next meaning, per-theme readability and instrumental suppression.

### Task 4: Personal export and software boundaries

**Files:** Modify `PersonalLibraryArchive.cs`, `PersonalLibrarySnapshot.cs`, `PersonalLibraryManifest.cs`, `scripts/verify-distribution.ps1`, `scripts/test-distribution.ps1`, `tests/LumaLyrics.Infrastructure.Tests/PersonalLibraryTests.cs`.

**Interfaces:** Optional snapshot `state/translations.db`; readable `translations/<hash>.json` with manifest kind `translation`. Existing version-1 personal archives remain accepted.

- [x] Snapshot and validate translations independently with SQLite backup and schema validation. Export readable source/translation/provenance records and restore the state into the new personal root.
- [x] Test translated and legacy archive round trips, corruption, path rejection and preservation of source hashes/manual records.
- [x] Reject private translations, queues and preparation reports from software archives; run `scripts/test-distribution.ps1`.

### Task 5: Release and live preparation audit

**Files:** Modify `Directory.Build.props`, lock files as required, `README.md`, release instructions and `AGENTS.md`; add self-authored screenshots/check report in `docs/screenshots-ui-v0100/`.

- [x] Set version `0.10.0`, restore once with NuGet audit disabled, build Release and run all units. Run full UI verification in an isolated root, inspect every Boolean check and original screenshots, then package with `scripts/publish-ui.ps1` and repeat full WPF verification from the packaged Microsoft host.
- [x] Audit the real personal preparation results: translated, already Chinese, confirmed instrumental, no reliable original, no translation, ambiguous recording and network retry. Keep track-level metadata/lyrics/report only in the personal root. Report remaining limitations explicitly.
- [x] Normalize changed text to LF; stage only product source/tests/docs; `git diff --check`; commit `feat: add optional Chinese lyric translations and personal preparation`. Upload the tested tree, update main using the inspected remote SHA, verify remote commit/tree and align the local branch. Verify clean status and final ZIP SHA-256.

## Self-review

Coverage: settings, both renderers, accuracy/provenance, private batch preparation, data separation, export/restore, regressions, distribution and GitHub synchronization are assigned above. All referenced interfaces are defined by the tasks that produce them. No additional dependency or cloud account is required.
