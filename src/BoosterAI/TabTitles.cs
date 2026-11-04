using System.Diagnostics;

namespace BoosterAI;

/// <summary>Refreshes only verified managed PTYs, including shells recovered after an app restart.</summary>
internal static class TabTitles
{
    internal sealed record Target(int Pid, long StartTime, string Name);
    // Tests record requests instead of writing to the user's open terminals.
    internal static Action<IReadOnlyList<Target>, string> Restore { get; set; } = RestoreCore;

    internal static ProcessStartInfo Command(IReadOnlyList<Target> tabs, string distro)
    {
        var windows = OperatingSystem.IsWindows();
        var psi = new ProcessStartInfo(windows ? "wsl.exe" : "bash")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        if (windows)
            foreach (var arg in new[] { "-d", distro, "--exec", "bash" }) psi.ArgumentList.Add(arg);
        foreach (var arg in new[] { "-c", Script, "booster-titles" }) psi.ArgumentList.Add(arg);
        foreach (var tab in tabs)
        {
            psi.ArgumentList.Add(tab.Pid.ToString(System.Globalization.CultureInfo.InvariantCulture));
            psi.ArgumentList.Add(tab.StartTime.ToString(System.Globalization.CultureInfo.InvariantCulture));
            psi.ArgumentList.Add(new string(tab.Name.Where(c => !char.IsControl(c)).ToArray()));
        }
        return psi;
    }

    private static void RestoreCore(IReadOnlyList<Target> tabs, string distro)
    {
        if (tabs.Count == 0) return;
        try
        {
            using var process = Process.Start(Command(tabs, distro));
            if (process is null) return;
            try
            {
                _ = process.StandardOutput.ReadToEndAsync();
                _ = process.StandardError.ReadToEndAsync();
                process.WaitForExit(3000);
            }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
        }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        { /* A closed PTY or unavailable distro is retried at the next status poll. */ }
    }

    // No input is read or injected. Open the exact live shell's output PTY and recheck identity after opening it.
    internal const string Script = """
        same_process() {
          local stat fields
          stat=$(cat "/proc/$pid/stat" 2>/dev/null) || return 1
          read -ra fields <<< "${stat##*) }"
          [[ ${fields[0]} != Z && ${fields[19]} == "$started" ]]
        }
        while [ "$#" -ge 3 ]; do
          pid=$1 started=$2 name=$3
          shift 3
          [[ $pid =~ ^[1-9][0-9]*$ && $started =~ ^[0-9]+$ && -n $name ]] || continue
          same_process || continue
          tty=$(readlink "/proc/$pid/fd/1" 2>/dev/null) || continue
          [[ $tty =~ ^/dev/pts/[0-9]+$ && -c $tty ]] || continue
          if { exec {out}>"$tty"; } 2>/dev/null; then
            if same_process && [ "$(readlink "/proc/$pid/fd/1" 2>/dev/null)" = "$tty" ]; then
              printf '\033]0;%s\007' "$name" >&$out 2>/dev/null
            fi
            exec {out}>&-
          fi
        done
        """;
}
