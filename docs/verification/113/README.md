# Issue 113 — apply a selected permission preset after one confirmation

A supported preset can replace a custom alias or function from AI agents. **Apply
alias** opens one confirmation showing the actual definition, the selected result
and its scope; YOLO includes its warning in the same dialog. Cancel is the default.
There are no manual shell-edit instructions in the page.

Explicit replacement appends a canonical per-CLI override at the end of `.bashrc`.
It clears only that name's alias/function and sets the exact supported preset, or
leaves the bare executable for No alias. Original definition lines and other agents
are preserved. Subsequent changes replace only the same canonical block; malformed
or duplicate blocks are refused without writes. Existing legacy managed aliases
retain their ordinary editing behavior.

Detection reports the actual shell result and recognizes the canonical override.
At an explicit start/restart, the current tab library reads only exact reviewed
rows, without sourcing `.bashrc` or evaluating file text. A confirmed override can
clear the old function. Without one, the conservative original-ownership rule stays
in place. Running turns are unchanged; tabs opened with an older library may need
closing and reopening. Later startup files can override the choice; detection still
reports their actual result.

- Release build: 0 warnings/errors. Full WSL suite: 758 passed, 0 failed/skipped
  on the issue base before #111's two tests. Existing test analyzer warnings remain.
- 47 targeted permission tests pass. New cases cover all six CLIs, both Interpreter
  generations, custom aliases/functions, all supported presets including No alias,
  actual isolated Bash startup plus detection, repeated changes, other-agent and
  CRLF preservation, explicit refresh and rejection of injected unknown commands.
  All test shells are waited and killed as entire process trees in finally.
- Reproducible [headless probe](probe/Program.cs) uses the real Settings apply
  handler and dialog with temporary config/startup files. [sv](interaction-sv.json)
  and [en](interaction-en.json) cover Escape/Cancel/close/default Enter (no writes)
  and confirmed Auto Approve/YOLO/No alias (one popup, original preserved).
- Inspected sv/light and en/dark [preview](preview-sv-light.png),
  [confirmation](confirm-sv-light.png), [YOLO](confirm-yolo-sv-light.png) and
  [No alias](confirm-no-alias-sv-light.png); matching English images are included.
- [Dry-run](dry-run.txt) prints neutral tab, terminal and setup commands; the
  machine-specific home is normalized. No commands were executed by dry-run.

No personal startup/configuration files, model sessions or real CLI updates are
used in these tests. Combined Windows tests, isolated real Terminal marker and
normal background installation are integration gates recorded separately below.
