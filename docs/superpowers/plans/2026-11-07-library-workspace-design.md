# Library workspace design and agent handoff Implementation Plan

> Execute inline in this session under AGENTS.md and the owner's request. Preserve current personal data and the library responsiveness fixes.

**Goal:** Copy an immediately usable local-agent translation prompt for the selected registered task and improve all four library/lyrics workspace views.

**Architecture:** Add a revalidated handoff prompt service over LibraryTaskCatalog. Use shared theme-bound workspace layout helpers for headings, cards, metrics and virtualized row templates; keep selection, async ownership and existing services.

**Tech Stack:** Existing .NET 10/WPF, SQLite and xUnit. No production dependency or personal schema change.

**Spec:** The two requirements in the current chat and repository AGENTS.md.

## Constraints

Copy only the chosen task's registered entry after fresh identity/path/chunk validation. Never guess a missing batch's membership or copy another batch on selection changes. Do not upload lyrics, write databases from an agent prompt, retime originals, or promote AI provenance. All six application themes, reduced motion, 640/820/980/1220 bounds, independent floating appearance, async unload cancellation and virtualized large lists remain required.

## Task 1: Safe prompt handoff

**Files:** Add Infrastructure/LocalAgentTranslationPrompt.cs and focused tests; extend LibraryTaskCatalog metadata and App/LibraryTasksPanel.cs.

- [x] Test two registered batches: Create(root,selectedId) contains exactly the selected entry, handles spaces/Chinese paths, refuses missing chunks and never creates a replacement task.
- [x] Build Chinese instructions that point to the verified MD, require full reading, native file I/O, exact original/group/hash preservation, per-song final JSON and continued processing, and distinguish submission from acceptance.
- [x] Add the primary task-page copy button with async revalidation, selection-change guards, busy/unavailable states and short success/failure feedback. Verify the actual WPF handler through a clipboard adapter without replacing the user's clipboard during QA.

## Task 2: Theme-consistent workspace layout

**Files:** Add App/LibraryWorkspaceUi.cs; revise LibraryPanel, TranslationJobListPanel, LibraryTasksPanel, ChineseTranslationPanel, LibraryWorkspacePanel and scoped Settings heading behavior.

- [x] Give overview separate coverage metrics, source synchronization and background preparation cards; retain source selection and progress subscription semantics.
- [x] Give songs a clear primary action, search hint, structured ellipsized virtual rows, compact selection footer and empty state; retain bulk selection and hidden selections.
- [x] Give tasks a batch list and virtualized per-song status list, selected-task summary and primary copy action, adapting between columns and stacked panes.
- [x] Give display separate main/floating switch cards, a bilingual preview and current source. Reuse theme surface/radius/type tokens and existing control feedback.
- [x] Compact the Settings heading only for the workspace and keep narrow layouts inside the available viewport.

## Task 3: Verify and deliver

- [x] Build and run unit tests, enhanced workspace/prompt UI verification, old translation display and library browsing checks in isolated directories.
- [x] Capture and inspect all four views in six themes and narrow widths; preserve 3,003-song performance assertions and record practical limits.
- [x] Update instructions/release notes, publish a separate library-workspace-design preview, validate private-file boundaries and ZIP hashes.
- [x] Inspect remote head, commit conventionally, push normally and verify remote commit and source tree. User data and QA screenshots stay outside Git.

## Delivery evidence

460 unit tests passed. Enhanced workspace verification passed 100 Boolean assertions, translation display 55 and music browsing 108 in the preview package. All four views were rendered in six themes, with task/songs viewport checks at narrow dimensions and retained 3,003-song selection checks. Clipboard handoff verification invokes the real button handler through a capture adapter, preserving the user's clipboard. The first compact task layout exposed a zero-height song viewport; the implementation was corrected and all original/new assertions retained. No user database, lyric, task or screenshot is committed.
