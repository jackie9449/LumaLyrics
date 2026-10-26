# Playlist continuation and native playback modes

**Goal:** Start an exact occurrence inside an Apple Music playlist and let its native queue continue, with native shuffle and repeat choices.

**Architecture:** Capture a playlist/index/options request independently of search. Reuse native page verification and latest-selection cancellation, enumerate and validate the native ordered occurrences before invoking one captured row. Apply and read back transport shuffle/repeat states before Play; verify fresh media, mode and queue according to sequential or shuffled semantics.

**Constraints:** Preserve personal data and native filters; no library-song fallback or repeated Next/Play commands. Retain duplicate occurrences, stale guards, bounded waits, window ownership and private native reports. UI choices apply when playback starts; unspecified choices keep native settings. Publish tested changes with ordinary Git commits/pushes and verify remote tree.

## Implementation

- [x] Add immutable `MusicPlaylistSelection`, `MusicPlaybackOptions`, repeat/mode state and pure request/queue validation; add tests for filtered indexes, duplicates, suffixes, shuffle multisets, repeat, invalid/stale requests and partial evidence.
- [x] Add native mode state parsing/application with observed transitions and readback. Only change an explicitly requested setting. Guard each mutation with current cancellation/ownership and never retry an uncertain action.
- [x] Enumerate native playlist rows in provider order with virtualization, bounded reads and scroll restoration. Validate full ordered membership before acting on an exact occurrence; retain original native filters and foreground checks. Share the whole-playlist path and bridge confirmation.
- [x] Add row Play, double-click, Enter and selected-row continuation, plus independent native mode selectors in the themed playlist header. Capture original indexes before filtering and restore browse context.
- [x] Extend original synthetic WPF tests across six themes/1220/980/820 widths, cancellation, incomplete/stale contexts and controls; exercise native integration privately and retain prior live-playback limitations if still present.
Publication procedure: run full units and relevant UI regressions, publish a separate preview, update docs/validation, check privacy, commit and push, and verify remote SHA/tree.

Native playback remains unconfirmed until fresh actual media and queue evidence match; a successful UI or API call alone is insufficient.

## Validation status

Implementation and isolated verification are complete. Native list enumeration and current mode reads were exercised, but playback did not confirm the requested recording. Further native probes returned UIA Win32 error 1300. Native continuation, mode transitions and successful minimization remain unverified; no privilege/account changes or false success were used. Publication is performed after this plan snapshot is committed; the final task result records the verified remote commit.
