# Wide playback lyrics Implementation Plan

> Execute inline under the owner's screenshot and request. Preserve unrelated work and synchronize tested source changes under AGENTS.md.

**Goal:** Maximized playback lyrics use the available panel width instead of staying in a narrow centered column.

**Architecture:** Remove the inner 820-DIP content cap. Keep the existing bounded page, responsive insets, typography, square artwork and queue column. Original and translated rows share the expanded finite scroll viewport.

**Tech Stack:** Existing WPF XAML, playback layout and isolated responsive verification; bundled SDK via scripts/common.ps1.

**Spec:** The owner's 2560×1440 screenshot shows excessive blank margins inside the lyrics panel with the queue hidden.

- [x] Extend ResponsiveLayoutVerification.cs to check panel-relative content bounds, wide bilingual wrapping, queue on/off, maximization and return to normal size across all six themes; reproduce the old-width failure.
- [x] Remove MaxWidth=820 from MainWindow.xaml's LyricContent only; retain the existing outer page bounds and adaptive insets.
- [x] Run relevant unit and WPF responsive/translation/sidebar checks using isolated data. Inspect wide and narrow screenshots; update release/verification notes.
- [x] Generate a separate wide-lyrics preview with scripts/publish-ui.ps1, validate distribution and ZIP hashes, commit/push and verify the remote head/tree.
