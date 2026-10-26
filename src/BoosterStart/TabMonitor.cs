using System.Globalization;

namespace BoosterStart;

/// <summary>
/// Where a tab writes its status log, as the tab's shell sees the paths (see Launcher.TabScript).
/// Lib is the sourced script, Dir the session directory, Id the tab's file name in it.
/// </summary>
public sealed record TabTrack(string Lib, string Dir, string Id);

public enum TabState { Starting, Running, CliExited, CliMissing, NotStarted, Closed, Lost }

/// <summary>
/// What a tab's log says. One line per event, written by tab-lib.sh:
/// "&lt;epoch-ms&gt; start pid=N starttime=N", "… cli-start", "… cli-exit rc=N", "… exit".
/// </summary>
public sealed record TabLog(int? Pid, long? StartTime, bool CliStarted, int? CliExit, bool Exited)
{
    public static TabLog Parse(string text)
    {
        int? pid = null, rc = null;
        long? start = null;
        bool cliStarted = false, exited = false;
        foreach (var line in text.Split('\n'))
        {
            var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // A line still being written (no event yet) is ignored; the next poll reads it whole.
            if (parts.Length < 2 || !long.TryParse(parts[0], out _)) continue;
            var values = parts.Skip(2).Select(p => p.Split('=', 2)).Where(kv => kv.Length == 2)
                .ToDictionary(kv => kv[0], kv => kv[1]);
            switch (parts[1])
            {
                case "start":
                    pid = Int(values, "pid");
                    start = values.TryGetValue("starttime", out var s) && long.TryParse(s, CultureInfo.InvariantCulture, out var st) ? st : null;
                    break;
                case "cli-start": cliStarted = true; break;
                case "cli-exit": rc = Int(values, "rc") ?? -1; break;
                case "exit": exited = true; break;
            }
        }
        return new TabLog(pid, start, cliStarted, rc, exited);
    }

    private static int? Int(Dictionary<string, string> v, string key) =>
        v.TryGetValue(key, out var s) && int.TryParse(s, CultureInfo.InvariantCulture, out var i) ? i : null;
}

public static class TabStatus
{
    /// <summary>How long a tab may take (WSL boot, login shell) before it counts as not started.</summary>
    public static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(45);

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
            _ => TabState.CliExited,
        };
    }

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
/// %LOCALAPPDATA%\BoosterStart\sessions (Windows, written by WSL via /mnt/c) or
/// ~/.local/share/BoosterStart/sessions (Linux).
/// </summary>
public sealed class TabSession
{
    public const string Lib = """
        # BoosterStart tab status log, sourced by the tab's shell: . tab-lib.sh <dir> <tab-id>
        bs_log="$1/$2.log"
        bs_ev() { printf '%s %s\n' "$(date +%s%3N)" "$*" >> "$bs_log" 2>/dev/null; }
        bs_ev start pid=$$ starttime=$(cut -d' ' -f22 /proc/$$/stat)
        trap 'bs_ev exit' EXIT
        bs_cli() { bs_ev cli-start; }
        bs_done() { bs_ev cli-exit rc=$1; }

        """;

    private int _next;

    public string Directory { get; }
    /// <summary>The Windows Terminal window all tabs and restarts of this session open in.</summary>
    public string WindowName { get; }

    public TabSession(string root, DateTime now)
    {
        var id = now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        Directory = Path.Combine(root, id);
        WindowName = "BoosterStart-" + id;
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllText(Path.Combine(Directory, "tab-lib.sh"), Lib.Replace("\r\n", "\n"));
        CleanUp(root, now);
    }

    public static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create), "BoosterStart", "sessions");

    /// <summary>A new log id and the paths the tab's shell uses; null when the path cannot go into a tab script.</summary>
    public TabTrack? NewTrack()
    {
        var dir = OperatingSystem.IsWindows() ? PathMapping.ToWslPath(Directory) : Directory;
        if (dir.IndexOfAny([';', '\n', '\r']) >= 0) return null;
        return new TabTrack(dir + "/tab-lib.sh", dir, "tab" + Interlocked.Increment(ref _next));
    }

    public TabLog? ReadLog(string id)
    {
        try
        {
            var path = Path.Combine(Directory, id + ".log");
            return File.Exists(path) ? TabLog.Parse(File.ReadAllText(path)) : null;
        }
        catch (IOException) { return null; }
    }

    /// <summary>Old sessions are only logs; keep two days.</summary>
    private static void CleanUp(string root, DateTime now)
    {
        try
        {
            foreach (var d in new DirectoryInfo(root).GetDirectories())
                if (d.CreationTime < now.AddDays(-2)) d.Delete(recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

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
