# Issue 104 — permission aliases made explicit

The permission page now explains shell aliases, shows the definition detected in
the login shell, and separates it from an unsaved preset preview. `Standard` is
labelled **No alias**; external aliases, functions and unrecognized managed blocks
have distinct labels. The preset dropdown shows CLI-specific effects and recommends
No alias as a starting point without promising to reset external CLI settings.

- Release build: no warnings/errors.
- WSL full suite: 722 passed, 0 failed/skipped.
- Regression checks cover detected versus selected alias, read-only external
  alias/function previews, rejected external writes, and shell function inspection
  without executing the function. The login-shell fixture was updated to expect
  the newly detected function metadata.
- Headless interaction probe: actual ComboBox changes update the preview while
  preserving the detected alias; Apply opens the YOLO confirmation; cancelling
  writes nothing. Custom definitions cannot save a preset. Navigation keeps the
  draft, and scrolling reaches Save at the 640×400 minimum size.
- Inspected Swedish light managed/no-alias/function states, YOLO preview, custom
  preview, English dark mode and minimum layout.

[Managed alias](managed.png) · [No alias](none.png) ·
[Custom function](function.png) · [YOLO draft](yolo-preview.png) ·
[Custom preset preview](custom-preview.png) · [English dark](none-en-dark.png) ·
[Minimum size](managed-min.png).

Custom definitions remain protected: the UI provides inspection/edit/refresh steps
rather than guessing their source file or overwriting arbitrary shell code. Function
metadata is limited to 8 KiB, with a truncation notice and command for full inspection.
Only synthetic definitions were used in tests and screenshots. No real aliases or
CLI settings were edited, and no model was invoked. Launchers, tab scripts and
terminal arguments are unchanged.

## Resumed delivery

The interrupted implementation `7a880de` was preserved and integrated as `ae90842`.
The conductor reviewed the complete diff and all seven images. The original Release
build, 722-test WSL suite and headless interaction evidence apply unchanged to the
integrated source tree; the concurrent Orchestra import changes instructions only.

The Windows mirror was synced to `ae90842`; all 722 Windows tests passed with no
failures or skips. The self-contained win-x64 publish succeeded with the existing
single-file `IL3000` warning. The normal installer replaced the installed copy and
left exactly one new background process running, version `0.29.0+ae90842`. Foreground
focus stayed unchanged, saved 80% zoom was restored (font size 9.6), the old process
exited, and the installed executable hash matches the published build. See
[installation evidence](installation.json). No real terminal tab was opened.
