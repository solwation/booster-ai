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
