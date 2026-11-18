# Library Manual Lyrics Implementation Plan

**Goal:** Provide manual lyric search and confirmation from the Songs row context menu.

**Architecture:** Reuse the candidate and search dialogs with a library target. Execute catalog queries independently of playback and save through the existing manual binding transaction with native membership and prior-binding checks.

**Tech Stack:** .NET 10, WPF, SQLite, existing lyric providers.

- [x] Add a themed context menu to realized song rows in `TranslationJobListPanel.cs`. Capture the opened row via `PlacementTarget` and open `CandidateWindow` for that row. Keep selection intact and support the keyboard context-menu gesture through the row's native `ContextMenu` property.
- [x] Add a library mode to `CandidateWindow` in `DialogWindows.cs`. Load candidates using the row metadata, allow the existing edit-search button, keep a fixed binding target, and present results and errors locally. Retain the existing playback mode and its restore behavior.
- [x] Add scoped lookup and confirmation in `PlaybackLibraryManualLyrics.cs`. Use the shared providers without replacing playback lookup state. Pass the captured membership and binding ID to `LyricsRepository.Save`, recheck both in the transaction, and apply to playback only when the confirmed recording is the same.
- [x] Verify menu targeting independently of selected/current song, editable search, cancellation, stale member or binding refusal, manual lock, persistence, refresh and theme/localization. Run both unit suites and targeted isolated UI checks. Update guide, release notes and development log, then commit locally.

## Results · 2026-11-18

- Core 161/161 and Infrastructure 560/560. Release App and Probe published locally.
- Packaged right-click exercise passed 12/12 in each of four languages. Playback manual search, original acquisition, batch acquisition and translation job checks brought the total to 266/266.
- The source and playback recordings were fixtures. Verified original binding freshness and membership in the repository tests, and viewed snapshots of the menu and candidate dialog.
