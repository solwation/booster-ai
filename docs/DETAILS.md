# Details

How BoosterAI works, per platform and per feature. For the overview see the [README](../README.md).

## How it works per platform

| | Terminal | Where tabs run |
|---|---|---|
| Windows | Windows Terminal (`wt.exe`), one tab per project with colour and title | The WSL distribution you choose (default `Ubuntu`), in your Windows folder (`C:\…` opens as `/mnt/c/…`) |
| Linux | `ptyxis`, `gnome-terminal`, `konsole`, `xfce4-terminal`, `kitty`, `wezterm` or `xterm` (chosen or auto-detected) | Locally |

Settings are stored in `%APPDATA%\BoosterAI\config.json` on Windows and `~/.config/BoosterAI/config.json` on Linux. A project entry is just a name and a directory, for example `C:\Projects\webshop` or `~/webshop`.

`BoosterAI --dry-run` prints the commands that Start would run, without opening anything.
The app follows the system's light or dark theme; `--theme light` or `--theme dark` overrides it.

## Settings

The settings window holds your projects (name and directory, with a folder picker), the tab colour theme, and where tabs run. On Windows you point at your normal Windows folders; WSL shares (`\\wsl.localhost\...`) and Linux paths such as `~/webshop` work too. Whether an agent then works in a mirror elsewhere is up to your own agent instructions.

### Font size

*Font size* in the settings window zooms the CLI text in all tabs (80–200 %; saved as `"ZoomPercent"` in `config.json`, `0` = the terminal's default, which changes nothing). It applies on the next Start.

On Windows, BoosterAI writes `%LOCALAPPDATA%\Microsoft\Windows Terminal\Fragments\BoosterAI\boosterai.json` (a hidden "BoosterAI" profile that only sets `font.size`, as a percentage of Windows Terminal's 12 pt default, and inherits your other defaults) and opens the tabs with `-p BoosterAI`; at the default size the file is removed. Windows Terminal reads fragments when it starts, so a new or changed size applies once every Windows Terminal window has been closed and reopened (until then the tabs open at your default size). Your own `settings.json` is never touched.

On Linux all tabs open as tabs of ONE window in `gnome-terminal` and `xfce4-terminal` (a single call with `--window` and `--tab` groups), and in `ptyxis` (Ubuntu 25.10+ and Fedora default; `--new-window`, then `--tab` in that window) and `konsole` (`--new-tab`, which joins an existing window and may open a new one on some setups). `kitty`, `wezterm`, `xterm` and `x-terminal-emulator` have no tabs here, so each tab gets its own window. With `auto`, Ptyxis is tried first, then gnome-terminal and the rest in the order listed.

On Linux the zoom is passed to gnome-terminal (`--zoom`), kitty (`-o font_size`, 11 pt base) and wezterm (`--config font_size`, 12 pt base); other terminals (ptyxis, konsole, xfce4-terminal, xterm, ...) have no command-line option for it and ignore the setting. `BoosterAI --dry-run` prints the resulting commands and fragment.

## Language

BoosterAI speaks Swedish and English. By default it follows the operating system's UI language (Swedish if that is Swedish, otherwise English). Choose **Automatiskt / Svenska / English** under *Language* in the settings window; the choice is saved in `config.json` (`"Language": "auto" | "sv" | "en"`) and **takes effect after BoosterAI is restarted**. The prompt sent to the AI when it installs the orchestra follows the chosen language too.

## The orchestra toggle

[Dirigenten och orkestern](https://github.com/solwation/dirigenten-och-orkestern) lets AI agents work through a project's GitHub issues. A conductor picks issues by priority, hands them to parallel worker agents in their own checkouts, then reviews and delivers the results.

The toggle appears only for projects that have it installed. Other projects show an "Install" link: a dialog explains what will happen, then the AI you chose opens in a new tab and installs it, asking you about the rules before it commits anything.

## Run once (non-interactive)

With the orchestra on, a **Run once** toggle per project (`"NonInteractive": true` in `config.json`) starts the CLI non-interactively with the orchestra command instead of an interactive session: `claude -p '/orkester'`, `agy -p '/orkester'` and `codex exec <orkester skill or /orkester>`. The tab keeps a normal shell afterwards so the output stays on screen, and the tab status shows *CLI exited (0)* or the failing exit code. The restart button of such a tab runs the orchestra again (its tooltip says so).

It means: one task that ends. You cannot answer or steer during the run, and tools that need approval are refused (no `--dangerously-*` flag is ever added). The toggle is disabled while the orchestra is off, and the install prompt always runs interactively.

It does not save tokens by itself: every turn sends the same context in both modes. In a test on #20 (claude haiku, a 34-line project) two identical one-shot runs differed more from each other than any mode difference could explain. Background tasks (`sleep` polls) kept the claude, codex and agy runs alive until they finished in a 20-second test; a long multi-hour queue run is not verified.

## Watching the tabs

After **Start**, BoosterAI stays running and shows a status per tab: *Starting…*, *Running*, *CLI exited (code)*, *CLI not found*, *Did not start*, *Tab closed* or *Lost*. On Windows the window goes to the system tray, and clicking the icon brings it back. On Linux it minimizes instead, because many desktops (plain GNOME, for example) have no tray. A tab that has closed or whose CLI has exited gets a restart button. The button opens the tab again with the same AI, model and orchestra choice, in the same Windows Terminal window.

When something unexpected happens (a tab closes, does not start, or its CLI exits with an error), BoosterAI shows a system notification. On Windows this is a toast. On Linux it uses `notify-send` when that is installed. In *Settings* you can turn **Notifications** off, and you can choose **Close after Start** to get the old behaviour back.

How it works: each Start creates a session folder (`%LOCALAPPDATA%\BoosterAI\sessions\…` on Windows, which WSL writes to through `/mnt/c`, or `~/.local/share/BoosterAI/sessions/…` on Linux) containing a small `tab-lib.sh`. Each tab's shell sources that script, and it appends `start` (PID and start time), `cli-start`, `cli-exit rc=N` and `exit` to the tab's log. The CLI still runs in the tab's own interactive login shell, so `~/.bashrc` aliases still apply, and closing the tab still ends the CLI. The app polls the logs every 2 seconds. A shell that is gone without logging `exit` (for example after `wsl --shutdown`) counts as *Lost*. Windows detects this through `\\wsl.localhost\<distro>\proc`. Session folders older than two days are removed.

Known limit: when you close a Windows Terminal window that holds several tabs, WSL may keep their shells alive for up to about a minute, so *Tab closed* can appear late.

`--screenshot` shows sample statuses and renders the window headless, so nothing appears on screen (see [Development](DEVELOPMENT.md#screenshots)).
