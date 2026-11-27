using System.Diagnostics;
using System.Text.Json;

namespace BoosterAI;

/// <summary>
/// What Codex's app-server says about an account's earned rate-limit resets (#274): how many are available and whether the
/// account's ordinary usage is used up right now (<see cref="Exhausted"/>: a window at 100 %, a reached limit, or usage not allowed).
/// </summary>
public sealed record ResetCredits(int Available, bool Exhausted);

/// <summary>The answer to one redemption (#274); <see cref="Failed"/> covers no answer, an error and an unknown outcome.</summary>
public enum ResetOutcome { Reset, AlreadyRedeemed, NothingToReset, NoCredit, Failed }

/// <summary>
/// Reads and redeems Codex's earned rate-limit resets (#274) through <c>codex app-server</c>, the documented JSON-RPC interface:
/// <c>account/rateLimits/read</c> gives <c>rateLimitResetCredits.availableCount</c>, and <c>account/rateLimitResetCredit/consume</c>
/// with an idempotency key uses exactly one. Runs like <see cref="AccountLimitsProbe"/> (<c>bash -lic</c>, through <c>wsl.exe</c> on
/// Windows) with the account's <c>CODEX_HOME</c>. A small Python driver starts the server in its own process group, sends the two
/// requests, prints one marker line with the answer and always kills the group; an outer timeout bounds it, and
/// <see cref="Prerequisites.Run"/> kills the shell's tree if even that hangs. Claude and agy expose no such interface (see the issue).
/// </summary>
public static class CodexResets
{
    internal const string Marker = "BOOSTER_RESETS=";
    internal static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

    /// <summary>How often an account's count is read again on its own; a redemption reads it again at once.</summary>
    internal static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The driver: argv[1] "read" or "consume", argv[2] the account's directory ("" for the default), argv[3] the idempotency key.
    /// Prints the marker with {"result": ...} or {"error": "..."}; never the server's other output.
    /// </summary>
    internal const string Driver = """
import json, os, select, signal, subprocess, sys, time
mode, home = sys.argv[1], sys.argv[2]
key = sys.argv[3] if len(sys.argv) > 3 else ''
env = dict(os.environ)
if home: env['CODEX_HOME'] = os.path.expanduser(home)
deadline = time.monotonic() + 20
out = {'error': 'no answer'}
proc = None
def send(msg):
    proc.stdin.write((json.dumps(msg) + '\n').encode())
    proc.stdin.flush()
def answer(want):
    buf = b''
    while time.monotonic() < deadline:
        ready, _, _ = select.select([proc.stdout], [], [], max(0.05, deadline - time.monotonic()))
        if not ready: continue
        chunk = os.read(proc.stdout.fileno(), 65536)
        if not chunk: return None
        buf += chunk
        while b'\n' in buf:
            line, buf = buf.split(b'\n', 1)
            try: msg = json.loads(line)
            except Exception: continue
            if isinstance(msg, dict) and msg.get('id') == want and ('result' in msg or 'error' in msg): return msg
    return None
try:
    proc = subprocess.Popen(['codex', 'app-server'], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                            env=env, cwd='/tmp', start_new_session=True)
    send({'id': 1, 'method': 'initialize', 'params': {'clientInfo': {'name': 'boosterai', 'version': '1'}}})
    if answer(1) is not None:
        send({'method': 'initialized'})
        if mode == 'consume': send({'id': 2, 'method': 'account/rateLimitResetCredit/consume', 'params': {'idempotencyKey': key}})
        else: send({'id': 2, 'method': 'account/rateLimits/read', 'params': {'excludeResetCreditDetails': True}})
        msg = answer(2)
        if msg is None: out = {'error': 'no answer'}
        elif 'result' in msg: out = {'result': msg['result']}
        else:
            err = msg.get('error')
            text = err.get('message') if isinstance(err, dict) else None
            out = {'error': text[:300] if isinstance(text, str) else 'error'}
except Exception as e:
    out = {'error': type(e).__name__}
finally:
    if proc is not None:
        try: os.killpg(proc.pid, signal.SIGTERM)
        except Exception: pass
        try: proc.wait(timeout=2)
        except Exception:
            try: os.killpg(proc.pid, signal.SIGKILL)
            except Exception: pass
            try: proc.wait(timeout=2)
            except Exception: pass
print('BOOSTER_RESETS=' + json.dumps(out))
""";

    /// <summary>The bash that runs <see cref="Driver"/>; <paramref name="key"/> must be a GUID for a redemption.</summary>
    internal static string Script(string mode, string? directory, string? key = null)
    {
        if (mode is not ("read" or "consume")) throw new ArgumentException("Unknown mode.", nameof(mode));
        if (directory is not null && !Accounts.IsValidDirectory(directory))
            throw new ArgumentException("Invalid Codex account directory.", nameof(directory));
        if (mode == "consume" && !Guid.TryParseExact(key, "D", out _)) throw new ArgumentException("The key must be a GUID.", nameof(key));
        var dir = directory is null ? "''" : Accounts.ShellPath(directory);
        return $"cd /tmp && timeout -k 2 25 python3 - {mode} {dir} {(mode == "consume" ? key : "''")} <<'PY'\n{Driver}\nPY";
    }

    internal static ProcessStartInfo Command(string distro, string script) =>
        OperatingSystem.IsWindows()
            ? Prerequisites.Hidden("wsl.exe", ["-d", distro, "--exec", "bash", "-lic", script])
            : Prerequisites.Hidden("bash", ["-lic", script]);

    /// <summary>Reads the account's resets (blocking, bounded by <see cref="Limit"/>); null when unknown. Call it off the UI thread.</summary>
    public static ResetCredits? Read(string distro, string? directory)
    {
        var (started, code, output) = Prerequisites.Run(Command(distro, Script("read", directory)), Limit);
        return started && code == 0 ? ParseRead(output) : null;
    }

    /// <summary>Redeems one reset with <paramref name="key"/> (blocking); retrying with the same key never uses a second one.</summary>
    public static ResetOutcome Consume(string distro, string? directory, Guid key)
    {
        var (started, code, output) = Prerequisites.Run(Command(distro, Script("consume", directory, key.ToString("D"))), Limit);
        return started && code == 0 ? ParseConsume(output) : ResetOutcome.Failed;
    }

    private static JsonElement? Result(JsonDocument? doc)
    {
        if (doc is null) return null;
        var root = doc.RootElement;
        return root.ValueKind == JsonValueKind.Object && root.TryGetProperty("result", out var r) && r.ValueKind == JsonValueKind.Object ? r : null;
    }

    private static JsonDocument? Document(string? output)
    {
        var line = (output ?? "").Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith(Marker, StringComparison.Ordinal));
        if (line is null || line.Length > 64 * 1024) return null;
        try { return JsonDocument.Parse(line[Marker.Length..]); }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// The count and whether usage is used up, from <c>account/rateLimits/read</c>'s result. Null when the server gave no count
    /// (<c>rateLimitResetCredits</c> null: the service does not say) or the answer is broken; never a guessed zero.
    /// </summary>
    public static ResetCredits? ParseRead(string? output)
    {
        using var doc = Document(output);
        if (Result(doc) is not { } result) return null;
        if (!result.TryGetProperty("rateLimitResetCredits", out var credits) || credits.ValueKind != JsonValueKind.Object
            || !credits.TryGetProperty("availableCount", out var count) || count.ValueKind != JsonValueKind.Number
            || !count.TryGetInt64(out var available) || available is < 0 or > 1000) return null;
        var exhausted = result.TryGetProperty("ordinaryUsageAllowed", out var allowed) && allowed.ValueKind == JsonValueKind.False;
        if (result.TryGetProperty("rateLimits", out var limits) && limits.ValueKind == JsonValueKind.Object)
        {
            exhausted |= limits.TryGetProperty("rateLimitReachedType", out var reached) && reached.ValueKind == JsonValueKind.String;
            foreach (var name in new[] { "primary", "secondary" })
                exhausted |= limits.TryGetProperty(name, out var w) && w.ValueKind == JsonValueKind.Object
                    && w.TryGetProperty("usedPercent", out var used) && used.ValueKind == JsonValueKind.Number
                    && used.TryGetDouble(out var percent) && percent >= 100;
        }
        return new ResetCredits((int)available, exhausted);
    }

    /// <summary>The outcome of <c>account/rateLimitResetCredit/consume</c>; anything else, an error or no answer is <see cref="ResetOutcome.Failed"/>.</summary>
    public static ResetOutcome ParseConsume(string? output)
    {
        using var doc = Document(output);
        if (Result(doc) is not { } result || !result.TryGetProperty("outcome", out var outcome) || outcome.ValueKind != JsonValueKind.String)
            return ResetOutcome.Failed;
        return outcome.GetString() switch
        {
            "reset" => ResetOutcome.Reset,
            "alreadyRedeemed" => ResetOutcome.AlreadyRedeemed,
            "nothingToReset" => ResetOutcome.NothingToReset,
            "noCredit" => ResetOutcome.NoCredit,
            _ => ResetOutcome.Failed,
        };
    }

    /// <summary>Whether the redemption used a reset (now or with the same key before): only then is the count read again as success.</summary>
    public static bool Succeeded(ResetOutcome outcome) => outcome is ResetOutcome.Reset or ResetOutcome.AlreadyRedeemed;
}
