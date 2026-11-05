# Issue 97 verification

Verified on Linux/WSL, 2026-11-05, on top of the integrated issue 96 implementation. No model request, package installation, Windows launch, or real CLI configuration write was performed.

- Release build: passed, zero warnings/errors, 3.69 seconds.
- Full WSL suite: **691 passed, 0 failed, 0 skipped**, 35 seconds. Three existing test-project analyzer warnings remain (CliUpdateTests CA1416/xUnit1031 and ProjectScannerTests xUnit1031).
- Focused correction check: 39 passed, including the four real-PTY exited-session restart/continue cases, 30 seconds. The subsequent full suite also includes the added metadata projection test.
- Neutral `--dry-run`: passed; the selected Claude window is passed as fixed arguments behind a bounded help guard. No whitespace-dependent command substitution or semicolon separator is introduced.
- `git diff --check`: passed. All test/screenshot processes exited; temporary fake CLI/cache directories were removed.

The new tests cover profile reserve calculations, Aider's separate file budget, supported input boundaries, unknown/insufficient capacities, config roundtrip and multi-instance merge, exact agent/model matching, active profile changes, manual overrides, CLI-default removal, invalid input, metadata shape/size/effective-percent validation, filtered cache output with login banners, Interpreter generation gating, and start/run-once/restart arguments. A fake Claude executable checks installed-help support, environment precedence notices, newline IFS, and preservation of a failing CLI's exit status without a second attempt. The existing PTY lifecycle suite verifies restart and cleanup alongside the new session snapshot behavior.

Inspected neutral renders:

- [Swedish Claude](compact-sv.png): native-context assumption, orchestra reserve and 600,000-token window, with Auto Approve remaining separate.
- [English Codex, dark](compact-en-dark.png): total-context threshold and explicitly supplied capacity; no real model cache is used in screenshots.
- [Swedish Aider](compact-aider.png): chat-history-only limit with the separate 64,000-token file budget.
- [English Interpreter](compact-interpreter.png): current-generation control and explicit manual local-model capacity/value.
- [Minimum width, full form](compact-narrow.png): 640-pixel width, wrapping text and usable controls.
- [Minimum 640 × 400](compact-minimum.png): vertical scroll preserves access to the form and the persistent footer; no horizontal overlap.

Reproduce with `--screenshot <path> --window settings --tab agents --section <cli> --config <neutral.json>`. `--section <cli>-compact` scrolls directly to the expanded compaction form; save a non-default sample preference in the neutral config to expand it. Full samples use height 900, the narrow form uses width 640/height 700, and the minimum uses width 640/height 400. The main window is unchanged, so its existing screenshots remain current.

Official source mapping and calculation assumptions are in [DETAILS](../../DETAILS.md#auto-compaction-budgets). There is deliberately no fabricated token threshold for Antigravity or OpenHands; legacy Python Interpreter has no equivalent supported control. Claude's environment override wins, default/gateway/local-model capacity is never inferred from a friendly name, and custom wrappers or runtime context overrides require the user's verified capacity. Model metadata is advisory and can be overridden. The heuristic is not an efficiency guarantee, and compaction can lose detail.

The conductor owns Windows build/tests, the real Windows Terminal argument check, installation, publishing and issue closure.
