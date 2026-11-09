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

Normal delivery publishes a verified release and uses the app's automatic update; a manual installation is not required after every change. Install a local build only when explicitly requested or needed to verify installation/update changes, using `tools/install-linux.sh` or `tools\install-windows.ps1` from the checkout. The Windows installer stops only installed copies, replaces the executable and starts one new installed copy with `--background` and hidden process startup. A manual installation must leave that app running, with no focus change and its saved zoom restored. If a portable copy already owns the single-instance guard, a clean secondary exit acknowledges it without failing installation; the installer reports that the existing copy must be restarted to use the new version. `-NoStart` is only for packaging/test automation, never a normal user delivery.

### Setup mode (Windows)

The release's `BoosterAI-Setup-<version>-win-x64.exe` is the same executable as `BoosterAI.exe`; a file name starting with `BoosterAI-Setup` (any case) or the `--install` flag opens the setup wizard instead of the main window (welcome, basic settings, *Find projects*, install). Every other start, portable or installed, behaves as before. Setup mode does not enter the single-instance guard. *Install* does what `install-windows.ps1` does (remove the BoosterStart install, stop only the copy at the install path, copy with retries while locked, Start menu and desktop shortcuts, start the installed copy hidden with `--background`, treating a quick exit 0 as a running copy's acknowledgement), adds the HKCU `Software\Microsoft\Windows\CurrentVersion\Uninstall\BoosterAI` entry for *Apps & features*, and merges the wizard's language, appearance, found projects and *Find projects* base folder (`ProjectSearchRoot`, which the wizard's search uses) into the existing `config.json` (an upgrade keeps all projects and settings; skipped steps change nothing). The installed copy refreshes that entry's version after a self-update. `--uninstall` (the entry's `UninstallString`) asks for confirmation, stops the installed copy, removes the shortcuts, the entry and the install folder (via a hidden helper once it exits) and keeps `config.json`. `--dry-run-install [--config file.json]` prints every install and uninstall action (paths, shortcuts, registry values) without changing anything; outside Windows it shows `%LOCALAPPDATA%`-style placeholders.

Run the tests with:

```sh
dotnet test tests/BoosterAI.Tests
```

On Windows, run `dotnet test tests\BoosterAI.Tests` from the Windows mirror after it has synced the revision under test.

A test run never opens terminal windows: the test assembly records every launch instead (`tests/BoosterAI.Tests/TestTerminal.cs`) and keeps sessions in a temporary folder, away from the real `sessions` folder BoosterAI watches. It also records zoom requests instead of writing the real Windows Terminal profile or nudging real settings; zoom persistence tests must not resize the user's terminals. The one test that drives real Ptyxis windows (`PtyxisTabsAreVisibleInATSPITabBar`) only runs with `BOOSTERAI_REAL_TERMINAL_TESTS=1`.

## UI strings

To add a string: add the same key to both `src/BoosterAI/Resources/Strings.sv.json` and `Strings.en.json` (a test fails if they differ), then use `{local:T some.key}` in XAML or `Loc.Get("some.key")` / `Loc.Format("some.key", args)` in code.

## Screenshots

`BoosterAI --screenshot <file.png> [--window main|settings|install|import|restart|close|alias|wsl|wsl-shutdown|agent-yolo] [--tab projects|general|optimization|appearance|agents] [--theme light|dark] --config <neutral.json>` renders a window headless: nothing is shown and focus does not change. The title bar is not rendered. Use a config with invented project names, never personal ones; `Language` in it picks Swedish or English. The sample status column shows automatically. A neutral config can include `SubTabs` under a project and `Expanded: true` to render its expanded group; child directories resolve to that project folder. `docs/screenshot.png` and `docs/settings.png` in the README are made this way. `--tab <tab>` selects the tab when rendering `--window settings` (projects by default; `--fake-setup` defaults to general). The optimization tab opens its RAM overview; `--section usage|limit|environment` selects its left-hand page. `--width 640 --height 400` renders the minimum Settings size. `--fake-memory-state empty|partial|stale|legacy|outside` supplies neutral edge-case data on optimization pages. The optimization tab uses neutral sample hardware (64 GB) and 30 minutes of sample WSL memory history (8.2–17.6 GB, average 11.7 GB, current 12.4 GB); no live process query is made. `--fake-memory-cap 48` shows a selected cap outside the suggested 24–32 GB range; `--fake-memory-saved 24` treats that cap as already in `.wslconfig` (no banner), so *Save to .wslconfig* stays disabled until the selection differs. `--window import` shows the Find projects dialog with a fixed sample list (no disk access). `--window setup --section welcome|settings|projects|install|progress|done|upgrade` renders a step of the setup wizard with a neutral sample install plan (`C:\Users\me\...`) and, from the projects step on, a fixed sample project list (no disk scan, nothing installed); `--window uninstall` shows the uninstall confirmation for the same sample plan. `--fake-update 0.3.0` adds the update bar as an installed copy shows it, `--fake-update manual:0.3.0` as a copy that can only link to the releases page (screenshots only, no network); both show a sample one-line summary with *Show more*, `plain:0.3.0` leaves it out as for an older release. `--fake-status "text"` / `--fake-status "error:text"` sets the status line at the bottom to a normal / error message (screenshots only). `--fake-orchestra-idle` shows a neutral idle Claude orchestra with its in-session reload icon (no live session or process); `--fake-changed` shows the first AI tab running a turn with another model than the chosen one (the restart button with its "restart with" tip); `--window restart` shows the restart confirmation for it. `--fake-restartable N` ends the first N AI sample tabs so the per-tab restart buttons and the "Restart all" button beside Start (shown from two restartable tabs) appear. `--window close` shows the save-and-close progress dialog (sample only, no process control). `--dry-run-close` prints the save/close commands with example process identities without running them. `--fake-output` opens the first running sample tab's status popup with a sample claude screen (replayed through `TerminalText`), `--fake-output 1` the Watching Run once tab's with sample formatter lines; the popup is drawn onto the window frame below the status, since a headless popup is its own surface. `--fake-setup` shows the setup bar with codex and agy missing (and npm), `--fake-setup wsl` the missing-WSL bar and `--fake-setup alias` installed CLIs without permission aliases (no setup offer) (samples only, no shell is run; Settings then shows the sample check result); `--window alias` and `--window wsl` show their confirmation dialogs. Screenshots and tests never run the real setup check: only the real app starts it.

The update tests (`tests/BoosterAI.Tests/UpdateTests.cs`) run the real bash helper (Linux) or PowerShell helper (Windows) against a fake install in the temp folder. `BOOSTERAI_UPDATE_E2E=<scratch dir> dotnet test tests/BoosterAI.Tests --filter EndToEndLinuxUpdateFromGitHub` also downloads the latest Linux release (~40 MB), verifies it, swaps it into `<scratch dir>/fake-install` and runs its `--dry-run`; `~/.local/bin` is never touched.

## Icon

The app icon is drawn in `assets/boosterai.svg` (with a bolt-only variant for 16–24 px in `assets/boosterai-small.svg`). After editing either, regenerate the PNG and the multi-size ICO:

```sh
dotnet run --project tools/IconRender
```

## Releases

Pushing a tag `vX.Y.Z` runs `.github/workflows/release.yml`: `dotnet test` and `tools/publish.sh` on Linux, then (when [code signing](#code-signing) is enabled) Authenticode signing of the Windows files on a Windows runner, then `SKIP_PUBLISH=1 tools/package.sh` (win-x64 setup exe and zip, linux-x64 tar.gz, `SHA256SUMS` of the final, signed files), then a GitHub Release with generated notes (a tag with a `-suffix` becomes a pre-release). The tag sets the version; the `<Version>` in `BoosterAI.csproj` is only the default for local builds, so bump it afterwards. Try it locally with `tools/package.sh v0.0.0-test` (output in `dist/release/`).

```sh
git tag v0.2.0 && git push origin v0.2.0
```

### Code signing

The workflow's `sign` job signs `BoosterAI.exe` and `install-windows.ps1` (`package.sh` then copies the signed `BoosterAI.exe` to `BoosterAI-Setup-<version>-win-x64.exe`; the embedded signature travels with the copy) with Svep's Azure Artifact Signing (formerly Trusted Signing) certificate profile, the same service `controlled-mlqs-server` uses, through [`azure/artifact-signing-action`](https://github.com/Azure/artifact-signing-action) on a GitHub-hosted `windows-latest` runner. It timestamps with `http://timestamp.acs.microsoft.com` (RFC 3161; the certificates live 72 hours, so the timestamp keeps the signature valid) and fails the release, rather than shipping unsigned files, if a signature is not `Valid` or lacks a timestamp. Without `SIGNING_ENABLED` the job is skipped and the release is unsigned as before. No self-hosted runner is used: this repository is public.

Setup, once. `tools/setup-signing.sh` does steps 1–3 from WSL with the signed-in Windows Azure CLI and `gh` (`--dry-run` first; it asks before each change and creates no secret):

1. **Azure (Svep IT).** An Entra app registration with the *Artifact Signing Certificate Profile Signer* role on the signing account's certificate profile. Either add a federated credential (recommended, no secret leaves Azure): issuer `https://token.actions.githubusercontent.com`, subject `repo:solwation/booster-ai:environment:signing`, audience `api://AzureADTokenExchange`; or issue a client secret.
2. **GitHub environment `signing`** (Settings → Environments), deployment restricted to tags `v*`. Environment secrets `AZURE_TENANT_ID`, `AZURE_CLIENT_ID` and, only without a federated credential, `AZURE_CLIENT_SECRET`. Environment or repository variables `SIGNING_ENDPOINT` (for example `https://weu.codesigning.azure.net/`), `SIGNING_ACCOUNT`, `SIGNING_PROFILE` and optionally `SIGNING_EXPECTED_SUBJECT` (text the signer subject must contain, such as `O=Svep`).
3. **Repository variable `SIGNING_ENABLED=true`** turns the job on (`gh variable set SIGNING_ENABLED -b true`).

Check a signed release on Windows: `Get-AuthenticodeSignature .\BoosterAI.exe | Format-List Status,SignerCertificate,TimeStamperCertificate` shows `Valid`, Svep's certificate and a timestamp; `sha256sum -c SHA256SUMS` still passes since the sums are computed after signing.

## Workflow

Development is issue-driven and run with the orchestra itself. See [AGENTS.md](../AGENTS.md) and [.orchestra/project.md](../.orchestra/project.md).

The project-removal confirmation can be rendered with `--window remove-project`; it uses the neutral config’s first project name and child count, without removing anything. The Projects page disables removal and shows an inline explanation for synthetic running sample rows.

The agent settings screenshot (`--window settings --tab agents`) uses fixed neutral versions, installed/missing states and managed/custom aliases without running detection. `--section claude|codex|agy|aider|interpreter|openhands` selects that agent’s page; append `-permissions` or `-compact` to scroll to its corresponding group for minimum-window checks; `--window agent-yolo` renders its explicit YOLO confirmation without writing settings.
