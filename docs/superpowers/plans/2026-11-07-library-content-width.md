# Library content width Implementation Plan

> Execute inline under the owner's request to fix the four workspace pages; preserve unrelated work and synchronize tested changes under AGENTS.md.

**Goal:** All four workspace pages fill their available content area immediately and retain stable width during scrolling and virtualized row recycling.

**Architecture:** Preserve native TabControl layout semantics. Stretch each TabItem's selected content, while centering only its header presenter. Keep finite list viewports, theme resources, motion, keyboard navigation and private data boundaries.

**Tech Stack:** WPF, existing application theme resources, isolated --verify-ui checks and bundled SDK.

**Spec:** The owner's report of narrow workspace pages and song-page widening after scrolling.

## Files and verification

- Modify src/LumaLyrics.App/ApplicationStyles.xaml: separate header alignment from selected-content alignment within LibraryWorkspaceTab only.
- Create src/LumaLyrics.App/LibraryLayoutVerification.cs and register --verify-library-layout in UiThemeVerification.cs: use 3,003 original fixture recordings of different title lengths, compare actual page bounds to the available content slot, and measure before/after scroll with virtualized rows. Cover all four pages, six themes, 640/820/980/1220 widths, resize/reentry and reduced motion. Keep fixture data and screenshots outside the repository.
- Update docs/release-notes.md, add docs/verification-library-layout/README.md and include the plan/record in scripts/publish-ui.ps1.

## Execution

- [x] Reproduce with actual WPF page bounds and scrolling before editing the style.
- [x] Stretch selected content horizontally/vertically and center the header presenter independently.
- [x] Run the layout, tab and workspace checks; visually inspect wide/narrow screenshots. Use scripts/common.ps1 for SDK selection and isolated personal QA directories.
- [x] Publish a separate library-layout preview, check distribution boundaries and archive hashes; commit/push without force and verify remote commit/tree.
