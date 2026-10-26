<p align="center"><img src="assets/boosterstart.png" alt="BoosterStart icon" width="128"></p>

# BoosterStart

Start your whole working day with one click. BoosterStart opens every project you work on as a terminal tab, each with an AI coding CLI (`claude`, `codex` or `agy`) already running in it. You pick the model per tab, so you can match each day's tasks to the tokens you have left.

![The BoosterStart start window](docs/screenshot.png)

BoosterStart is a small desktop app for Windows and Linux, built with Avalonia on .NET 10. The interface is currently in Swedish.

## Why

If you work in several repositories with AI coding tools, the morning routine is the same every day: open a terminal, change directory, start the CLI, choose a model, repeat for each project. Some days you want the strongest model everywhere; on others, with tokens running low, a cheaper model for routine work and the best one only where it counts. BoosterStart keeps that choice in one window and does the rest for you.

## Features

- **One click, many tabs.** Every project you include opens in its own tab, in the right directory, with its own colour and title.
- **Per-tab choices each day.** Include the project or skip it, pick the LLM (`claude`, `codex`, `agy`, or a plain shell), pick the model from a list or type any model id. "Standard" uses the CLI's own default.
- **Remembered between runs.** Your choices are saved, so the next start is usually one click.
- **Orchestra on demand.** For projects that use [Dirigenten och orkestern](https://github.com/solwation/dirigenten-och-orkestern), a toggle sends `/orkester` as the first prompt. See below.
- **Your shell, your setup.** Each tab runs the CLI through `bash -lic`, so your aliases and `PATH` apply. When the CLI exits, you keep a normal shell in the tab.
- **Themes.** Five tab colour themes: Standard, Pastell, Nordisk, Neon and Solnedgång.

### The orchestra toggle

[Dirigenten och orkestern](https://github.com/solwation/dirigenten-och-orkestern) lets AI agents work through a project's GitHub issues. A conductor picks issues by priority, hands them to parallel worker agents in their own checkouts, then reviews and delivers the results.

The toggle appears only for projects that have it installed. Other projects show an "Installera" link: a dialog explains what will happen, then the AI you chose opens in a new tab and installs it, asking you about the rules before it commits anything.

![Confirmation dialog for installing the orchestra](docs/install-orchestra.png)

## Settings

The settings window holds your projects (name and directory, with a folder picker), the tab colour theme, and where tabs run. On Windows you can browse WSL folders through `\\wsl.localhost\...` and the choice becomes a Linux path.

![Settings window with the Nordisk theme](docs/verification/5/theme-nordic.png)

## How it works per platform

| | Terminal | Where tabs run |
|---|---|---|
| Windows | Windows Terminal (`wt.exe`), one tab per project with colour and title | The WSL distribution you choose (default `Ubuntu`); directories are Linux paths |
| Linux | `gnome-terminal`, `konsole`, `xfce4-terminal`, `kitty`, `wezterm` or `xterm` (chosen or auto-detected) | Locally |

Settings are stored in `%APPDATA%\BoosterStart\config.json` on Windows and `~/.config/BoosterStart/config.json` on Linux. A project entry is just a name and a directory, for example `~/webshop`.

`BoosterStart --dry-run` prints the commands that Start would run, without opening anything.

## Install

Builds are self-contained single files, so the target machine needs no .NET. Build first (see below), then run the installer for your platform.

Linux, installs to `~/.local/bin` and adds a menu entry:

```sh
tools/install-linux.sh
```

Windows, adds Start menu and desktop shortcuts, no admin rights needed:

```powershell
powershell -ExecutionPolicy Bypass -File tools\install-windows.ps1
```

You also need at least one of the CLIs you want to start (`claude`, `codex` or `agy`) installed where the tabs run, and on Windows a WSL distribution.

## Build from source

You need the .NET 10 SDK.

```sh
tools/publish.sh    # dist/win-x64/BoosterStart.exe and dist/linux-x64/BoosterStart
```

Run the tests with:

```sh
dotnet test tests/BoosterStart.Tests
```

## Contributing

Development is issue-driven and run with the orchestra itself: issues are picked by priority and delivered by worker agents. Read [AGENTS.md](AGENTS.md) and [.orchestra/project.md](.orchestra/project.md) before you start. Open an issue to propose a change.

## License

[MIT](LICENSE)
