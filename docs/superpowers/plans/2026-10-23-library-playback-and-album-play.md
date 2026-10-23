# Library Playback and Album Play Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Restore confirmed song playback and add native whole-album playback from both album shelf and detail.

**Architecture:** Diagnose the existing UI Automation handoff against the installed Apple Music client using the application's own explicit integration-verification entry point. Keep one serialized, cancellable native handoff; use scoped metadata matching and native album commands, then confirm the real GSMTC snapshot. Extend the existing virtualized album presenter without nesting interactive controls.

**Tech Stack:** C# 14, .NET 10, WPF, FlaUI UIA3, GSMTC, xUnit, PowerShell.

**Spec:** `docs/superpowers/specs/2026-10-23-library-playback-and-album-play.md`

## Global Constraints

- Never invoke a command for an ambiguous recording or album.
- Native Apple Music owns the album queue and track order.
- Preserve unrelated settings, browsing context, existing lyrics and covers.
- UI fixtures are self-authored; live metadata and diagnostics stay outside Git/software archives.
- No new private lyrics or translations are prepared during verification.
- Do not claim fully background playback without live state evidence.
- Validate and synchronize with expected remote SHA; no force-push.

Execution is inline in the current task under the user's existing authorization.

### Task 1: Reproduce and repair native song handoff

**Files:** `AppleMusicLibraryHandoff.cs`, `AppleMusicPlaybackBridge.cs`, new `NativeLibraryPlaybackVerification.cs`, `MainWindow.xaml.cs`, `App.xaml.cs`, `MusicLibraryCatalog.cs`, `MusicLibraryTests.cs`.

**Interfaces:** Keep `Task<LibraryPlayResult> AppleMusicPlaybackBridge.PlayAsync(MusicLibrarySong song, CancellationToken token)`. Add bounded optional handoff diagnostics consumed only by explicit isolated `--verify-native-library-playback` mode; ordinary playback does not dump UI metadata.

- [x] Capture the actual native navigation/filter/row/command structure and reproduce a failed request through the existing product handoff. Save failure stage and actual source state in `.tools/qa-v0110-native/`.
- [x] Add regression tests for observed command labels and scoped recording matching; run focused `MusicLibraryTests` to establish red/green behavior.
- [x] Repair navigation, filtering, fresh row lookup, command selection and media confirmation using the observed native controls. Bound waits, serialize the whole handoff and preserve cancellation/filter/minimized cleanup.
- [x] Verify two different real library songs through the same bridge and confirm requested recording plus `Playing` state. Record limitations without changing personal lyric data.

### Task 2: Native whole-album playback

**Files:** native handoff/bridge above, `MusicLibraryAlbums.cs`, `MusicLibraryView.cs`, `MusicLibraryAlbumView.cs`, `MusicLibraryAlbumNavigation.cs`, `MusicLibraryNavigation.cs`, `MusicAlbumTests.cs`, `AlbumBrowserVerification.cs`.

**Interfaces:** Add `Task<LibraryPlayResult> PlayAlbumAsync(MusicAlbum album, CancellationToken token)` and an album callback to `MusicLibraryView`. Album identity uses the existing confirmed grouping and complete member recording metadata.

- [x] Add album matching tests: exact title/artist or confirmed member recordings succeed; edition, artist, source and ambiguous results fail. Native media confirmation accepts only a member of the requested album.
- [x] Use the observed native song menu command 「在资料库中显示专辑」 from the verified anchor recording, then the album header's own `PlayButton`, preserving native queue order. Validate the header group rather than the empty `PageHeaderTitle` used on native album pages. Do not synthesize a queue by alphabetically sorting local songs.
- [x] Add a separate accessible play button on every virtualized album card and in album detail. Keep card open and album play actions distinct, with scoped theme resources and keyboard focus.
- [x] Reuse the song request cancellation/version mechanism for albums; preserve status and browsing context through refresh. Verify cancel-old/play-latest, disabled/unavailable albums and native failure handling.
- [x] Verify one real multi-track album and its next track against native album membership/queue behavior; preserve the original playback paused state after testing where practical.

### Task 3: Release verification and synchronization

**Files:** `Directory.Build.props`, `README.md`, `docs/music-library.md`, `AGENTS.md`, `scripts/publish-ui.ps1`, `scripts/verify-distribution.ps1`, new `docs/screenshots-ui-v0110/`.

- [x] Run unit tests, zero-warning Release build, complete isolated WPF checks and distribution fixtures. Inspect album-card/detail screenshots using original fixtures only.
- [x] Publish 0.11.0, verify the actual package with its bundled host, record observed checks and live playback findings, and calculate the archive SHA-256.
- [x] Review diff/private boundaries, conventional-commit and publish with a fresh expected-head check; verify remote commit/tree, clean local checkout and the delivered ZIP's application binary.

## Self-review

Task 1 covers broken song playback; Task 2 covers album shelf and detail playback plus native queue semantics; Task 3 supplies tested distribution and GitHub synchronization. Native diagnosis will refine concrete selector names, which must be documented from observations rather than guessed. The old fully-background experiment remains deferred.
