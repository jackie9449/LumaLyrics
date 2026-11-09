# Existing Chinese translation filter Implementation Plan

> Execute inline under the owner's request. Preserve existing selected-song semantics, translation provenance, revision guards and personal storage boundaries; synchronize tested source updates under AGENTS.md.

**Goal:** Users can filter songs with current Chinese translations and explicitly regenerate local-agent translation tasks for selected songs they want translated again.

**Architecture:** Add a current-translation filter to TranslationJobListPanel using Complete/Partial status, covering community, AI and manual records. Keep outdated originals in their existing separate filter. Rename the existing replacement menu command to 重新生成翻译任务… and retain its confirmation, ReplaceExpected workflow and delayed replacement/history behavior.

**Tech Stack:** Existing WPF ComboBox/context menu, playback workspace coordinator and translation revision protocol; isolated UI verification via bundled SDK.

- [x] Extend workspace verification with mixed complete/manual/partial/missing/outdated/blocked/instrumental fixtures; reproduce the absent filter/action.
- [x] Append 有中文译文 without changing old filter indexes, update the regeneration menu/confirmation copy, and keep search plus retained selection semantics.
- [x] Verify actual filtered selection, cancelled confirmation, exact generated task targets and expected revisions, unchanged active translations before results arrive, and existing workspace/theme/narrow-window regressions. Use a confirmation adapter in isolated verification only.
- [x] Update user/release/verification docs, create an existing-chinese preview, validate tests and distribution/ZIP hashes, then commit/push and verify the remote commit/tree.
