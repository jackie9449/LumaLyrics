# Control rendering and page decoration implementation plan

> Execute inline in this session, preserving the user's local repository workflow.

**Goal:** Fix clipped button/tab feedback, audit application controls, and remove remaining promotional headings.

**Architecture:** Reuse the existing margin-aware horizontal tab template for appearance sections. Keep functional headings, instructions and the application titlebar; remove decorative slogans from production views. Exercise rendered bounds, focus and selection in isolated WPF verification, then inspect captures and run existing broad UI checks.

**Tech Stack:** C# / .NET 10 / WPF / PowerShell.

**Spec:** User request of 2026-11-15 and repository AGENTS.md.

## Constraints

- Preserve independent overlay appearance, personal data, playback, localization and input semantics.
- Validate all six application themes, wide/narrow layouts, four interface languages and reduced motion.
- Use scripts/common.ps1 and external isolated personal directories; keep reports/build outputs out of Git.
- Commit locally with development-log evidence; do not push or publish.

### 1. Reproduce and fix horizontal tab clipping

Files: ApplicationStyles.xaml, ControlRenderingVerification.cs, UiThemeVerification.cs under src/LumaLyrics.App.

- [x] Add `--verify-control-rendering` using real AppearanceEditor tabs; compare `VisualTreeHelper.GetDescendantBounds(tab)` against `VisualTreeHelper.GetClip(tab)?.Bounds ?? new Rect(tab.RenderSize)`, across themes and widths; check focus and keyboard traversal.
- [x] Run against existing templates and retain failing report/screenshots.
- [x] Replace section style with `BasedOn="{StaticResource LibraryWorkspaceTab}"` and section ItemsPanel with `<WrapPanel KeyboardNavigation.DirectionalNavigation="Cycle" KeyboardNavigation.TabNavigation="Once"/>`; retain selected-content stretch and full-width layout.
- [x] Run again; inspect right edges in selected/focused/hover/pressed captures. Audit shared buttons, selector toggles, swatches, menus and page/dialog controls using existing design/localization suites; repair any concrete failures.

- [x] Keep selected header font weight/foreground on the header presenter, not TabItem, and assert the selected page retains Normal weight. Reserve 3 DIP inside CheckBox layout for its existing external focus ring.

- [x] Keep ButtonBase hover scale at 1 and translation at 0 in AppMotion.cs; retain animated state layers and inward press. Audit visible descendant ink (skip Collapsed cached drawings), and exercise hover/press explicitly.

### 2. Remove decorative copy

Files: AppearanceEditor.cs, MusicLibraryView.cs, LyricsReviewWindow.cs, PersonalLibraryPanel.cs, ChineseTranslationPanel.cs, LibraryPanel.cs, MainWindow.xaml, MainWindow.xaml.cs, PlayerViewModel.cs.

- [x] Remove `A P P E A R A N C E`, its slogan, and the Music Library slogan/eyebrow; retain count and save/cancel guidance.
- [x] Remove the review slogan (retain its window title), personal-backup slogan and overview promotional subtitle; retain functional headings and instructions.
- [x] Replace empty-playback promotional title/text with existing localized `等待播放` and `打开 Apple Music，播放你喜欢的歌`; leave actual lyrics and preview specimens alone.
- [x] Verify rendered pages contain no removed slogans in each interface language, retain titlebar and instructions; inspect screenshots.

### 3. Regression and delivery

- [x] Build Release, run control-rendering, library-tabs, application-design, localization and integrated-appearance checks; use four UI languages for new geometry coverage.
- [x] Run relevant unit tests and full UI regression for shared templates; inspect representative wide/narrow captures.
- [x] Update release notes and development log with actual outcomes and limits; create local preview package with privacy validation and verify packaged UI.
- [x] Commit only source/docs/tests, verify HEAD/status and deliver preview path and commit SHA.
