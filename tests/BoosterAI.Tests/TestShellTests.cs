using System.Diagnostics;
using BoosterAI;
using Xunit;

public class TestShellTests
{
    public static IEnumerable<object[]> Agents() => Prerequisites.Clis.Select(cli => new object[] { cli });

    [Theory]
    [MemberData(nameof(Agents))]
    public async Task HostileInheritedEnvironmentCannotReplaceTheFixture(string cli)
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/script")) return;
        var root = Directory.CreateTempSubdirectory("booster-shell-isolation-").FullName;
        try
        {
            var hostile = Directory.CreateDirectory(Path.Combine(root, "hostile")).FullName;
            var marker = Path.Combine(root, "host-startup-ran");
            File.WriteAllText(Path.Combine(hostile, ".bashrc"), PermissionOverrides.Change("", cli,
                AgentModes.AllKnownChoices(cli).First(c => c.Mode == AgentMode.Yolo)));
            var startup = Path.Combine(hostile, "startup.sh");
            File.WriteAllText(startup, "touch " + Launcher.Quote(marker));
            var psi = Start(root, cli);
            psi.Environment["HOME"] = hostile;
            psi.Environment["PATH"] = hostile;
            psi.Environment["SHELL"] = "/missing-host-shell";
            psi.Environment["BASH_ENV"] = startup;
            psi.Environment["ENV"] = startup;
            psi.Environment["BASH_FUNC_" + cli + "%%"] = "() { touch " + Launcher.Quote(marker) + "; }";
            TestShell.Configure(psi, root);
            var result = await Run(psi);
            Assert.Equal(0, result.Code);
            Assert.Contains("fixture-cli", result.Output);
            Assert.False(File.Exists(marker));
            Assert.False(File.Exists(Path.Combine(root, "unexpected-agent")));
        }
        finally { TestShell.StopLeftovers(root); Directory.Delete(root, true); }
    }

    [Theory]
    [MemberData(nameof(Agents))]
    public async Task RemovedFunctionFallsBackToABarrierInsteadOfAnInstalledAgent(string cli)
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/script")) return;
        var root = Directory.CreateTempSubdirectory("booster-shell-barrier-").FullName;
        try
        {
            var psi = Start(root, cli);
            var home = TestShell.Configure(psi, root);
            // Exercise the production path that previously removed a fake function and launched the real CLI.
            File.WriteAllText(Path.Combine(home, ".bashrc"), PermissionOverrides.Change("", cli,
                AgentModes.AllKnownChoices(cli).First(c => c.Mode == AgentMode.Yolo)));
            var result = await Run(psi);
            Assert.Equal(126, result.Code);
            Assert.DoesNotContain("fixture-cli", result.Output);
            Assert.Equal(cli + "\n", File.ReadAllText(Path.Combine(root, "unexpected-agent")));
        }
        finally { TestShell.StopLeftovers(root); Directory.Delete(root, true); }
    }

    private static ProcessStartInfo Start(string root, string cli)
    {
        File.WriteAllText(Path.Combine(root, "lib.sh"), TabSession.Lib);
        return new ProcessStartInfo("/bin/bash")
        {
            WorkingDirectory = root, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            ArgumentList = { "--noprofile", "--norc", "-c", cli + "() { printf 'fixture-cli\\n'; }\n. ./lib.sh . tab1\nbs_run " + Launcher.Quote(cli) },
        };
    }

    private static async Task<(int Code, string Output, string Error)> Run(ProcessStartInfo psi)
    {
        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            process.StandardInput.Close();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            return (process.ExitCode, await output, await error);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await Task.WhenAll(output, error).WaitAsync(TimeSpan.FromSeconds(10));
        }
    }
}
