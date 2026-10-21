# Per-theme Floating Appearance Memory Implementation Plan

> Execute inline in this session. Follow AGENTS.md and keep personal settings outside source and software packages.

**Goal:** Restore each floating lyric theme's previous custom appearance when switching back, including after saving and restarting.

**Architecture:** Add persisted appearance snapshots keyed by preset name to AppSettings. A focused editing session clones those snapshots, seeds the active theme from legacy flat settings, and keeps all theme changes in drafts until Save. Switching restores a draft or saved snapshot; first visits retain the existing typography inheritance. Cancel restores only the original active appearance. Reset explicitly replaces only the current theme's material defaults while retaining existing typography behavior. Font hover remains a transient renderer override.

**Tech stack:** C# 14, .NET 10, existing SettingsStore JSON persistence, WPF appearance editor and isolated UI verification.

### Task 1: Implement independent theme drafts and persistence

- [x] Add backward-compatible profile storage and a theme editing session; preserve legacy active customization and unrelated playback/library settings.
- [x] Test full appearance restoration across themes, JSON save/reload, cancellation, save failure, legacy settings, reset isolation and normalized profiles. Capture direct overlay resizing from live settings.
- [x] Integrate theme selection, controls, reset, save and cancel without changing font hover semantics or floating interaction.

### Task 2: Verify actual editor behavior

- [x] Exercise real theme list selection and appearance controls, switching away/back before and after save/reopen, cancel across multiple themes, and resetting one theme.
- [x] Confirm hover fonts cannot enter any theme profile and retain the existing glass/dragging regressions.

### Task 3: Release and synchronize

- [x] Update version and instructions, run unit tests, source and packaged WPF checks, and distribution privacy checks.
- [x] Commit the tested source with a conventional message, synchronize to GitHub with a checked remote head, and verify the resulting commit/tree. Publication evidence is the resulting commit and verified remote SHA/tree.

Validation record: 126 unit tests (32 Core, 94 Infrastructure) and 236 actual WPF checks pass against both source and the self-contained 0.7.1 package, with zero build warnings. Theme checks cover legacy migration, all appearance fields, JSON persistence, live resizing, failed writes, cancellation and current-theme reset; UI checks use real theme items and controls. Existing glass, font hover, dragging, personal export/restore and lyric checks remain green on Windows 11 26100 at 150% DPI. The 563-entry ZIP contains no personal files; distribution fixtures accept 2 software cases and reject 13 private-file cases.
