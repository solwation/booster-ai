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
