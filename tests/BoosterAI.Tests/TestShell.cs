using System.Diagnostics;
using BoosterAI;

/// <summary>Keep shell fixtures off personal startup files, exported functions and installed agent binaries.</summary>
internal static class TestShell
{
    public static string Configure(ProcessStartInfo start, string root, string? bin = null, string? home = null, bool blockAgents = true)
    {
        home ??= Path.Combine(root, "shell-home");
        bin ??= Path.Combine(root, "shell-bin");
        Directory.CreateDirectory(home);
        Directory.CreateDirectory(bin);
        foreach (var cli in blockAgents ? Prerequisites.Clis : Array.Empty<string>())
        {
            var path = Path.Combine(bin, cli);
            if (File.Exists(path)) continue; // A fixture may supply its own executable stub.
            File.WriteAllText(path, "#!/bin/sh\nprintf '%s\\n' " + Launcher.Quote(cli)
                + " >> " + Launcher.Quote(Path.Combine(root, "unexpected-agent")) + "\nexit 126\n");
            if (OperatingSystem.IsLinux())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        // This is only the child process environment. The test runner and user's environment are untouched.
        start.Environment.Clear();
        start.Environment["HOME"] = home;
        start.Environment["PATH"] = bin + ":/usr/bin:/bin";
        start.Environment["SHELL"] = "/bin/bash";
        start.Environment["TERM"] = "xterm-256color";
        return home;
    }

    public static void StopLeftovers(string root)
    {
        if (!OperatingSystem.IsLinux()) return;
        foreach (var directory in Directory.GetDirectories("/proc"))
        {
            try
            {
                var cwd = new DirectoryInfo(Path.Combine(directory, "cwd")).LinkTarget;
                if (cwd != root && cwd?.StartsWith(root + "/", StringComparison.Ordinal) != true) continue;
                var stat = Path.Combine(directory, "stat");
                var identity = TabStatus.ParseProcStartTime(File.ReadAllText(stat));
                if (identity is null) continue;
                using var process = Process.GetProcessById(int.Parse(Path.GetFileName(directory)));
                // Recheck identity before killing: a PID may have exited and been reused after the scan.
                if (TabStatus.ParseProcStartTime(File.ReadAllText(stat)) != identity) continue;
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                if (!process.WaitForExit(5000)) throw new TimeoutException("A fixture process did not exit after termination.");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }
    }

}
