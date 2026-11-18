# Floating Lyric Corner Contour Implementation Plan

**Goal:** Make the floating capsule contour follow the rendered surface, including fractional device pixels and changes in content height.

**Architecture:** Reproduce the contour on a real layered WPF window over an opaque synthetic background. Compare its pixels with the intended rounded rectangle and inspect the native region. Keep clip coordinates tied to one surface, with any native foreground region outside the antialiased edge and the compositor substrate within the painted shape.

**Tech Stack:** .NET 10, WPF, GDI window regions, Windows Composition.

- [x] Extend `OverlayCornerVerification.cs` with a warm-paper reproduction at 100%, using the reported size and appearance with synthetic content. Capture the window over a dark fixture, check mirrored contour and sample against a semicircle. Vary blank, current lyric, long wrapping text and Chinese translation.
- [x] Measure WPF and native bounds, reproduce the unbalanced contour, and fix the layer responsible. Keep the 0–100% ratio, fade behavior and click-through. Apply the same surface bounds to native glass if needed.
- [x] Run the real compositor checks for rounded blur, consecutive frames, retained color and locked hit testing. Run relevant motion/appearance checks and both unit suites. Verify the isolated preview build and its distribution boundary.
- [x] Record the cause, validation and limits in `docs/development-log.md` and release notes. Commit locally and preserve the current user's settings.

## Results · 2026-11-18

- Reproduced the pointed sides as a wrapped outgoing line faded inside a shorter window. The layout-relative mask fix passed all three overflow frames at 150% DPI.
- Final package passed 74 corner and composition checks plus 238 related UI checks. Core 161/161 and Infrastructure 560/560. Distribution checks passed.
