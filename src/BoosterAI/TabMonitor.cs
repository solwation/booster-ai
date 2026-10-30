using System.Globalization;
using System.Text.Json;

namespace BoosterAI;

/// <summary>
/// Where a tab writes its status log, as the tab's shell sees the paths (see Launcher.TabScript).
/// Lib is the sourced script, Dir the session directory, Id the tab's file name in it.
/// <paramref name="UpdateClis"/>: the tab updates its AI CLI before starting it (AppConfig.AutoUpdateClis, tab-lib.sh's bs_update).
/// <paramref name="AutoClear"/>: an interactive claude runs under bs_pty so BoosterAI can type /clear into it (AppConfig.AutoClearIdle, #69).
/// </summary>
public sealed record TabTrack(string Lib, string Dir, string Id, bool UpdateClis = false, bool AutoClear = false);

public enum TabState { Starting, Running, CliExited, CliMissing, NotStarted, Closed, Lost, Denied }

/// <summary>
/// Whether a running CLI is working on a turn; Unknown for CLIs without hooks (codex, agy). Watching: a one-shot
/// run (bs_stream) finished a turn but its CLI lives on, e.g. claude -p waiting for an orchestra idle watch.
/// </summary>
public enum Activity { Unknown, Busy, Idle, Watching }

/// <summary>
/// What a tab's log says. One line per event, written by tab-lib.sh:
/// "&lt;epoch-ms&gt; start pid=N starttime=N", "… cli-start", "… cli-exit rc=N", "… exit", and
/// "… denied cli=agy cmd=&lt;base64&gt;" when agy refused a command it could not ask about, and "… busy" /
/// "… idle" from claude's hooks, "… input-ready" / "… input-sent" / "… input-skipped reason=…" from bs_pty, "… turn-done" / "… turn-start" from bs_stream's formatter (a turn ended / output
/// resumed). <paramref name="WatchingSince"/> is the epoch-ms of a turn-done not yet followed by output (null: none).
/// <paramref name="Denied"/> holds those command lines (null: none). An in-place
/// restart logs a new cli-start, which starts the CLI's part of the log over.
/// </summary>
public sealed record TabLog(int? Pid, long? StartTime, bool CliStarted, int? CliExit, bool Exited, IReadOnlyList<string>? Denied = null,
    Activity Activity = Activity.Unknown, long? WatchingSince = null, string? CloseSaveToken = null, bool CanCloseCleanly = false, bool CanRestartExited = false)
{
    /// <summary>Epoch-ms of the last "busy" / "idle" from claude's hooks since the CLI started (null: none).</summary>
    public long? BusyAt { get; init; }
    public long? IdleAt { get; init; }
    /// <summary>The CLI runs under tab-lib.sh's bs_pty, which types what BoosterAI writes to tabN.input ("input-ready").</summary>
    public bool InputReady { get; init; }
    /// <summary>Epoch-ms of the last request bs_pty handled ("input-sent" / "input-skipped"); <see cref="InputSent"/>: it was typed.</summary>
    public long? InputAt { get; init; }
    public bool InputSent { get; init; }

    public static TabLog Parse(string text)
    {
        int? pid = null, rc = null;
        long? start = null, watching = null, busyAt = null, idleAt = null, inputAt = null;
        bool cliStarted = false, exited = false, canClose = false, canRestart = false, inputReady = false, inputSent = false;
        List<string>? denied = null;
        var activity = Activity.Unknown;
        string? closeToken = null;
        foreach (var line in text.Split('\n'))
        {
            var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // A line still being written (no event yet) is ignored; the next poll reads it whole.
            if (parts.Length < 2 || !long.TryParse(parts[0], CultureInfo.InvariantCulture, out var at)) continue;
            var values = parts.Skip(2).Select(p => p.Split('=', 2)).Where(kv => kv.Length == 2)
                .ToDictionary(kv => kv[0], kv => kv[1]);
            switch (parts[1])
            {
                case "start":
                    pid = Int(values, "pid");
                    start = values.TryGetValue("starttime", out var s) && long.TryParse(s, CultureInfo.InvariantCulture, out var st) ? st : null;
                    break;
                case "cli-start":
                    closeToken = null; cliStarted = true; rc = null; denied = null; activity = Activity.Unknown; watching = null;
                    busyAt = idleAt = inputAt = null; inputReady = inputSent = false;
                    break;
                case "input-ready": inputReady = true; break;
                case "input-sent": inputAt = at; inputSent = true; break;
                case "input-skipped": inputAt = at; inputSent = false; break;
                case "close-ready": canClose = true; break;
                case "restart-ready": canRestart = true; break;
                case "close-save": closeToken = values.GetValueOrDefault("token"); break;
                case "turn-done": watching = at; break;
                case "turn-start": watching = null; break;
                case "busy": activity = Activity.Busy; busyAt = at; break;
                case "idle": activity = Activity.Idle; idleAt = at; break;
                case "cli-exit": rc = Int(values, "rc") ?? -1; inputReady = false; break;
                case "exit": exited = true; break;
                case "denied": (denied ??= []).Add(Decode(values.GetValueOrDefault("cmd"))); break;
            }
        }
        return new TabLog(pid, start, cliStarted, rc, exited, denied, activity, watching, closeToken, canClose, canRestart)
        {
            BusyAt = busyAt, IdleAt = idleAt, InputReady = inputReady, InputAt = inputAt, InputSent = inputSent,
        };
    }

    private static string Decode(string? base64)
    {
        try { return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64 ?? "")); }
        catch (FormatException) { return ""; }
    }

    private static int? Int(Dictionary<string, string> v, string key) =>
        v.TryGetValue(key, out var s) && int.TryParse(s, CultureInfo.InvariantCulture, out var i) ? i : null;
}

public static class TabStatus
{
    /// <summary>How long a tab may take (WSL boot, login shell) before it counts as not started.</summary>
    public static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(45);

    /// <summary>How long terminal output can be quiet before a live CLI is shown as idle.</summary>
    public static readonly TimeSpan OutputIdleTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The tab's state from its log (null: no log yet). <paramref name="liveStartTime"/> returns the
    /// start time of a live process with that PID (null: no such process); pass null when liveness
    /// cannot be checked. A PID that is gone or reused while the log has no "exit" means the tab died
    /// without its shell exiting normally (wsl --shutdown, SIGKILL).
    /// </summary>
    public static TabState Evaluate(TabLog? log, TimeSpan sinceLaunch, Func<int, long?>? liveStartTime)
    {
        if (log?.Pid is null) return sinceLaunch < StartTimeout ? TabState.Starting : TabState.NotStarted;
        if (log.Exited) return TabState.Closed;
        if (liveStartTime is not null && liveStartTime(log.Pid.Value) is var live
            && (live is null || (log.StartTime is not null && live != log.StartTime)))
            return TabState.Lost;
        return log.CliExit switch
        {
            null => TabState.Running,
            // No cli-start: the cd failed before the CLI ran.
            _ when !log.CliStarted => TabState.NotStarted,
            126 or 127 => TabState.CliMissing,
            // agy reports success (rc 0) even when it stopped at a denied command.
            _ when log.Denied is not null => TabState.Denied,
            _ => TabState.CliExited,
        };
    }

    /// <summary>Whole minutes since a turn ended (log epoch-ms), for the Watching status; 0 when unknown or in the future.</summary>
    public static long WatchingMinutes(long? sinceMs, DateTime nowUtc) =>
        sinceMs is { } ms ? Math.Max(0, (long)(nowUtc - DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime).TotalMinutes) : 0;

    /// <summary>Whether entering this state deserves a notification; a CLI the user quit (rc 0) does not.</summary>
    public static bool IsUnexpected(TabState state, int? rc) => state switch
    {
        TabState.Starting or TabState.Running => false,
        TabState.CliExited => rc != 0,
        _ => true,
    };

    /// <summary>Field 22 (starttime) of /proc/&lt;pid&gt;/stat; the command name may contain spaces.</summary>
    public static long? ParseProcStartTime(string stat)
    {
        var close = stat.LastIndexOf(')');
        if (close < 0) return null;
        var fields = stat[(close + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // fields[0] is field 3 (state), so field 22 is fields[19].
        return fields.Length > 19 && long.TryParse(fields[19], CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}

/// <summary>
/// One "Start": a directory with the sourced tab-lib.sh and one log per tab, under
/// %LOCALAPPDATA%\BoosterAI\sessions (Windows, written by WSL via /mnt/c) or
/// ~/.local/share/BoosterAI/sessions (Linux).
/// </summary>
public sealed class TabSession
{
    public const string Lib = """
# BoosterAI tab status log, sourced by the tab's shell: . tab-lib.sh <dir> <tab-id>
bs_log="$1/$2.log"
bs_timing="$1/$2.timing"
bs_ev() { printf '%s %s\n' "$(date +%s%3N)" "$*" >> "$bs_log" 2>/dev/null; }
bs_out="$1/$2.out"
bs_size="$1/$2.size"
bs_input="$1/$2.input"
bs_ev start pid=$$ starttime=$(cut -d' ' -f22 /proc/$$/stat)
# The captured output only lives as long as the tab (and is replaced when a new CLI starts).
bs_rm_out() { rm -f "$bs_out" "$bs_out.1" "$bs_size" "$bs_input" 2>/dev/null || true; }
trap 'bs_rm_out && bs_ev exit' EXIT
# A requested close returns success to the terminal, even when the foreground CLI had to be stopped.
trap 'exit 0' HUP USR1
bs_ev close-ready
# The tab's configured name (third argument, #66), without control characters. bs_title sets it as the terminal title
# whenever a CLI starts or exits, and the idle shell's prompt repeats it, so a title an application set is replaced.
# Claude Code does not set titles in these tabs; codex and agy may, until the next start, exit or prompt.
bs_name=${3//[$'\001'-$'\037'$'\177']/}
if [ -n "$bs_name" ]; then
  export CLAUDE_CODE_DISABLE_TERMINAL_TITLE="${CLAUDE_CODE_DISABLE_TERMINAL_TITLE:-1}"
  bs_t=${bs_name//\\/\\\\}
  bs_t=${bs_t//\$/\\\$}
  bs_t=${bs_t//\`/\\\`}
  export BS_PS1_TITLE="\[\e]0;$bs_t\a\]"
  unset bs_t
fi
bs_title() { [ -n "$bs_name" ] && [ -t 1 ] || return 0; printf '\033]0;%s\007' "$bs_name"; }
bs_cli() { : > "$bs_timing" 2>/dev/null || true; bs_rm_out; bs_ev cli-start; bs_title; }
bs_done() { bs_ev cli-exit rc=$1; bs_title; }
# bs_fg <command>: run it with a bash as the leader of the terminal's foreground process group. Ptyxis appends the
# foreground program's command line to the tab name ("name — script --quiet ..."), except for a shell (#66). The
# subshell waits for the command when BoosterAI stops it (TERM) instead of letting the chain go on early; the
# command itself gets the default signal handling again.
bs_fg() ( trap : INT TERM; "$@"; exit $? )
# bs_reset: put the terminal in a clean state before a CLI starts (#67). A TUI that was stopped or killed can leave
# the alternate screen, mouse reporting, bracketed paste, a hidden cursor or odd attributes behind, and the next CLI
# then draws wrongly. Soft on purpose: no full reset (ESC c), which would also drop the tab title. Only on a terminal.
bs_reset() {
  [ -t 1 ] || return 0
  stty sane 2>/dev/null || true
  printf '\033[?1049l\033[?1000l\033[?1002l\033[?1003l\033[?1006l\033[?2004l\033[?25h\033[!p\033[0m\033[H\033[2J\033[3J'
}
# bs_cap [tee]: append stdin to the tab's output capture (tee: also pass it on), which BoosterAI shows while
# the status is hovered. bs_trim keeps it bounded: past bs_cap_max bytes the file's last bs_cap_max bytes move
# to .1 and the file is emptied (the writer appends, so it continues at the new start); only these two stay. It also notes
# the tab's terminal size, which BoosterAI needs to replay the output (rows cols), and stops with bs_cap.
bs_cap_max=131072
bs_trim() {
  local s z=
  while kill -0 "$1" 2>/dev/null; do
    # stty may print the size and still fail (EIO from a background process group): check the text.
    s=$(stty size </dev/tty 2>/dev/null)
    case $s in
      [0-9]*' '[0-9]*) if [ "$s" != "$z" ]; then printf '%s\n' "$s" > "$bs_size" && z=$s; fi ;;
    esac
    if [ "$(stat -c %s "$bs_out" 2>/dev/null || echo 0)" -gt "$bs_cap_max" ]; then
      tail -c "$bs_cap_max" "$bs_out" > "$bs_out.1" 2>/dev/null && : > "$bs_out"
    fi
    sleep 1
  done
}
bs_cap() {
  # Take the PID first: on the & line $BASHPID expands in the background child, so the loop would watch itself.
  local me=$BASHPID
  bs_trim "$me" </dev/null >/dev/null 2>&1 &
  if [ "$1" = tee ]; then tee -a "$bs_out" 2>/dev/null || cat; else cat >> "$bs_out" 2>/dev/null || cat >/dev/null; fi
}
# Run interactive commands under a nested PTY so util-linux script can record output timing and
# the output itself (bs_cap). Keep aliases and shell functions from the user's login shell.
bs_run() {
  local cmd="$1"
  case "$cmd" in
    bs_stream\ *) eval "$cmd"; return $? ;;
  esac
  local name="${cmd%% *}"
  if [ -n "${BASH_ALIASES[$name]+x}" ]; then cmd="${BASH_ALIASES[$name]}${cmd#"$name"}"; fi
  if declare -F "$name" >/dev/null; then export -f "$name"; fi
  if ! command -v script >/dev/null 2>&1; then rm -f "$bs_timing"; bs_fg eval "$cmd"; return $?; fi
  if [ "$2" = input ] && [ -t 0 ] && command -v python3 >/dev/null 2>&1; then
    BS_COMMAND="$cmd" bs_fg bs_pty script --quiet --flush --return --log-out >(bs_cap) --log-timing "$bs_timing" --command 'eval "$BS_COMMAND"'
    return $?
  fi
  BS_COMMAND="$cmd" bs_fg script --quiet --flush --return --log-out >(bs_cap) --log-timing "$bs_timing" --command 'eval "$BS_COMMAND"'
}
# bs_pty <command>: run it on a pseudo-terminal of its own and pass the tab's keyboard and screen through unchanged
# (raw bytes, so raw mode, bracketed paste and mouse/focus reports reach the CLI as before; window size changes are
# copied). It is BoosterAI's only way to type into a running CLI (auto-clear, #69): it polls for the tab's request
# file "<since-epoch-ms> <base64 text>", deletes it, and types the text, a final CR separately 0.3 s later so the CLI
# sees a typed command, not a paste. A request is skipped (logged input-skipped) when a key was pressed after <since>,
# so a draft in the prompt is never sent along. No TIOCSTI (disabled on current kernels), no window. TERM, INT and
# QUIT are passed on; a hangup of the tab stops the command with HUP and TERM (script ignores a hangup it does not see
# on its own terminal) instead of leaving it behind. Logs input-ready while it accepts requests.
bs_pty() {
  BS_INPUT="$bs_input" BS_LOG="$bs_log" python3 -c 'import os, sys, pty, tty, termios, fcntl, signal, select, time, base64
cmd = sys.argv[1:]
req = os.environ["BS_INPUT"]
def ev(s):
    try:
        with open(os.environ["BS_LOG"], "a") as f:
            f.write("%d %s\n" % (time.time() * 1000, s))
    except (KeyError, OSError):
        pass
def drop():
    try:
        os.unlink(req)
    except OSError:
        pass
def size(fd):
    return fcntl.ioctl(fd, termios.TIOCGWINSZ, b"\0" * 8)
if not os.isatty(0):
    os.execvp(cmd[0], cmd)
attrs = termios.tcgetattr(0)
ws = size(0)
drop()
pid, master = pty.fork()
if pid == 0:
    termios.tcsetattr(0, termios.TCSANOW, attrs)
    fcntl.ioctl(0, termios.TIOCSWINSZ, ws)
    os.execvp(cmd[0], cmd)
def winch(*_):
    try:
        fcntl.ioctl(master, termios.TIOCSWINSZ, size(0))
    except OSError:
        pass
def forward(sig, _=None):
    try:
        os.kill(pid, sig)
    except OSError:
        pass
def hangup(*_):
    forward(signal.SIGHUP)
    forward(signal.SIGTERM)
signal.signal(signal.SIGWINCH, winch)
for s in (signal.SIGTERM, signal.SIGINT, signal.SIGQUIT):
    signal.signal(s, forward)
signal.signal(signal.SIGHUP, hangup)
def put(b):
    while b:
        b = b[os.write(master, b):]
typed = 0.0
due = []
checked = 0.0
fds = [0, master]
ev("input-ready")
try:
    tty.setraw(0)
    while True:
        r = select.select(fds, [], [], 0.25)[0]
        if master in r:
            try:
                b = os.read(master, 65536)
            except OSError:
                b = b""
            if not b:
                break
            o = memoryview(b)
            try:
                while o:
                    o = o[os.write(1, o):]
            except OSError:
                hangup()
        if 0 in r:
            try:
                b = os.read(0, 65536)
            except OSError:
                b = b""
            if not b:
                fds = [master]
                hangup()
            else:
                # Focus reports are the terminal, not the user typing.
                if b.replace(b"\x1b[I", b"").replace(b"\x1b[O", b""):
                    typed = time.time()
                put(b)
        now = time.time()
        if due and now >= due[0][0]:
            put(due.pop(0)[1])
        if now - checked >= 1:
            checked = now
            try:
                with open(req) as f:
                    text = f.read()
                drop()
            except OSError:
                text = None
            if text is not None:
                try:
                    since, data = text.split()
                    since = int(since) / 1000
                    data = base64.b64decode(data, validate=True)
                except ValueError:
                    since, data = now, b""
                if not data:
                    ev("input-skipped reason=invalid")
                elif typed > since:
                    ev("input-skipped reason=typed")
                else:
                    put(data.rstrip(b"\r"))
                    if data.endswith(b"\r"):
                        due.append((now + 0.3, b"\r"))
                    ev("input-sent")
except OSError:
    hangup()
finally:
    try:
        termios.tcsetattr(0, termios.TCSAFLUSH, attrs)
    except termios.error:
        pass
    drop()
while True:
    try:
        rc = os.waitstatus_to_exitcode(os.waitpid(pid, 0)[1])
        break
    except ChildProcessError:
        rc = 1
        break
sys.exit(rc if rc >= 0 else 128 - rc)
' "$@"
}
# bs_next: run what BoosterAI queued for an in-place restart (it then stops the CLI), until nothing is queued.
bs_next_f="$1/$2.next"
bs_next() {
  local c
  while [ -f "$bs_next_f" ]; do c=$(cat "$bs_next_f") && rm -f "$bs_next_f" && eval "$c"; done
  return 0; }
# Keep the parent chain available after the CLI exits. Restart wakes the idle child shell,
# then this loop consumes its queued command in the original tracked shell. The child records
# itself only after ~/.bashrc (seconds on a slow one), then looks for a restart queued meanwhile and
# hands it to this loop at once: BoosterAI queues before it looks for .idle, so one side always sees the other.
bs_shell() {
  bs_ev restart-ready
  while true; do
    rm -f "$bs_next_f.idle"
    bs_next
    BS_IDLE_FILE="$bs_next_f.idle" bash --rcfile <(printf '%s\n' '[ ! -f ~/.bashrc ] || . ~/.bashrc' '[ -z "$BS_PS1_TITLE" ] || PS1="$PS1$BS_PS1_TITLE"' 'printf "%s %s\n" "$$" "$(cut -d" " -f22 /proc/$$/stat)" > "$BS_IDLE_FILE"' '[ ! -f "${BS_IDLE_FILE%.idle}" ] || exit 0')
    rm -f "$bs_next_f.idle"
    [ -f "$bs_next_f" ] || break
  done
}
# bs_update <cli>: before claude, codex or agy starts, update it when a newer version exists (Launcher.UpdateHook).
# At most once per bs_up_every seconds per CLI (the stamp's time) and one tab at a time (flock: a tab that waited
# finds the fresh stamp and starts). Every failure prints one line and the installed version starts. The state lives
# in the Linux home next to the CLIs it describes (on Windows the session folder is on /mnt/c, where flock is unreliable).
bs_up_dir="$HOME/.local/share/BoosterAI/cli-update"
bs_up_every=21600
# Seconds a registry query and an update may take.
bs_up_net=20
bs_up_limit=300
bs_up_ver() { "$@" --version </dev/null 2>/dev/null | grep -oE '[0-9]+(\.[0-9]+)+([-+][0-9A-Za-z.]+)?' | head -n 1; }
bs_up_due() {
  local t n
  t=$(stat -c %Y "$1" 2>/dev/null) || return 0
  n=$(date +%s)
  [ $((n - t)) -ge "$bs_up_every" ] || [ "$t" -gt "$n" ]
}
# The reason a command failed: timeout's 124, or the last line it logged.
bs_up_why() {
  if [ "$1" = 124 ]; then echo "timed out"; return; fi
  local l
  l=$(grep -v '^[[:space:]]*$' "$2" 2>/dev/null | tail -n 1 | tr -s ' \t' ' ')
  echo "${l:0:120}" | grep . || echo "exit code $1"
}
bs_update() (
  cli=$1
  case $cli in claude|codex|agy) ;; *) exit 0 ;; esac
  # Only a CLI on PATH: a shell function or alias is not something BoosterAI can update.
  exe=$(type -P "$cli") || exit 0
  bs_up_due "$bs_up_dir/$cli.checked" || exit 0
  say() { printf 'BoosterAI: %s\n' "$*"; }
  if ! command -v flock >/dev/null 2>&1 || ! command -v timeout >/dev/null 2>&1; then say "$cli update skipped: flock or timeout is missing"; exit 0; fi
  if ! mkdir -p "$bs_up_dir" 2>/dev/null || [ ! -w "$bs_up_dir" ] || ! exec 8>"$bs_up_dir/$cli.lock"; then say "$cli update skipped: cannot write $bs_up_dir"; exit 0; fi
  pid= stamp=
  # Stamped when the check ends, also after a failure (offline, no rights): it is retried after the interval, not on
  # every start. Until then a tab started meanwhile waits for the lock instead of starting a CLI being replaced.
  trap '[ -z "$stamp" ] || touch "$bs_up_dir/$cli.checked" 2>/dev/null' EXIT
  trap 'stamp=
    [ -z "$pid" ] || kill -TERM "$pid" 2>/dev/null
    printf "\nBoosterAI: %s update stopped, starting the installed version\n" "$cli"
    exit 0' INT TERM HUP
  if ! flock -n 8; then
    say "waiting for another tab's $cli update..."
    flock -w 45 8 || { say "$cli update skipped: another tab is still updating it"; exit 0; }
  fi
  # Another tab may have checked while this one waited.
  bs_up_due "$bs_up_dir/$cli.checked" || exit 0
  stamp=1
  log="$bs_up_dir/$cli.log"
  : > "$log" 2>/dev/null || log=/dev/null
  cur=$(bs_up_ver "$exe")
  printf 'BoosterAI: checking for a newer %s %s... ' "$cli" "$cur"
  # codex installed with npm: compare with the registry and install only a newer version. Everything else uses the
  # CLI's own updater, which checks first (claude update also handles package-manager installs).
  root=
  [ "$cli" != codex ] || ! command -v npm >/dev/null 2>&1 || root=$(timeout 10 npm root -g 2>/dev/null </dev/null) || root=
  if [ -n "$root" ] && [[ $(readlink -f "$exe") == "$root"/* ]]; then
    latest=$(timeout "$bs_up_net" npm view @openai/codex version 2>>"$log" </dev/null)
    rc=$?
    if [ $rc != 0 ] || [ -z "$latest" ]; then echo "skipped: npm view @openai/codex failed ($(bs_up_why $rc "$log"))"; exit 0; fi
    if [ -n "$cur" ] && [ "$(printf '%s\n%s\n' "$cur" "$latest" | sort -V | tail -n 1)" = "$cur" ]; then echo "up to date"; exit 0; fi
    what="npm install -g @openai/codex@$latest"
    set -- npm install -g "@openai/codex@$latest"
  else
    what="$cli update"
    set -- "$exe" update
  fi
  timeout "$bs_up_limit" "$@" </dev/null >>"$log" 2>&1 &
  pid=$!
  wait "$pid"
  rc=$?
  pid=
  if [ $rc != 0 ]; then echo "skipped: $what failed ($(bs_up_why $rc "$log"))"; exit 0; fi
  new=$(bs_up_ver "$(type -P "$cli" || echo "$exe")")
  if [ "$new" != "$cur" ]; then echo "updated to $new"; else echo "up to date"; fi
)
# bs_stream <cli> <args>: run a CLI that emits stream-json (claude, agy), show it as readable
# progress, and return the CLI's own exit code (not the formatter's). The CLI is an argument
# here, so bash does not expand the user's alias for it (agy='agy --dangerously-skip-permissions'):
# apply it explicitly, as a typed command would.
bs_stream() {
  if [ -n "${BASH_ALIASES[$1]+x}" ]; then eval "set -- ${BASH_ALIASES[$1]}"' "${@:2}"'; fi
  local t0=$SECONDS rc
  # The group runs in this shell (rc stays set); its output also goes to the capture. bs_fg: see there.
  { bs_fg bs_pipe "$@"; rc=$?; echo "== exit $rc, total $((SECONDS - t0)) s"; } > >(bs_cap tee)
  # Let the capture print its last line before the shell goes on (bash 5.1+ waits for a process substitution).
  wait "$!" 2>/dev/null
  return "$rc"; }
bs_pipe() { "$@" | bs_fmt; return "${PIPESTATUS[0]}"; }
bs_fmt() {
  if command -v python3 >/dev/null 2>&1; then
    BS_LOG="$bs_log" BS_TIMING="$bs_timing" PYTHONIOENCODING=utf-8 python3 -u -c 'import sys, json, os, time, base64, shutil
last = ""
watching = False
def cols():
    # stdout goes to the capture (bs_cap), so ask the terminal through stderr.
    for fd in (1, 2):
        try:
            return os.get_terminal_size(fd).columns
        except OSError:
            pass
    return shutil.get_terminal_size((110, 24)).columns
def one(v, n=None):
    # Up to three lines of the current terminal width, so a wider window or a smaller zoom shows more.
    n = n or max(110, 3 * cols() - 4)
    s = " ".join(str(v).split())
    return s if len(s) <= n else s[:n - 1] + "..."
def args(d):
    if not isinstance(d, dict) or not d:
        return ""
    for k in ("command", "file_path", "path", "pattern", "url", "query", "description", "prompt", "skill"):
        if k in d:
            return one(d[k])
    return one(next(iter(d.values())))
def ev(s):
    try:
        with open(os.environ["BS_LOG"], "a") as f:
            f.write("%d %s\n" % (time.time() * 1000, s))
    except (KeyError, OSError):
        pass
def alive():
    # Output after a finished turn: the CLI works again (BoosterAI shows Running instead of Watching).
    global watching
    if watching:
        watching = False
        ev("turn-start")
    try:
        os.utime(os.environ["BS_TIMING"], None)
    except (KeyError, OSError):
        pass
def say(s):
    print(s, flush=True)
    alive()
def turn_end(s):
    # The CLI may live on (claude -p waits for background tasks): BoosterAI shows Watching until it writes again or exits.
    global watching
    say(s)
    watching = True
    ev("turn-done")
def denied(cli, cmd):
    # BoosterAI reads this from the tab log and offers an allow-rule (AgyPermissions).
    say("  ! denied without asking: " + one(cmd or "?", 4000))
    ev("denied cli=%s cmd=%s" % (cli, base64.b64encode(cmd.encode()).decode()))
for line in sys.stdin:
    try:
        e = json.loads(line)
    except ValueError:
        sys.stdout.write(line)
        sys.stdout.flush()
        try:
            os.utime(os.environ["BS_TIMING"], None)
        except (KeyError, OSError):
            pass
        continue
    if not isinstance(e, dict):
        continue
    t = e.get("type")
    if t == "assistant":
        for c in (e.get("message") or {}).get("content") or []:
            if c.get("type") == "text" and c.get("text", "").strip():
                say(c["text"].strip())
            elif c.get("type") == "tool_use":
                say("> " + str(c.get("name")) + " " + args(c.get("input")))
    elif t == "user":
        c = (e.get("message") or {}).get("content")
        for r in c if isinstance(c, list) else []:
            if isinstance(r, dict) and r.get("is_error"):
                say("  ! " + one(r.get("content")))
    elif t == "result":
        turn_end("== " + ("turn error: " if e.get("is_error") else "turn done: ") + str(e.get("num_turns", "?")) + " turns, " + str(round((e.get("duration_ms") or 0) / 1000)) + " s")
    elif e.get("event") == "step_update":
        u = e.get("step_update") or {}
        if u.get("text_delta"):
            sys.stdout.write(u["text_delta"])
            sys.stdout.flush()
            alive()
        elif u.get("step_type") == "tool" and u.get("state") == "ACTIVE":
            i = u.get("tool_info") or {}
            p = i.get("parameters") or {}
            say("> " + str(i.get("name")) + " " + args(p))
            if i.get("name") == "run_command" and isinstance(p, dict):
                last = str(p.get("CommandLine", ""))
    elif e.get("event") == "result":
        r = e.get("result") or {}
        # A command agy could not ask about ends the turn, so it is the last one started.
        if any(isinstance(d, dict) and d.get("action") == "command" for d in r.get("denied_actions") or []):
            denied("agy", last)
        turn_end("== turn " + str(r.get("status", "done")).lower() + ", " + str(r.get("num_turns", "?")) + " turns")
'
  else
    # No python3: pull the text and tool names out of each JSON line with grep/sed.
    local l w=
    while IFS= read -r l; do
      case $l in
        '{'*) touch "$bs_timing" 2>/dev/null || true
          case $l in
            '{"type":"result"'*|'{"event":"result"'*) echo "== turn done"; bs_ev turn-done; w=1 ;;
            *) if [ -n "$w" ]; then w=; bs_ev turn-start; fi ;;
          esac
          printf '%s\n' "$l" | grep -oE '"(text|text_delta|name)":"([^"\\]|\\.)*"' | sed -e 's/^"[a-z_]*":"//' -e 's/"$//' -e 's/\\n/ /g' -e 's/\\"/"/g' ;;
        *) touch "$bs_timing" 2>/dev/null || true; printf '%s\n' "$l" ;;
      esac
    done
  fi
}

""";

    private int _next;

    public string Directory { get; }
    /// <summary>The Windows Terminal window all tabs and restarts of this session open in.</summary>
    public string WindowName { get; }

    /// <summary>A new session; <paramref name="live"/> (see Liveness) keeps old sessions whose tabs still run.</summary>
    public TabSession(string root, DateTime now, Func<int, long?>? live = null)
        : this(Path.Combine(root, now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)))
    {
        CleanUp(root, now, live);
        if (_defaultRoot is null && root == DefaultRoot) LegacyNames.RemoveOldLocalData(LegacyNames.OldLocalAppDir);
    }

    /// <summary>
    /// Opens a session directory, new or from an earlier BoosterAI run whose tabs still run; new tab ids follow
    /// the ones already there. The lib is rewritten for new tabs (running shells sourced theirs already).
    /// </summary>
    private TabSession(string directory)
    {
        Directory = directory;
        WindowName = "BoosterAI-" + Path.GetFileName(directory);
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllText(Path.Combine(Directory, "tab-lib.sh"), Lib.Replace("\r\n", "\n"));
        _next = System.IO.Directory.GetFiles(Directory, "tab*.log")
            .Select(f => int.TryParse(Path.GetFileNameWithoutExtension(f)[3..], CultureInfo.InvariantCulture, out var n) ? n : 0)
            .DefaultIfEmpty(0).Max();
    }

    public static TabSession Open(string directory) => new(directory);

    /// <summary>The paths the tab's shell uses for <paramref name="id"/>.</summary>
    public TabTrack Track(string id)
    {
        var dir = OperatingSystem.IsWindows() ? PathMapping.ToWslPath(Directory) : Directory;
        return new TabTrack(dir + "/tab-lib.sh", dir, id);
    }

    /// <summary>Remembers what tab <paramref name="id"/> was started with, so a later BoosterAI can find it again (FindRunning).</summary>
    public void SaveTab(string id, TabConfig tab)
    {
        try { File.WriteAllText(Path.Combine(Directory, id + ".tab.json"), JsonSerializer.Serialize(tab, ConfigJsonContext.Default.TabConfig)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* not found again after a restart */ }
    }

    /// <summary>A tab from an earlier BoosterAI run whose shell still runs.</summary>
    public sealed record RunningTab(string SessionDirectory, string Id, TabConfig Tab, TabLog Log);

    /// <summary>
    /// The tabs under <paramref name="root"/> whose shell is still alive (same PID and start time, no "exit"),
    /// newest session first. Nothing when liveness cannot be checked (<paramref name="live"/> null).
    /// </summary>
    public static List<RunningTab> FindRunning(string root, Func<int, long?>? live)
    {
        var found = new List<RunningTab>();
        if (live is null || !System.IO.Directory.Exists(root)) return found;
        try
        {
            foreach (var dir in System.IO.Directory.GetDirectories(root).OrderByDescending(Path.GetFileName, StringComparer.Ordinal))
                foreach (var meta in System.IO.Directory.GetFiles(dir, "tab*.tab.json").Order(StringComparer.Ordinal))
                {
                    var id = Path.GetFileName(meta)[..^".tab.json".Length];
                    var logPath = Path.Combine(dir, id + ".log");
                    if (!File.Exists(logPath)) continue;
                    var log = TabLog.Parse(File.ReadAllText(logPath));
                    if (!IsAlive(log, live)) continue;
                    if (JsonSerializer.Deserialize(File.ReadAllText(meta), ConfigJsonContext.Default.TabConfig) is { } tab)
                        found.Add(new RunningTab(dir, id, tab, log));
                }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        return found;
    }

    internal static bool IsAlive(TabLog log, Func<int, long?> live) =>
        log is { Pid: { } pid, Exited: false, StartTime: { } start } && live(pid) == start;

    /// <summary>Where sessions live; the test assembly points it at a temporary folder, away from the real tabs.</summary>
    public static string DefaultRoot
    {
        get => _defaultRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create), "BoosterAI", "sessions");
        internal set => _defaultRoot = value;
    }

    private static string? _defaultRoot;

    /// <summary>
    /// A new log id and the paths the tab's shell uses; null when the path cannot go into a tab script.
    /// Also writes the claude settings whose hooks log busy (prompt sent) and idle (turn done).
    /// </summary>
    public TabTrack? NewTrack(bool updateClis = false, bool autoClear = false)
    {
        var dir = OperatingSystem.IsWindows() ? PathMapping.ToWslPath(Directory) : Directory;
        if (dir.IndexOfAny([';', '\n', '\r']) >= 0) return null;
        var track = new TabTrack(dir + "/tab-lib.sh", dir, "tab" + Interlocked.Increment(ref _next), updateClis, autoClear);
        try { File.WriteAllText(Path.Combine(Directory, track.Id + ".claude.json"), HooksJson($"{dir}/{track.Id}.log")); }
        catch (IOException) { /* no hooks: the tab's activity stays unknown */ }
        return track;
    }

    public static string HooksJson(string log)
    {
        object Hook(string ev) => new[] { new { hooks = new[] { new { type = "command",
            command = $"printf '%s {ev}\\n' \"$(date +%s%3N)\" >> '{log.Replace("'", "'\\''")}'" } } } };
        return JsonSerializer.Serialize(new
        {
            hooks = new Dictionary<string, object>
            {
                // Not Notification: it also fires for a permission prompt in the middle of a turn.
                ["UserPromptSubmit"] = Hook("busy"), ["Stop"] = Hook("idle"),
            },
        });
    }

    /// <summary>Queues <paramref name="script"/> for the tab's bs_next (an in-place restart).</summary>
    public void QueueNext(string id, string script)
    {
        var path = Path.Combine(Directory, id + ".next");
        var temporary = path + "." + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, script.Replace("\r\n", "\n") + "\n");
            File.Move(temporary, path, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }

    /// <summary>
    /// Asks the tab's bs_pty to type <paramref name="text"/> into its CLI (within about a second), unless a key was pressed
    /// in the tab after <paramref name="sinceMs"/> (epoch-ms). The tab logs input-sent or input-skipped.
    /// </summary>
    public void RequestInput(string id, long sinceMs, string text)
    {
        var path = Path.Combine(Directory, id + ".input");
        var temporary = path + "." + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, sinceMs.ToString(CultureInfo.InvariantCulture) + " " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text)) + "\n");
            File.Move(temporary, path, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }

    public void CancelNext(string id) => File.Delete(Path.Combine(Directory, id + ".next"));

    public TabLog? ReadLog(string id)
    {
        try
        {
            var path = Path.Combine(Directory, id + ".log");
            return File.Exists(path) ? TabLog.Parse(File.ReadAllText(path)) : null;
        }
        catch (IOException) { return null; }
    }

    /// <summary>Uses explicit CLI hooks when available, otherwise the last terminal output time.</summary>
    public Activity ReadActivity(string id, TabLog? log, DateTime nowUtc)
    {
        if (log is null) return Activity.Unknown;
        if (log.Activity != Activity.Unknown) return log.Activity;
        if (log.WatchingSince is not null && log.CliExit is null) return Activity.Watching;

        try
        {
            var timing = Path.Combine(Directory, id + ".timing");
            if (!File.Exists(timing)) return Activity.Unknown;
            var lastOutput = File.GetLastWriteTimeUtc(timing);
            return nowUtc - lastOutput < TabStatus.OutputIdleTimeout ? Activity.Busy : Activity.Idle;
        }
        catch (IOException) { return Activity.Unknown; }
        catch (UnauthorizedAccessException) { return Activity.Unknown; }
    }

    /// <summary>How many lines of a tab's output the status popup shows.</summary>
    public const int OutputLines = 20;

    /// <summary>The most a read takes of each capture part (tab-lib.sh keeps each near 128 KiB).</summary>
    private const int OutputReadLimit = 256 * 1024;

    /// <summary>
    /// The last lines tab <paramref name="id"/> shows, replayed from its output capture (tabN.out.1 + tabN.out,
    /// written by tab-lib.sh's bs_cap) at the terminal size it noted (tabN.size, "rows cols"); null when nothing
    /// was captured, e.g. a tab started by an older BoosterAI.
    /// </summary>
    public IReadOnlyList<string>? ReadOutput(string id, int maxLines = OutputLines)
    {
        try
        {
            var path = Path.Combine(Directory, id + ".out");
            var older = ReadTail(path + ".1");
            var current = ReadTail(path);
            if (older is null && current is null) return null;
            var bytes = (older?.Bytes ?? []).Concat(current?.Bytes ?? []).ToArray();
            // Whole from the start only when no part was cut: no rotation and no read limit.
            var fromStart = older is null && current is { Cut: false };
            var (rows, cols) = ReadSize(Path.Combine(Directory, id + ".size"));
            return TerminalText.Render(TerminalText.Decode(bytes, fromStart), rows, cols, maxLines);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    private static (byte[] Bytes, bool Cut)? ReadTail(string path)
    {
        try
        {
            // The tab keeps appending (and rotating) while this reads.
            using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var cut = f.Length > OutputReadLimit;
            if (cut) f.Seek(-OutputReadLimit, SeekOrigin.End);
            using var m = new MemoryStream();
            f.CopyTo(m);
            return (m.ToArray(), cut);
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    /// <summary>The tab's terminal size from "rows cols"; 40 × 120 when unknown.</summary>
    internal static (int Rows, int Cols) ReadSize(string path)
    {
        try
        {
            var parts = File.Exists(path) ? File.ReadAllText(path).Split(' ', '\n', StringSplitOptions.RemoveEmptyEntries) : [];
            if (parts.Length >= 2 && int.TryParse(parts[0], CultureInfo.InvariantCulture, out var rows)
                && int.TryParse(parts[1], CultureInfo.InvariantCulture, out var cols) && rows > 0 && cols > 0)
                return (rows, cols);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return (40, 120);
    }

    /// <summary>Removes a tab's output capture; for a tab that is gone without its shell cleaning up (Lost).</summary>
    public void DeleteOutput(string id)
    {
        foreach (var name in new[] { id + ".out", id + ".out.1", id + ".size" })
        {
            try { File.Delete(Path.Combine(Directory, name)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Old sessions are only logs; keep two days, and longer while one of their tabs still runs.</summary>
    internal static void CleanUp(string root, DateTime now, Func<int, long?>? live)
    {
        try
        {
            foreach (var d in new DirectoryInfo(root).GetDirectories())
                if (d.CreationTime < now.AddDays(-2) && !HasLiveTab(d.FullName, live)) d.Delete(recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static bool HasLiveTab(string dir, Func<int, long?>? live) =>
        live is not null && System.IO.Directory.GetFiles(dir, "tab*.log").Any(f => IsAlive(TabLog.Parse(File.ReadAllText(f)), live));

    /// <summary>
    /// Start time of a live process in the tab's Linux, or null when there is none. Returns
    /// null as a whole when /proc cannot be read at all (then liveness is not judged).
    /// </summary>
    public static Func<int, long?>? Liveness(string wslDistro)
    {
        var proc = OperatingSystem.IsWindows() ? $@"\\wsl.localhost\{wslDistro}\proc" : "/proc";
        try { if (!File.Exists(Path.Combine(proc, "1", "stat"))) return null; }
        catch (Exception) { return null; }
        return pid =>
        {
            try { return TabStatus.ParseProcStartTime(File.ReadAllText(Path.Combine(proc, pid.ToString(CultureInfo.InvariantCulture), "stat"))); }
            catch (Exception) { return null; }
        };
    }
}
