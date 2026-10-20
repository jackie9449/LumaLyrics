# LumaLyrics repository workflow

## Product requirements

- Application UI themes are selected in Settings. Keep theme selectors off the playback/lyrics main page.
- Liquid Glass and Frosted Glass derive readable surfaces, glows and control colors from the current cover. Preserve neutral grayscale and default fallbacks, reduced-motion behavior and interruption continuity. Material keeps its fixed palette.
- Floating desktop lyrics have independent appearance settings, previews, save/cancel behavior and scoped UI resources. Changing the application theme must not change floating lyrics, their toolbar or their appearance editor.
- Preserve existing lyric bindings, library data, playback behavior and compatibility fallbacks.
- Automatically import only checked timed lyrics. Preserve manual bindings, never fabricate timestamps, and do not label missing lyrics as instrumental.
- Confirmed instrumentals leave the main lyric area blank and suppress floating text and its empty surface. Restore the UI when vocal lyrics return.
- Map covers through Apple Music persistent IDs and confirmed album references. Count high resolution from actual image dimensions, preserve usable fallbacks, and keep personal metadata, images and downloaded lyrics out of Git.

## Authorized GitHub synchronization

The repository owner explicitly requested that every subsequent update to this repository be committed and synchronized to GitHub.

1. Inspect working-tree changes and the remote head before editing or publishing. Preserve unrelated user work.
2. Implement the requested change, run relevant unit tests and UI verification, and update release notes or instructions when behavior changes.
3. Use descriptive conventional commit messages. Exclude .tools, build outputs, local settings, media libraries, databases, credentials and private test data.
4. Synchronize tested updates to jackie9449/LumaLyrics. The user's standing instruction authorizes ordinary commits and pushes for requested repository changes; do not stop to request the same permission again.
5. Never force-push or overwrite newer remote changes. Use a checked expected SHA when updating a ref through GitHub tools. Integrate concurrent changes first.
6. Honor branch protection. If direct updates are disallowed, push a feature branch and create a pull request, attach it to the task, and report the link.
7. Verify the remote commit and source tree after synchronization. Report the actual commit or pull request URL and any remaining verification limits.

## Validation

Use scripts/common.ps1 to select the bundled SDK when present. Use isolated --data-dir directories for --verify-ui checks. The packaged Microsoft host is an available fallback for the previously documented apphost startup stall. Do not infer that successful UI checks prove live Apple Music synchronization.
