# Issue 111 — keyboard access to status and Live Peek

Each status is now a focusable button whose accessible name includes the project,
full status explanation and action. Tab reaches it; Enter/Space opens persistent
status/output details. Escape closes them and returns visible keyboard focus to
that status. Enter/Space are consumed both at the button and inside the details,
so they cannot reach the main window's default Start action.

Hover keeps the quick tooltip preview. Hover and keyboard share `StatusPeekView`,
including full `StatusTip`, the same terminal output and the same no-output notes.
The keyboard flyout fits a 900×380 window and scrolls with PageDown to expose all
20 captured lines. Standard focus adorners remain visible in light and dark mode.

Output polling still uses the existing single one-second timer. Keyboard opening
starts it; flyout closing stops it. Hover starts on tooltip opening and stops when
its actual `IsOpen` property becomes false. This avoids a premature routed
`ToolTipClosing` event stopping polling while the tooltip remains open. Hiding,
minimizing or closing the owner also stops the preview and polling. No terminal
control, launcher argument or script changes are included.

## Verification

- Release build: 0 warnings/errors.
- Full WSL Release suite: **742 passed**, 0 failed/skipped on this branch's base
  (before the concurrent 110 removal). Two new regressions cover accessible-name
  updates on rename, runtime transitions, full Watching explanation and close
  progress. Three analyzer warnings in older tests are unchanged.
- A reproducible headless probe drives the actual main window at 900×380 in
  Swedish/light and English/dark. It uses a neutral temporary config and synthetic
  capture files; no shell, terminal or model runs. Launcher calls are stubbed and
  counted: **0** in both languages.
- The probe verifies Tab navigation; complete accessible names; Enter and Space
  opening; Escape closure and restored focus; Enter/Space consumed inside the
  details; PageDown scrolling; timer start/stop; real capture refresh while open;
  no polling after closure; real pointer hover and pointer leave; returning to
  hover after keyboard use; stopping on window hide/close and resuming after show.
- Timer checks run a real headless dispatcher frame, rather than only draining
  immediate jobs. The latter does not advance the one-second dispatcher timer.
- Popup pictures use the actual headless overlay when it shares the owner's
  surface; a separate popup surface is composited only when necessary. No native
  window chrome is rendered.
- `git diff --check` passed. No probe or test process remained running.

All eight current PNGs were opened and inspected:

| State | Swedish/light | English/dark |
| --- | --- | --- |
| Keyboard focus | [Minimum](focus-sv-light-minimum.png) | [Minimum](focus-en-dark-minimum.png) |
| Full status and output | [Details](keyboard-sv-light-minimum.png) | [Details](keyboard-en-dark-minimum.png) |
| Scrolled to line 20 | [Bottom](keyboard-bottom-sv-light-minimum.png) | [Bottom](keyboard-bottom-en-dark-minimum.png) |
| Hover preview | [Hover](hover-sv-light-minimum.png) | [Hover](hover-en-dark-minimum.png) |

[Swedish probe results](interaction-sv-light.json) ·
[English probe results](interaction-en-dark.json).

## Reproduce

```sh
dotnet build src/BoosterAI -c Release
dotnet test tests/BoosterAI.Tests -c Release
dotnet build docs/verification/111/probe/probe.csproj -c Release
dotnet run --project docs/verification/111/probe/probe.csproj -c Release --no-build -- sv light
dotnet run --project docs/verification/111/probe/probe.csproj -c Release --no-build -- en dark
```

The probe removes its own temporary config/session data in `finally`. It inspects
existing private watch fields only to assert lifecycle, without adding test hooks
to production. Windows verification and installation belong to the conductor.
Native popup placement, screen readers and display scaling remain platform checks;
the headless pass establishes keyboard routing, content, bounds and watch behavior.
The shared README screenshots are left to the conductor's combined UI delivery.

## Integrated delivery

Integrated as `2554f42`, combined with #110/#112/#113. The combined Release build
has zero warnings/errors and the synced Windows mirror passes all **760** tests.
The keyboard/hover/timer probe was rerun on the combined source in Swedish/light:
all checks pass, with zero launcher calls. Shared [light](../../screenshot.png) and
[dark](../../screenshot-dark.png) main screenshots were regenerated and inspected.

Normal Windows installation delivered `0.30.0+5febd5a`: one new installed process
running in the background, old process exited, unchanged focus, restored 80% zoom
(font 9.6) and matching published/installed hashes. See the shared
[installation record](../113/installation.json). Native popup positioning, screen
reader output and DPI behavior were not exercised; no user desktop window was
opened by the headless checks.
