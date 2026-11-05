# Issue 108 — protect project groups during removal

Settings → Projects now disables removal while the project's main tab or any child
has a running/starting session or an open terminal shell. A visible explanation
remains in the row; it does not depend on hovering the disabled button. Runtime
and membership changes update the binding immediately.

Inactive groups require confirmation showing the project name and child count.
The dialog explains that saved tab settings are removed and folders/files remain.
Cancel has initial focus and is both the default and Escape action. The model
rechecks the runtime state and group membership after confirmation, so a newly
active tab or newly added child cannot be removed under an older confirmation.
Neither the guard nor confirmation stops, restarts or sends input to a terminal.

## Checks

- Release build: 0 warnings/errors.
- Full WSL Release suite: **738 passed**, 0 failed/skipped, including 14 new
  project-removal regression cases. The suite emitted three existing analyzer
  warnings in `CliUpdateTests` and `ProjectScannerTests`; none in the new tests.
- Regression cases cover active main/child, starting sessions, exited CLI with
  open shell, missing CLI with open shell, closed/lost groups, cancel/unconfirmed
  removal, changed runtime/membership during confirmation, foreign/child input,
  persistence, unaffected sibling projects and binding notifications.
- Headless UI probe: actual Settings remove-button activation with Space opens
  the real dialog. Escape, window close, Cancel and default Enter each
  preserve the group. Explicit Space on confirmation removes only the selected
  group and persists the result. Active child and open idle shell disable the
  control; the explanation remains visible at 640×400. Cancel's initial focus and
  default/cancel flags are checked. Both Swedish light and English dark passed.
- Inspected [Swedish minimum settings](blocked-sv-light-minimum.png),
  [Swedish confirmation](confirm-sv-light.png),
  [English dark minimum settings](blocked-en-dark-minimum.png) and
  [English dark confirmation](confirm-en-dark.png).
- `git diff --check`: passed. No test/probe process remained running.

The first probe attempt checked owner-window membership after Escape; a closed
headless dialog can remain referenced there. Releasing a key on its disposed
native implementation also fails. The final probe checks visibility and sends
Escape/Enter key releases to the surviving owner. No app workaround was needed
for those harness details. Initial Cancel focus is explicit in the dialog.

## Reproduction

```sh
dotnet build src/BoosterAI -c Release
dotnet test tests/BoosterAI.Tests -c Release
dotnet build docs/verification/108/probe/probe.csproj -c Release
dotnet run --project docs/verification/108/probe/probe.csproj -c Release --no-build -- sv light
dotnet run --project docs/verification/108/probe/probe.csproj -c Release --no-build -- en dark
```

The probe uses a unique temporary neutral config, invented project names and
synthetic runtime/track states. It runs no shell or model and deletes its temporary
config. Results are preserved in [Swedish JSON](interaction-sv-light.json) and
[English JSON](interaction-en-dark.json). Native Windows tests and installation
belong to conductor integration; they were not performed by this worker.

For an isolated headless confirmation screenshot, use
`BoosterAI --screenshot confirmation.png --window remove-project --config neutral.json`.
The first configured project's name/count supplies the example; no removal runs.

## Integrated delivery

Integrated as `2a017ea` alongside WSL confirmation `fec0914`. The combined Release
build passed with zero warnings/errors, and the synced Windows mirror passed all
740 tests. Existing worker WSL evidence (738 tests, including 14 new group-removal
regressions) applies to this source; the other delivery adds two independent WSL
confirmation tests. Localization conflicts were resolved by retaining both sets of
keys; both screenshot window cases and Settings handlers are present.

The normal installer left one new background process `0.29.0+2a017ea`, old process
exited, with unchanged focus, restored 80% zoom (font 9.6) and a matching executable
hash. See [installation evidence](installation.json). No real session was stopped.
