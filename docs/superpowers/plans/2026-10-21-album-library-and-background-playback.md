# Album Library and Background Playback Implementation Plan

> Execute inline in this session using the existing checkout. User authorization for implementation, debugging, packaging and GitHub synchronization persists. No delegation or separate task is needed.

**Goal:** Browse albums before tracks and determine whether native Apple Music selection can stay in the background.

**Architecture:** Add a read-only album grouping model using source and native album references, with conservative artist/title fallbacks. Render responsive, virtualized rows of album cards and an album detail track list within the existing music library. Keep playback behind the existing recording-checked bridge. Trial the native accessibility route without restoring the Apple Music window; retain the current route if that trial cannot reliably confirm background playback.

**Tech Stack:** C# 14, .NET 10 WPF, SQLite personal caches, existing musicdb reader, FlaUI UIA3 and GSMTC.

**Spec:** The user's current two numbered requests: Apple Music-like album browsing, then selecting a track; analyze background playback and defer it if infeasible.

## Constraints

- Preserve independent main/floating theme scopes, cover-derived colors, saved appearances and original personal files.
- Keep personal metadata, covers, settings and live test outputs outside Git and the software ZIP.
- Never infer successful playback from accepting a command; confirm actual metadata and playback state.
- Never hide a failed background attempt behind automatic window restoration. An unsupported result means defer that behavior and explain the observed limit.

### Task 1: Album model and ordering

Files: `AppleMusicLibraryReader.cs`, `MusicLibraryCatalog.cs`, new `MusicLibraryAlbums.cs`, infrastructure tests.

- [x] Carry native album artist and album references confirmed by the artwork index into catalog rows, without changing personal database schemas. Windows track/disc positions were not sufficiently confirmed, so do not read guessed offsets.
- [x] Group by source/title/available album artist plus artwork-index-confirmed native reference; when unavailable use album artist/title. A real-library smoke found that native references can span different albums: explicit title/album-artist boundaries now take precedence and a regression covers shared IDs. Separate unnamed albums, tolerate missing covers and keep compilations together when identity supports it. Use explicit title/artist/album sorting without claiming original track order.
- [x] Test same-name albums, compilations, missing fields, stable keys, search and ordering. Preserve all existing song records and bindings.

### Task 2: Album shelves and detail page

Files: `MusicLibraryView.cs`, new `MusicLibraryAlbumView.cs`, album UI verification partial.

- [x] Add an Albums/Songs view selector and make Albums the first entry. Render square cover cards, album name, artist and track count in responsive virtualized rows.
- [x] Enter an album by card or keyboard; show its cover, metadata and only its tracks. Provide a back action and preserve album search/scroll context; retain global song search and cache management.
- [x] Exercise navigation, track playback routing, search, covers, all three themes, narrow sizes and a large catalog using original fixtures. Visual review also caught and fixed the system ListBox white background in glass themes.

### Task 3: Background feasibility

File: `AppleMusicLibraryHandoff.cs`, isolated live-test logs and the music library guide.

- [x] Read the native route and provider limitations, then trial UIA selection while Apple Music remains minimized, with window-state observation and actual GSMTC confirmation.
- [x] Minimized filtering worked but the playback menu command was unavailable; visible playback succeeded and Apple Music took foreground itself. Reverted the experiment and documented that fully background selection is deferred.

### Task 4: Release

- [x] Update version, documentation, original screenshots and dependency locks. 146 unit tests and 355 WPF checks pass against source and the self-contained 0.9.0 package; Release has zero warnings. The packaged live album-to-track flow confirms actual playback and pause through GSMTC. Personal fixtures remain ignored.
- [x] Verify package privacy (two accepted software fixtures and 13 rejected personal-file cases), prepare the conventional commit and synchronize using a checked expected GitHub head. The delivery workflow verifies the published source tree and local tracking refs before reporting completion.
