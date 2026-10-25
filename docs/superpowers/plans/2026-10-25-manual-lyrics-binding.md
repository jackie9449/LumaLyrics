# Manual lyrics binding implementation plan

**Goal:** Reproduce the false song-change warning after manual search, then apply the selected lyrics immediately to the original playback target with manual priority.

**Architecture:** Keep the playback binding target separate from editable provider search metadata in `CandidateWindow`. Retain playback session/generation/revision checks for genuine song changes. Exercise the real WPF search and selection buttons using an injected provider and original fixtures, without starting media/network services or accessing personal data.

**Tech stack:** C# / .NET SDK 10.0.402, WPF, SQLite, existing isolated `--verify-ui` harness.

**Spec:** User bug report and clarification in this chat, 2026-10-25: manual selection has the highest priority and must apply directly. Repository constraints are in `AGENTS.md`.

## Constraints

- Preserve personal lyrics, translations, library data, offsets and playback behavior.
- Store verification data outside the software directory; never test against the user's live profile.
- Do not upgrade dependencies, rewrite lock files, or relax unrelated verification assertions.
- Commit and synchronize the tested fix according to `AGENTS.md`.

## Task 1: Reproduce through the existing dialog

Files: `src/LumaLyrics.App/DialogWindows.cs`, `PlaybackCoordinator.cs`, new `ManualLyricsVerification.cs`, `UiThemeVerification.cs`.

- [x] Add internal injection points for `ILyricsProvider`, `Func<Window,Track?>` manual input and `Action<Window,string>` messages; production entry points retain their existing implementations.
- [x] Add `--verify-manual-lyrics` under the existing isolated `--verify-ui` entry. Use original fixture tracks, fixed candidates and actual WPF button events.
- [x] Query a playback song with a nonempty album using an empty-album query; also query with a different title/artist. Select the result and assert `Binding.Manual == true`, the selected text renders immediately, and SQLite binds the playback track rather than the query. Run before the fix and retain the failure report externally.

## Task 2: Correct target ownership and protect manual priority

Files: `DialogWindows.cs`, `PlaybackCoordinator.cs`, `ManualLyricsVerification.cs`, `docs/release-notes.md`.

- [x] Capture the playback target at window creation. Manual search updates only search metadata when a playback target exists; offline searches continue to choose their own target. Show the target separately from lookup status.
- [x] Disable selection during lookup, retain only results returned for that window's query, and ignore completions after close or supersession. Return nullable candidate results from `LookupManualAsync`; null denotes cancellation/failure. Keep foreground lookup ownership until completion.
- [x] Verify the original session, generation, revision and track before saving. Reject genuine song/session changes and session loss, while allowing playback position/state changes and duration arrival.
- [x] Use the original playback track in `SelectCandidate`; keep its existing cancellation and manual save semantics. Verify manual replacement of an existing manual binding, automatic lookup/cache protection, persistence, offline operation, stale requests and genuine song changes.
- [x] Run `scripts/build.ps1`, `scripts/test.ps1`, and isolated `--verify-ui <report> --verify-manual-lyrics --exit-after-capture`; inspect JSON assertions and screenshots. Record actual results and limits in the release notes.
- [x] Review `git diff --check` and final diff, fetch and integrate newer remote changes if necessary, commit as `fix: apply manually selected lyrics to the playback target`, push without force, and verify the remote SHA.

## Verification outcome

- Reproduced both reported paths before the fix: each displayed the original warning and failed to apply/save the selected lyrics.
- Release build: 0 warnings, 0 errors. Existing unit tests: 78 Core + 259 Infrastructure = 337 passed.
- Final source and self-contained preview package: 32/32 manual-lyrics WPF assertions passed in separate external profiles. Inspected the candidate-window screenshots.
- Preview package passed the existing distribution privacy check. No personal profile, live Apple Music control, full UI regression, public Release publication, or installed application replacement was performed.
