# Issue 87: one GUI instance and menu activation

The GUI holds a per-user named mutex for its entire desktop lifetime. A second launch uses a current-user-only named pipe to request activation, then exits. Early requests wait for the first main window's `Opened` event, including the initial background hide. The window restores from the tray or minimized state; maximized windows keep their state. On Windows the menu-launched secondary process grants foreground permission to the existing process before requesting activation.

Helper modes (`--dry-run`, `--dry-run-close`, `--screenshot`, terminal placement watcher) return before entering the guard. `--background` acknowledges an existing owner without activating it. A dead or abandoned owner permits a fresh GUI; a live but unresponsive owner never permits a duplicate. Pipe clients have bounded timeouts, and simultaneous launches retry temporary disconnects within a shared deadline. The Windows installer accepts a clean secondary exit when a portable copy already owns the guard, while reporting nonzero startup exits as failures.

Worker checks on WSL:

- `dotnet build src/BoosterAI -c Release`: passed, zero warnings/errors.
- `dotnet test tests/BoosterAI.Tests -c Release`: 577 passed.
- Nine new OS mutex/pipe tests cover activation, early request, background request, clean shutdown, abandonment, silent client timeout, eight concurrent launches, an unresponsive live owner, and stable private user identity.
- Windows PowerShell parser accepted the updated installer script.
- `git diff --check`: passed.

No terminal, GUI window, or user work tab was opened for worker verification. The conductor owns Windows tests, isolated desktop activation verification and the final background installation. No visible layout changed, so screenshot comparison does not test this behavior.

Conductor verification on combined revision `9843bb4`: 579/579 tests pass on
Windows and WSL, Release build has zero warnings/errors. An actual Avalonia App
on a separate non-input Windows desktop used a private guard key (the live app
was never activated). [Activation evidence](activation.json) verifies minimized
and hidden restoration, background no-op, preserved maximization and secondary
process exit without a new owner. Initial probe setup created a dispatcher before
platform initialization; fixing that harness setup produced the passing run.

The final Windows install automatically restarted exactly one installed app at
0.24.1+9843bb4, without changing the foreground window. All six observed managed
PTYs returned to saved 80% after the startup pulse. A subsequent real installed
`--background` launch exited 0 and preserved the same owner and focus
([evidence](installed-secondary.json)). No native probe or secondary remains running.
