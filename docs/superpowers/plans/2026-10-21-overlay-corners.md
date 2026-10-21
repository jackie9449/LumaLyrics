# Floating Glass Corners Implementation Plan

> Execute inline in this session, preserving the user's running application and personal data.

**Goal:** Make the actual Liquid Glass and Frosted Glass floating lyric windows honor the selected corner radius, including native background blur.

**Architecture:** Verify the real OverlayWindow HWND and desktop composition after WPF processes layout and style changes. The existing HWND region and antialiased WPF mask are already rounded; Accent blur ignores both. Select the existing glass material fallback for rounded layered windows, while preserving the user's blur preference for radius zero. Preserve transparent mode and independent overlay styling.

**Tech Stack:** C# 14, .NET 10 WPF, Win32 window regions, existing isolated WPF verification.

**Spec:** The user's report of square floating-window corners with a nonzero radius; repository AGENTS.md product and validation requirements.

## Global Constraints

- Do not modify the running user's settings, lyric library or cover cache.
- Keep the floating appearance independent of the main application theme.
- Run actual-window checks using --verify-ui and an isolated --data-dir.
- Publish a separate patched software archive without personal data; synchronize tested source to GitHub with a checked expected SHA.

### Task 1: Reproduce, fix and verify floating-window corner coverage

**Files:**
- Create: `src/LumaLyrics.App/OverlayCornerVerification.cs` — real HWND region and alpha regression checks.
- Modify: `src/LumaLyrics.App/UiThemeVerification.cs` — invoke the new regression checks.
- Modify: `src/LumaLyrics.App/NativeBackdrop.cs` — suppress overflowing native blur for rounded layered windows and report the fallback accurately.
- Modify: `Directory.Build.props`, `README.md`, `docs/ui-themes.md` — describe the patch and verification.
- Modify: `scripts/publish-ui.ps1`, `scripts/verify-distribution.ps1` — include only the new original verification screenshots in the patched software archive.

**Interfaces:**
- Consumes `OverlayWindow.ApplyStyle()`, `OverlayAppearance.Apply(AppSettings)`, `NativeWindow.Handle(Window)` and `VisualVerification.Render(FrameworkElement)`.
- Produces `Task VerifyOverlayCornersAsync(string directory, Dictionary<string,object?> checks)` in the existing `MainWindow` partial class; results are included in `ui-check.json`.

- [x] Add real-window checks using `GetWindowRgn` / `PtInRegion` after dispatcher idle: four excluded corners and included edge centers for nonzero radius; included corners for radius zero. Test both glass presets, blur disabled/enabled, lock/unlock, hide/show, larger font and width changes.
- [x] Reproduce the failure with bounded native captures over an original checker scene: both themes have a maximum corner channel delta of 98 despite passing HWND/WPF rounding checks. Neither applying a DWM blur region nor changing legacy accent flags corrected layered-window composition.
- [x] Implement the existing material fallback for rounded transparent HWNDs. Both glass presets now have zero corner channel delta, including a switch from square native blur back to radius 24. Preserve blur preference, transparency, instrumental suppression and WPF antialiasing.
- [x] Run the complete unit and isolated WPF verification suites: 113 unit tests, 171 UI checks in both the source build and self-contained 0.6.1 package. All four native corner comparisons have zero channel delta. Inspect the two original-fixture screenshots. Distribution checks pass with 2 valid fixtures and 13 rejected private-file cases; the ZIP contains 552 entries and no personal files. The packaged host has no installed SDK and Launch.vbs forwards the isolated verification arguments successfully.
- [ ] Commit with `fix: preserve rounded floating glass window corners`; synchronize and verify the remote SHA/tree. Report the package, commit and any remaining desktop-capture limitation.

The computer-use inspection tool failed to initialize (`failed to write kernel assets`), including one reset/retry. Actual desktop composition was instead verified by the application's opt-in isolated test, using only its own opaque checker scene and original text. The user's running window and personal profile were not modified.
