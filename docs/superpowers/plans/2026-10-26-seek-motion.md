# Playback seeking and range feedback implementation plan

**Goal:** Support click/drag/keyboard playback seeking and consistent animated range controls.

**Architecture:** A dedicated WPF seek slider owns editing and commit/cancel semantics. A guarded seek request compares session, generation and recording revision before issuing one command and confirms a fresh timeline. Shared application styles use interruptible AppMotion feedback; progress fill transitions own and release their clocks.

**Tech Stack:** .NET 10, WPF, GSMTC, existing FlaUI native adapter, xUnit.

**Spec:** User request 2026-10-26 and repository AGENTS.md.

## Constraints

- Keep overlay resources scoped and preserve all six application themes.
- Respect app reduced motion and system animation preferences; detach on unload.
- Never manufacture playback positions or repeat an attempted native command.
- Isolate UI verification and private native reports outside software directories.
- Commit and synchronize verified source to GitHub without overwriting remote work.

## Tasks

- [x] Add seek rules and tests for finite/clamped targets, stale sessions/recordings, capability and fresh confirmation. Use `dotnet test` through scripts/common.ps1.
- [x] Add SeekSlider editing events with track click/drag, keyboard, Escape/capture cancellation, and target time feedback. Connect MainWindow to guarded requests; preview must not retime actual lyrics.
- [x] Consolidate application slider templates around LumaSliderStyle. Animate parent hover/press/focus and thumb/track within bounds. Smooth determinate progress changes and size-aware indeterminate motion with reduced-motion/unload cleanup.
- [x] Verify synthetic seek input, stale/failed/cancelled requests, all themes, range endpoints, live animation samples and cleanup with an isolated `--verify-seek-sliders` mode. Inspect PNGs and run responsive/application design regression checks.
- [x] Inspect live Apple Music capabilities and supported progress control, then attempt one bounded seek only if the recording and supported target are verified. Record any live limitation accurately.
- [x] Update instructions/release notes, publish a separate seek-motion portable preview, verify packaged UI and distribution boundary, then commit/push and verify remote commit/tree.

Execution: inline in the current authorized session.
