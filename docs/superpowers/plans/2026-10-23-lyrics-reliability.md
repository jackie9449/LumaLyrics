# Lyrics Reliability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore dependable lyrics lookup and prepare audited originals and Chinese translations for the complete personal library.

**Architecture:** Retain exact recording checks and add independent provider fallback with honest provenance. Audit active library bindings and original-hash translation coverage into personal reports. Run resumable personal workers after fixture tests, preserving manual content and semantic review status.

**Tech Stack:** .NET 10, HttpClient, SQLite, existing local translation runtime, xUnit, WPF.

**Spec:** `docs/superpowers/specs/2026-10-23-lyrics-reliability-and-review.md`

## Global Constraints

- Personal text and reports remain outside Git and software packages.
- Never fabricate synchronization times or infer instrumentals from absence.
- Preserve manual bindings and existing offsets/translations.
- Do not label machine drafts semantically reviewed or claim full coverage while rows remain unresolved.

### Task 1: Provider reliability

**Files:** `LrclibProvider.cs`, new `ResilientLyricsProvider.cs`, `NetEaseTranslationProvider.cs`, `LibraryPrefetcher.cs`, `PlaybackCoordinator.cs`, `Models.cs`, Probe `Program.cs`, provider tests.

**Interfaces:** `ResilientLyricsProvider : ILyricsProvider, IDisposable` supplies default foreground/background lookup. `LyricsCandidate.ProviderName` and `SourceLabel` retain source identity. Existing `AutomaticLyricsMatcher.Select(track,candidates)` remains the automatic-import gate.

- [x] Add tests for exact lookup surviving search 503, small artist searches not proving absence, independent fallback, cancellation and correct source labels. Run focused tests before implementation.
- [x] Prefer exact lookup in batch mode; preserve usable exact results when search is unavailable, bound search retries and add a cooldown after transient service failure. An absent artist-search result must use the recording signature rather than become a negative cache.
- [x] Adapt matching NetEase timed originals through `TranslationCandidate.OriginalCandidateFor(local)`; try fallback when primary has no checked result or is unavailable. Preserve cancellation, rate-limit behavior and original raw provider metadata.
- [x] Use the composite in foreground, background and Probe; replace hard-coded LRCLIB save/display labels and provider-ID response filenames. Run provider/storage regression tests and build.

### Task 2: Audited personal preparation

**Files:** new `LibraryCoverageAudit.cs` and Probe audit command, personal worker scripts under ignored `.tools`, personal JSON/Markdown reports outside software.

**Interfaces:** `LibraryCoverageAudit.Create(root, snapshot, token)` and `LibraryCoverageAudit.Write(report, personalDestination)` records active recordings, validated originals, confirmed instrumentals, manual protections and exact-hash translation coverage. It must not change bindings.

- [x] Audit the current native snapshot and save a consistent personal database backup before new imports.
- [x] Run the repaired resumable prefetcher over all active rows, with progress/errors and bounded retries; separately classify source absence, unverified versions and unavailable services.
- [ ] Refresh source translations for valid foreign originals; export missing exact-hash draft rows, perform local inference, and inspect language/context anomalies before importing complete mappings. Preserve existing manual translations and AI provenance.
- [x] Produce a current coverage report, unresolved recording list and semantic-review status. Continue independent pending work rather than equating an exhausted service pass with complete lyrics.

### Task 3: Integration and publication

- [x] Run source and packaged WPF checks, appropriate unit tests, native integration and distribution privacy validation. Update source documentation using only original fixtures and aggregate evidence.
- [x] Publish a versioned package; commit conventional changes and synchronize GitHub with a checked expected SHA, then verify the actual commit and tree.

Execution is inline under the owner's standing authorization; queue and OCR implementation have separate plans.

## Execution status

Provider, audit, alignment, whole-original AI draft reuse, queue and OCR software work is implemented and verified. Source and packaged WPF each passed 613 checks, with 280 unit tests and live packaged queue/OCR proof. Real personal review JSON/images survived export/restore unchanged.

Personal source and translation workers completed a full pass. The private coverage report and unresolved-recording CSV retain remaining missing originals, source failures, version mismatches and translation gaps; full-library preparation and semantic review remain incomplete. Cached source pairs were reconciled and twelve complete personal AI drafts were reused only for identical complete original sequences. No isolated phrase translations or guessed time stamps were imported. A request to download additional model weights was rejected by automatic approval without a stated reason; no new model inference was performed. Existing AI provenance remains intact.

Publication is completed only after the versioned archive passes the privacy guard and the expected-SHA GitHub update has been read back.
