# Periodic managed-tab title restoration (#86)

The existing status poll snapshots each configured name and batches one bounded
command every two seconds. It also covers recovered sessions and live idle shells.
The command verifies PID/starttime twice, resolves only the shell's output PTY,
and writes a literal OSC 0 title to output. It sends no input and changes no focus.
Exited or replaced processes, non-PTY output and unavailable distributions are skipped.
No persistent watcher is introduced; every command is waited, killed on timeout
with its process tree, and reaped. The test module disables real title writes.

Tests exercise a real PTY: an agent overwrites its title twice while its shell stays
alive; both subsequent refreshes restore the configured project/subtab name.
A mismatched start time writes nothing. Names containing shell syntax remain literal;
control characters cannot terminate the title sequence. Windows command arguments
keep the distro and script separate from title data. The tests kill/reap their PTYs.

Release build: no warnings/errors. Both new tests pass in WSL. The first full run
found an unrelated existing language race (569 passed, one failed); #88 isolates
LocTests before final combined platform verification.

Windows Terminal reapplies the profile's SuppressApplicationTitle setting on reload;
its initial --suppressApplicationTitle flag is insufficient after a settings change.
See upstream [Terminal::UpdateSettings](https://github.com/microsoft/terminal/blob/main/src/cascadia/TerminalCore/Terminal.cpp)
and [TerminalPaneContent::UpdateSettings](https://github.com/microsoft/terminal/blob/main/src/cascadia/TerminalApp/TerminalPaneContent.cpp).

Correction follows BoosterAI's lifetime; quitting the app stops the poll, and
restarting it recovers live managed shells and resumes correction. Manually renamed
terminal tabs can have their own terminal-side title override and are outside this
OSC mechanism. No UI layout changes, so the screenshot hook does not apply.
