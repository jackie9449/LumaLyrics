# Offline Lyric Timing Implementation Plan

> **For agentic workers:** Execute task by task with the listed validation checkpoints.

**Goal:** Turn the corresponding static lyrics and audio into editable timed lyric suggestions on Windows.

**Architecture:** Compare pinned models on identical audio/text. A bounded Windows capture source and a cancellable local worker deliver per-line suggestions to the existing editor; only its reviewed save command writes the lyric binding.

**Tech Stack:** .NET 10, WPF, WASAPI process loopback, local pretrained alignment models, SQLite.

**Spec:** [离线歌词打轴](../specs/2026-11-19-offline-lyric-timing.md)

**File map:** `Core/LyricTimingAudio.cs` validates PCM and media clock; `Windows/AppleMusicLyricCapture.cs` owns the WASAPI session; `Infrastructure/OfflineLyricTiming.cs` and `Resources/lyric-timing-*` own pinned installation and worker I/O; `App/LyricTimingWindow.Auto.cs` adds the workflow; `Core/ManualLyricTiming.cs` validates and edits the line drafts. Project folder names are prefixed `src/LumaLyrics.`.

## Global Constraints

- Use the original recording and an exact captured selection/binding. No cross-track timeline or generated text.
- Keep PCM and worker requests in memory, one active job, with explicit size/time limits and cancellation. Store only the approved LRC in personal data.
- Preserve the manual timing shortcuts, checked timed originals, raw text and translation hash checks. Never write an unreviewed draft into the active lyric cache.
- Fetch only pinned HTTPS bits on request; keep native libraries and model data outside the application directory. No changes to GitHub.

---

### Task 1: Compare the model candidates

- [x] Use a confirmed blue-album sample from the Apple Music render PID. Keep the audio in memory. Compare Qwen3-ForcedAligner via audio.cpp 0.9.5 (Q8, CPU) and music-adapted W2V-BERT (`1be9145`, CPU). Pass the same text without reference timestamps. Derive the 34 s clip's included lines from the reference; compare separately from the whole-track run.
- [x] Score the number of present/missing rows, absolute start error, large timing errors and wall time. Use reference timestamps to score, never as model input. Document the measured startup bias and its correction, as well as the case where the tested Qwen port refused the whole track.

### Task 2: Capture and validate a timing draft

- [x] Implement `AppleMusicLyricCapture.CaptureAsync(Track, Func<PlaybackSnapshot?>, IProgress<Progress>?, CancellationToken)`. Resolve the Apple Music render process, release packets and COM objects on the capture thread and use an exact media revision. Use packet sample counts for durations and reject clock discontinuities. Read local PCM with `LyricTimingAudio.ReadWave(Stream, double, CancellationToken)`.
- [x] `IOfflineLyricTiming.AlignAsync(LyricTimingAudio, string[], CancellationToken)` returns optional `LyricTimingSpan` values in source line order. Preserve available onsets with missing ends; `ManualLyricTiming.LoadDraft` and `Edit` enforce order and bounds. Save only when complete. Keep explicit gaps and allow reversible whole-draft shifts.
- [x] Exercise wrong recording, time jumps, gaps, incomplete results, repeat handling and bounds in `OfflineLyricTimingTests` and the actual editor in `LyricTimingVerification`.

### Task 3: Run the selected model on demand

- [x] Pin the runtime/model URLs, lengths and SHA-256 in `lyric-timing-lock.json`. Download into a versioned tools directory with cancellable progress, resume and staged publication. Validate bytes before extracting. Send a little-endian length, UTF-8 `{Count, Lines}` JSON, then mono PCM via stdin; return bounded `{Spans}` JSON. Terminate on cancellation or timeout.
- [x] Run `lyric-timing-worker.py` on the real Windows-installed model. Infer on overlapping windows, align over the combined frames, and use both text evidence and vocal activity before keeping a boundary. Leave unsupported text and uncertain times incomplete. Check that a synthetic gap remains blank and compare the worker's filtered result on the actual recording.

### Task 4: Integrate, verify and commit

- [x] Add model preparation, playback capture and local audio controls in `LyricTimingWindow`. Expose current phase, cancel and the original manual entry. Check the binding both before generation and at save, and record the model revision in the confirmed binding.
- [x] Run `dotnet test LumaLyrics.slnx --no-restore` with `scripts/common.ps1`. Run the packaged app with `--verify-ui <report> --verify-lyric-timing --exit-after-capture` using separate `--data-dir` profiles for `zh-Hans`, `zh-Hant`, `en`, `ja`. Inspect the narrow editor rendering. Update instructions, log and release notes, verify the archive and commit locally.
