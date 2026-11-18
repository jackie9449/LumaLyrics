# Manual Lyric Timing Implementation Plan

**Goal:** Turn static lyrics into a timed LRC by marking the beginnings and ends heard in the correct recording. Assess direct FFmpeg access to local Apple Music downloads.

**Architecture:** The Core timing session owns line boundaries and emits blank LRC groups for explicitly marked gaps. The themed WPF editor obtains raw time from the playback coordinator and saves through the existing guarded manual binding. Audio inspection runs separately against local inputs, with disposable tools and no writes to Apple Music storage.

**Tech Stack:** .NET 10, WPF, SQLite, Apple Music media session, FFmpeg.

- [x] Implement a timing session using ordered line intervals. `Enter` starts the first line, then ends each following line and advances to the next. `G` ends the current line and opens an instrumental gap. `Enter` at the next vocal starts the new line. Support undo and explicit edits. Export LRC only after all lines are marked, keeping blank intervals and repeated lines.
- [x] Add `制作动态歌词…` to the Songs row context menu. Use a static binding or allow pasted text, keep song identity locked, and use an app-themed editor with playback clock, active lyric, progress, scrollable lines, shortcuts and review. Guard stale playback clock, unexpected track change, invalid edits and stale binding changes. Preserve the main-window Space shortcut and original settings.
- [x] Test accepted/invalid timing, empty intervals, undo, raw time, track change, persistence, right-click scoping, keyboard flow, and narrow layout in the shipped window. Verify all application themes and localized copy. Keep personal material outside tests/Git/package.
- [x] Assess file categories in the local Apple Music directories. Test FFmpeg decode/FLAC encoding on accessible files or fixtures, and ordinary read-only decode on sampled subscription caches. Record result and supported batch scope without changing the library. Update docs, validate the local preview, and commit.

The final packaged editor passed 77 checks across four interface languages, plus 289 related checks. Core and Infrastructure passed 752 tests. The playback fixture exercises raw time and recording changes. See the [development record](../../development-log.md) for the local audio probe.
