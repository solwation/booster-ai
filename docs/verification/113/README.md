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

## Combined Windows delivery

The combined Release build has zero warnings/errors. The synced Windows mirror
passes **760** tests, including #111's two new regressions. The integrated real
Settings probe passes in sv/light and en/dark, including scroll-to-Apply checks
and inspected [sv](apply-ready-sv-light.png)/[en](apply-ready-en-dark.png) views.
The shared WSL-shutdown dialog was rechecked with its isolated stub: cancelled
actions call it zero times and confirmation once; no actual shutdown runs.

Normal background installation delivered `0.30.0+5febd5a` with one new process,
unchanged focus, restored 80% zoom/font9.6 and matching hashes; see
[installation.json](installation.json). The isolated real Terminal marker gate passed; see below.

## Real Windows Terminal marker

[Result](terminal-marker.json): a fresh named terminal window ran the current
TabSession library in WSL, replaced a custom Codex function with the exact Auto
Approve alias, then cleared it for No alias. The unrelated helper alias stayed
unchanged, and an outside `touch` command in the synthetic startup file never ran.
Only after all assertions did it write `booster-wt113-ok` and exit.

A private portable Windows Terminal1.25.2733.0 was extracted from the official
Microsoft release asset (SHA256
`bf3ef2012f6c44d8340a4c58125acc9498d19b580f9890dc043cdf831852e796`,
matching GitHub's digest; executable Authenticode signature Valid). A unique
unpackaged path isolates Terminal's instance routing from the installed process.
The conductor, not a worker, launched it suspended on a non-input desktop,
assigned its own kill-on-close job and resumed it. One owned named window was
observed and closed through WM_CLOSE. Foreground focus/input desktop stayed
unchanged; the root exited, zero owned processes remained and all handles and
the private desktop closed. No forced cleanup was needed in the successful run.

The initial observer treated a false empty-desktop enumeration as failure; its
cleanup was complete, but a marker appeared afterward. A stale-marker preflight
then refused to launch. The final run used a separate fresh fixture and runtime;
it observed one empty poll with native error0 before the window appeared. Only
the final fresh result is used as delivery evidence.

The [fixture generator](terminal-fixture/Program.cs) consumes the actual library
and permission writer. It can recreate the isolated Bash checks without a model:

```sh
dotnet run --project docs/verification/113/terminal-fixture -- '/tmp/booster alias fixture'
bash '/tmp/booster alias fixture/run.sh'
cat '/tmp/booster alias fixture/marker'
```

This standalone reproduction is not a real Terminal test. The Windows harness
was private and separately reviewed; the result records library/script hashes
and the official Terminal executable hash without publishing personal paths.
No real model, CLI update, user startup edit or WSL shutdown occurred.
