# Japanese Lyrics Metadata Implementation Plan

**Goal:** Find and safely match recordings whose catalog labels differ between Japanese kana and romanization.

**Architecture:** Add a deterministic kana reading comparison in Core and use it only as corroborated recording evidence. Build a bounded optional kana search query for provider fallback. Keep original catalog metadata and lyric/translation identity unchanged.

**Tech Stack:** .NET 10, WPF, SQLite, existing LRCLIB and NetEase providers.

- [x] Add `JapaneseKana` reading and discovery conversion. Cover katakana, hiragana, NFKC halfwidth, common romanization variants, long vowels, sokuon and moraic n. Keep dictionary-free kanji handling conservative.
- [x] Wire comparisons through `MatchRules`, `AutomaticLyricsMatcher`, NetEase recording checks, candidate ranking and cache validation. Require corroborating album and close duration when the title or artist depends on a reading match. Keep exact matches preferred and version markers effective. Use a new lookup policy revision for old failures.
- [x] Add a bounded kana candidate query when the normal fields do not find the recording. Reuse the existing providers, limits, cancellation and follow-up fallback. Keep the original source fields in provenance and compare translations to the exact bound original.
- [x] Verify same recording, altered album/edition/artist, unrelated English, rejected timing, provider query fallback, cached binding and translation alignment. Run both unit suites and isolated acquisition, batch and localization UI checks. Update user notes, build a local preview and commit without personal data.

## Results · 2026-11-18

- Core 182/182 and Infrastructure 564/564. Mocked LRCLIB and NetEase fixtures verified kana fallback and recording rejection. Translation reuse stayed bound to the original hash.
- Release App and Probe published locally. The package passed 548 targeted UI checks. No live library coverage was measured.
