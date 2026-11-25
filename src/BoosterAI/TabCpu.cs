using System.Collections.Concurrent;
using System.Globalization;

namespace BoosterAI;

/// <summary>One process from /proc/&lt;pid&gt;/stat: its parent, program name (comm, no arguments), own CPU time and start time.</summary>
/// <param name="Ticks">utime + stime in clock ticks: the process's own CPU time, without its waited-for children.</param>
/// <param name="StartTime">Clock ticks after boot when it started (field 22), which tells a reused PID apart.</param>
public readonly record struct ProcEntry(int PPid, string Name, long Ticks, long StartTime);

/// <summary>
/// All processes of the tabs' Linux at one moment (#215): one scan of /proc per status poll, shared by every tab, with
/// /proc/uptime as the scan's own clock. On Windows the scan reads <c>\\wsl.localhost\&lt;distro&gt;\proc</c>, as the Lost check does.
/// </summary>
public sealed class ProcSnapshot
{
    /// <summary>USER_HZ, the unit of /proc's CPU and start times; 100 on every Linux ABI WSL and desktops run.</summary>
    public const int TicksPerSecond = 100;

    public IReadOnlyDictionary<int, ProcEntry> Processes { get; }
    /// <summary>When the scan began, in clock ticks after boot (/proc/uptime).</summary>
    public long Uptime { get; }

    private Dictionary<int, List<int>>? _children;

    public ProcSnapshot(IReadOnlyDictionary<int, ProcEntry> processes, long uptime)
    {
        Processes = processes;
        Uptime = uptime;
    }

    /// <summary>Where the tabs' /proc is: the distribution's share on Windows, the local one on Linux.</summary>
    public static string Root(string wslDistro) => OperatingSystem.IsWindows() ? $@"\\wsl.localhost\{wslDistro}\proc" : "/proc";

    /// <summary>Fields of /proc/&lt;pid&gt;/stat; the program name sits in parentheses and may itself hold spaces and parentheses.</summary>
    public static ProcEntry? ParseStat(string stat)
    {
        var open = stat.IndexOf('(');
        var close = stat.LastIndexOf(')');
        if (open < 0 || close < open) return null;
        var f = stat[(close + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // f[0] is field 3 (state): ppid is field 4, utime/stime fields 14/15, starttime field 22.
        if (f.Length <= 19) return null;
        static long? L(string s) => long.TryParse(s, CultureInfo.InvariantCulture, out var v) ? v : null;
        if (!int.TryParse(f[1], CultureInfo.InvariantCulture, out var ppid) || L(f[11]) is not { } utime || L(f[12]) is not { } stime
            || L(f[19]) is not { } start) return null;
        return new ProcEntry(ppid, stat[(open + 1)..close], utime + stime, start);
    }

    /// <summary>The first number of /proc/uptime (seconds since boot) in clock ticks.</summary>
    public static long? ParseUptime(string text)
    {
        var first = text.Split(' ', '\n').FirstOrDefault();
        return double.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0
            ? (long)Math.Round(seconds * TicksPerSecond) : null;
    }

    /// <summary>
    /// Reads every process under <paramref name="proc"/> once; null when /proc cannot be read at all. A process that ends
    /// during the scan is left out. Over \\wsl.localhost each read is a round trip, so there several run at once.
    /// </summary>
    public static ProcSnapshot? Read(string proc)
    {
        try
        {
            if (ParseUptime(File.ReadAllText(Path.Combine(proc, "uptime"))) is not { } uptime) return null;
            var pids = new List<int>();
            foreach (var dir in Directory.EnumerateDirectories(proc))
                if (Path.GetFileName(dir) is { Length: > 0 } name && name.All(char.IsAsciiDigit)
                    && int.TryParse(name, CultureInfo.InvariantCulture, out var pid)) pids.Add(pid);
            var found = new ConcurrentDictionary<int, ProcEntry>();
            Parallel.ForEach(pids, new ParallelOptions { MaxDegreeOfParallelism = OperatingSystem.IsWindows() ? 8 : 1 }, pid =>
            {
                try
                {
                    if (ParseStat(File.ReadAllText(Path.Combine(proc, pid.ToString(CultureInfo.InvariantCulture), "stat"))) is { } entry)
                        found[pid] = entry;
                }
                catch (Exception) { /* ended during the scan */ }
            });
            return new ProcSnapshot(found, uptime);
        }
        catch (Exception) { return null; }
    }

    /// <summary><paramref name="root"/> and all its live descendants (by parent PID); empty when it is not there.</summary>
    public IReadOnlyList<int> Subtree(int root)
    {
        if (!Processes.ContainsKey(root)) return [];
        if (_children is null)
        {
            var children = new Dictionary<int, List<int>>();
            foreach (var (pid, p) in Processes)
            {
                if (!children.TryGetValue(p.PPid, out var list)) children[p.PPid] = list = [];
                list.Add(pid);
            }
            _children = children;
        }
        var result = new List<int>();
        var stack = new Stack<int>([root]);
        var seen = new HashSet<int>();
        while (stack.Count > 0)
        {
            var pid = stack.Pop();
            if (!seen.Add(pid)) continue;
            result.Add(pid);
            if (_children.TryGetValue(pid, out var kids)) foreach (var kid in kids) stack.Push(kid);
        }
        return result;
    }
}

/// <summary>A tab's CPU use between two scans, as a share of one core (0.5 = half a core), and its busiest process.</summary>
public sealed record CpuSample(double Share, string? Busiest, double BusiestShare);

/// <summary>
/// CPU activity per tab (#215): a tab whose process tree uses the CPU counts as working even when its agent's hooks or
/// its quiet output say idle, e.g. while the agent waits for a build or test run that prints nothing.
/// </summary>
public static class TabCpu
{
    /// <summary>
    /// Above this share of one core a reading counts as active. Idle tabs measured 0–6.5 % per 2 s reading (an idle codex
    /// up to 6.5 %, rarely one reading near 11 %; idle claude and agy and the tab's own helper loops 0–2 %); builds and test
    /// runs 30–300 %.
    /// </summary>
    public const double Threshold = 0.10;

    /// <summary>Consecutive readings above the threshold before a tab counts as working, and below it before it is idle again.</summary>
    public const int Readings = 2;

    /// <summary>
    /// The CPU the tab's shell <paramref name="rootPid"/> and all its descendants used between the two scans (null: no
    /// earlier scan, or the shell is gone or is another process now). Counted is each live process's own CPU time:
    /// the growth since the earlier scan, or all of it for a process started since then. A process that started and
    /// ended between two scans is not seen; its parent's children time is not counted, because that also grows with the
    /// tab's own short helper commands (stty, sleep) and would count again what the scans already saw.
    /// </summary>
    public static CpuSample? Measure(ProcSnapshot? previous, ProcSnapshot current, int rootPid, long? rootStartTime)
    {
        if (previous is null || current.Uptime - previous.Uptime is var elapsed && elapsed <= 0) return null;
        if (!current.Processes.TryGetValue(rootPid, out var root) || rootStartTime is { } start && root.StartTime != start) return null;
        long total = 0, best = -1;
        string? busiest = null;
        foreach (var pid in current.Subtree(rootPid))
        {
            var p = current.Processes[pid];
            var delta = previous.Processes.TryGetValue(pid, out var before) && before.StartTime == p.StartTime ? p.Ticks - before.Ticks
                : p.StartTime >= previous.Uptime ? p.Ticks : 0;
            if (delta <= 0) continue;
            total += delta;
            if (delta > best) (best, busiest) = (delta, p.Name);
        }
        return new CpuSample(total / (double)elapsed, busiest, busiest is null ? 0 : best / (double)elapsed);
    }

    /// <summary>A tab its hooks or output call idle (or unknown) but whose processes work is Working; other states stay.</summary>
    public static Activity Combine(Activity activity, bool cpuActive) =>
        cpuActive && activity is Activity.Idle or Activity.Unknown ? Activity.Working : activity;
}

/// <summary>
/// One tab's CPU activity with hysteresis: working after <see cref="TabCpu.Readings"/> readings in a row above the
/// threshold, idle again after as many below it, so one spike does not flip the status and one dip does not end it.
/// </summary>
public sealed class CpuActivity
{
    private int _above, _below;

    public bool Active { get; private set; }
    /// <summary>The last reading above the threshold, for the tooltip's process name.</summary>
    public CpuSample? Busy { get; private set; }
    /// <summary>The last poll at which the tab was active (UTC); auto-clear counts its idle time from it.</summary>
    public DateTime? ActiveAt { get; private set; }

    /// <summary>Takes the poll's reading (null: none, e.g. the first scan or a tab not running) and returns whether the tab is active.</summary>
    public bool Update(CpuSample? sample, DateTime nowUtc)
    {
        if (sample is null)
        {
            _above = _below = 0;
            Active = false;
        }
        else if (sample.Share > TabCpu.Threshold)
        {
            Busy = sample;
            _below = 0;
            if (++_above >= TabCpu.Readings) Active = true;
        }
        else
        {
            _above = 0;
            if (++_below >= TabCpu.Readings) Active = false;
        }
        if (Active) ActiveAt = nowUtc;
        return Active;
    }
}
