# Retroactive instrumental gap implementation plan

**Goal:** Advance immediately on Enter, and let G turn the already marked transition into the beginning of an instrumental gap.

**Architecture:** `ManualLyricTiming.Mark(position)` shares a boundary when ending a line. `BeginGap()` clears only the next line's automatically shared start, retaining the previous end in both the timeline and undo history. A later Enter uses the playback position to start that same next line and close the gap.

**Tech Stack:** .NET 10, WPF, LRC.

**Spec:** Owner's 2026-11-18 clarification. Enter ends the line and shows the next lyric. G then inserts a gap after the previous line. Enter closes the gap. Continuous lines need one Enter.

## Constraints

Keep raw playback time, recording and binding guards, timestamp ordering, explicit undo, localized theme-compatible controls and the existing personal-data boundaries.

## Task 1 — Restore continuous Enter and insert gaps after the transition

- [x] Update the Core tests to mark a shared boundary, check that the next line already starts there, call `BeginGap()` and verify both the cleared start and retained end. Mark vocal return, then assert that the blank group began at the shared boundary. Cover undo and repeated G.
- [x] In `ManualLyricTiming.cs` replace the optional join with the normal Enter path. Add `CanInsertGap` for an open line whose start shares the preceding end. Record its undo state before clearing its start in `BeginGap()`. Keep the preceding end and recording binding.
- [x] In `LyricTimingWindow.xaml(.cs)` map G and its button to `BeginGap()`. Show the pending interval on the previous timeline row and the waiting state on the right. Preserve key ownership and repeat suppression. Amend the three timing localization files and the user guide.
- [x] Drive consecutive Enter, later G at a different playback time, undo, G again and Enter at vocal return in `LyricTimingVerification.cs`. Verify recorded timestamps and preview. Run Core and Infrastructure tests and four-language WPF verification on the preview package.
- [x] Record the results in release notes and the development log. Verify the package and commit the change locally.
