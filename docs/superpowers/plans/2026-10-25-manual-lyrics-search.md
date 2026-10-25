# Manual lyrics search implementation plan

**Goal:** Improve manual-search recall, including the user-supplied title/artist example, without weakening automatic recording matching or manual-binding priority.

**Architecture:** Add `IManualLyricsSearch.SearchManualAsync(Track,CancellationToken)` to the existing infrastructure providers. Reuse their HTTP clients, rate limiters, response bounds and provenance, but use bounded broad queries and retain original source metadata for manual choice. Aggregate the two sources concurrently and rank/deduplicate their candidates. The automatic `LookupAsync` methods remain strict.

**Tech stack:** C# / .NET 10.0.402, HttpClient, WPF, xUnit, existing isolated UI verifier.

**Spec:** User request in this chat on 2026-10-25; the sample and live metadata are retained only in a local ignored probe. Preserve the prior fix that manual choice applies directly to the original playback target.

## Constraints

- Preserve strict automatic matching, manual priority, exact translation hashes and personal data boundaries in `AGENTS.md`.
- Honor HTTP rate limits, cancellation, bounded query/result counts and the existing 2 MiB response limit.
- Never infer instrumental status from missing lyrics, fabricate timing, or rewrite candidate artist/album metadata to make it appear to match.
- Keep test fixtures original and private live responses outside Git/software packages. Do not alter the running personal profile.

## Task 1: Broader source searches and regression tests

Files: new `src/LumaLyrics.Infrastructure/ManualLyricsSearch.cs`, existing `LrclibProvider.cs`, `NetEaseTranslationProvider.cs`, `ResilientLyricsProvider.cs`; new `tests/LumaLyrics.Infrastructure.Tests/ManualLyricsSearchTests.cs`.

- [x] Add failing tests showing missing duration skips the secondary source and strict artist matching misses available originals.
- [x] Implement up to three distinct queries: original title/artist, cleaned keyword query, title-only fallback. Cleaning is supplementary; candidate identities and binding targets stay intact.
- [x] LRCLIB manual search aggregates usable synced/plain/instrumental candidates across bounded queries, retaining partial results on source errors.
- [x] NetEase manual search accepts missing artist/duration, ranks returned metadata without the automatic recording gate, and fetches at most eight originals. Preserve real source artist/version/duration, static lyrics and explicit instrumental evidence.
- [x] Query independent sources concurrently, preserve one source's results when another is unavailable, deduplicate by provider/id and rank against the query without filtering low scores. Source outages remain visible to the user.
- [x] Run the focused provider tests, including cancellation, concurrency, partial failures, query escaping, request bounds and unchanged automatic matching.

## Task 2: Search entry, integration and delivery

Files: new `src/LumaLyrics.App/LyricsSearchWindow.cs`, existing `DialogWindows.cs`, `PlaybackCoordinator.cs`, `ManualLyricsVerification.cs`, `docs/release-notes.md`.

- [x] Prefill a dedicated search form from the current song or last query; require only title/keywords, make singer/album/duration optional, and support Enter to search.
- [x] Route manual requests through `SearchManualAsync`, retain the original binding target, show candidate types and partial-source failures, and keep stale-request/closed-window protections.
- [x] Extend isolated WPF checks for the form, title-only searches, result ordering/status and the existing manual-binding contract.
- [x] Run locked restore/Release build, all unit tests and isolated manual-lyrics verification. Probe the supplied public song metadata through the actual sources; report real results and limitations without storing third-party lyrics in Git.
- [x] Build a local self-contained preview, verify it using a fresh external profile and run the distribution privacy check.
- [x] Record outcomes, review the final diff, fetch, commit and push according to `AGENTS.md`, then verify the remote SHA/tree.

## Verification outcome

- The two original regression fixtures failed on the former automatic-only entry, then passed through the dedicated manual API.
- Locked restore and Release build: 0 warnings, 0 errors. Full unit suite: 78 Core + 276 Infrastructure = 354 passed, including 17 new manual-search cases.
- Source and self-contained package each passed 42/42 isolated WPF checks, including the previous manual-binding checks, the real search form, retained input and partial-source feedback. Search-form screenshot inspected.
- A public metadata-only live probe returned 0 candidates on the old path and 7 on the new path in approximately 11 seconds; one source returned 503. A related cover was returned; this does not establish availability of the requested original recording. The probe and third-party content remain outside tracked files.
- Preview uses an independent labeled directory because the previous preview was running. Distribution privacy check passed. No installed application replacement, personal-library update, public Release publication or full UI regression was performed.
