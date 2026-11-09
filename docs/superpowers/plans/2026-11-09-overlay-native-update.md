# Overlay controls and Apple Music compatibility Implementation Plan

> Execute inline under the owner's five-part request. Keep private native diagnostics outside software/Git and preserve standing AGENTS.md playback, theme, data and synchronization requirements.

**Goal:** Provide horizontal-only centering, appearance-matched floating controls, adjacent edge-aware playback controls, fully visible floating geometry, and verified selection against the updated native Apple Music client.

**Architecture:** Keep floating chrome resources scoped to the overlay. Centralize screen-constrained geometry and non-overlapping popup placement using physical monitor coordinates and DPI conversion. Adapt native discovery only from observed controls, retaining exact FilterTextBox targeting, recording verification, latest-request cancellation, bounded waits, single-attempt commands and owned window restoration/minimization.

**Tech Stack:** Existing WPF overlay/appearance resources, Win32 position messages, FlaUI native handoff, bundled SDK, isolated WPF integration verification and private real-client diagnostics. Computer Use is for visual inspection; the existing native verification entry point tests the product's own adapter.

## Floating controls and bounds

- [x] Inspect current placement, theme resource lookup and screen handling; add regression coverage for the four reported floating issues.
- [x] Add a toolbar 居中 action. Center within the current monitor's working area using actual physical window bounds, preserve top exactly, remember position and retain lock/visibility/editing preferences.
- [x] Derive both toolbars' surfaces, foreground, accents, interaction feedback and shapes from the current floating appearance. Verify custom palettes and application-theme independence; keep the settings editor chrome tied to the application.
- [x] Replace fixed upward offset with measured toolbar placement. Prefer playback below the lyric surface; at the bottom place it directly above. Keep function/playback popups separated and inside work area, including narrow overlays and small monitors.
- [x] Constrain moving/sizing/initial/show/style/text/DPI/display-change geometry, including oversized saved widths and negative monitor coordinates. Preserve glass substrate synchronization, nonactivation, click-through and rounded clips.

## Native selection compatibility

- [x] Inspect installed version, actual native window and current adapter diagnostics. Preserve the original filter and private current-recording/window baseline.
- [x] Observe the collapsed native library group that prevents Songs discovery; adapt only that group and add focused UIA regression fixtures. No row/menu identity or command rules changed.
- [x] Verify single-song handoff against the actual client: fresh playing recording, original filter restoration, confirmed minimization and pause restoration. Retain existing cancellation, ambiguity and command guards and queue/Play Next paths.
- [ ] Additional live album, playlist and queue verification remains limited: passive queue inspection did not expose a usable baseline, so this task did not alter an unknown queue for extra tests. The shared library navigation is repaired; no claim of complete live coverage.

## Delivery

- [x] Run relevant unit, overlay geometry/transport/appearance and native handoff tests; inspect actual frames. Record real-client verification separately from isolated results and any remaining limits.
- [x] Update instructions/release notes and add verification records. Produce a separate overlay-native-update preview and validate privacy/distribution/ZIP hashes.

Delivery uses the standing authorized conventional commit and ordinary push, followed by remote commit/tree verification. The actual commit URL is reported in the delivery message.
