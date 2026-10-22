# Album Artwork Repair Implementation Plan

> Execute inline using the existing checkout and standing authorization to debug, package and synchronize GitHub updates. No delegation is required.

**Goal:** Display the existing personal library covers on album cards and album details without bundling personal data with the application.

**Architecture:** Audit the read-only catalog-to-artwork mapping and the exact WPF thumbnail decoding path against native `.img` caches. Repair the confirmed failure, retain conservative album boundaries and lazy bounded image loading, then verify the path using original cache-format fixtures and a separate personal-data smoke check.

**Tech Stack:** .NET 10 WPF, existing SQLite artwork cache, musicdb metadata reader and isolated UI verification.

**Spec:** The user's report that Music Library albums are missing covers. Existing personal files must remain separate from software and GitHub.

## Constraints

- Read personal databases without changing original bindings, image files or selected data location.
- Match exact persistent IDs with conservative metadata fallbacks; never share a cover based solely on an album title.
- Exercise the same `.img` paths and encoded image formats used by the real cache, not only `.png` sample paths.
- Keep main/floating themes independent and preserve album search, navigation, virtualization and playback behavior.
- Commit only code, documentation and original fixtures; keep live metadata and screenshots in ignored directories.

### Task 1: Reproduce and repair

Files: `MusicLibraryCatalog.cs`, `MusicLibraryView.cs`, `MusicLibraryAlbumView.cs`, infrastructure and WPF verification files as required by the audit.

- [x] Read-only audit found that all 3212 catalog rows and 561 albums already had existing cover paths; sample `.img` decoding worked. The page retained its first catalog snapshot instead of observing subsequent imports.
- [x] The old code failed `albumArtwork.reopenSeesImportedCovers` in the focused WPF run. Revalidate on reentry, observe and coalesce import progress, preserve browsing/playback context, and discard stale thumbnail/URI-cache results.
- [x] Sixteen artwork checks pass, including PNG/JPEG `.img` paths, card/detail/song covers, automatic visible updates, hidden-page deferral, transient failure, selection/search/scroll/playback retention and same-path replacement. Existing missing/corrupt/cache-limit checks remain passing.

### Task 2: Release and synchronize

Files: `Directory.Build.props`, locks, `README.md`, `docs/music-library.md`, original screenshots and package scripts.

- [x] 147 unit tests and 371 source/packaged WPF checks pass with zero Release warnings. An ignored in-process harness used the packaged assemblies and an isolated personal-cache copy to render missing/restored album artwork and detail/song covers after an external-progress notification. Original personal files remained unchanged.
- [x] Document the fix and validation, create a software-only 0.9.1 package and verify the privacy boundary (two accepted software fixtures and 13 rejected personal-file cases).
- [x] Prepare the conventional commit and checked-head GitHub synchronization. The delivery workflow verifies the published source tree and clean local tracking refs before reporting completion.
