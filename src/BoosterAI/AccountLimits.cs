using System.Globalization;
using System.Text.Json;

namespace BoosterAI;

/// <summary>One plan window (#199): how much of it is used and when it starts over. <see cref="Minutes"/> is its length when the agent says.</summary>
public sealed record LimitWindow(double UsedPercent, DateTimeOffset ResetsAt, int? Minutes = null);

/// <summary>Codex's credits beside the plan windows (#199), as its last turn reported them.</summary>
public sealed record LimitCredits(bool HasCredits, bool Unlimited, string? Balance);

/// <summary>
/// What BoosterAI last read about an account's plan limits (#199). A window that is null is unknown: never read, missing, invalid or
/// already reset. <see cref="At"/> is when the agent reported the values (not when BoosterAI read them); null when nothing was read.
/// </summary>
public sealed record AccountLimits(LimitWindow? Session, LimitWindow? Week, LimitCredits? Credits, DateTimeOffset? At)
{
    public static readonly AccountLimits Unknown = new(null, null, null, null);

    /// <summary>Drops the windows whose reset time has passed: their old percentage says nothing about the new window.</summary>
    public AccountLimits AsOf(DateTimeOffset now) => this with
    {
        Session = Session is { } s && s.ResetsAt > now ? s : null,
        Week = Week is { } w && w.ResetsAt > now ? w : null,
    };
}

/// <summary>
/// The small plan-limit indicator beside an agent in the main window's Agent box (#200): the higher of the 5-hour and weekly use
/// as "62 %", a warning cue from <see cref="WarnAt"/> percent, and a tooltip with both windows. Null when no window is known.
/// </summary>
public sealed record LimitIndicator(string Caption, bool Warn, string Tip, string Name)
{
    public const double WarnAt = 80;

    public static LimitIndicator? For(AccountLimits limits, DateTimeOffset now)
    {
        limits = limits.AsOf(now);
        var known = new[] { limits.Session, limits.Week }.OfType<LimitWindow>().ToList();
        if (known.Count == 0) return null;
        var percent = Math.Round(known.Max(w => w.UsedPercent), MidpointRounding.AwayFromZero);
        var warn = percent >= WarnAt;
        var number = Loc.Number(percent, "0");
        var session = Loc.Format("account.limit.hours", Loc.Number((limits.Session?.Minutes ?? 300) / 60.0, "0.#"));
        var lines = new List<string>
        {
            Loc.Get("account.limit.title"),
            session + ": " + LimitText.Used(limits.Session, now),
            Loc.Get("account.limit.week") + ": " + LimitText.Used(limits.Week, now),
        };
        if (limits.At is { } at) lines.Add(Loc.Format("account.limit.as", LimitText.When(at, now)));
        var name = Loc.Format("account.limit.chip.name", number) + (warn ? ", " + Loc.Get("account.limit.near") : "");
        return new LimitIndicator(Loc.Format("account.limit.chip", number), warn, string.Join("\n", lines), name);
    }
}

/// <summary>The texts of the Accounts page's limit lines (#199): "37 % använt · återställs 14:20", "Okänd", credits and times.</summary>
public static class LimitText
{
    public static string Used(LimitWindow? window, DateTimeOffset now) => window is null ? Loc.Get("account.limit.unknown")
        : Loc.Format("account.limit.used", Loc.Number(window.UsedPercent, "0"), When(window.ResetsAt, now));

    /// <summary>Codex's credits: unlimited, a balance, or none; empty when the agent reported nothing (Claude never does).</summary>
    public static string Credits(LimitCredits? credits) => credits switch
    {
        null => "",
        { Unlimited: true } => Loc.Format("account.limit.credits", Loc.Get("account.limit.credits.unlimited")),
        { HasCredits: true, Balance: { } balance } => Loc.Format("account.limit.credits", balance),
        { HasCredits: true } => Loc.Format("account.limit.credits", Loc.Get("account.limit.unknown")),
        _ => Loc.Format("account.limit.credits", Loc.Get("account.limit.credits.none")),
    };

    /// <summary>A time in local time: "14:20" today, "i morgon 14:20", "i går 14:20", a weekday within a week, else the date.</summary>
    public static string When(DateTimeOffset time, DateTimeOffset now)
    {
        var local = time.ToLocalTime();
        var clock = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        return (local.Date - now.ToLocalTime().Date).Days switch
        {
            0 => clock,
            1 => Loc.Format("account.limit.tomorrow", clock),
            -1 => Loc.Format("account.limit.yesterday", clock),
            > 1 and < 7 or < -1 and > -7 => Loc.Get("account.limit.day." + (int)local.DayOfWeek) + " " + clock,
            _ => local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " " + clock,
        };
    }
}

/// <summary>
/// Reads an account's plan limits in the tab's environment (#199), like <see cref="AccountStatusProbe"/>: <c>bash -lic</c>, through
/// <c>wsl.exe</c> on Windows. Codex: the last <c>token_count</c> event of the newest rollout under <c>$CODEX_HOME/sessions</c>.
/// Claude: the snapshot that BoosterAI's status line (<see cref="ClaudeStatusLine"/>) saved in the account's directory. Only
/// read: no network, no credentials. The script prints one projected JSON line, never file contents; anything else is Unknown.
/// </summary>
public static class AccountLimitsProbe
{
    internal static readonly TimeSpan Limit = TimeSpan.FromSeconds(12);
    internal const string Marker = "BOOSTER_LIMITS=";

    /// <summary>
    /// Python run by <see cref="Script"/>: argv[1] the agent, argv[2] the account's directory (the default account: the agent's
    /// variable or its usual directory). Reads at most the newest few rollouts' last 4 MB; prints the marker line once.
    /// </summary>
    internal const string Reader = """
import json, os, sys
def num(v):
    return v if isinstance(v, (int, float)) and not isinstance(v, bool) else None
def window(w, used, minutes):
    if not isinstance(w, dict): return None
    out = {'used': num(w.get(used)), 'resets_at': num(w.get('resets_at'))}
    if minutes: out['minutes'] = num(w.get(minutes))
    return out if out['used'] is not None and out['resets_at'] is not None else None
def last_count(path):
    with open(path, 'rb') as f:
        f.seek(0, 2)
        size = f.tell()
        start = max(0, size - 4000000)
        f.seek(start)
        lines = f.read().split(b'\n')
    if start > 0: lines = lines[1:]
    for line in reversed(lines):
        if b'"token_count"' not in line: continue
        try: event = json.loads(line)
        except Exception: continue
        payload = event.get('payload') if isinstance(event, dict) else None
        if not isinstance(payload, dict) or payload.get('type') != 'token_count': continue
        limits = payload.get('rate_limits')
        if not isinstance(limits, dict): continue
        credits = limits.get('credits')
        out = {'at': event.get('timestamp') if isinstance(event.get('timestamp'), str) else None,
               'primary': window(limits.get('primary'), 'used_percent', 'window_minutes'),
               'secondary': window(limits.get('secondary'), 'used_percent', 'window_minutes')}
        if isinstance(credits, dict):
            balance = credits.get('balance')
            out['credits'] = {'has_credits': credits.get('has_credits') is True, 'unlimited': credits.get('unlimited') is True,
                              'balance': balance[:40] if isinstance(balance, str) else None}
        return out
    return None
def codex(home):
    files = []
    stack = [os.path.join(home, 'sessions')]
    while stack and len(files) < 20000:
        try: entries = list(os.scandir(stack.pop()))
        except OSError: continue
        for e in entries:
            try:
                if e.is_dir(follow_symlinks=False): stack.append(e.path)
                elif e.name.startswith('rollout-') and e.name.endswith('.jsonl'): files.append((e.stat().st_mtime, e.path))
            except OSError: pass
    for _, path in sorted(files, reverse=True)[:5]:
        try: found = last_count(path)
        except OSError: continue
        if found: return found
    return {}
def claude(home):
    with open(os.path.join(home, 'boosterai-limits.json'), 'rb') as f: raw = f.read(65537)
    if len(raw) > 65536: return {}
    data = json.loads(raw)
    if not isinstance(data, dict): return {}
    return {'at': num(data.get('captured_at')), 'five_hour': window(data.get('five_hour'), 'used_percentage', None),
            'seven_day': window(data.get('seven_day'), 'used_percentage', None)}
try:
    agent = sys.argv[1]
    home = sys.argv[2] if len(sys.argv) > 2 and sys.argv[2] else None
    if agent == 'codex': out = codex(home or os.environ.get('CODEX_HOME') or os.path.expanduser('~/.codex'))
    else: out = claude(home or os.environ.get('CLAUDE_CONFIG_DIR') or os.path.expanduser('~/.claude'))
except Exception: out = {}
print('BOOSTER_LIMITS=' + json.dumps(out))
""";

    /// <summary>The bash that runs <see cref="Reader"/>; the inner timeout keeps a slow disk from outliving the check.</summary>
    public static string Script(string llm, string? directory)
    {
        if (!Accounts.Supports(llm)) throw new ArgumentException("The agent has no separate accounts.", nameof(llm));
        var dir = directory is null ? "''" : Accounts.ShellPath(directory);
        return $"timeout -k 1 9 python3 - {llm} {dir} <<'PY'\n{Reader}\nPY";
    }

    public static System.Diagnostics.ProcessStartInfo Command(string distro, string llm, string? directory) =>
        OperatingSystem.IsWindows()
            ? Prerequisites.Hidden("wsl.exe", ["-d", distro, "--exec", "bash", "-lic", Script(llm, directory)])
            : Prerequisites.Hidden("bash", ["-lic", Script(llm, directory)]);

    /// <summary>Runs the read (blocking, bounded by <see cref="Limit"/>); call it off the UI thread.</summary>
    public static AccountLimits Read(string distro, string llm, string? directory)
    {
        var (started, code, output) = Prerequisites.Run(Command(distro, llm, directory), Limit);
        return started && code == 0 ? Parse(llm, output) : AccountLimits.Unknown;
    }

    /// <summary>
    /// The script's marker line as limits. Startup files may print around it; a missing line, broken JSON or a field of the wrong
    /// type is Unknown for what it concerns, never zero. Expired windows are dropped later (<see cref="AccountLimits.AsOf"/>).
    /// </summary>
    public static AccountLimits Parse(string llm, string? output)
    {
        var line = (output ?? "").Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith(Marker, StringComparison.Ordinal));
        if (line is null || line.Length > 8192) return AccountLimits.Unknown;
        try
        {
            using var doc = JsonDocument.Parse(line[Marker.Length..]);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return AccountLimits.Unknown;
            return llm == "codex" ? ParseCodex(root) : ParseClaude(root);
        }
        catch (JsonException) { return AccountLimits.Unknown; }
    }

    private static AccountLimits ParseClaude(JsonElement root)
    {
        var at = Number(root, "at") is { } s && s > 0 ? FromSeconds(s) : null;
        return new AccountLimits(Window(root, "five_hour"), Window(root, "seven_day"), null, at);
    }

    /// <summary>Codex names its windows primary and secondary; their length decides which is the short one and which the week.</summary>
    private static AccountLimits ParseCodex(JsonElement root)
    {
        LimitWindow? session = null, week = null;
        foreach (var name in new[] { "primary", "secondary" })
        {
            if (Window(root, name) is not { Minutes: { } minutes } w) continue;
            if (minutes > 0 && minutes <= 24 * 60) session ??= w;
            else if (minutes >= 6 * 24 * 60 && minutes <= 8 * 24 * 60) week ??= w;
        }
        LimitCredits? credits = null;
        if (root.TryGetProperty("credits", out var c) && c.ValueKind == JsonValueKind.Object)
            credits = new LimitCredits(Bool(c, "has_credits"), Bool(c, "unlimited"),
                c.TryGetProperty("balance", out var b) && b.ValueKind == JsonValueKind.String && Clean(b.GetString()) is { } balance ? balance : null);
        DateTimeOffset? at = root.TryGetProperty("at", out var a) && a.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(a.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t : null;
        return new AccountLimits(session, week, credits, at);
    }

    private static LimitWindow? Window(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var w) || w.ValueKind != JsonValueKind.Object) return null;
        if (Number(w, "used") is not { } used || used < 0 || used > 1000 || Number(w, "resets_at") is not { } resets || resets <= 0) return null;
        var minutes = Number(w, "minutes") is { } m && m > 0 && m < int.MaxValue ? (int?)m : null;
        return FromSeconds(resets) is { } at ? new LimitWindow(used, at, minutes) : null;
    }

    private static DateTimeOffset? FromSeconds(double seconds) =>
        seconds is > 0 and < 253402300799 ? DateTimeOffset.FromUnixTimeSeconds((long)seconds) : null;

    private static double? Number(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) && double.IsFinite(d) ? d : null;

    private static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    /// <summary>Single line, printable, bounded: the balance comes from a file and is only displayed.</summary>
    private static string? Clean(string? value)
    {
        var text = new string((value ?? "").Where(ch => !char.IsControl(ch)).ToArray()).Trim();
        return text.Length is 0 or > 40 ? null : text;
    }
}

/// <summary>
/// The status line BoosterAI gives its interactive Claude tabs (#199) through their <c>--settings</c> file. Claude passes the status
/// line JSON on stdin; the script saves only <c>rate_limits.five_hour</c>/<c>seven_day</c> (used percentage, reset time) and the
/// time to <c>boosterai-limits.json</c> in the account's directory (<c>$CLAUDE_CONFIG_DIR</c>, else <c>~/.claude</c>), then runs
/// the status line the user configured (project <c>.claude/settings.local.json</c>, <c>.claude/settings.json</c>, then the account's
/// <c>settings.json</c>) with the same stdin and prints its output, so the user's status line stays as it was. Without one it
/// prints nothing. Every step is bounded and a failure in it only skips that step.
/// </summary>
public static class ClaudeStatusLine
{
    /// <summary>The snapshot's file name in the account's directory; <see cref="AccountLimitsProbe"/> reads it.</summary>
    public const string SnapshotFile = "boosterai-limits.json";

    /// <summary>Written beside tab-lib.sh in each session directory (TabSession).</summary>
    public const string ScriptFile = "statusline.py";

    /// <summary>The <c>statusLine.command</c> for a session directory (a Linux path); the directory is single-quoted for the shell.</summary>
    public static string Command(string sessionDir) => "python3 '" + sessionDir.Replace("'", "'\\''") + "/" + ScriptFile + "'";

    public const string Script = """
# BoosterAI status line (#199): saves the plan limits from Claude's status line input, then runs the user's own status line.
import json, os, subprocess, sys, time
raw = sys.stdin.buffer.read(1048577)
try: data = json.loads(raw) if len(raw) <= 1048576 else None
except Exception: data = None
if not isinstance(data, dict): data = {}
home = os.environ.get('CLAUDE_CONFIG_DIR') or os.path.expanduser('~/.claude')
def num(v):
    return v if isinstance(v, (int, float)) and not isinstance(v, bool) else None
def save():
    limits = data.get('rate_limits')
    if not isinstance(limits, dict) or not os.path.isdir(home): return
    snap = {}
    for key in ('five_hour', 'seven_day'):
        w = limits.get(key)
        if isinstance(w, dict) and num(w.get('used_percentage')) is not None and num(w.get('resets_at')) is not None:
            snap[key] = {'used_percentage': w['used_percentage'], 'resets_at': w['resets_at']}
    if not snap: return
    path = os.path.join(home, 'boosterai-limits.json')
    now = int(time.time())
    try:
        with open(path, 'rb') as f: old = json.loads(f.read(65536))
        if {k: v for k, v in old.items() if k != 'captured_at'} == snap and 0 <= now - old.get('captured_at', 0) < 30: return
    except Exception: pass
    snap['captured_at'] = now
    tmp = '%s.%d.tmp' % (path, os.getpid())
    try:
        with open(tmp, 'w') as f: json.dump(snap, f)
        os.replace(tmp, path)
    finally:
        if os.path.exists(tmp): os.remove(tmp)
def own():
    workspace = data.get('workspace') if isinstance(data.get('workspace'), dict) else {}
    project = workspace.get('project_dir') or data.get('cwd') or os.getcwd()
    files = [os.path.join(home, 'settings.json')]
    if isinstance(project, str): files = [os.path.join(project, '.claude', n) for n in ('settings.local.json', 'settings.json')] + files
    for path in files:
        try:
            with open(path, 'rb') as f: settings = json.loads(f.read(1048576))
        except Exception: continue
        if not isinstance(settings, dict) or 'statusLine' not in settings: continue
        line = settings['statusLine']
        if isinstance(line, dict) and line.get('type', 'command') == 'command' and isinstance(line.get('command'), str) and line['command'].strip():
            return line['command']
        return None
    return None
try: save()
except Exception: pass
if os.environ.get('BOOSTERAI_STATUSLINE'): sys.exit(0)
try: command = own()
except Exception: command = None
if command:
    env = dict(os.environ, BOOSTERAI_STATUSLINE='1')
    try:
        p = subprocess.Popen(['bash', '-c', command], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, env=env, start_new_session=True)
        try: out = p.communicate(raw, timeout=10)[0]
        except subprocess.TimeoutExpired:
            os.killpg(p.pid, 9)
            out = p.communicate()[0]
        sys.stdout.buffer.write(out[:65536])
    except Exception: pass
""";
}
