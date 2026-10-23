# Playback Queue Implementation Plan

> **For agentic workers:** Execute this plan inline, task by task. The user authorized implementation and GitHub synchronization; no separate planning approval is required.

**Goal:** Display Apple Music's current playback queue in the music library and support inserting a library song as Play Next.

**Architecture:** Extend the existing serialized native Apple Music handoff with a bounded queue reader and an explicit Play Next command. Keep queue parsing and identity logic separate from WPF; a dedicated library panel displays actual queue snapshots, preserves browsing context, and refreshes after native changes. Native verification runs only in an explicit isolated diagnostic mode.

**Tech Stack:** .NET 10, C# 14, WPF, FlaUI UIA3, GSMTC, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-23-playback-queue.md`

## Global Constraints

- Keep personal queue metadata and live diagnostics outside Git and software archives.
- Preserve actual native queue order and repeated recordings; do not synthesize a queue from library sorting.
- Preserve native minimized preference, temporary local filter, current playback, and library browsing context.
- Never substitute global search for the local `FilterTextBox`.
- Keep the three application themes and independent floating lyric settings intact.
- Update GitHub using a conventional commit and checked expected SHA; never force-push.

---

### Task 1: Observe the installed native queue

**Files:** Modify `src/LumaLyrics.App/NativeLibraryPlaybackVerification.cs`; create `src/LumaLyrics.App/AppleMusicQueueHandoff.cs`.

**Interfaces:** Add `AppleMusicLibraryHandoff.InspectQueueAsync(CancellationToken)` for an explicit diagnostic entry. It acquires the existing gate, toggles `PlayQueueToggleButton` only if closed, captures bounded control metadata, and restores the original toggle/minimized state in `finally`.

- [x] Add `--native-inspect-queue` before the recording-required branch in the explicit verification entry point.
- [x] Build with `scripts/common.ps1` and run the isolated diagnostic; save only beneath `.tools/qa-v0120-native`.
- [x] Inspect queue list/section identities and raw row metadata. Record supported structure in implementation comments without personal track names.

### Task 2: Native queue and Play Next behavior

**Files:** Create `src/LumaLyrics.Infrastructure/MusicPlaybackQueue.cs`, `tests/LumaLyrics.Infrastructure.Tests/MusicPlaybackQueueTests.cs`; modify `src/LumaLyrics.App/AppleMusicLibraryHandoff.cs`, `src/LumaLyrics.App/AppleMusicQueueHandoff.cs`, and `src/LumaLyrics.App/AppleMusicPlaybackBridge.cs`.

**Interfaces:** `MusicQueueEntry` holds recording metadata and manual-insertion marker; the snapshot holds the separate current recording; `MusicQueueSnapshot` holds ordered entries, capture time, completeness, and a user-facing status. Native `ReadQueueAsync` returns a bounded actual snapshot; `InsertNextAsync(MusicLibrarySong,CancellationToken)` invokes only the exact Play Next command and returns `LibraryPlayResult`.

- [x] Add meaningful tests for exact localized Play Next recognition and queue parsing from the observed native structure, including duplicate titles/recordings and unavailable metadata.
- [x] Run focused tests and resolve observed failures; implement parsing without sorting or deduplication.
- [x] Reuse exact recording selection with an explicit command mode for Play Next. Preserve cancellation, gate cleanup, local filter, and minimized preference.
- [x] Extend native verification to capture before/after queue, verify current-song continuity, and verify the actual native next slot contains the inserted recording, including repeated occurrences.
- [x] Run native verification with exact private recording identities; keep all resulting metadata in `.tools`.

### Task 3: Library queue UI and insertion

**Files:** Create `src/LumaLyrics.App/MusicLibraryQueueView.cs`, `src/LumaLyrics.App/PlaybackQueueView.cs`, and `src/LumaLyrics.App/PlaybackQueueVerification.cs`; modify `MusicLibraryView.cs`, `MusicLibraryNavigation.cs`, and the WPF verification entry.

**Interfaces:** Append optional queue-read and Play Next callbacks to `MusicLibraryView` so existing verification fixtures remain compatible. Queue operations use cancellation/version guards. A themed panel displays current/upcoming tracks, loading/empty/error/partial states, and refresh; Play and album actions retain latest-request cancellation; accepted insertions execute serially without dropping clicks and then refresh the visible queue.

- [x] Add a 播放列表 entry alongside the existing library navigation; opening it preserves album/song search, selection, and scroll.
- [x] Add a distinct 插播 button to song rows with keyboard-accessible names; keep immediate Play separate.
- [x] Show manual-insertion marker; the snapshot holds the separate current recordings with native ordering and original-cover fallbacks. Refresh only while the queue is visible and on actual current-track changes or insertion.
- [x] Verify duplicate rows, stale reads, cancellation, insertion failure, disposal, layout, keyboard access, and all three themes with original fixture recordings.

### Task 4: Release and synchronize

**Files:** Update `Directory.Build.props`, `README.md`, `docs/music-library.md`, `AGENTS.md`, `scripts/publish-ui.ps1`, `scripts/verify-distribution.ps1`; create `docs/screenshots-ui-v0120/` with synthetic verification artifacts only.

- [x] Set version 0.12.0, document actual queue behavior and native integration limits, and complete this checklist.
- [x] Run unit tests, distribution boundary tests, a warning-free Release build, and full isolated source WPF verification.
- [x] Publish the application, run packaged WPF verification, verify the tested DLL and ZIP hashes, and audit archive contents for personal data.
- [x] Review the diff and staged files; create a conventional commit, synchronize GitHub with expected-head protection, and verify remote/local trees match.

Implementation observations: the installed client exposes manual insertions in PlayQueueListViewManual before PlayQueueListView. Confirmation waits for asynchronous native updates and requires an increased occurrence count when the recording was already first. The live check preserves the current recording and verifies the actual next slot without skipping audio; test insertions are removed only against the exact captured baseline, and the original queue is then read back in full.
