# Enter-only instrumental timing implementation plan

**Goal:** Mark a line ending with Enter, then start the next vocal with Enter after any instrumental gap. Verify the requested album's downloaded audio format and ordinary FFmpeg compatibility.

**Architecture:** The existing Core timing session records Enter as a single boundary. Only an explicit join action shares a boundary across two lines. The WPF editor shows which boundary Enter will record and saves the existing LRC format.

**Tech Stack:** .NET 10, WPF, LRC, FFmpeg.

**Spec:** The 2026-11-18 follow-up request and [original timing plan](2026-11-18-manual-lyric-timing.md).

## Constraints

Keep existing recording and stale-binding checks, undo, raw playback time, theme resources, localization and personal storage boundaries. Audio inspection uses local read-only input. Only independently decodable audio can be supplied to FLAC encoding.

## Task 1 — Enter marks the boundary the listener hears

- [x] Revise `ManualLyricTimingTests.cs` to call `Mark(13000)` with ordinary Enter in the long-gap case. Assert the next line has no start, `LineAt(18000)` returns -1, and a later `Mark(42500)` controls exactly when vocals resume. Test undo, equal adjacent boundaries and a backwards start.
- [x] Replace `leaveGap` with opt-in `startNext` in `ManualLyricTiming.Mark`. In the normal path close the active line and advance to its successor without a start. When no line is open, allow the same time as the previous end and reject a timestamp earlier than it. Keep final tails and repeated text.
- [x] In `LyricTimingWindow.xaml(.cs)`, use Enter for every ordinary start/end. Expose the existing one-tap join as Shift+Enter only while a line with a successor is open. Explicitly label the waiting state and keep input fields, button keyboard behavior and Backspace handling intact. Update all three `Localization/lyric-timing.*.json` files and the usage notes.
- [x] Drive the dialog through ordinary Enter at 12500 and later 39000 in `LyricTimingVerification.cs`. Assert the wait stage, no lyric during the gap and stored LRC group boundaries. Run Release Core and Infrastructure tests, four-language WPF checks and related regressions from the portable package.

## Task 2 — Check the album audio and finish packaging

- [x] Identify the album using the newly downloaded local files, matching media path metadata or exact track durations where available. Read container and codec metadata. Attempt ordinary FFmpeg decoding with local-file access only. For a decodable ALAC input verify a FLAC output in a temporary directory. Record separately what was identified and what decoded successfully.
- [x] Update release notes and the development log with actual results. Verify the distribution and ZIP checksum, then commit the source and notes in local Git.
