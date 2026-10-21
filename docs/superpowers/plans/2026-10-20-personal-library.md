# Personal Library Implementation Plan

> Execute these steps inline in this task; no delegation or separate approval is required. The user authorized personal-file separation and standing GitHub synchronization.

**Goal:** Keep private lyrics, original cover images and recoverable library state separate from software distributions, and export the user's existing collection into personal files.

**Architecture:** Preserve the current live data root. Export consistent SQLite snapshots with the online backup API, readable UTF-8 lyric files, deduplicated JPEG/PNG covers and a versioned, hash-checked manifest into a new external folder. Restore only into a new data root, reconstruct the artwork cache from the exported covers and preserve manual bindings, offsets and import queues. A machine-local profile pointer optionally selects that root after restart; explicit `--data-dir` always wins. Software archives reject private artifacts.

**Tech Stack:** Existing .NET 10, WPF, Microsoft.Data.Sqlite; built-in JSON, SHA-256 and ZIP APIs. Version 0.6.0, no new dependencies.

**Spec:** User request: personal lyrics and covers must be saved separately from the application for independent distribution and updates. Current data and manual corrections must remain intact.

## Constraints

- Personal files and real-library reports stay outside Git and software packages.
- Missing lyrics stay missing; only checked automatic timed lyrics and existing manual text are exported as readable lyric files. Instrumentals have no lyric file.
- Original database snapshots retain source provenance and queues; they are private backup state, not a claim that every stored candidate is correct.
- New snapshots never replace an existing collection or restore target. Version/hash/path checks happen before restore publication. Cancellation removes only the newly created staging directory.
- No live worker is stopped and no user cache is moved or deleted.

## Task 1: Personal archive format and storage

Files: create `PersonalLibraryArchive.cs`, `PersonalLibraryManifest.cs`, `PersonalLibrarySnapshot.cs` under Infrastructure; create `PersonalLibraryTests.cs`.

Interfaces: `PersonalLibraryArchive.Export(string dataRoot,string destination,CancellationToken token=default,IProgress<string>? progress=null)`, `Validate(string directory)`, `Restore(string directory,string destination,...)`, `CreateZip(string directory,string zipPath,...)`. Return `PersonalLibraryManifest` with format/version, asset hashes, per-track state and summary counts.

- [x] Add original fixture tests covering manual LRC/TXT and offsets, instrumental blanks, missing lyrics, shared/low-resolution covers, WAL snapshots, corrupt assets, future versions, traversal, existing destinations and cancellation.
- [x] Implement staged export with `SqliteConnection.BackupDatabase`; convert cache bytes to normal `.jpg`/`.png` names without re-encoding, and keep one image per content hash.
- [x] Implement full manifest validation and atomic restore into a new root. Rebuild `cache/artwork/<key>.img` from hash-matched covers and preserve snapshots exactly. Never merge over existing manual data.

Example round-trip acceptance:

```csharp
var manifest=PersonalLibraryArchive.Export(source,collection);
PersonalLibraryArchive.Restore(collection,restored);
using var repository=new LyricsRepository(Path.Combine(restored,"lyrics.db"));
Assert.Equal(450,repository.Find(originalTrack)!.OffsetMs);
Assert.True(repository.Find(originalTrack)!.Manual);
```

## Task 2: Profile location and user controls

Files: create Infrastructure `DataDirectoryConfiguration.cs`, App `PersonalLibraryPanel.cs`; modify `SettingsStore.cs`, `SettingsWindow.cs`, `PlaybackCoordinator.cs`, Probe `Program.cs`, `Launch.vbs`.

- [x] Resolve default profile through an external machine-local pointer; explicit data roots bypass it, invalid configured roots report an error rather than silently creating an empty library.
- [x] Add a Settings tab for personal-folder/ZIP export, restoring to a new directory, and explicitly selecting the restored directory for the next start. Display the current root and progress, without adding main-page controls.
- [x] Add CLI `--export-personal`, `--pack-personal`, `--restore-personal`, `--validate-personal`; forward launcher arguments safely.
- [x] Verify Settings controls with isolated WPF tests; existing themes and independent floating appearance remain unchanged.

## Task 3: Software distribution boundary

Files: `scripts/publish-ui.ps1`, `.gitignore`, `AGENTS.md`, `README.md`, `docs/personal-library.md`, `Directory.Build.props`.

- [x] Version 0.6.0; document distinct software ZIP and personal `.lumalibrary.zip`, repeatable snapshot updates, offline restore, explicit directory selection and private-data scope.
- [x] Reject private databases, lyric files, collection manifests and machine settings from software output before compression. Copy only documentation and original fixture screenshots.
- [x] Run all unit tests, Release build, isolated source and self-contained WPF checks. Inspect the personal-library Settings tab.

## Task 4: Export the user's collection and synchronize source

- [x] Use the packaged CLI to create a dated private collection in the user's Documents directory, validate it, ZIP it separately and verify a full offline restore into an ignored isolated root. Record aggregate counts only.
- [x] Confirm old data and active workers were preserved and software ZIP has no private entries. Do not report incomplete lyric coverage as complete.
- [ ] Commit source conventionally; publish the exact tested tree against the checked GitHub head, without force, then verify local/remote agreement. The branch history records this publication step after the plan snapshot is committed.

Validation evidence: 113 unit tests, 137 source WPF checks, 137 packaged WPF checks, and 2 accepted software / 13 rejected private-file distribution fixtures passed. The packaged launcher passed with Chinese, spaces and a semicolon in its explicit data path. The real collection was exported outside the repository, checked and restored offline; original database/settings hashes and pause state were unchanged. The final publication step is recorded by the resulting branch history.
