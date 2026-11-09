# Auto-clear explanation (#145)

Tested source: `89cbdb0b978ac7e0cc6627cddee8d725d6d59c55`. The following commit changes only documentation, captures and the Pages cache key.

General settings now state before the switch that auto-clear empties Claude's active context without a summary. A second line limits its scope to new or explicitly restarted Claude sessions. The Auto-compact link describes the summary-based alternative and uses the existing Optimization navigation handler. The original checkbox and minute field remain; idle guards are expandable. No algorithm, launcher, saved configuration or confirmation changed.

Verification:

- Release app build: success, zero warnings/errors.
- Existing AutoClear and localization tests: 22 passed, zero failed/skipped. Two existing test-project analyzer warnings remain in unrelated CliUpdateTests.
- Actual headless link clicks reached Optimization → Auto-compact in Off, Auto and Manual with auto-clear both off and on (six cases). Mode, enabled state and 17-minute value were unchanged both in memory and after closing/saving Settings.
- Swedish and English, light and dark, at 920×700 and 640×400 were rendered and visually inspected. The minimum-size captures scroll the General page to auto-clear; the consequence, scope, link, switch and time field fit together without horizontal clipping. Representative captures: [Swedish/light](general-sv-light-640x400.png), [English/dark](general-en-dark-640x400.png).
- The refreshed [General preview](../../settings-general.png) shows the full Linux page at 920×900; the Pages preview cache key is `20261109-145`. The Projects screenshot is unchanged because this issue changes only General.

Captures use an invented Atlas project and an explicit `/home/me/Projects` search root. Headless windows never appear on the desktop. Every capture and route-check process exited; no task processes or locks remain. Final shared full-suite and native Windows checks belong to integration.
