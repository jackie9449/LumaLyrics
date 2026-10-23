# Playback Queue Navigation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the owner hide the playback queue and jump to an exact queued occurrence by double-clicking it.

**Architecture:** Persist visibility in AppSettings and scope sidebar polling to its actual host. Pass a displayed snapshot and zero-based occurrence index from the shared queue view into a native queue handoff. Validate the native snapshot and invoke the captured native item, then verify actual playback and queue progression through the existing bridge.

**Tech Stack:** .NET 10, WPF, FlaUI UIA3, WinRT media sessions, xUnit, PowerShell.

**Spec:** `docs/superpowers/specs/2026-10-23-queue-navigation.md`

## Global Constraints

- Default to visible for existing profiles; stop hidden queue polling.
- Do not replace the queue by playing a library search result, deduplicate repeated tracks, or issue repeated Next commands.
- Keep real queue metadata and integration diagnostics private.
- Preserve independent floating appearance and ownership-aware Apple Music minimization.
- Version 0.14.0; synchronize tested changes without overwriting newer remote changes.

---

### Task 1: Persistent visibility and responsive layout

**Files:** Modify `src/LumaLyrics.Infrastructure/SettingsStore.cs`, `src/LumaLyrics.App/MainWindow.xaml`, `src/LumaLyrics.App/MainWindow.xaml.cs`; create `src/LumaLyrics.App/PlaybackQueueNavigation.cs`.

**Interfaces:** `AppSettings.ShowPlaybackQueue` defaults to true. `SetPlaybackQueueVisible(bool visible, bool persist=true)` updates the toggle, host visibility and column layout. Sidebar scope changes from LyricsPage to PlaybackQueueHost.

- [x] Add a themed accessible ToggleButton beside the lower-right playback-page actions. Bind its visual checked state to `ShowPlaybackQueue` during initialization, and process Checked/Unchecked with an initialization guard.
- [x] Extract `ApplyPlaybackLayout()` from OnSize; use `PlaybackQueueColumn.Width = new GridLength(settings.ShowPlaybackQueue ? width : 0)` and `PlaybackQueueHost.Visibility = visible ? Visibility.Visible : Visibility.Collapsed`.
- [x] Verify all three themes at 1220/980/820 widths, hidden polling, reentry, settings save/reload and keyboard focus. Preserve the existing resize and lyrics-centering behavior.

### Task 2: Native occurrence playback and shared interactions

**Files:** Modify `src/LumaLyrics.Infrastructure/MusicPlaybackQueue.cs`, `src/LumaLyrics.App/AppleMusicQueueReader.cs`, `src/LumaLyrics.App/AppleMusicPlaybackBridge.cs`, `src/LumaLyrics.App/PlaybackQueueView.cs`, `src/LumaLyrics.App/PlaybackQueueSidebar.cs`, `src/LumaLyrics.App/MusicLibraryQueueView.cs`, `src/LumaLyrics.App/MusicLibraryView.cs`, `src/LumaLyrics.App/MusicLibraryNavigation.cs`; create `src/LumaLyrics.App/AppleMusicQueuePlayback.cs`; extend `tests/LumaLyrics.Infrastructure.Tests/MusicPlaybackQueueTests.cs`.

**Interfaces:** `MusicQueueSelection(MusicQueueSnapshot Snapshot, int Index)` identifies a zero-based occurrence. `MusicPlaybackQueue.MatchesSelection(selection, actual)` rejects stale snapshots and invalid metadata. `ConfirmsJump(selection, after)` proves requested current recording and expected suffix. `AppleMusicPlaybackBridge.PlayQueueAsync(selection, token)` returns LibraryPlayResult.

- [x] Add unit cases for repeated occurrences, reordered/stale lists, changed current recording, unknown metadata, invalid indexes, partial snapshots and suffix confirmation. Run the focused tests and confirm missing API failures before implementation.
- [x] Inspect native queue row patterns in the explicit private diagnostic; prefer Invoke, otherwise use the verified native queue row's selection and Enter action with foreground/focus/modifier guards, never the library Play path. Extend `CaptureQueue(..., List<AutomationElement>? nativeItems=null)` to retain the exact captured occurrences for an action.
- [x] Implement a bounded cancellable queue handoff using the existing gate and HandoffContext. Compare snapshot and selected raw metadata, mark CommandAttempted before invoking, preserve native queue/history/scroll settings in finally, and never retry an ambiguous attempted RPC.
- [x] Route double-click and Enter on a real upcoming ListBoxItem to a shared queue-view async command. A single click, blank space, buttons or incomplete metadata must not start playback. Preserve latest-request cancellation, visible errors, safe disposal and confirmed-only activation.
- [x] Reuse MusicPlaybackConfirmation and verify `ConfirmsJump` through native queue reads; only confirmed success minimizes Apple Music and activates the owner. Cover delayed and failed commands with isolated WPF fixtures.

### Task 3: Verification, package and synchronization

**Files:** Extend the playback sidebar/native queue WPF verification files and private native playback diagnostic. Update `Directory.Build.props`, `README.md`, `docs/music-library.md`, `AGENTS.md`, publishing allowlists, and add `docs/screenshots-ui-v0140/` original evidence.

- [x] Run relevant infrastructure tests, then the solution test suite and Release build through `scripts/common.ps1`.
- [x] Run the focused queue-navigation WPF checks, inspect original screenshots, then run full source verification with an isolated data directory.
- [x] Verify actual native queue jumping and retained suffix; preserve the original recording, queue and pause state as supported. Keep diagnostics under ignored `.tools` and publish booleans only.
- [x] Publish 0.14.0, run packaged WPF checks and distribution privacy validation, and verify the ZIP contains the tested DLL.
- [x] Review `git diff --check` and public files for personal data; create a conventional commit, publish using the checked remote SHA, and verify local/remote commit plus tree. Report the package and commit links.

Execution proceeds inline under the owner's existing authorization; no delegation or additional approval is needed.
