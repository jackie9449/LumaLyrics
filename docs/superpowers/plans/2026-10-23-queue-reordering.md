# Queue Reordering Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Drag a queued occurrence to a new actual Apple Music queue position without interrupting the current recording.

**Architecture:** Model a snapshot-bound source occurrence and insertion index, validate the expected permutation in Infrastructure, and adapt the native queue's supported reorder action. Both WPF queue views share drag presentation and an async reorder callback. Confirm actual order and current playback before reporting success.

**Tech Stack:** WPF drag/drop, FlaUI UIA3, guarded native input, .NET 10, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-23-queue-reordering.md`

## Global Constraints

- Retain repeated occurrences and the original current recording/pause state.
- Do not merely reorder displayed rows, replace the queue through library Play or send blind input.
- Keep real native diagnostics private; confirm the actual final order.

### Task 1: Native feasibility and move model

**Files:** `MusicPlaybackQueue.cs`, `AppleMusicQueueReader.cs`, new `AppleMusicQueueReorder.cs`, native private verifier and queue unit tests.

**Interfaces:** `MusicQueueMove(MusicQueueSnapshot Snapshot,int SourceIndex,int InsertionIndex)` uses the original queue's insertion boundary. `MusicPlaybackQueue.MovedEntries(move)` computes the exact permutation. `MatchesMove(move,actual)` rejects stale source snapshots; `ConfirmsMove(move,actual)` validates order with recording identities and counts.

- [x] Test beginning/end/up/down/no-op moves, repeated identities, invalid boundaries, stale snapshots and changed current recording.
- [x] Inspect actual native row accessibility and reorder support; trial a reversible same-section move and restore it before enabling UI actions.
- [x] Implement one guarded bounded native move using captured occurrence identities and verified window/row geometry or a supported accessibility action. Release input on cancellation/failure, avoid repeated ambiguous mutations, and verify the resulting actual queue and unchanged playback.

### Task 2: Shared drag UI

**Files:** `PlaybackQueueView.cs`, new `PlaybackQueueReordering.cs`, `PlaybackQueueSidebar.cs`, `MusicLibraryQueueView.cs`, `AppleMusicPlaybackBridge.cs`, `MusicLibraryNavigation.cs`.

**Interfaces:** Both queue views receive `Func<MusicQueueMove,CancellationToken,Task<LibraryPlayResult>> reorder`. Drag data includes a private view token and the displayed snapshot, never only a song title.

- [x] Start dragging only after the system threshold on a real row; draw an insertion adorner, autoscroll near edges, and reject external/current/stale/incomplete drags.
- [x] On drop call the native reorder callback, suspend reads while pending, preserve click/double-click/Enter behavior, and refresh from actual native state after completion. Do not optimistically change the queue.
- [x] Verify all themes and narrow widths, scrolling, cancelled drag, duplicates, delayed native callbacks and protected playback using original fixtures and live integration. Publish with the lyrics/OCR work after independent checks pass.

Execution proceeds inline; unsupported native operations remain explicit rather than falsely reporting a local reorder.

Verified: 280 unit tests; source and packaged WPF each 613 passing checks. Native packaged queue move/restore and OCR/native-text comparison passed, with personal captures kept outside Git. Publication status is recorded by the checked GitHub synchronization.
