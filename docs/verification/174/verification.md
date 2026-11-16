# Advisor is explicitly off by default (#174)

Verified source tree based on `ef2d75d`; the completion issue records its exact delivered commit. One agent implemented and reviewed the combined change in sequence.

## Behavior

- New projects use the stable config value `off`. Missing, blank and legacy `standard` choices normalize to Off. Explicit model choices remain intact.
- The editor shows localized **Av** / **Off**, independently of the stored ID. Project and Claude-subtab inheritance, saving, warnings and explicit Opus/Fable choices are covered by regression tests.
- When the cost policy does not allow an explicit advisor, the launcher scopes `CLAUDE_CODE_DISABLE_ADVISOR_TOOL=1` to the CLI/runner. This blocks inherited Claude advisor settings on start, continue/restart, Run once and save/close, without modifying global configuration or bypassing shell aliases. Hook settings and stream formatting remain unchanged.
- A running legacy `standard` launch is distinct from the new explicit Off state and shows the existing restart affordance. Existing processes must be restarted before the opt-out can apply.
- Off makes `/advisor` unavailable in the launched process; choose a valid model in BoosterAI and restart to enable it. The tooltip and DETAILS explain this limitation.

## Checks

Linux/WSL, SDK 10.0.115:

- `dotnet build src/BoosterAI -c Release`: passed, 0 warnings/errors.
- `dotnet test tests/BoosterAI.Tests`: final revision 1193 passed, 0 failed/skipped. The existing analyzer warnings in CliUpdateTests and LocTests remain. The first run identified three command-string assertions that needed the new opt-out prefix; those were updated. A later source change to the running-legacy restart indicator warranted the final full rerun.
- New tests cover localized/persisted IDs, missing/old values, explicit choices, inheritance, restart marking, command variants and actual bash alias/environment behavior. Child processes are waited for or killed as a process tree in `finally`.
- `--dry-run --config docs/verification/174/default-sv.json`: opt-out present in Claude commands, including tracked/fallback paths; other agents unchanged.
- Real Claude Code 2.1.325, isolated temporary config, synthetic API key, localhost-only endpoint and no model requests: with `advisorModel=opus` and the opt-out it reports `/advisor isn't available in this environment.` The control without the opt-out reports `Advisor: Opus 5.5`. See [off result](claude-cli.json) and [control](claude-cli-control.json). No personal Claude settings were read or changed.
- `git diff --check`: passed.

The opt-out is documented in [Claude Code's advisor guide](https://code.claude.com/docs/en/advisor#turn-the-advisor-off), reviewed 2026-11-16.

## UI evidence

Headless render, no desktop window or focus change. Inspected Swedish light/dark, English at 900 px, and an explicit Fable choice preserved beside an Off project. README main images updated from the default Swedish light/dark samples.

- [Default, Swedish](default-sv.png), [dark](default-dark-sv.png), [input](default-sv.json)
- [Default, English, dark at 900 px](default-en.png), [input](default-en.json)
- [Explicit Fable preserved](selected-sv.png), [input](selected-sv.json)

Reproduce: `dotnet src/BoosterAI/bin/Release/net10.0/BoosterAI.dll --screenshot <image> --config <input> --height 680`; add `--theme dark`, and `--width 900` for English.

## Windows Terminal command check

[CommandCheck](CommandCheck/Program.cs) calls the actual `Launcher.TabScript` and `WindowsTerminalArgs`. It defines a neutral `claude` function that writes the opt-out value and its shell identity to a unique WSL marker, then starts one uniquely named WT window on a private non-input Windows desktop. It never switches desktops. Only the generated command is tested, with no real agent or network call. The desktop's owned HWND is closed with `WM_CLOSE`, process trees are cleaned in `finally`, and remaining WSL-shell cleanup checks its recorded start identity before signaling it.

The installed packaged exe was inaccessible to direct CreateProcess; the application alias did not create an owned window, and old portable fixture directories were incomplete. The setup was corrected using the official portable [Windows Terminal 1.24.12741.0 archive](https://github.com/microsoft/terminal/releases/tag/v1.24.12741.0), a private `.portable` root and a neutral `settings/settings.json` containing only a hidden BoosterAI profile with closeOnExit=always. No user terminal settings were changed. This was a test-environment correction, not a product change.

[Final result](windows-command.json): passed; marker value `1`, generated opt-out true, no script semicolon, private desktop, no desktop switch, foreground unchanged and terminal closed. Post-check: zero WindowsTerminal processes from the private portable root and no marker left behind. Native Windows unit tests were not required for this platform-independent command change; the real Windows/WT/WSL path was exercised by this check.

To reproduce, build `dotnet build docs/verification/174/CommandCheck -c Release`, then run its DLL with Windows .NET 10, passing a report-file path and the complete private portable `WindowsTerminal.exe` path. The private desktop is created by the harness. Use the neutral portable profile described above.

Release, package checksums and the local installed version are recorded separately on #174.
