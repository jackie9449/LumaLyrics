# Playback Sidebar and Resilient Handoff Implementation Plan

> **For agentic workers:** Execute inline, task by task. The user requested implementation and previously authorized GitHub synchronization.

**Goal:** Add the current playback queue to the right of the playback page, tolerate slow Apple Music responses, and minimize the client after confirmed selection.

**Architecture:** Extract a testable media-confirmation waiter and use cancellable native readiness waits. A per-operation native window context guards delayed minimization against stale requests. Reuse the queue rendering with a compact sidebar presentation and refresh it only while the playback page is visible.

**Tech Stack:** .NET 10, C# 14, WPF, FlaUI UIA3, GSMTC, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-23-playback-sidebar.md`

## Global Constraints

- Preserve actual native queue order and repeated occurrences; never infer a queue from library sorting.
- Read-only queue refreshes never restore or activate Apple Music.
- Keep personal data and full native diagnostics outside Git and software archives.
- Preserve independent floating lyric themes, existing bindings and library browsing state.
- Minimize and activate only after confirmed native playback/insertion; stale operations cannot change focus.
- Synchronize tested changes to GitHub without force-pushing or overwriting newer remote commits.

---

### Task 1: Slow-client playback and window lifecycle

**Files:** Create `src/LumaLyrics.Infrastructure/MusicPlaybackConfirmation.cs`, `tests/LumaLyrics.Infrastructure.Tests/MusicPlaybackConfirmationTests.cs`, `src/LumaLyrics.App/AppleMusicHandoffWindow.cs`; modify `AppleMusicLibraryHandoff.cs`, `AppleMusicAlbumHandoff.cs`, `AppleMusicPlaybackBridge.cs`, `MusicLibraryView.cs`, `MusicLibraryQueueView.cs`, `src/LumaLyrics.Windows/GsmtcPlaybackSource.cs`.

**Interfaces:** `MusicPlaybackConfirmation.WaitAsync(Func<CancellationToken,Task<PlaybackSnapshot?>>, Func<Track,bool>, DateTimeOffset, CancellationToken, PlaybackConfirmationOptions?)` returns confirmed/unconfirmed. Options default to a 60 second total budget, 5 second soft read timeout and 250 ms polling. `GsmtcPlaybackSource.Latest` exposes the actual last published snapshot independently of dispatcher delivery. A `HandoffContext` stores native command attempt and window ownership; completion minimizes only the current confirmed operation.

- [x] Write tests that keep an in-flight read pending beyond its soft timeout, retry transient timeouts, reject stale/wrong/non-playing/non-Apple snapshots, expire at the total deadline and cancel without accepting late success.
- [x] Run the failing tests, implement the waiter, then run them to passing. Keep timed-out reads observed and avoid overlapping probes.
- [x] Replace short fixed native retries with bounded readiness polling for navigation/filter/rows/menu/album context. Mark Play/Play Next attempted before invocation and never reissue it on an ambiguous COM timeout.
- [x] Move native window restoration/minimization to confirmed operation completion, guard it with a native ownership version and gate, and suppress activation on failure/stale completion.
- [x] Add delayed original WPF native-readiness verification and explicit live delayed-confirmation/window-state diagnostics under `.tools` only.

### Task 2: Right-side playback list

**Files:** Modify `MainWindow.xaml`, `MainWindow.xaml.cs`, `MusicLibraryNavigation.cs`, `PlaybackQueueView.cs`; create `PlaybackQueueSidebar.cs` and `PlaybackSidebarVerification.cs`; update `UiThemeVerification.cs`.

**Interfaces:** `PlaybackQueueView(Action,Action,bool compact=false)` retains the full library presentation and provides a smaller sidebar. `MainWindow` owns one playback bridge and a sidebar controller with cancellation/version guards, a 15 second visible-page refresh timer, and exact library-cover lookup. Sidebar controls open the library or refresh the actual queue.

- [x] Add a third column to the playback body and a themed queue surface. Keep it on the right at all supported widths; below 1180 pixels stack metadata/transport above lyrics in the left region.
- [x] Reuse queue row rendering in compact form, retaining virtualization and lazy image decoding. Keep library queue navigation unchanged.
- [x] Start/stop refresh with actual page/window visibility, coalesce current-recording changes and artwork updates, skip reads while a native selection is pending, and discard stale or disposed results.
- [x] Add WPF checks for current/upcoming order, duplicates, refresh, navigation, delayed/stale reads, loading/failure/recovery, themes, 1220/980/820 layouts and disposal. Capture only original fixture images.

### Task 3: Release and synchronize

**Files:** Update `Directory.Build.props`, `README.md`, `docs/music-library.md`, `AGENTS.md`, distribution scripts; create `docs/screenshots-ui-v0130/` with synthetic screenshots, UI reports and redacted live-check booleans.

- [x] Set version 0.13.0 and document successful-selection minimization, bounded slow-client waiting and the right-side queue.
- [x] Run unit tests, privacy boundary tests, warning-free Release build and full isolated source WPF verification.
- [x] Publish and verify the packaged WPF app; check the tested DLL hash and final ZIP contents/hash.
- [x] Review and commit the changes, synchronize with expected-head protection, and verify remote/local source trees and a clean working tree.
