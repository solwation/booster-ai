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
/// <paramref name="Percent"/> is that higher use, rounded and kept within 0–100, for the closed box's meter (#202);
/// <see cref="RemainingPercent"/> and <see cref="RemainingCaption"/> give the remaining share for the status column (#207).
/// </summary>
public sealed record LimitIndicator(string Caption, bool Warn, string Tip, string Name, string Line = "", double Percent = 0)
{
    public const double WarnAt = 80;

    /// <summary>Remaining share of the tightest known window (0–100 %), for the status column (#207).</summary>
    public double RemainingPercent => Math.Clamp(100 - Percent, 0, 100);

    /// <summary>Localized remaining text for the status column (#207): "38 % kvar" (sv) or "38% left" (en).</summary>
    public string RemainingCaption => Loc.Format("account.limit.remaining", Loc.Number(RemainingPercent, "0"));

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
        if (limits.At is { } at) lines.Add(LimitText.Age(at, now));
        var name = Loc.Format("account.limit.chip.name", number) + (warn ? ", " + Loc.Get("account.limit.near") : "");
        // The status popup's one line: "5 h 37 % · Vecka 62 % · avläst för 8 min sedan"; an unknown window reads "Okänd".
        string Part(string label, LimitWindow? w) => label + " " + (w is null ? Loc.Get("account.limit.unknown") : Loc.Format("account.limit.chip", Loc.Number(w.UsedPercent, "0")));
        var line = Part(session, limits.Session) + " · " + Part(Loc.Get("account.limit.week"), limits.Week)
            + (limits.At is { } when ? " · " + LimitText.Age(when, now) : "");
        return new LimitIndicator(Loc.Format("account.limit.chip", number), warn, string.Join("\n", lines), name, line, Math.Clamp(percent, 0, 100));
    }
}

/// <summary>The texts of the Accounts page's limit lines (#199): "37 % använt · återställs om 4 h 20 min (14:20)", "Okänd", credits and times.</summary>
public static class LimitText
{
    public static string Used(LimitWindow? window, DateTimeOffset now) => window is null ? Loc.Get("account.limit.unknown")
        : Loc.Format("account.limit.used", Loc.Number(window.UsedPercent, "0"), Resets(window.ResetsAt, now));

    /// <summary>Codex's credits: unlimited, a balance, or none; empty when the agent reported nothing (Claude never does).</summary>
    public static string Credits(LimitCredits? credits) => credits switch
    {
        null => "",
        { Unlimited: true } => Loc.Format("account.limit.credits", Loc.Get("account.limit.credits.unlimited")),
        { HasCredits: true, Balance: { } balance } => Loc.Format("account.limit.credits", balance),
        { HasCredits: true } => Loc.Format("account.limit.credits", Loc.Get("account.limit.unknown")),
        _ => Loc.Format("account.limit.credits", Loc.Get("account.limit.credits.none")),
    };

    /// <summary>A reset time (#208): "om 1 h 10 min (15:30)" within 24 hours, else "i morgon 09:00", "fredag 10:00", "5 dec 10:00".</summary>
    public static string Resets(DateTimeOffset time, DateTimeOffset now) => HumanTime.Until(time, now);

    /// <summary>When the agent reported the values (#208): "avläst nyss", "avläst för 8 min sedan", "avläst i går 18:10".</summary>
    public static string Age(DateTimeOffset at, DateTimeOffset now) => Loc.Format("account.limit.as", HumanTime.Since(at, now));
}

/// <summary>
/// Reads an account's plan limits in the tab's environment (#199, #206), like <see cref="AccountStatusProbe"/>: <c>bash -lic</c>,
/// through <c>wsl.exe</c> on Windows. Codex: the last <c>token_count</c> event of the newest rollout under <c>$CODEX_HOME/sessions</c>.
/// Claude and AGY: the snapshot that BoosterAI's status line (<see cref="ClaudeStatusLine"/>, <see cref="AgyStatusLine"/>) saved in
/// the account's directory. Only read: no network, no credentials. The script prints one projected JSON line, never file contents;
/// anything else is Unknown.
/// </summary>
public static class AccountLimitsProbe
{
    internal static readonly TimeSpan Limit = TimeSpan.FromSeconds(12);
    internal const string Marker = "BOOSTER_LIMITS=";

    /// <summary>Agents BoosterAI can read plan or quota limits for (#199, #206): multi-account claude/codex and single-account agy.</summary>
    public static bool Supports(string llm) => llm is "claude" or "codex" or "agy";

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
def snapshot(home):
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
    elif agent == 'agy': out = snapshot(home or os.environ.get('BOOSTERAI_AGY_HOME') or os.path.expanduser('~/.gemini/antigravity-cli'))
    else: out = snapshot(home or os.environ.get('CLAUDE_CONFIG_DIR') or os.path.expanduser('~/.claude'))
except Exception: out = {}
print('BOOSTER_LIMITS=' + json.dumps(out))
""";

    /// <summary>The bash that runs <see cref="Reader"/>; the inner timeout keeps a slow disk from outliving the check.</summary>
    public static string Script(string llm, string? directory)
    {
        if (!Supports(llm)) throw new ArgumentException("The agent has no plan limits.", nameof(llm));
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

/// <summary>
/// The status line BoosterAI gives its interactive AGY tabs (#206) through their <c>--settings</c> file. AGY passes its
/// documented status line JSON on stdin, including <c>quota</c> buckets (<c>remaining_fraction</c>, <c>reset_time</c>,
/// <c>reset_in_seconds</c>, <c>disabled</c>). The script saves only the highest used percentage of active short (≤ 24 h) and
/// weekly buckets to <c>boosterai-limits.json</c> under <c>~/.gemini/antigravity-cli</c> (no email, transcripts or credentials),
/// then chains the user's own configured <c>statusLine</c> if any; without one it prints nothing and AGY keeps its built-in
/// status line through <c>stack_with_default: true</c>.
/// </summary>
public static class AgyStatusLine
{
    /// <summary>The snapshot's file name in AGY's directory; <see cref="AccountLimitsProbe"/> reads it.</summary>
    public const string SnapshotFile = "boosterai-limits.json";

    /// <summary>Written beside tab-lib.sh in each session directory (TabSession).</summary>
    public const string ScriptFile = "agy-statusline.py";

    /// <summary>The <c>statusLine.command</c> for a session directory (a Linux path); the directory is single-quoted for the shell.</summary>
    public static string Command(string sessionDir) => "python3 '" + sessionDir.Replace("'", "'\\''") + "/" + ScriptFile + "'";

    public const string Script = """
# BoosterAI AGY status line (#206): saves quota usage from AGY's status line input, then runs the user's own status line if configured.
import datetime, json, os, subprocess, sys, time
raw = sys.stdin.buffer.read(1048577)
try: data = json.loads(raw) if len(raw) <= 1048576 else None
except Exception: data = None
if not isinstance(data, dict): data = {}
home = os.environ.get('BOOSTERAI_AGY_HOME') or os.path.expanduser('~/.gemini/antigravity-cli')
def num(v):
    return v if isinstance(v, (int, float)) and not isinstance(v, bool) else None
def parse_reset(bucket, now):
    rt = bucket.get('reset_time')
    if isinstance(rt, str) and rt.strip():
        try:
            dt = datetime.datetime.fromisoformat(rt.strip().replace('Z', '+00:00'))
            if dt.tzinfo is None: dt = dt.replace(tzinfo=datetime.timezone.utc)
            ts = int(dt.timestamp())
            if ts > 0: return ts
        except Exception: pass
    sec = num(bucket.get('reset_in_seconds'))
    if sec is not None and sec > 0: return int(now + sec)
    return None
def save():
    quota = data.get('quota')
    if not isinstance(quota, dict) or not os.path.isdir(home): return
    now = int(time.time())
    snap = {}
    for bid, b in quota.items():
        if not isinstance(bid, str) or not isinstance(b, dict) or b.get('disabled') is True: continue
        rem = num(b.get('remaining_fraction'))
        if rem is None or rem < 0 or rem > 1: continue
        resets_at = parse_reset(b, now)
        if resets_at is None: continue
        used = round(max(0.0, min(100.0, (1.0 - rem) * 100.0)), 2)
        low = bid.lower()
        rsec = num(b.get('reset_in_seconds'))
        is_week = ('week' in low or '7d' in low) or (not any(k in low for k in ('5h', 'hour', 'session')) and ((rsec is not None and rsec > 86400) or (resets_at - now > 86400)))
        key = 'seven_day' if is_week else 'five_hour'
        prev = snap.get(key)
        if prev is None or used > prev['used_percentage'] or (used == prev['used_percentage'] and resets_at < prev['resets_at']):
            snap[key] = {'used_percentage': used, 'resets_at': resets_at}
    if not snap: return
    path = os.path.join(home, 'boosterai-limits.json')
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
    path = os.path.join(home, 'settings.json')
    try:
        with open(path, 'rb') as f: settings = json.loads(f.read(1048576))
    except Exception: return None
    if not isinstance(settings, dict) or 'statusLine' not in settings: return None
    line = settings['statusLine']
    if isinstance(line, dict) and line.get('enabled', True) is not False and line.get('type', 'command') == 'command' and isinstance(line.get('command'), str) and line['command'].strip():
        cmd = line['command'].strip()
        return os.path.expanduser(cmd) if cmd.startswith('~') else cmd
    return None
try: save()
except Exception: pass
if os.environ.get('BOOSTERAI_STATUSLINE'): sys.exit(0)
try: command = own()
except Exception: command = None
if command:
    env = dict(os.environ, BOOSTERAI_STATUSLINE='1')
    try:
        p = subprocess.Popen(['sh', '-c', command], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, env=env, start_new_session=True)
        try: out = p.communicate(raw, timeout=4.5)[0]
        except subprocess.TimeoutExpired:
            os.killpg(p.pid, 9)
            out = p.communicate()[0]
        sys.stdout.buffer.write(out[:65536])
    except Exception: pass
""";
}
