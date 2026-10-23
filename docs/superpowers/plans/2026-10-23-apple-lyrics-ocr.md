# Apple Music Lyrics OCR Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Provide an on-device, inspectable comparison of Apple Music's visible lyrics against the exact personal original.

**Architecture:** Verify real native lyric-pane capture and local recognition first. Separate Windows capture/OCR from Core alignment and personal evidence storage. Bind every observation to current recording, native capture bounds, source hash and capture time; present partial comparison and differences without generating timestamps or silently replacing text.

**Tech Stack:** .NET 10, Windows OCR or a verified local fallback, FlaUI/native capture, WPF, SQLite/JSON, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-23-lyrics-reliability-and-review.md`

## Global Constraints

- OCR is text evidence, not authoritative timing or authorial-intent verification.
- Keep captures and recognized personal text outside Git and packages.
- Preserve playback and queue, bind observations to exact original hash, and require actual capture/recognition evidence before claiming feasibility.

### Task 1: Runtime and native feasibility

**Files:** exploratory `.tools/winrt-ocr-probe`, existing explicit native verifier, new Windows OCR adapter only after a successful probe.

- [x] Test available local recognition languages and real recognition of an original fixture through the unpackaged host; catch missing package/language support and record a clear unsupported result.
- [x] Inspect the installed Apple Music lyrics toggle, pane and text accessibility; capture only its verified visible lyric area and restore panel/window preferences.
- [x] Recognize an actual native lyric viewport and compare with an existing original privately. Record scope, blur/animation/scroll and language limitations; do not treat a single viewport as full-song coverage.

### Task 2: Comparison, evidence and user controls

**Files:** new Core `LyricsOcrComparison.cs`, Windows `AppleLyricsCapture.cs`/`LocalLyricsOcr.cs`, Infrastructure `LyricsReviewStore.cs`, App `LyricsReviewWindow.cs`, coordinator/diagnostics integration and tests.

**Interfaces:** `LyricsOcrObservation` contains recording identity, original hash, capture UTC, media position when available, recognized lines, language and capture bounds. `LyricsOcrComparison.Compare(document,observation)` aligns normalized visible text and emits matched/uncertain/different lines plus observed coverage. Review storage is personal-only.

- [x] Test punctuation/case normalization, repeated lines, CJK spacing, recognition ambiguity, changed recording/hash and partial coverage before implementation.
- [x] Add start/stop/current capture and inspectable comparison UI, preserve cancellation and native state, and save evidence only for matching recording/hash observations.
- [x] Verify real capture, local-only OCR and public original fixtures, and add OCR/review file patterns to personal backup and distribution guards. Publish honest feasibility and compatibility results with the tested update.

Execution is inline. If the current native viewport or recognizer is unavailable, retain diagnostics and continue the other authorized work while investigating a supported fallback.

Verified: 280 unit tests; source and packaged WPF each 613 passing checks. Native packaged queue move/restore and OCR/native-text comparison passed, with personal captures kept outside Git. Publication status is recorded by the checked GitHub synchronization.
