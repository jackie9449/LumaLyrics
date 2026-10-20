# Library Assets Implementation Plan

> **For agentic workers:** Execute these checkboxes inline in the current authorized task. Review each deliverable before continuing; no separate task or agent is required.

**Goal:** Import available verified timed lyrics and high resolution covers for the complete local Apple Music library, with blank instrumental lyrics.

**Architecture:** Keep lyric matching and resumable lyric states in the existing core/repository. Read Apple's artwork index in a separate read-only reader and store validated images plus bindings in a dedicated local artwork cache. Run lyric and artwork imports independently, then connect cached covers and progress to playback.

**Tech Stack:** C# 14, .NET 10, WPF, Microsoft.Data.Sqlite, xUnit, HTTP.

**Spec:** `docs/library-assets.md`

## Global Constraints

- Only verified synced lyrics are automatically accepted; manual bindings survive.
- Missing lyrics never imply an instrumental track.
- Do not modify Apple's databases or access account credentials.
- Personal metadata, downloaded lyrics and covers stay outside Git.
- Preserve the three app themes in Settings and independent floating appearance.
- Test requested updates and synchronize conventional commits to GitHub without force.

### Task 1: Synced-only lyric selection and honest library states

**Files:** `src/LumaLyrics.Core/AutomaticLyricsMatcher.cs`, `src/LumaLyrics.Infrastructure/{LyricsRepository,LibraryPrefetcher,LrclibProvider}.cs`, `tests/LumaLyrics.Core.Tests/AutomaticMatchingTests.cs`, `tests/LumaLyrics.Infrastructure.Tests/{LibraryTests,StorageTests,ProviderTests}.cs`.

**Interfaces:** Consume `LyricsCandidate`, `LyricsBinding`, `Track`; preserve `Assess`, `Select`, `RunAsync`. Add library progress counts for synced, instrumental and preserved manual content.

- [ ] Add a failing assertion: `Assert.Null(AutomaticLyricsMatcher.Select(track, [Candidate() with { SyncedLyrics = null }]));` and reject contradictory instrumental candidates with nonempty vocal lyrics.
- [ ] Run `.tools/dotnet/dotnet.exe test tests/LumaLyrics.Core.Tests --no-restore` and confirm the changed requirement fails.
- [ ] Reject untimed automatic candidates with an explicit reason; retain actual source timestamps. Reject instrumental candidates containing vocal text. Revalidate old automatic caches on upgrade, distinguish manual static content, back up the old database before migrating.
- [ ] Add repository/prefetch tests proving static cache replacement, manual preservation, retry persistence and accurate progress categories. Run the core and infrastructure suites.

### Task 2: Exact library cover mapping and resumable high resolution cache

**Files:** Create `src/LumaLyrics.Infrastructure/{AppleMusicArtworkReader,ArtworkCache,LibraryArtworkImporter,ImageDimensions}.cs`, `tests/LumaLyrics.Infrastructure.Tests/ArtworkTests.cs`; modify `tools/LumaLyrics.Probe/Program.cs`.

**Interfaces:** `AppleMusicArtworkReader.Read(string libraryPath)` returns persistent-ID mappings. `ArtworkCache.Find(Track)` returns validated cached artwork. `LibraryArtworkImporter.RunAsync(string libraryPath, CancellationToken token, int? maximum = null)` emits progress independently from lyrics.

- [ ] Add tests using a temporary Apple artwork SQLite fixture: exact signed 64-bit ID conversion, template expansion, duplicate album downloads, rejected foreign hosts, interrupted import resume, malformed image rejection and actual resolution counting.
- [ ] Open Apple SQLite with `Mode=ReadOnly;Pooling=false`, require a valid persistent-ID match, derive only known local artwork paths. Request 1200-pixel Apple images; retain source URL and actual dimensions. Write cache files atomically, and keep progress in a separate local database.
- [ ] Support `--artwork`, `--asset-status`, and bounded/one-pass lyric import in Probe so real imports can be audited without printing lyrics or uploading metadata.
- [ ] Run the infrastructure tests and a bounded real library import. Check source database hashes and cached image dimensions.

### Task 3: Playback integration, instrumental blank state and delivery

**Files:** `src/LumaLyrics.App/{PlaybackCoordinator,MainWindow.xaml.cs,DialogWindows,UiThemeVerification}.cs`, `Directory.Build.props`, `README.md`, `scripts/publish-ui.ps1`, `docs/library-assets.md`.

**Interfaces:** Playback loads `ArtworkCache.Find(Track)` before the session thumbnail and refreshes after import; UI displays separate lyric and artwork progress. Instrumental documents suppress the empty-state call to action and both floating lyric strings.

- [ ] Add UI checks for zero instrumental lyric blocks, hidden empty-state, empty floating current/next strings and restored rendering after switching back. Keep all existing theme and floating regression checks.
- [ ] Run lyric and artwork jobs independently under cancellation and separate exclusive leases; preserve playback priority, retry delays and resume state. Prevent stale track artwork after track changes.
- [ ] Run `.tools/dotnet/dotnet.exe test -c Release --no-restore`, build the WPF project and verify the isolated UI using `--data-dir --verify-ui --exit-after-capture`.
- [ ] Run a complete local import pass; report aggregate synced/instrumental/missing/errors and actual high resolution counts. Do not claim unavailable lyrics were imported or Apple-verified.
- [ ] Publish the tested package, update usage notes/version, inspect `git diff`, commit conventionally, synchronize with a checked expected remote SHA, and verify the remote tree.
