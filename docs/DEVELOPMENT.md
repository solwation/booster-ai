# Development

For the overview see the [README](../README.md); for how the app behaves see [Details](DETAILS.md).

## Build from source

You need the .NET 10 SDK. On Windows, install it without administrator rights in a PowerShell session:

```powershell
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\dotnet'
$scriptPath = Join-Path $env:TEMP 'dotnet-install.ps1'
Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile $scriptPath
& $scriptPath -Channel 10.0 -InstallDir $installDir -NoPath
$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
$userPathParts = @($userPath -split ';' | Where-Object { $_ -and $_.TrimEnd('\') -ne $installDir.TrimEnd('\') })
[Environment]::SetEnvironmentVariable('Path', (@($installDir) + $userPathParts) -join ';', 'User')
$env:Path = "$installDir;$env:Path"
dotnet --info
```

Open a new PowerShell window after installation so it picks up the user PATH. The install script installs the latest .NET 10 SDK; it is available from [Microsoft's .NET install page](https://learn.microsoft.com/dotnet/core/install/windows). If Windows reports `0x800711C7` and says Application Control blocked a test assembly, the machine's security policy is blocking the unsigned build; do not change that policy as part of test setup.

```sh
tools/publish.sh    # dist/win-x64/BoosterAI.exe and dist/linux-x64/BoosterAI
```

Install the result with `tools/install-linux.sh` or `tools\install-windows.ps1` from the checkout. The Windows installer stops only installed copies, replaces the executable and starts one new installed copy with `--background` and hidden process startup. Normal delivery must leave that app running, with no focus change and its saved zoom restored. If a portable copy already owns the single-instance guard, a clean secondary exit acknowledges it without failing installation; the installer reports that the existing copy must be restarted to use the new version. `-NoStart` is only for packaging/test automation, never a normal user delivery.

Run the tests with:

```sh
dotnet test tests/BoosterAI.Tests
```

On Windows, run `dotnet test tests\BoosterAI.Tests` from the Windows mirror after it has synced the revision under test.

A test run never opens terminal windows: the test assembly records every launch instead (`tests/BoosterAI.Tests/TestTerminal.cs`) and keeps sessions in a temporary folder, away from the real `sessions` folder BoosterAI watches. It also records zoom requests instead of writing the real Windows Terminal profile or nudging real settings; zoom persistence tests must not resize the user's terminals. The one test that drives real Ptyxis windows (`PtyxisTabsAreVisibleInATSPITabBar`) only runs with `BOOSTERAI_REAL_TERMINAL_TESTS=1`.

## UI strings

To add a string: add the same key to both `src/BoosterAI/Resources/Strings.sv.json` and `Strings.en.json` (a test fails if they differ), then use `{local:T some.key}` in XAML or `Loc.Get("some.key")` / `Loc.Format("some.key", args)` in code.

## Screenshots

`BoosterAI --screenshot <file.png> [--window main|settings|install|import|restart|close|alias|wsl|wsl-shutdown|agent-yolo] [--tab projects|general|optimization|appearance|agents] [--theme light|dark] --config <neutral.json>` renders a window headless: nothing is shown and focus does not change. The title bar is not rendered. Use a config with invented project names, never personal ones; `Language` in it picks Swedish or English. The sample status column shows automatically. A neutral config can include `SubTabs` under a project and `Expanded: true` to render its expanded group; child directories resolve to that project folder. `docs/screenshot.png` and `docs/settings.png` in the README are made this way. `--tab <tab>` selects the tab when rendering `--window settings` (projects by default; `--fake-setup` defaults to general). The optimization tab opens its RAM overview; `--section usage|limit|environment` selects its left-hand page. `--width 640 --height 400` renders the minimum Settings size. `--fake-memory-state empty|partial|stale|legacy|outside` supplies neutral edge-case data on optimization pages. The optimization tab uses neutral sample hardware (64 GB) and 30 minutes of sample WSL memory history (8.2–17.6 GB, average 11.7 GB, current 12.4 GB); no live process query is made. `--fake-memory-cap 48` shows a selected cap outside the suggested 24–32 GB range. `--window import` shows the Find projects dialog with a fixed sample list (no disk access). `--fake-update 0.3.0` adds the update bar as an installed copy shows it, `--fake-update manual:0.3.0` as a copy that can only link to the releases page (screenshots only, no network); both show a sample one-line summary with *Show more*, `plain:0.3.0` leaves it out as for an older release. `--fake-status "text"` / `--fake-status "error:text"` sets the status line at the bottom to a normal / error message (screenshots only). `--fake-orchestra-idle` shows a neutral idle Claude orchestra with its in-session reload icon (no live session or process); `--fake-changed` shows the first AI tab running a turn with another model than the chosen one (the restart button with its "restart with" tip); `--window restart` shows the restart confirmation for it. `--fake-restartable N` ends the first N AI sample tabs so the per-tab restart buttons and the "Restart all" button beside Start (shown from two restartable tabs) appear. `--window close` shows the save-and-close progress dialog (sample only, no process control). `--dry-run-close` prints the save/close commands with example process identities without running them. `--fake-output` opens the first running sample tab's status popup with a sample claude screen (replayed through `TerminalText`), `--fake-output 1` the Watching Run once tab's with sample formatter lines; the popup is drawn onto the window frame below the status, since a headless popup is its own surface. `--fake-setup` shows the setup bar with codex and agy missing (and npm), `--fake-setup wsl` the missing-WSL bar and `--fake-setup alias` the full-auto alias offer (samples only, no shell is run; Settings then shows the sample check result); `--window alias` and `--window wsl` show their confirmation dialogs. Screenshots and tests never run the real setup check: only the real app starts it.

The update tests (`tests/BoosterAI.Tests/UpdateTests.cs`) run the real bash helper (Linux) or PowerShell helper (Windows) against a fake install in the temp folder. `BOOSTERAI_UPDATE_E2E=<scratch dir> dotnet test tests/BoosterAI.Tests --filter EndToEndLinuxUpdateFromGitHub` also downloads the latest Linux release (~40 MB), verifies it, swaps it into `<scratch dir>/fake-install` and runs its `--dry-run`; `~/.local/bin` is never touched.

## Icon

The app icon is drawn in `assets/boosterai.svg` (with a bolt-only variant for 16–24 px in `assets/boosterai-small.svg`). After editing either, regenerate the PNG and the multi-size ICO:

```sh
dotnet run --project tools/IconRender
```

## Releases

Pushing a tag `vX.Y.Z` runs `.github/workflows/release.yml`: `dotnet test`, then `tools/package.sh` (win-x64 zip, linux-x64 tar.gz, `SHA256SUMS`), then a GitHub Release with generated notes (a tag with a `-suffix` becomes a pre-release). The tag sets the version; the `<Version>` in `BoosterAI.csproj` is only the default for local builds, so bump it afterwards. Try it locally with `tools/package.sh v0.0.0-test` (output in `dist/release/`).

```sh
git tag v0.2.0 && git push origin v0.2.0
```

## Workflow

Development is issue-driven and run with the orchestra itself. See [AGENTS.md](../AGENTS.md) and [.orchestra/project.md](../.orchestra/project.md).

The project-removal confirmation can be rendered with `--window remove-project`; it uses the neutral config’s first project name and child count, without removing anything. The Projects page disables removal and shows an inline explanation for synthetic running sample rows.

The agent settings screenshot (`--window settings --tab agents`) uses fixed neutral versions, installed/missing states and managed/custom aliases without running detection. `--section claude|codex|agy|aider|interpreter|openhands` selects that agent’s page; append `-permissions` or `-compact` to scroll to its corresponding group for minimum-window checks; `--window agent-yolo` renders its explicit YOLO confirmation without writing settings.
