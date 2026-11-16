# Playback Clock Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Follow Apple Music's reported timeline without history-dependent smoothing or counting paused/delivery time incorrectly.

**Architecture:** Anchor the clock at each snapshot's monotonic capture time. Accept authoritative fresh positions directly; keep interpolation for identical quantized positions and fence pre-resume evidence. Verify delayed delivery independently from UI rendering and from audio synchronization.

**Tech Stack:** .NET 10, TimeProvider, GSMTC, WPF, xUnit, existing private timing probe and isolated UI harness.

**Spec:** User request to fix the playback clock; findings in `docs/playback-clock-audit-2026-11-16.md`; repository `AGENTS.md`.

## Global Constraints

- Preserve original lyrics, translations, hashes, offsets, bindings and media control behavior.
- No automatic playback, seek, focus change or personal data mutation for live measurements.
- Keep raw live diagnostics outside software/Git; report actual coverage and limits.
- Use bundled SDK through scripts/common.ps1; isolate UI data. Commit locally, no GitHub synchronization.
- Execute inline in this session; no separate agents or unavailable execution plugins are needed.

## Task 1: Deterministic clock and regression coverage

**Files:** `src/LumaLyrics.Core/PlaybackClock.cs`, `tests/LumaLyrics.Core.Tests/ClockTests.cs`.

**Interface:** Keep `IPlaybackClock.Update(PlaybackSnapshot?)`, `PositionMs`, `Quality` and `Invalidate()` unchanged.

- [x] Add the two failing resume tests from the audit, plus delayed pause, rate changes, stale evidence, same-raw repeats, exact small correction, cross-instance convergence, seek and track-change cases. Use the existing `TestTime` / `Snapshot` fixtures. Exact fresh evidence must produce `raw + snapshot age + delivery` while playing, and just raw while paused.
- [x] Run `Invoke-Dotnet test tests/LumaLyrics.Core.Tests -c Release --no-restore --filter FullyQualifiedName~ClockTests` and retain failures.
- [x] Replace history-weighted correction with snapshot-time anchors. Compute state transitions at capture ticks, not delivery ticks; resume rejects pause-age projection, remembers its boundary across subsequent stale polls, and still accounts for post-capture playback. Reject regressing timestamps/captures, accept fresh backward seeks, and honor rate-only changes.
- [x] Run the clock suite, then both existing unit-test projects once after the final implementation.

## Task 2: Application and live verification

**Files:** `src/LumaLyrics.App/PlaybackClockVerification.cs`, `src/LumaLyrics.App/UiThemeVerification.cs`, `tools/LumaLyrics.Probe/TimingProbe.cs`; change coordinator/source only if tests or live evidence require it.

**Interface:** Add isolated `--verify-playback-clock`, using real coordinator, delayed WPF dispatch, original fixture lyrics and the existing UI report dictionary. Existing `--probe-timing <private path>` remains read-only unless explicitly passed `--play`.

- [x] Feed timed synthetic snapshots through the production coordinator receive path; verify the main position, current original and translation share one timeline after delivery delay, pause/resume and seek. Assert source raw data/offsets remain unchanged.
- [x] Extend the timing probe to log the expected source projection and actual clock together under the same lock, with no track metadata. Preserve the existing optional --play behavior and default read-only mode.
- [x] Build Release and run isolated playback-clock, translation and seek UI checks. Inspect failed checks before changing tolerances.
- [x] Collect a bounded current Apple Music playback sample if available; compare fresh source evidence and the final clock. Do not equate frozen integer positions with projected playback time or claim sample coverage when paused.

## Task 3: Delivery

**Files:** `docs/development-log.md`, `docs/release-notes.md`, `docs/playback-controls.md`.

- [x] Record actual test counts and live source agreement, including any missing native-slider/audio reference.
- [x] Publish a local `clock-sync` preview using `scripts/publish-ui.ps1 -PreviewLabel clock-sync`; validate distribution boundaries and run relevant checks using the packaged host and external data.
- [x] Commit tracked source/tests/docs with a conventional message; verify the commit and working tree. Preserve the user's handoff ZIP files. Return package link, commit SHA and synchronization limits.
