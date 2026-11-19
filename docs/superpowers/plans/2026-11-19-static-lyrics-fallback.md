# Static original lyrics fallback implementation plan

**Goal:** Use checked static lyrics when the normal search does not yield a usable timed original, and expose static-only songs in the library filter.

**Architecture:** Keep the timed matcher and translation gates intact. Check text separately after the timed search. Persist accepted fallback text with the existing plain flag and without a manual lock so a later successful timed result upgrades it.

**Tech Stack:** .NET 10, WPF, SQLite, existing lyric providers.

**Spec:** Owner's 2026-11-19 request for automatic static lyrics and the “仅有静态歌词” filter.

## Tasks

- [x] Add `AutomaticLyricsMatcher.SelectStatic` alongside `Select`. Share recording checks. Prefer a legitimate plain field, allow genuinely untimed provider text, and reject empty, placeholder, malformed timed and wrong-recording results. Increment the matching policy so older misses can retry on the ordinary schedule.
- [x] Persist via `LyricsRepository.SaveStaticFallback` from the playback lookup, selected-song preparation and background fetch. Use the existing transaction to protect reliable and manually locked originals, retain an existing usable fallback, and continue recording the result of the timed search. Read cached static lyrics with network disabled and use the existing retry paths to upgrade them. Verify readable TXT in new personal backups.
- [x] In the workspace distinguish automatic static lyrics from unconfirmed content. Add the static-only filter to the existing selector, include manual static bindings, and enable manual timing for both. Update UI copies and core repository guidance.
- [x] Exercise the matcher, transaction and workspace projection with fixtures. Verify the actual preparation button and filter, refresh after timed replacement, supplied-source degradation and existing translation jobs. Build the preview and verify the software archive.
- [x] Update usage notes, local development record and release notes. Commit locally.
