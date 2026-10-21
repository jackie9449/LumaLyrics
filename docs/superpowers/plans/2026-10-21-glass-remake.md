# Glass Materials and Live Appearance Editing Implementation Plan

> Execute inline in this session. Follow AGENTS.md; keep all personal files outside source and software archives.

**Goal:** Rebuild the Liquid Glass and Frosted Glass floating lyric materials from Apple's design principles, allow dragging during appearance editing, and preview installed fonts on pointer hover.

**Architecture:** Separate the desktop blur substrate from the layered WPF lyric surface so rounded geometry can coexist with real blur; validate that substrate over controlled original backgrounds before integrating it. Draw distinct lens-edge/highlight and frosted diffusion layers in OverlaySurface, sharing the same renderer with the appearance preview. Make the editor modeless, using a temporary edit interaction state and transient font previews separate from selected appearance state.

**Tech Stack:** C# 14, .NET 10 WPF, Win32 composition and window regions, existing SettingsStore and isolated --verify-ui runner.

**Spec:** This turn's three user requirements. Design references: [Apple Materials](https://developer.apple.com/design/human-interface-guidelines/materials), [Meet Liquid Glass](https://developer.apple.com/videos/play/wwdc2025/219/). Prioritize readable text, bounded rounded materials, separation from main application styling, and reduced-motion/transparency fallbacks.

## Constraints

- Preserve user fonts, layout overrides, manual bindings, lyric/cover databases and independent overlay/application resources.
- Do not distribute Apple assets/fonts or claim to run Apple's native material engine on Windows.
- Material sampling/rendering is local and ephemeral; do not save or transmit desktop imagery. Verification uses only an opaque original fixture scene.
- The editor must not stop playback or leave a previously locked/hidden overlay unlocked/visible after closing.
- Hovering a font is temporary; leaving the font list, closing it or cancelling restores the selected/saved font. Saving cannot accidentally commit an unselected hover font.
- Font previews use installed fonts and readable Chinese/Latin sample text with fallback.
- Release a separate software package, validate privacy boundaries, commit and synchronize to GitHub using a checked remote head.

### Task 1: Establish rounded desktop blur and rebuild glass visuals

**Files:** `src/LumaLyrics.App/NativeBackdrop.cs`, a focused native substrate helper if required, `OverlaySurface.xaml`, `OverlaySurface.xaml.cs`, `src/LumaLyrics.Infrastructure/OverlayAppearance.cs`, `ThemeManager.cs`, relevant original fixture verification.

- [x] Read Apple's material principles and record the concrete optical/legibility targets in `docs/glass-materials.md`.
- [x] Prototype a separate no-activate native blur HWND against an original checker scene. Require unchanged pixels outside rounded corners, blurred interior detail, clean hide/close, and click-through support. Do not enable any path that reintroduces square blur.
- [x] Integrate the validated rounded substrate with overlay placement, resize/DPI, lock, visibility and instrumental suppression; retain a clear material fallback when the OS cannot provide it.
- [x] Rebuild Liquid Glass with restrained neutral tint, broad specular edge highlights, a darker counter-edge, depth and interaction lighting. Rebuild Frosted Glass with diffuse scattering, quieter rim lighting and softer contrast. Keep text sharp, preserve user opacity and avoid unrelated gradients.
- [x] Give the in-panel preview a genuine local backdrop rendered through the same rounded material layers. Keep main application cover-derived color harmony and independent floating resources.
- [x] Verify native corner/interior pixels and inspect both materials over original light, dark and colorful fixture scenes. Unit-test any new appearance policy.

### Task 2: Keep the overlay interactive during editing

**Files:** `src/LumaLyrics.App/MainWindow.xaml.cs`, `OverlayWindow.xaml.cs`, `SettingsWindow.cs`, a focused editing-session helper/verification if needed.

- [x] Replace `ShowDialog()` for the appearance editor with a single modeless window and closed-event cleanup.
- [x] Temporarily show/unlock the overlay for editing without overwriting the saved visible/locked preference. On close restore interaction state, preserve deliberate dragging/position changes, and retain appearance save/cancel behavior.
- [x] Verify entry through settings/main/tray, duplicate-open behavior, dragging availability, placement preservation and cleanup on shutdown.

### Task 3: Add installed-font specimen rows and hover previews

**Files:** a focused font-picker helper in `src/LumaLyrics.App/`, `AppearanceWindow.cs`, isolated WPF verification.

- [x] Render each installed-font option with its name plus a Chinese/Latin specimen in that font, with list virtualization.
- [x] Handle pointer enter/focus on rows by applying a transient font to both the actual overlay and the panel preview. End preview on pointer leave/dropdown close; clicking selects; keyboard navigation remains usable.
- [x] Keep transient hover state separate from selected settings. Verify hover A to B, leave, selection, dropdown close, save/cancel and font fallback; ensure no preview-only setting reaches disk.

### Task 4: Release and synchronize

**Files:** `Directory.Build.props`, `README.md`, theme/material guides, original screenshots/results, packaging scripts.

- [x] Run relevant unit tests and all isolated WPF regression checks against source and a new self-contained package; review screenshots and verify no personal files in the ZIP.
- [ ] Commit tested source with a descriptive conventional message, synchronize to jackie9449/LumaLyrics without force or overwriting newer work, and verify the remote commit/tree.

Validation record: 116 unit tests; 211 UI checks against both source and the self-contained 0.7.0 package, all passing on Windows 11 26100 at 150% DPI. Four native corner comparisons have zero color delta. The background preserves scene color (mean channel delta below 2) and reduces checker variance to about 4.8% for clear glass and 0.04% for frosted glass. Startup through Launch.vbs passed with isolated arguments; the bundled host reports no SDKs. The 559-entry software ZIP contains no personal files, and distribution fixtures accept 2 software cases and reject 13 private-file cases. Publication is recorded by the resulting commit and checked remote SHA/tree.
