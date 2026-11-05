# Issue 105 — bounded UI/UX review

Reviewed 2026-11-05 against `7a880def70e9cb089bdf74d1d3a8a362f2a32527`,
including completed issue 104. This delivery contains review docs and evidence;
it does not change application behavior.

The main layout has a usable hierarchy: everyday launch choices are separate from
project administration, agent configuration and global resource settings. The
actionable problems concern preserving control of active work, explaining a
disruptive action, consistent onboarding and keyboard access. A broad settings
redesign is not justified by the evidence collected here.

## Findings

| Issue | User problem | Evidence and proposed outcome |
| --- | --- | --- |
| [108](https://github.com/solwation/booster-ai/issues/108) | Removing a project silently removes active child rows and their monitoring, although individual active child removal is blocked. | [Project controls](projects-min-sv.png), [probe results](interaction-probe.json). Preserve active groups until their sessions are closed; explain the scope when removing inactive groups. |
| [109](https://github.com/solwation/booster-ai/issues/109) | Restart WSL immediately executes `wsl.exe --shutdown`; its effect on all distributions is absent from the visible page. | [Memory limit](limit-sv.png), `SettingsWindow.OnRestartWsl` → `OptimizationViewModel.RestartWsl` → `WslOptimization.ShutdownWsl`. Confirm explicitly with a visible warning and safe cancellation. No shutdown was executed. |
| [110](https://github.com/solwation/booster-ai/issues/110) | The start banner presents absence of a full-auto alias as missing setup while settings recommend No alias; dismissing is forgotten on next app instance. | [Start offer](main-alias-sv.png), [existing No alias page](../104/none-en-dark.png), [probe](interaction-probe.json). Treat No alias as a complete installation and respect a declined offer. |
| [111](https://github.com/solwation/booster-ai/issues/111) | Full status and Live Peek require hovering a nonfocusable Grid; truncated status cannot be expanded from the keyboard. | [Minimum main window](main-min-en-dark.png), [probe](interaction-probe.json). Provide a named keyboard-operable status/output control and Escape dismissal. |

All four have medium priority: they affect useful workflows or control of active
work, with reproducible behavior and bounded fixes. Each issue includes Swedish
reproduction, priority rationale, a proposed outcome and acceptance criteria.
The open issue list and recent closed titles were checked before filing. These do
not duplicate 106 (Restart all), 107 (Orchestra import), or the completed 104 editor:
110 concerns the older main-window onboarding offer outside that editor.

## Coverage

All 30 new PNGs in this directory were opened and inspected after rendering. They
use invented names and paths. Native title bars are not rendered. Sample running
states have no real process; disabled Close all or absent orchestra toggles do not
demonstrate runtime failures. The denial sample supplies its own CLI state,
independent of the neutral row's configured CLI; it demonstrates banner text/layout.

| Area | Scenarios inspected | Assessment / follow-up |
| --- | --- | --- |
| Main | [Swedish light](main-sv-light.png), [empty first start](main-empty-sv.png), [English dark 900×380](main-min-en-dark.png), expanded project/child rows, agent/model, launch/close controls | Useful empty-state actions and aligned fields. Content scrolls at minimum size. Active-group removal: 108; full status: 111. |
| Startup, update, error | [Missing CLI/npm](main-setup-sv.png), [alias offer](main-alias-sv.png), [English dark update minimum](main-update-min-en-dark.png), [failure](main-error-sv.png), [denied command](main-denied-en-dark.png) | Failure text is visibly distinct. Missing dependency copy explains where installation runs. Alias offer: 110. No real install/update/permission write was run. |
| Projects | [640×400](projects-min-sv.png); name/path, browse/open, color, add/import/remove read in XAML and handlers | Removal inconsistency confirmed with synthetic active state: 108. |
| General | [640×400](general-min-sv.png); after-launch behavior, tray, notifications, auto-clear, application/CLI updates and Linux terminal selector read in XAML, translations and bindings | Separate behavior/update groups and scrollable content. Auto-clear scope is in its tooltip; global compact copy explicitly distinguishes it from compression. No behavior issue proposed without testing a real idle session. |
| Appearance | [English dark 640×400](appearance-min-en-dark.png); theme/language/CLI zoom/palettes read in XAML and bindings | Language restart and platform-specific zoom timing are explicit. The screenshot initially scrolls to lower fields after focus; this is not classified as missing theme controls. Palette names remain readable. |
| Optimization | [Usage](usage-sv.png), [limit](limit-sv.png), [limit minimum](limit-min-sv.png), [environment](environment-sv.png), [global compact minimum](compact-global-min-sv.png) | Usage/editing are separate. Observed, recommended and configured values are labelled. Minimum content scrolls. Global shutdown: 109. Environment and compact navigation traced in handlers. |
| Agents/install | [Hub](agents-sv.png), [Codex English dark](agent-codex-en-dark.png), [Antigravity](agent-agy-sv.png), [Aider](agent-aider-sv.png), [Interpreter](agent-interpreter-sv.png), [missing OpenHands](agent-openhands-sv.png) | Consistent overview/permissions/context hierarchy across six agents. Installed, unknown version and missing remain distinct. Installation/update details are in an expander. |
| Permissions | Reopened 104's [managed](../104/managed.png), [function](../104/function.png), [English dark No alias](../104/none-en-dark.png), [minimum](../104/managed-min.png); new [YOLO confirmation](yolo-en-dark.png), [legacy alias confirmation](alias-sv.png) | Detected state and unsaved preview are distinct; external definitions protected. Previous 104 save/cancel/minimum-scroll checks are reused, not claimed as new tests. Main onboarding: 110. |
| Auto-compact | [Global Off minimum](compact-global-min-sv.png), [Manual model page](compact-manual-sv.png); global Off/Auto/Manual copy, lock state, per-agent supported/unsupported paths, save/reset validation read in code; 102–104 evidence reviewed | Global mode and per-model save have separate scope; CLI defaults and next-launch application explained. No duplicate of 97/99/102. Capacity correctness and efficiency not independently benchmarked. |
| Dialogs | [Import 520×320](import-min-sv.png), [busy restart](restart-sv.png), [orchestra install](install-sv.png), [close progress English dark](close-en-dark.png), [WSL install](wsl-sv.png), [alias](alias-sv.png), [YOLO](yolo-en-dark.png) | Controls visible at inspected sizes. Restart explains immediate/idle; close progress explains cancel/force; install explains destination/privileges. Global WSL shutdown lacks equivalent confirmation: 109. |
| Keyboard/minimum | Default/cancel declarations and navigation handlers read; status focus attempted in a headless visual tree; main 900×380, settings 640×400, import 520×320 | Status focus fails: 111. Other images/XAML establish layout and declared semantics, not exhaustive keyboard/screen-reader certification. Drag reorder remains pointer-driven; alternatives were not expanded into another issue in this bounded pass. |
| Language/theme | Swedish light across areas; English dark main/minimum, appearance/minimum, agent overview, close, YOLO, denied; reused 104 English dark permissions | No new language-dependent overlap or lost meaning in sampled states. Not an exhaustive Cartesian product or measured WCAG contrast audit. |

## Reproduction and checks

From the repository root:

```sh
dotnet build src/BoosterAI -c Release
python3 docs/verification/105/render.py
dotnet run --project docs/verification/105/probe/probe.csproj -c Release
```

- Release build: zero warnings/errors.
- Renderer: 30 synthetic scenarios completed with exit code 0, using the official
  headless screenshot path. Temporary scenario JSON files are deleted.
- Headless probe: exit code 0; [JSON](interaction-probe.json) records standalone
  active child removal refused, project removal leaves zero rows, both tooltip
  targets reject focus, and alias dismissal forgotten by a new instance. This
  records current defects rather than asserting desired fixed behavior.
- The probe uses a unique temporary neutral config and deletes it afterward.
  App source, launcher scripts, real configuration and aliases were not changed.
- 104 records a Release build, 722 WSL tests and alias interaction checks. These
  are previous evidence; the full suite was not rerun for this docs-only review.
- `git diff --check` passed. No renderer or probe process remained running.

No Windows Terminal, WSL shutdown, software installation or model was invoked.
Native Windows focus/tray behavior, assistive technology and high-DPI multi-monitor
layouts need platform verification during implementation. Headless screenshots
cannot establish those behaviors. This bounded pass creates actionable issues;
it does not certify every UI state as defect-free.
