# Apple Music playlists implementation plan

> Execute inline in this session. The owner has authorized implementation, validation, commits and GitHub synchronization.

**Goal:** Browse on-device Apple Music playlists and play their native queue from Music Library.

**Architecture:** Extend the read-only musicdb parser with ordered playlist references and stable plist identities. Keep master membership independent. Add a themed playlist shelf/details inside the existing browser; issue a single verified native playlist Play command through the shared cancellation and window handoff.

**Tech Stack:** .NET 10, C#, WPF, FlaUI UIA3, xUnit.

**Spec:** User request in this chat and repository AGENTS.md.

## Constraints

- Never modify Apple's database or personal bindings. Preserve duplicate occurrences.
- Do not turn playlist-only tracks into global library membership.
- Reject missing/ambiguous/stale native contexts before issuing playback.
- Use latest-request cancellation, bounded native waits and fresh media confirmation; minimize only after confirmation.
- Store live verification reports outside the repository.

## Task 1: Read-only playlist model and parser

Files: AppleMusicLibraryReader.cs, AppleMusicPlaylists.cs, MusicLibraryCatalog.cs, LibraryMembershipTests.cs.

- [x] Add `LibraryPlaylist` and `MusicPlaylist` records with identity, name, ordered songs and completeness.
- [x] Read boma 200 names, 205 XML identities with DTD/network resolution disabled, and 206 references. Resolve references against the full track table, without changing master membership validation.
- [x] Test order, duplicates, hidden internal lists, missing references, invalid metadata and global membership isolation.

## Task 2: Browser navigation

Files: MusicLibraryPlaylistView.cs, MusicLibraryView.cs, MusicLibraryAlbumNavigation.cs, MusicLibraryQueueView.cs, MusicLibraryNavigation.cs.

- [x] Add a virtualized playlist shelf and ordered detail rows with search, back navigation and Play Playlist.
- [x] Reuse theme resources and playback request lifecycle. Preserve browse state across refresh and queue navigation.
- [x] Verify all themes at wide and narrow sizes, empty/search/detail states and callbacks with original synthetic fixtures.

## Task 3: Verified native playback

Files: AppleMusicPlaylistHandoff.cs, AppleMusicPlaybackBridge.cs, NativeLibraryPlaybackVerification.cs.

- [x] Select one exact native playlist sidebar entry, verify page header and native first recording before one Play invocation.
- [x] Confirm fresh Apple Music media and ordered queue, retaining cancellation and window ownership.
- [x] Exercise the on-device playlist with private reports and record actual verification limits.

## Task 4: Ship

- [x] Run relevant and full unit tests, specialized UI checks, build independent portable preview and update release notes.
Publication procedure: inspect staged files and distribution boundaries, fetch remote, commit conventionally and push without force.
After pushing, verify remote SHA and source tree and provide the preview location and commit link in the task result.

## Validation limitation

Native playlist reading, context verification and single Play invocation were exercised. Apple Music briefly exposed first-track playback then cleared its media/queue; directly clicking its own Play button reproduced the same empty state. The application correctly kept the result unconfirmed. Continuous native playback and successful minimization remain unverified on this machine. See docs/verification-playlists/README.md; private native reports remain outside Git.
