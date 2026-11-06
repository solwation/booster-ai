# Issue 121 — installation only for a missing agent

The selected agent page keeps its name/icon and shows one Install button when no
executable is detected. Its version/status row, section navigation and complete
settings subtree are hidden, regardless of the remembered section. Existing
installed-agent controls remain mounted with their previous selection behavior.
The Install button uses the existing CLI-specific handler and official script.

- Release build: 0 warnings/errors. Full WSL suite: **780 passed**, 0 failed/skipped.
- [Headless UI probe](probe/Program.cs): [72 cases](interaction.json), all six CLIs,
  all three remembered sections, sv/light and en/dark, normal and640×400 windows.
  It checks the actual visible page contains only identity and Install, keyboard
  focus/visible bounds, missing→installed→missing refreshes and restored installed
  navigation/permission/context editors. Missing fixtures include aliases/functions
  without executables, which correctly remain missing.
- **24 real Install button events** reach recorded terminal requests with the exact
  expected CLI install script. The recorder throws a synthetic error after recording,
  preventing both an installer launch and its follow-up timer. No model, shell,
  real installer, user startup file or personal app configuration is touched.
- All eight current images were opened and inspected. The initial capture fixture
  needed extra render ticks for a complete frame; only corrected images are included.

| View | Swedish/light | English/dark |
| --- | --- | --- |
| Aider missing | [Normal](missing-sv-light.png) | [Normal](missing-en-dark.png) |
| Aider missing,640×400 | [Minimum](missing-sv-light-minimum.png) | [Minimum](missing-en-dark-minimum.png) |
| Aider installed | [Overview](installed-sv-light.png) | [Overview](installed-en-dark.png) |
| OpenHands missing, screenshot CLI | [Normal](openhands-sv-light.png) | [Normal](openhands-en-dark.png) |

Reproduce the UI checks with `dotnet run --project docs/verification/121/probe`.
The output directory can be passed as the first argument. The probe removes its own
config directory in finally. The OpenHands images use the app's `--screenshot`
command with `--window settings --tab agents --section openhands` and neutral config.

No launcher/path/terminal command logic changed, so no real Terminal gate applies.
Main-window screenshots remain representative. Windows tests and release/update
publication are integration gates; local installation is reported separately under
the automatic-update delivery policy from #120.

## Windows and integration

Integrated as `6b168ec`. The synced Windows mirror passed **780 tests**, 0 failed/
skipped. Its headless probe Release build has zero warnings/errors and the same
**72 UI cases / 24 recorded Install actions / zero real installers** pass on Windows.
[Windows results](windows/interaction.json) and all six Windows images were inspected,
including [missing Aider](windows/missing-sv-light.png),
[minimum dark](windows/missing-en-dark-minimum.png) and
[installed Aider](windows/installed-en-dark.png).

The conductor reviewed the combined diff against acceptance, documentation, existing
agent selection/refresh behavior and the inspected images; no concrete findings.
No second worker or routine rerun was used. Startup/planning took about4minutes,
implementation/WSL/UI verification about5minutes, Windows verification about2minutes;
these are coarse phase observations, not performance comparisons.

Publication is through the app's normal automatic update. No manual Windows
installation was requested or attempted, and no local update success is claimed.
Native desktop placement and real agent installation are outside the headless proof.
