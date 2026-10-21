# Integrated Music Library and Smooth Overlay Implementation Plan

> Execute inline in this session, task by task. Follow AGENTS.md; preserve personal data and use isolated fixtures for screenshots and automated checks.

**Goal:** Make floating glass movement responsive, keep main application styling independent from floating themes, and add a left navigation music library for selecting songs inside LumaLyrics.

**Architecture:** Remove desktop readbacks and redundant compositor geometry work from the movement path, keeping native substrate positioning synchronous and deferring ambient contrast until movement ends. Explicitly scope the main application's palette and verify isolation in both directions. Add a virtualized, searchable library view backed by read-only local metadata/covers and a separate playback handoff service that validates the requested recording against actual Apple Music playback; retain the existing cache management entry.

**Tech Stack:** C# 14, .NET 10 WPF, Win32/Windows Composition, existing Apple Music library reader, SQLite personal cache, GSMTC playback verification.

**Spec:** The current user's three numbered requirements. Each task delivers a separately testable behavior.

## Global Constraints

- Preserve main cover-derived palettes, independent theme memories, font hover, rounded blur, temporary dragging, save/cancel and instrumental blank states.
- Keep personal library metadata, artwork, settings, identifiers and live-test outputs outside source and distribution archives.
- Do not infer successful song playback merely from opening a link or accepting a command; compare the returned media session to the requested recording.
- Do not retrieve account credentials or decrypt protected audio. Apple Music remains responsible for licensed audio playback.
- Keep the main page clean and preserve existing library cache management, playback and lyric binding controls.

### Task 1: Smooth floating glass movement

**Files:** `src/LumaLyrics.App/OverlayWindow.xaml.cs`, `NativeBackdrop.cs`, `DesktopGlassHost.cs`, a focused movement verification partial.

- [x] Instrument native position, geometry, window region and ambient sampling work in isolated UI verification; record movement timing.
- [x] Suspend ambient readback during native move/resize, sample after settling, and reuse unchanged geometry/regions/opacity while updating substrate bounds immediately.
- [x] Verify final foreground/substrate alignment, no per-move readback or geometry rebuild, clean round corners, visibility and DPI behavior.

### Task 2: Enforce application/floating theme isolation

**Files:** `src/LumaLyrics.App/ThemeManager.cs`, `MainWindow.xaml.cs`, appearance/settings verification.

- [x] Check main palette resources, rendered controls, selected application theme and persisted settings while selecting/customizing/saving/cancelling all floating themes.
- [x] Give the main window an explicit application palette scope and keep floating palettes local; cover changes and application theme changes continue to update business views.
- [x] Verify isolation through both the direct appearance entry and Settings, with save, cancel and reset.

### Task 3: Browse and select music inside LumaLyrics

**Files:** `src/LumaLyrics.App/MainWindow.xaml`, a music library view and navigation partial, `PlaybackCoordinator.cs`, focused library query/playback services and tests.

- [x] Add a left navigation music library button and an integrated view with local covers, title, artist, album, duration, search and playback actions; virtualize rows and decode only visible thumbnails.
- [x] Read the complete local library without depending on lyric download completion; preserve manual bindings and existing cache management.
- [x] Validate Apple Music's supported local playback route against the installed client, isolate it behind a service, and report unavailable or unconfirmed playback honestly.
- [x] Exercise search, selection, keyboard/double-click playback, loading/empty/error states, cancellation, rapid song changes and exact-recording matching with fixtures and a bounded live handoff check.

### Task 4: Release and synchronize

- [x] Update instructions/version and original fixture screenshots; run relevant unit tests and source/packaged UI checks.
- [x] Validate software ZIP privacy boundaries, commit tested changes conventionally, synchronize to GitHub with a checked expected head, and verify remote SHA/tree.

Validation record: 138 unit tests (32 Core, 106 Infrastructure) pass. Source and the self-contained 0.8.0 package each pass 327 isolated WPF checks, including 54 reverse theme-isolation checks, 29 library browser checks and eight movement checks. Native Apple Music 1.1540.23042.0 accepts the local Songs filter and exact recording play command; both visible and minimized entry states have actual playback confirmation. Personal metadata remains in ignored local test files. Live audio was returned to the original recording and paused. Release build has no warnings. Distribution fixtures accept two software cases and reject 13 private-file cases.
