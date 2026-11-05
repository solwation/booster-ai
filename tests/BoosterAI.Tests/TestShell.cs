using System.Diagnostics;
using BoosterAI;

/// <summary>Keep shell fixtures off personal startup files, exported functions and installed agent binaries.</summary>
internal static class TestShell
{
    public static string Configure(ProcessStartInfo start, string root, string? bin = null, string? home = null)
    {
        home ??= Path.Combine(root, "shell-home");
        bin ??= Path.Combine(root, "shell-bin");
        Directory.CreateDirectory(home);
        Directory.CreateDirectory(bin);
        foreach (var cli in Prerequisites.Clis)
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
}
