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

### Find projects

**Find projects** (in the empty start window and at the bottom of Settings) searches your Documents folder (on Linux `~/Documents`, or your home folder when there is none) three levels down. A folder counts as a project when it has `.git` (a folder, or a file in worktrees) or one of `AGENTS.md`, `CLAUDE.md`, `AI.md`, `GEMINI.md`, `.claude/`, `.orchestra/`, `.agents/`, `.cursorrules` or `.github/copilot-instructions.md` in its root. The search does not go into a project it found, hidden folders, links, or folders such as `node_modules`, `bin` and `obj`. Projects you already have are left out; tick the ones to add and each becomes a tab named after its folder. *Search another folder* picks a different starting point, for example a WSL share such as `\\wsl.localhost\Ubuntu\home\me`.

### Font size

*Font size* in the settings window zooms the CLI text in all tabs (80–200 %; saved as `"ZoomPercent"` in `config.json`, `0` = the terminal's default, which changes nothing). It applies on the next Start.

On Windows, BoosterAI writes `%LOCALAPPDATA%\Microsoft\Windows Terminal\Fragments\BoosterAI\boosterai.json` (a hidden "BoosterAI" profile that only sets `font.size`, as a percentage of Windows Terminal's 12 pt default, and inherits your other defaults) and opens the tabs with `-p BoosterAI`; at the default size the file is removed. Windows Terminal reads fragments only when it loads its settings, so when the fragment changes while Windows Terminal is running, BoosterAI adds or removes one trailing newline in Windows Terminal's `settings.json` to make it reload, and waits two seconds before opening the tabs. That newline is the only change to your `settings.json`; your settings themselves are never touched.

On Linux all tabs open as tabs of ONE window in `gnome-terminal` and `xfce4-terminal` (a single call with `--window` and `--tab` groups), and in `ptyxis` (Ubuntu 25.10+ and Fedora default; `--new-window`, then `--tab` in that window, each call waiting for the previous one; when Ptyxis is not running yet it first restores its saved session into a window, so the tabs then join that window instead of opening a second one) and `konsole` (`--new-tab`, which joins an existing window and may open a new one on some setups). `kitty`, `wezterm`, `xterm` and `x-terminal-emulator` have no tabs here, so each tab gets its own window. With `auto`, Ptyxis is tried first, then gnome-terminal and the rest in the order listed.

On Linux the zoom is passed to gnome-terminal (`--zoom`), kitty (`-o font_size`, 11 pt base) and wezterm (`--config font_size`, 12 pt base); other terminals (ptyxis, konsole, xfce4-terminal, xterm, ...) have no command-line option for it and ignore the setting. `BoosterAI --dry-run` prints the resulting commands and fragment.

## Appearance

*Appearance* in the settings window sets the app's light or dark look: **Auto** (follows the system, the default), **Light** or **Dark**. It is saved as `"AppTheme": "auto" | "light" | "dark"` in `config.json` and applies at once to all open windows. The command-line flag `--theme light|dark` wins over the config (used for screenshots). It does not affect the tab colour themes.

## Language

BoosterAI speaks Swedish and English. By default it follows the operating system's UI language (Swedish if that is Swedish, otherwise English). Choose **Automatiskt / Svenska / English** under *Language* in the settings window; the choice is saved in `config.json` (`"Language": "auto" | "sv" | "en"`) and **takes effect after BoosterAI is restarted**. The prompt sent to the AI when it installs the orchestra follows the chosen language too.

## The orchestra toggle

[Dirigenten och orkestern](https://github.com/solwation/dirigenten-och-orkestern) lets AI agents work through a project's GitHub issues. A conductor picks issues by priority, hands them to parallel worker agents in their own checkouts, then reviews and delivers the results.

The toggle appears only for projects that have it installed. Other projects show an "Install" link: a dialog explains what will happen, then the AI you chose opens in a new tab and installs it, asking you about the rules before it commits anything.

## Run once (non-interactive)

With the orchestra on, a **Run once** toggle per project (`"NonInteractive": true` in `config.json`) starts the CLI non-interactively with the orchestra command instead of an interactive session: `claude -p '/orkester'`, `agy -p '/orkester'` and `codex exec <orkester skill or /orkester>`. The tab shows live progress while it runs: for `claude` and `agy` the event stream (`--output-format stream-json`) goes through a small formatter in `tab-lib.sh` (`bs_stream`, uses python3 if present, otherwise a plain grep/sed fallback) that prints the assistant text, tool calls (`> Bash ls -la`) and a final summary line, and the CLI's own exit code is kept; `codex exec` already prints its progress. Without status tracking the tab falls back to the plain `-p` text mode. The tab keeps a normal shell afterwards so the output stays on screen, and the tab status shows *CLI exited (0)* or the failing exit code. The restart button of such a tab runs the orchestra again (its tooltip says so).

It means: one task that ends. You cannot answer or steer during the run, and tools that need approval are refused (no `--dangerously-*` flag is ever added). The toggle is disabled while the orchestra is off, and the install prompt always runs interactively.

Your alias for the CLI applies in Run once too (for example `alias agy='agy --dangerously-skip-permissions'`), so with such an alias nothing is denied. Without one, agy refuses every shell command without an allow-rule in `~/.gemini/antigravity-cli/settings.json` (`permissions.allow`, e.g. `command(git)`), ends the turn and still reports success. BoosterAI notices it: the formatter logs the denied command line, the tab shows *Denied: git, gh* (red, with a notification), and a bar in the main window offers **Allow and run again**. That adds `command(<program>)` for each program of the denied command line that is not allowed yet (on Windows in the WSL home), keeps everything else in the file and runs the affected tabs again. **Not now** hides the offer for those programs until BoosterAI restarts. `--mode auto` is no alternative: it needs agy's terminal sandbox. A rule allows the whole program, so `command(gh)` or `command(npm)` lets agy run anything those can do; review the list in `settings.json`. Detection needs python3 in the tab's Linux; `claude -p` denials are not detected yet.

It does not save tokens by itself: every turn sends the same context in both modes. In a test on #20 (claude haiku, a 34-line project) two identical one-shot runs differed more from each other than any mode difference could explain. Background tasks (`sleep` polls) kept the claude, codex and agy runs alive until they finished in a 20-second test; a long multi-hour queue run is not verified.

## Updates

With **Check for updates** on (the default, `"CheckForUpdates": true` in `config.json`), BoosterAI asks `https://api.github.com/repos/solwation/booster-ai/releases/latest` in the background at start, when the main window is activated and every five minutes while it stays active; an inactive or minimized window never checks. The last successful check is saved as `"LastUpdateCheck"`, and no check is made within five minutes of it, so at most 12 calls an hour stay well inside the 60 unauthenticated calls an hour that GitHub allows. Pre-releases and drafts are ignored, and the release tag is compared semantically with the app's own version (release builds get it from the tag, `-p:Version=${tag#v}`). A network error or an unexpected answer shows nothing; the next minute tick or activation tries again.

When a newer release exists, a bar above the projects says *Version X is available* with three choices:

- **Update** downloads the archive for this platform (`BoosterAI-<version>-win-x64.zip` or `-linux-x64.tar.gz`) and `SHA256SUMS` from the release, refuses the archive unless its SHA-256 matches, and extracts only the executable, next to the installed one as `BoosterAI(.exe).new`. A small helper script then takes over and BoosterAI exits. On Windows it is a hidden PowerShell script (`%TEMP%\BoosterAI-update-<version>\update.ps1`, no window); it waits for BoosterAI to exit, moves the old exe aside, moves the new one in (and puts the old one back if that fails) and starts BoosterAI again. On Linux a detached bash script waits the same way and renames the new binary over the old one. The helper writes `updated` or `failed: …` to `update.log` next to `config.json`. Tabs already open keep running, but the new instance does not watch them; clicking *Update* is the confirmation.
- **Later** hides that version for a day (until the app restarts); a newer release shows at once.
- **Skip this version** saves it as `"SkippedVersion"` and does not offer it again; a later release is offered as usual.

Only a copy in the location the installers use updates itself: `%LOCALAPPDATA%\Programs\BoosterAI\BoosterAI.exe` on Windows, `~/.local/bin/BoosterAI` on Linux. Anything else (a development build, `dotnet run`, another folder, a non-x64 machine) shows the bar with **Open the releases page** instead, as does a failed update. The Linux self-update replaces only the binary; the menu entry and icons from `install-linux.sh` stay as they are. The checksum comes from the same release as the archive, so it protects against corrupt or truncated downloads, not against a compromised GitHub release.

## Watching the tabs

After **Start**, BoosterAI stays running and shows a status per tab: *Starting…*, *Running*, *CLI exited (code)*, *CLI not found*, *Did not start*, *Tab closed* or *Lost*. The window minimizes to the taskbar, so the status is one click away. Closing the window quits BoosterAI. On Windows there is also a tray icon (Windows may tuck it behind the ^ arrow) whose menu shows the window or quits. Linux gets no tray icon, because many desktops (plain GNOME, for example) have no tray. A tab that has closed or whose CLI has exited gets a restart button. The button opens the tab again with the same AI, model and orchestra choice, in the same Windows Terminal window.

**Changing the AI or model of a running tab** shows the restart button too, with the status *Running – new choice* and the tip *Restart the tab with &lt;llm&gt; / &lt;model&gt;*. The restart happens in the same tab: BoosterAI queues the new command in the session directory (`tabN.next`), then stops the tab's foreground process group, which is the CLI and never the shell. The tab's chain then runs `bs_next` from `tab-lib.sh`, which starts the queued CLI and logs it like the first one. A model change on `claude` or `codex` keeps the conversation (`claude --continue --model X`, `codex resume --last --model X`). A new LLM, agy or a Run once tab starts fresh. Changing back to the running choice removes the button.

Interactive `claude` tabs start with `--settings tabN.claude.json`, whose hooks log *busy* on a submitted prompt and *idle* when a turn ends, so the status shows *Working* or *Idle*. The `Notification` hook is not used, because it also fires for a permission prompt in the middle of a turn. If the CLI is working, the restart asks first: **Restart now** interrupts the turn, and **Restart when idle** makes the row wait (*Waiting for idle…*, a second click cancels) until the next *idle*. For codex and agy, BoosterAI cannot tell whether the CLI is working, so the dialog says that and offers only **Restart now** or **Cancel**. An idle claude restarts without asking.

**Tabs that already run.** Each started tab saves its choices next to its log (`tabN.tab.json`). When BoosterAI starts, for example after an update, it looks through the session directories for tabs whose shell is still alive (same PID and start time in `/proc`, no *exit* in the log). Each one is matched to the configured tab with the same name and directory and is watched again, with status, restart and in-place restart, and the status line says how many were found. If the choice has changed since that tab was started, it shows *Running – new choice*. **Start** opens only the chosen tabs that are not running: the button counts those, says *All chosen tabs are running* when there are none, and opens new tabs in the window of the newest session it found. A session older than two days is kept while one of its tabs still runs.

When something unexpected happens (a tab closes, does not start, or its CLI exits with an error), BoosterAI shows a system notification. On Windows this is a toast. On Linux it uses `notify-send` when that is installed. In *Settings* you can turn **Notifications** off, and you can choose **Close after Start** to get the old behaviour back.

How it works: each Start creates a session folder (`%LOCALAPPDATA%\BoosterAI\sessions\…` on Windows, which WSL writes to through `/mnt/c`, or `~/.local/share/BoosterAI/sessions/…` on Linux) containing a small `tab-lib.sh`. Each tab's shell sources that script, and it appends `start` (PID and start time), `cli-start`, `cli-exit rc=N` and `exit` to the tab's log. The CLI still runs in the tab's own interactive login shell, so `~/.bashrc` aliases still apply, and closing the tab still ends the CLI. The app polls the logs every 2 seconds. A shell that is gone without logging `exit` (for example after `wsl --shutdown`) counts as *Lost*. Windows detects this through `\\wsl.localhost\<distro>\proc`. Session folders older than two days are removed.

Known limit: when you close a Windows Terminal window that holds several tabs, WSL may keep their shells alive for up to about a minute, so *Tab closed* can appear late.

`--screenshot` shows sample statuses and renders the window headless, so nothing appears on screen (see [Development](DEVELOPMENT.md#screenshots)).
