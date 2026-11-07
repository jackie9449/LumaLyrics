# Library tab clipping Implementation Plan

> Execute inline under AGENTS.md. Scope the horizontal tab fix to the library workspace.

**Goal:** Render complete selection, hover and keyboard focus outlines for 总览／歌曲／任务／显示.

**Architecture:** Give the workspace an explicit horizontal StackPanel and tab template with inset feedback layers, preserving native TabControl selection and content entrances. Keep the sidebar and floating appearance editor resources independent.

**Tech Stack:** Existing WPF theme resources, AppMotion and isolated UI verification.

**Spec:** The owner's supplied screenshot and request to repair the four tabs.

- [x] Reproduce the cut corners in isolated header-only renders and inspect selection/focus geometry.
- [x] Add LibraryWorkspaceTab/LibraryWorkspaceTabs styles, apply only to LibraryWorkspacePanel, and keep all brushes/radii theme-derived.
- [x] Check every tab selection/focus, all six themes, 640/820/980/1220 widths and reduced motion; render header-only evidence and retain workspace regressions.
- [x] Update release/verification notes, publish a separate library-tabs preview, validate distribution and hashes, then commit/push and verify remote tree.
