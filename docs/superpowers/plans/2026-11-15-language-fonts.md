# Language-specific floating lyric fonts implementation plan

> Execution: implement inline in this session, preserving the user's current checkout and unrelated migration archives.

**Goal:** Replace the single floating-lyric font selector with a system-language row and repeatable language-specific rows, backed by classified installed fonts and redistributable offline fonts.

**Architecture:** Store a value-comparable immutable language/font map inside each appearance snapshot. A shared language catalog defines language tags, representative alphabets and bundled fallbacks. WPF catalog filtering examines real glyph coverage and excludes East Asian/script-specific fonts from Latin lists. The actual overlay and preview use the same explicit script runs (composite maps alone were experimentally insufficient), with local Windows ELS language recognition for languages sharing a script.

**Tech Stack:** .NET 10 / C# 14, WPF, Windows Extended Linguistic Services, SIL OFL Noto fonts; no online lyric processing.

**Spec:** User request in this chat, 2026-11-15: system-language first row, unlimited additive common-language rows, language coverage, and no Chinese KaiTi in English choices.

## Constraints

- Preserve original text, hashes, translations, bindings, animation, lock, position and visibility semantics.
- Application language and Windows user language are independent. Read the Windows user language, not UiText.CurrentUICulture.
- Keep font drafts per floating theme; hover is transient; Save commits visited themes, Cancel discards drafts.
- No arbitrary duplicate rows; support all catalog languages, with add disabled only when exhausted.
- Uninstalled/unsuitable saved fonts remain recorded, with a visible fallback notice; do not add them to an incompatible language's list.
- Keep software font licenses/source checksums in Git; private verification data outside software. Commit locally only.

## Task 1 — model and migration

Files: new `src/LumaLyrics.Infrastructure/LyricFontLanguages.cs`, `LanguageFontPreferences.cs`; modify `OverlayAppearance.cs`, `SettingsStore.cs`, `OverlayThemeSession.cs`; new `tests/LumaLyrics.Infrastructure.Tests/LanguageFontTests.cs`.

- [x] Define stable BCP-47 tags, language labels/samples/coverage alphabets and fallback families for Chinese scripts, English/Japanese/French/Spanish/German/Korean and other common languages.
- [x] Store `FontLanguage` plus immutable `LanguageFonts` preferences in snapshots and flat settings. Normalize aliases, unknown tags, duplicate/default rows and invalid family names. Clone through immutable updates and compare by value.
- [x] Verify legacy font retention, settings JSON round trip, no shared mutable drafts, per-theme save/cancel/reset and unchanged unrelated settings.

## Task 2 — font assets and runtime

Files: new `src/LumaLyrics.App/Assets/Fonts/*`, `LanguageFontCatalog.cs`, `LyricFontRenderer.cs`, `src/LumaLyrics.Windows/TextLanguageDetector.cs`; modify `LumaLyrics.App.csproj`, `OverlaySurface.xaml.cs`.

- [x] Obtain static Noto fonts and OFL notices from upstream; record exact URLs and SHA-256 values, copy fonts/licenses to published output without installing system fonts.
- [x] Build cached font choices from real family/typeface character maps; require coverage and exclude CJK/script-specific designs from Latin lists. Include one verified bundled fallback for every language.
- [x] Use ELS locally with bounded input, bounded cache and freed native buffers. Script routing wins for kana/Hangul; Chinese script uses display text; uncertain same-script short lines use stable fallback and are documented.
- [x] Apply composite mappings to current/next/outgoing/original/translation text without altering Text, timelines or animation semantics. Include glyph fallback for missing characters and RTL shaping.

## Task 3 — settings interaction

Files: new `src/LumaLyrics.App/LanguageFontEditor.cs`; modify `AppearanceEditor.cs`, `InstalledFontPicker.cs`, `src/LumaLyrics.Core/Localization/fonts.*.json`.

- [x] Keep the existing first picker API for regression checks; label it with system/default language. Add a '+' language menu beneath rows. Prevent duplicates, allow removal of extra rows, retain keyboard/scroll accessibility.
- [x] Give each picker its language specimen and valid list, display missing-font notices, and preview the affected language without writing hover into settings.
- [x] Wire selection/add/remove/theme switch/save/cancel/unload to existing edit session. Localize new UI in zh-Hans, zh-Hant, en and ja.

## Task 4 — verification and delivery

Files: new `src/LumaLyrics.App/LanguageFontVerification.cs`; modify `UiThemeVerification.cs`, existing appearance checks where assumptions changed; `docs/language-fonts.md`, release notes and development log.

- [x] Run model tests and Release build via `scripts/common.ps1` and the bundled SDK.
- [x] Add opt-in `--verify-language-fonts` checks for all fallback glyph coverage, classification, actual render families, ELS cleanup/fallback, mixed scripts and translations, row addition/removal, hover isolation, persistence and narrow layouts.
- [x] Run appearance interaction, theme profile, embedded appearance and localization regressions with isolated `--data-dir`; inspect real screenshots.
- [x] Record actual results and limits, package a locally runnable preview if practical, commit all source/docs/assets with a conventional message, verify HEAD and status. Do not push or release.

## Evidence-driven corrections during implementation

- Native WPF glyph inspection found a composite map alone reused the SC face for adjacent Japanese/Korean glyphs. Explicit script runs now preserve distinct faces and exact display text; the physical glyph check requires all four expected font families in a mixed sample.
- At 150% DPI repeated font previews exposed accumulated truncation in native placement. Retain subpixel placement remainder across style updates, discard it after a deliberate move or boundary clamp, and verify repeated hover, Save and Cancel keep the physical lyric anchor.
- Font samples/menus use language-specific text; the preview forces the selected sample language while actual lyrics retain offline language recognition.
