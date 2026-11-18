# Batch Lyrics Speed Implementation Plan

> **For agentic workers:** Execute the tasks in order. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Show reliable original lyrics earlier during batch preparation and make fewer repeat requests.

**Architecture:** Split the first LRCLIB request from the existing fallback chain. Use that split in the selected-song workflow and reuse the successful NetEase search and lyric results when preparing Chinese translations. Check membership by native key and publish coalesced progress as results are committed.

**Tech Stack:** .NET 10, WPF, SQLite, existing LRCLIB and NetEase clients.

**Spec:** The owner approved two-pass lookup, reuse of requests, and replacing per-song full-library reads. The second pass must complete the existing fallback chain. Keep source pacing, foreground preference, exact recording checks, and existing manual and revision protections.

## Global Constraints

- Automatic originals still require the existing recording and timing checks. Translations still require alignment with the exact bound original.
- Keep source limiters, retry windows, cancellation, and rollback. Do not mark first-pass misses as final failures.
- Work locally and keep personal data outside Git and software outputs.

### Task 1: Split the provider request and reuse results

**Files:** `src/LumaLyrics.Infrastructure/LrclibProvider.cs`, `ResilientLyricsProvider.cs`, `TranslationWorkflow.cs` in the same directory, `tests/LumaLyrics.Infrastructure.Tests/AutomaticLyricsSearchTests.cs`.

- [x] Extract the exact request into `LookupExactAsync`. Pass its candidates and availability into `CompleteLookupAsync` so a fast miss does not cause the exact HTTP request to run again. Keep existing fallback and ranking rules by delegating the ordinary lookup to these same methods.
- [x] Carry the successful initial NetEase candidate list on `LyricsLookupResult`. Feed it to `TranslationWorkflow.PrepareAsync` when it came from the same recording lookup. Reuse a successful empty list too. Use the same provider client for any fresh request and keep original and translation validation intact.
- [x] Verify via HTTP fixtures that exact hits and fallback yield identical final choices, a fast miss makes one exact request, and a NetEase fallback with valid aligned translation is reused without a second source lookup.

### Task 2: Put fast results first in Songs

**Files:** `src/LumaLyrics.App/PlaybackLibraryWorkspace.cs`, `TranslationJobListPanel.cs`, `src/LumaLyrics.Infrastructure/LyricsRepository.cs`, interface-language catalog additions, `src/LumaLyrics.App/LyricAcquisitionVerification.cs`.

- [x] After one membership load, check individual rows by source, native ID and exact metadata using `IsCurrentLibraryMember(LibraryQueueItem)`. Repeat the check inside the save transaction. Use it for preparation and membership scoped failure writes.
- [x] Run an exact pass for all eligible selected recordings. Save checked vocal results immediately. In the second pass revisit misses, finish fallback, process existing translations and export the remaining agent tasks. Check cancellation and the selected source before scheduling or writing; keep existing busy and provider-error behavior.
- [x] Publish stage and counts with a bounded refresh interval. Reload visible rows after a group of saves and at stage boundaries, retain search/selection and show final rejection reasons only after fallback. Add UI checks with a blocked fallback and a later fast hit, cancellation and existing binding protection.

### Task 3: Verify and record

- [x] Run both unit suites, Release build and isolated acquisition, localization and jobs UI checks using `scripts/common.ps1`. Compare first result, complete result, and request counts on fixed fixtures. Do not extrapolate a measured network speedup from fixtures.
- [x] Update `docs/lyric-acquisition.md`, `docs/release-notes.md`, and `docs/development-log.md` with behavior, results and limits. Commit the code and record in local Git.

## Results · 2026-11-18

- The gated fixture showed two fast rows while fallback was still waiting. It finished with three ready rows, one real miss, 11 HTTP requests, and a single NetEase search and lyric request for the reused original and translation.
- The synthetic SQLite check read 200 rows across 200 indexed lookups in a 3,000-row library. The old whole-queue check fetched 600,000 rows for the same 200 calls.
- Core 161/161, Infrastructure 559/559. The final package passed 435 targeted UI checks. A Release build also passed localization checks in all four languages. No live throughput benchmark was run.
