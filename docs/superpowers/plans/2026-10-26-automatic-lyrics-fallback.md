# Automatic lyrics fallback implementation plan

**Goal:** Reuse expanded search to improve automatic lyric discovery after ordinary matching fails, without weakening recording/quality checks or manual priority.

**Architecture:** Keep existing exact source lookups and early success. Add `IAutomaticLyricsSearch.SearchAutomaticAsync(Track,IReadOnlyList<LyricsCandidate>,CancellationToken)` as a bounded fallback for reachable sources only, sharing manual search query construction and HTTP limiters. Expanded LRCLIB results retain real metadata; NetEase filters catalog metadata with the existing recording gate before fetching at most three unseen originals. Selection remains with `AutomaticLyricsMatcher`.

**Tech stack:** C# / .NET 10.0.402, existing provider clients, xUnit, isolated WPF manual-binding regression harness.

**Spec:** User request in this chat on 2026-10-26 to apply the manual-search improvements to automatic lyric matching. Repository constraints in `AGENTS.md` continue to apply.

## Constraints

- Preserve manual bindings, exact translation hashes, original candidate/provenance metadata and automatic lyric quality thresholds.
- Do not auto-bind other artists, cover/live/remix recordings, missing-duration candidates, static-only lyrics or truncated/placeholder text.
- Expand only after the normal result fails selection and only with nonempty title/artist and finite positive duration.
- Do not retry a source that already reported unavailable/rate-limited during this lookup. Reuse existing per-source HTTP gates and 20-second fallback budgets.
- Keep personal data and live recording diagnostics outside software/Git. Use external isolated profiles for UI verification.

## Task 1: Failing regression and bounded discovery

Files: new `src/LumaLyrics.Infrastructure/AutomaticLyricsSearch.cs`, existing `ResilientLyricsProvider.cs`, `LrclibManualLyricsSearch.cs`, `NetEaseManualLyricsSearch.cs`, `ManualLyricsSearch.cs`; new `tests/LumaLyrics.Infrastructure.Tests/AutomaticLyricsSearchTests.cs`.

- [x] Add a failing provider regression: ordinary results are empty but expanded search contains an exact, complete timed candidate. Verify that ordinary lookup currently never reaches the expanded candidate.
- [x] Define `IAutomaticLyricsSearch`; reuse the broad-source search implementations with separate automatic mode, at most two supplementary queries, and unchanged manual behavior.
- [x] Filter NetEase catalog candidates through `RecordingMatches` before lyric requests; exclude previously fetched provider IDs and cap new lyric requests at three. Normalize additional verified artist credits only after the existing recording check, retaining original provenance.
- [x] In `ResilientLyricsProvider.LookupAsync`, keep early success, skip failed/unavailable sources and unknown recording identity, run eligible fallbacks concurrently, merge diagnostics/results, then leave binding to the original matcher.
- [x] Test positive discovery, exact fast paths, rejected artists/versions/durations/content, request bounds, source failures, cancellation, deduplication, provenance and repository cache validation.

## Task 2: Validation and local delivery

Files: `README.md`, `docs/release-notes.md`, this plan; local ignored build outputs.

- [x] Run locked restore/Release build and all unit tests. Run existing isolated WPF manual-lyrics regression to verify manual priority and stale-request behavior.
- [x] Describe expanded discovery and unchanged strict acceptance in the README/release notes; report actual results and limits.
- [x] Build an independent `-PreviewLabel automatic-search` self-contained package, inspect the distribution check and rerun the isolated regression with its bundled host.
- [x] Review the final diff, fetch and preserve any concurrent work, commit/push according to `AGENTS.md`, and verify remote SHA/tree. Do not publish or overwrite existing Release assets.

## Verification outcome

- The missing-discovery fixture failed before the change, then passed with the automatic fallback interface.
- 25 new automatic-search tests passed, including an end-to-end ordinary-LRCLIB-miss / supplementary-hit fixture, pre-fetch NetEase recording filtering, request bounds, strict rejection, additional-credit provenance, cache validation, manual protection, concurrency and cancellation.
- Locked Release build: 0 warnings, 0 errors. Full suite: 78 Core + 301 Infrastructure = 379 passed.
- Source and self-contained automatic-search preview each passed 42/42 isolated manual-lyrics WPF regression assertions. This checks the shared manual search/binding behavior; it is not live Apple Music synchronization verification.
- Preview distribution privacy check passed. No personal-library prefetch, real media controls, public Release publication or full UI regression was performed.
