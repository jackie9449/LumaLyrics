# Overlay Controls and Shape Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Complete the four requested overlay improvements and distribute a verified 0.10.1 release.

**Architecture:** Reuse the existing overlay toolbar, coordinator visibility operation and theme profiles. Store a nullable corner ratio beside the legacy pixel field, normalize legacy appearances once, and share dimension-based radius and low-opacity envelope calculations across the WPF and compositor paths. Preserve private provenance while simplifying visible translation presentation.

**Tech Stack:** C# 14, .NET 10, WPF, Windows Composition, xUnit, PowerShell.

**Spec:** `docs/superpowers/specs/2026-10-23-overlay-controls-and-shape.md`

## Global Constraints

- Main and floating translation switches remain independent and immediately persistent.
- Personal files, original hashes and AI provenance remain outside software archives and Git.
- 0% corner ratio is square; 100% uses half the shorter actual edge.
- Background opacity uses 0.1% steps; optics ease in continuously over 0–10%.
- Preserve theme drafts, cancel, reset, dragging, input transparency and instrumental suppression.
- Validate in isolated data directories; do not resume personal batch preparation.
- Recheck the expected remote SHA before publishing; never force-push.

Execution is inline in the current authorized task. The plan's suggested handoff does not add another user approval step.

### Task 1: Translation presentation and toolbar control

**Files:** `OverlayWindow.xaml(.cs)`, `MainWindow.xaml.cs`, `ChineseTranslationPanel.cs`, `PlaybackTranslations.cs`, `OverlaySurface.xaml(.cs)`, `PlayerViewModel.cs`, `TranslationVerification.cs`, `AGENTS.md`.

**Interfaces:** Reuse `PlaybackCoordinator.SetTranslationVisibility(bool main, bool floating)`; add `MainWindow.SetOverlayChineseTranslation(bool enabled)` and `OverlayWindow.RefreshTranslationToggle()`.

- [x] Remove the visible draft notice and main status suffix; format AI source details without the pending-review badge while retaining `ChineseTranslation.Draft` and `Review` in storage.
- [x] Add an accessible, theme-scoped `ToggleButton` in the existing above-overlay toolbar. Use a synchronization guard so reflecting settings cannot write stale values back.
- [x] Synchronize both Settings checkboxes during `coordinator.Updated` under the same guard, preserving the other switch on a toolbar change.
- [x] Extend actual WPF verification to click the toggle, assert immediate disk persistence, Settings reflection, main-theme/switch independence, and absence of pending-review markers. Keep original hash, blank interval and instrumental tests.

### Task 2: Proportional geometry with legacy migration

**Files:** `OverlayAppearance.cs`, `SettingsStore.cs`, `AppearanceWindow.cs`, `OverlaySurface.xaml.cs`, `GlassMaterialSurface.cs`, `OverlayWindow.xaml.cs`, `DesktopGlassHost.cs`, `OverlayThemeProfileVerification.cs`, `OverlayCornerVerification.cs`, new `OverlayGeometryTests.cs`.

**Interfaces:** Add `OverlayAppearance.CornerRatio`, `AppSettings.OverlayCornerRatio`, and `double OverlayAppearance.ResolveCornerRadius(double width, double height)`; normalized appearances always contain a bounded ratio.

- [x] Add failing regression cases: `ratio=.8,width=820,height=120` gives radius 48; height 360 gives 144; tall/narrow bounds cap at half the shorter edge; square/max shapes and non-finite values remain safe.
- [x] Verify legacy `Radius=24` migrates using its existing typography/padding baseline and survives JSON reload, theme switching, save/cancel without repeatedly changing the ratio.
- [x] Normalize legacy pixel data into the ratio, make presets canonical and update the editor slider to `0..1`, `.01` steps, `%` display.
- [x] Replace every actual render/clip/region radius input with `ResolveCornerRadius` using the same layout and DPI basis; preserve cached geometry while dragging.
- [x] Update WPF theme-memory fixtures to modify the ratio. Extend native region/render checks for wrapped text, translated height, 0%, 50%, 100% and resize round trips.

### Task 3: Continuous opacity for all background layers

**Files:** `OverlayAppearance.cs`, `AppearanceWindow.cs`, `GlassMaterialSurface.cs`, `OverlaySurface.xaml.cs`, `DesktopGlassHost.cs`, `OverlayGeometryTests.cs`, `OverlayCornerVerification.cs`.

**Interfaces:** Add `double OverlayAppearance.BackgroundEffectOpacity()` returning smoothstep of `clamp(Opacity / .1, 0, 1)`; transparent mode returns zero.

- [x] Add failing behavior checks for zero, sub-percent and 1% background effects, monotonic finite output, unchanged 10%+ appearance and transparent suppression.
- [x] Apply the envelope to reflections, gradients, grain, borders, preview blur opacity and the native host alpha; keep the base tint at the requested opacity and text unchanged.
- [x] Use `.001` opacity slider steps and one decimal percent display. Verify 0%, 0.1%, 1%, 5%, 10% on both glass themes through render pixel alpha and actual native host alpha, with a bounded synthetic checker scene.

### Task 4: Verification, documentation and synchronization

**Files:** `Directory.Build.props`, `README.md`, `docs/chinese-translations.md`, `docs/glass-materials.md`, new `docs/screenshots-ui-v0101/`, `scripts/publish-ui.ps1`.

- [x] Run focused red/green geometry tests, then `scripts/test.ps1`, Release build and full isolated `--verify-ui`; inspect original synthetic screenshots and fix failures.
- [x] Record observed counts and behavior in the release documentation. Run distribution privacy fixtures, publish 0.10.1 with `scripts/publish-ui.ps1`, then run the same WPF checks from the packaged Microsoft host.
- [x] Ensure archive hash and private-data boundaries, review the complete diff, use a conventional commit and synchronize with an expected-head check. Verify remote head/tree and local working-tree state.

## Self-review

All four user requests map to Tasks 1–3. Task 4 supplies the runnable result and authorized GitHub synchronization. Legacy pixel fields are compatibility input only; the same normalized ratio and opacity envelope must be consumed by all three visible background paths. No personal lyric preparation is included.

## Observed validation

198 unit tests, 453 source WPF checks, 453 packaged WPF checks, Release 0 warnings, and 22 rejected private/model fixture cases. Both full WPF reports and original screenshots are in docs/screenshots-ui-v0101/.
