# Library synchronization and responsiveness Implementation Plan

> Execute inline in this session under AGENTS.md. Preserve personal files and the tested native membership boundaries.

**Goal:** Restore newly added albums in the library and keep the shared lyric management page responsive for thousands of songs and hundreds of translation jobs.

**Architecture:** Add the observed type-218/IPfa member reference alongside legacy type-206/ipfa, retaining exact boundary/count/identity checks. Remove repeated UI list reconstruction and quadratic selection handling; coalesce background snapshots and keep task/history I/O off the dispatcher.

**Tech Stack:** Existing .NET 10, WPF, SQLite, xUnit, bundled SDK; no new production dependency.

**Spec:** The owner's request in this chat, repository AGENTS.md, and the existing library workspace specification.

## Constraints

Only confirmed complete master membership may replace library scope. Preserve original hashes, manual translations, personal files, selection across filters, six themes and independent floating appearance. Private native snapshots and reports remain outside Git. Run isolated UI checks, then commit and synchronize without force pushing.

## Task 1: Read current native membership

- [x] Extend the synthetic playlist fixture with compact references (boma type 218, IPfa size 32, recording ID at 16); add complete, partial, invalid-tag and dangling-reference tests in LibraryMembershipTests, plus repeated ordered playlist references in PlaylistTests.
- [x] Run targeted tests to reproduce rejection, then extend AppleMusicPlaylists.cs to recognize the exact compact structure. Keep legacy decoding, unique master, count and reference-subset validation.
- [x] Recheck the live library read-only and confirm Operation: Mindcrime membership; do not publish private native data.

## Task 2: Make management interactions responsive

- [x] In TranslationJobListPanel use selection event deltas, one bulk selection update, debounced search, cancellation/version checks and skip replacing unchanged item sets. Preserve hidden selections and keyboard behavior.
- [x] Coalesce in-flight workspace reads, reuse a profile-owned catalog with bounded per-binding assessment reuse, and invalidate only on data changes rather than display refreshes. Read currently selected source afresh on explicit refresh.
- [x] Move history/job reads and cancellation writes off the dispatcher. Cancel/coalesce visible task reads; avoid full job deserialization merely to find the latest MD.
- [x] Add isolated WPF checks with 3,000 songs for search, bulk selection, retained hidden selection and refresh; record elapsed interaction times and verify dispatcher responsiveness. Retain original navigation/translation assertions.

## Task 3: Validate and deliver

- [x] Build with scripts/common.ps1 and run meaningful unit tests, workspace, translations and music browsing UI checks in new --data-dir locations.
- [x] Update release notes and instructions, publish a separate library-sync preview and validate distribution boundaries.
- [x] Revalidate the user's live library with the new reader and verify album presence without network lyric preparation; offer the working preview as the concrete fix.
- [x] Inspect remote head, commit conventional changes, push normally, fetch and verify commit/source tree equality.

## Delivery evidence

455 unit tests passed. Packaged workspace 74/74, translations 55/55 and music browsing 108/108 passed. A first menu-focus run had two transient focus failures; the complete rerun passed with no assertion removed. Private live validation read 3,004 master members and found all 15 recordings in the target album; a separately backed-up membership sync updated the personal inventory, while original documents, bindings, offsets and translations retained identical fingerprints. The desktop-control native pipe was unavailable, so the existing user process was left running and the separate repaired preview requires a normal exit/restart by the user.

The existing latest-entry query was already a single manifest lookup and required no change. New task/history reads were moved off the dispatcher, cancelled and coalesced. The final GitHub synchronization is verified separately by the repository commit and tree hashes.
