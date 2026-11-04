using System.Diagnostics;
using BoosterAI;
using Xunit;

public class TabTitlesTests
{
    [Fact]
    public void NamesAreLiteralArgumentsAndCannotInjectTitleControls()
    {
        var command = TabTitles.Command([new(123, 456, "folder; $(touch marker) `id`\u001b\u0007\u009c")], "Example Distro");
        Assert.False(command.UseShellExecute);
        Assert.True(command.CreateNoWindow);
        Assert.Equal(["123", "456", "folder; $(touch marker) `id`"], command.ArgumentList.TakeLast(3));
        if (OperatingSystem.IsWindows())
            Assert.Equal(["-d", "Example Distro", "--exec", "bash"], command.ArgumentList.Take(4));
    }

    [Fact]
    public async Task LivePtyIsCorrectedDuringRunAndStaleIdentityIsIgnored()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/script")) return;
        var root = Path.Combine(Path.GetTempPath(), "booster-title-guard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var psi = new ProcessStartInfo("script")
        {
            WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        psi.ArgumentList.Add("-qec");
        psi.ArgumentList.Add("bash --norc --noprofile -c 'printf \"%s %s\\n\" \"$$\" \"$(cut -d\" \" -f22 /proc/$$/stat)\" > identity; printf \"\\033]0;agent-overwrite\\007\"; while [ ! -f done ]; do if [ -f redo ] && [ ! -f overwritten ]; then printf \"\\033]0;agent-overwrite-again\\007\"; touch overwritten; fi; sleep .05; done'");
        psi.ArgumentList.Add("/dev/null");
        using var terminal = Process.Start(psi)!;
        var output = terminal.StandardOutput.ReadToEndAsync();
        _ = terminal.StandardError.ReadToEndAsync();
        try
        {
            var identity = Path.Combine(root, "identity");
            var deadline = Stopwatch.StartNew();
            while ((!File.Exists(identity) || new FileInfo(identity).Length == 0) && deadline.Elapsed < TimeSpan.FromSeconds(10))
                await Task.Delay(25);
            var parts = File.ReadAllText(identity).Trim().Split(' ');
            var pid = int.Parse(parts[0]);
            var started = long.Parse(parts[1]);
            await Run([new(pid, started + 1, "STALE"), new(Environment.ProcessId, started, "OTHER")]);
            const string name = "Project $(touch marker); `id` — subtab";
            await Run([new(pid, started, name)]);
            // Another application's later write must be corrected too, while the shell is still alive.
            File.WriteAllText(Path.Combine(root, "redo"), "");
            deadline.Restart();
            while (!File.Exists(Path.Combine(root, "overwritten")) && deadline.Elapsed < TimeSpan.FromSeconds(5))
                await Task.Delay(25);
            Assert.True(File.Exists(Path.Combine(root, "overwritten")));
            await Run([new(pid, started, name)]);
            File.WriteAllText(Path.Combine(root, "done"), "");
            Assert.True(terminal.WaitForExit(10000));
            var text = await output;
            Assert.Contains("\x1b]0;agent-overwrite\a", text);
            Assert.Equal(2, text.Split("\x1b]0;" + name + "\a").Length - 1);
            Assert.DoesNotContain("STALE", text);
            Assert.DoesNotContain("OTHER", text);
            Assert.False(File.Exists(Path.Combine(root, "marker")));
        }
        finally
        {
            if (!terminal.HasExited) terminal.Kill(entireProcessTree: true);
            terminal.WaitForExit();
            Directory.Delete(root, recursive: true);
        }

        static async Task Run(IReadOnlyList<TabTitles.Target> targets)
        {
            using var command = Process.Start(TabTitles.Command(targets, ""))!;
            var stdout = command.StandardOutput.ReadToEndAsync();
            var stderr = command.StandardError.ReadToEndAsync();
            try { Assert.True(command.WaitForExit(5000)); }
            finally
            {
                if (!command.HasExited) command.Kill(entireProcessTree: true);
                command.WaitForExit();
            }
            Assert.Equal("", await stdout);
            Assert.Equal("", await stderr);
        }
    }
}
