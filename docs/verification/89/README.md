# Main window size persistence (#89)

Normal client size in logical pixels and maximized state are saved atomically in
main-window.json beside the config. Resizes debounce for 250 ms, with synchronous
flush on minimize/close; forced installation shutdown therefore keeps settled
changes. Invalid or inaccessible state falls back safely. Restoration clamps to
the primary working area and existing minimum size. Only the real App enables
persistence, leaving screenshots/previews isolated. No layout or default size changed.

Release build: zero warnings/errors. WSL: 585/585 tests pass, including six new
state/persistence cases. An actual Avalonia App on a separate non-input Windows
desktop was started twice with private config/session/instance keys.
[First run](first.json): resize to1360x760 saved while open; maximize and minimize
preserved that normal size and maximized state. [Second run](second.json):
background launch stayed minimized, then actual single-instance IPC restored
maximized mode; unmaximizing restored1360x760. All checks passed. Probe processes
and secondary launches exited; the user's real app was never activated.

The final Windows full suite, installation restart and release are conductor gates.
Screenshot hook is inapplicable: no layout changed and the screenshot mode ignores
window persistence intentionally. No launcher/terminal arguments changed.

Final Windows suite:585/585 passed. [Real installation](installation.json) replaced
the old installed process and automatically started exactly one0.25.0+7ee2d03
process in the background; installed executable hash matches the verified build.
All six observed managed PTYs returned to saved80% after startup's zoom pulse.
The desktop foreground handle changed between the installation's before/after
samples, so that run does not establish unchanged foreground; the installed app
was subsequently verified hidden/minimized and not the foreground owner. Native
isolated-desktop checks independently verified minimized startup.
