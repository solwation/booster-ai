using System.Diagnostics;
using BoosterAI;
using Xunit;

/// <summary>#66: every tab keeps its configured name as the terminal title.</summary>
public class TabTitleTests
{
    private static readonly TabConfig Tab = new() { Name = "Mitt projekt", Llm = "claude", Directory = "~/p" };

    [Fact]
    public void TabScriptHandsTheNameToTheLib()
    {
        var track = new TabTrack("/s/tab-lib.sh", "/s", "tab1");
        Assert.Contains(". '/s/tab-lib.sh' '/s' 'tab1' 'Mitt projekt' 2>/dev/null", Launcher.TabScript(Tab, track: track));
    }

    [Fact]
    public void EveryTerminalGetsTheNameOnItsCommandLine()
    {
        TabTrack?[] tracks = [null];
        Assert.Equal(["--tab-with-profile=default", "--new-window", "-T", "Mitt projekt"],
            Launcher.LinuxCommands([Tab], "ptyxis", null, tracks, 0)[0].Args.Take(4));
        Assert.Contains("--title", Launcher.WindowsTerminalArgs([Tab], "Ubuntu"));
        Assert.Contains("--suppressApplicationTitle", Launcher.WindowsTerminalArgs([Tab], "Ubuntu"));
    }

    /// <summary>
    /// A tracked CLI start sets the title, Claude Code is told not to set its own, and the leader of the terminal's
    /// foreground process group is a bash rather than script (Ptyxis would append "— script --quiet ..." to the name).
    /// A fake claude on PATH reports what it sees; job control is on, as in a tab's interactive shell.
    /// </summary>
    [Fact]
    public void CliStartSetsTheTitleUnderAShellLeader()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/script")) return;
        var root = Path.Combine(Path.GetTempPath(), "bs-title-" + Guid.NewGuid().ToString("N"));
        try
        {
            var dir = new TabSession(root, DateTime.UtcNow).Directory;
            var bin = Path.Combine(dir, "bin");
            Directory.CreateDirectory(bin);
            var fake = Path.Combine(bin, "claude");
            File.WriteAllText(fake, """
                #!/bin/bash
                p=$$
                while [ "$p" -gt 1 ] && [ "$(cat /proc/$p/comm)" != script ]; do p=$(cut -d' ' -f4 /proc/$p/stat); done
                g=$(cut -d' ' -f5 /proc/$p/stat)
                printf 'LEADER=%s OFF=%s\n' "$(cat /proc/$g/comm)" "$CLAUDE_CODE_DISABLE_TERMINAL_TITLE"
                """.Replace("\r\n", "\n"));
            File.SetUnixFileMode(fake, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var chain = ". ./tab-lib.sh . tab1 'Mitt projekt' && (bs_cli 2>/dev/null || true) && bs_run claude && exit";
            var psi = new ProcessStartInfo("script") { RedirectStandardOutput = true, WorkingDirectory = dir };
            psi.Environment["PATH"] = bin + ":" + Environment.GetEnvironmentVariable("PATH");
            psi.Environment.Remove("CLAUDE_CODE_DISABLE_TERMINAL_TITLE");
            foreach (var a in new[] { "-qec", $"bash --norc --noprofile -ic \"{chain}\"", "/dev/null" }) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            try
            {
                var output = p.StandardOutput.ReadToEnd();
                Assert.True(p.WaitForExit(30000));
                Assert.Contains("\x1b]0;Mitt projekt\a", output);
                Assert.Contains("LEADER=bash OFF=1", output);
            }
            finally { if (!p.HasExited) p.Kill(entireProcessTree: true); }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    /// <summary>The idle shell's prompt repeats the name literally: no expansion, no control characters.</summary>
    [Fact]
    public void PromptTitleIsLiteral()
    {
        if (!OperatingSystem.IsLinux()) return;
        var root = Path.Combine(Path.GetTempPath(), "bs-ps1-" + Guid.NewGuid().ToString("N"));
        try
        {
            var dir = new TabSession(root, DateTime.UtcNow).Directory;
            var psi = new ProcessStartInfo("bash") { RedirectStandardOutput = true, WorkingDirectory = dir };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(". ./tab-lib.sh . tab1 \"$1\" && PS1=$BS_PS1_TITLE && printf '%s' \"${PS1@P}\"");
            psi.ArgumentList.Add("bash");
            psi.ArgumentList.Add("a$(echo x)`id`\\b\u0007c");
            using var p = Process.Start(psi)!;
            try
            {
                var output = p.StandardOutput.ReadToEnd();
                Assert.True(p.WaitForExit(10000));
                Assert.Equal("\x1b]0;a$(echo x)`id`\\bc\a", output);
            }
            finally { if (!p.HasExited) p.Kill(entireProcessTree: true); }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}
