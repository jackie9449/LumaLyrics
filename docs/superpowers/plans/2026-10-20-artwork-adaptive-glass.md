# Artwork-adaptive glass themes

Execute this plan inline in the existing checkout. The user has authorized implementation, debugging and GitHub synchronization; no separate agent or approval step is needed.

**Goal:** Liquid Glass and Frosted Glass follow the current album cover, with readable text and independent floating lyrics. Material retains its existing palette.

**Architecture:** Sample the successfully decoded cover at at most 64 × 64 pixels in WPF. A deterministic, platform-independent extractor finds a dominant color and a meaningful secondary color, ignoring transparent pixels and limiting the influence of white/black borders. Derive bounded dark/light surfaces, accents and ambient glows from those colors. Gray covers receive a neutral palette; missing or invalid covers restore each theme's default. The main window owns the current palette, so theme changes can reuse it without changing floating resources. Same-theme artwork changes interpolate existing visible brush colors over 650 ms, respecting reduced motion and Windows animation settings.

**Interfaces and files:**

- `Infrastructure/ArtworkPalette.cs`: color samples, extraction and `ApplyTo(AppTheme)`; no WPF dependency or network calls.
- `App/ArtworkPaletteSampler.cs`: bounded bitmap sampling; `ThemeManager.cs`: apply the supplied palette and animate background brushes only.
- `App/MainWindow.xaml.cs`: update palette with artwork, clear on missing/failed decode; keep settings-only theme switching.
- `Infrastructure.Tests/ArtworkPaletteTests.cs`: dominant/secondary colors, transparency, grayscale, missing data and contrast across hues.
- `App/UiThemeVerification.cs`: render original warm/cool/neutral fixtures; verify both themes, Material invariance, independent floating resources, rapid switching, animation completion and reduced motion.
- Version 0.5.1, current guide, original screenshots and packaging instructions.

## Implementation and acceptance

- [x] Implement extraction and bounded theme mapping. A predominantly orange cover produces warm deep glass and pale peach frost; a blue cover produces cool variants. Pure grayscale remains neutral.
- [x] Wire cover lifecycle, smooth transitions and settings copy. Null/broken/transparent cover data must not leave a previous song's palette behind.
- [x] Run meaningful unit tests, isolated source WPF verification and inspect warm/cool screenshots. Fix any contrast or resource isolation regressions. Passed 94 unit tests and 125 WPF checks; inspected warm/cool and settings captures.
- [x] Build the separate 0.5.1 self-contained package; run the same WPF checks through its app-local runtime. Passed all 125 checks with no installed SDK in the package runtime. Preserved the user's running import and private caches.
- [ ] Update documentation with actual results, commit conventionally, synchronize the exact tested tree against the checked GitHub head without force, and verify local/remote agreement.

The publication checkbox records the step after this source snapshot is committed; its completion evidence is the resulting branch history and verified remote tree.
