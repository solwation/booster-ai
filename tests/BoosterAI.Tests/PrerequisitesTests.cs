using BoosterAI;
using Xunit;

public class PrerequisitesTests
{
    private static SetupStatus Status(params CliStatus[] clis) => new(WslState.Ok, ["Ubuntu"], clis, true, false);

    [Fact]
    public void ParseDetectReadsOnlyMarkedLines()
    {
        const string output = "bash: cannot set terminal process group (-1): Inappropriate ioctl for device\n"
            + "Welcome to Ubuntu\r\n"
            + "bsdetect claude alias /home/me/.local/bin/claude\r\n"
            + "bsdetect codex file /home/me/.nvm/versions/node/v22/bin/codex\n"
            + "bsdetect agy function -\n"
            + "bsdetect npm none -\n"
            + "bsdetect claude file /usr/bin/claude\n"
            + "bsdetect-block yes\n";
        var (clis, npm, block) = Prerequisites.ParseDetect(output);
        Assert.Equal(["claude", "codex", "agy"], clis.Select(c => c.Cli));
        Assert.Equal(new CliStatus("claude", "alias", "/home/me/.local/bin/claude"), clis[0]);
        Assert.True(clis[0].Installed && clis[0].HasAlias);
        Assert.True(clis[1].Installed && !clis[1].HasAlias);
        Assert.False(clis[2].Installed);
        Assert.True(clis[2].HasAlias);
        Assert.False(npm);
        Assert.True(block);
    }

    [Fact]
    public void ParseDetectKeepsPathsWithSpacesAndFindsNpm()
    {
        var (clis, npm, block) = Prerequisites.ParseDetect("bsdetect codex file /opt/my tools/codex\nbsdetect npm file /usr/bin/npm\n");
        Assert.Equal("/opt/my tools/codex", Assert.Single(clis).Path);
        Assert.True(npm);
        Assert.False(block);
        Assert.False(new SetupStatus(WslState.Ok, [], Prerequisites.ParseDetect("").Clis, false, false).Checked);
    }

    [Fact]
    public void ParseDistrosHandlesWslOutput()
    {
        Assert.Equal(["Ubuntu", "Debian"], Prerequisites.ParseDistros("﻿U\0b\0u\0n\0t\0u\0\r\0\n\0Debian\r\n\r\n"));
        Assert.Empty(Prerequisites.ParseDistros(""));
    }

    [Theory]
    [InlineData(false, -1, "", "Ubuntu", WslState.Missing)]
    [InlineData(true, -1, "", "Ubuntu", WslState.Missing)]
    [InlineData(true, 0, "Ubuntu,Debian", "ubuntu", WslState.Ok)]
    [InlineData(true, 0, "Debian", "Ubuntu", WslState.DistroMissing)]
    [InlineData(true, 1, "Debian", "Ubuntu", WslState.Missing)]
    public void WslStateFollowsTheList(bool exe, int code, string distros, string configured, WslState expected) =>
        Assert.Equal(expected, Prerequisites.Wsl(exe, code, distros.Split(',', StringSplitOptions.RemoveEmptyEntries), configured));

    [Fact]
    public void MissingCliIsOfferedOnlyWhenARowUsesIt()
    {
        var s = Status(new CliStatus("claude", "file", "/bin/claude"), new CliStatus("codex", "none", null), new CliStatus("agy", "alias", null));
        Assert.Empty(Prerequisites.MissingToOffer(s, ["claude", Catalog.ShellOnly]));
        Assert.Equal(["codex"], Prerequisites.MissingToOffer(s, ["claude", "codex"]));
        // An alias without a program does not make agy installed.
        Assert.Equal(["codex", "agy"], Prerequisites.MissingToOffer(s, ["agy", "codex"]));
    }

    [Fact]
    public void NewUserWithoutAnyCliIsOfferedEverySupportedCli()
    {
        var s = Status(new CliStatus("claude", "none", null), new CliStatus("codex", "none", null), new CliStatus("agy", "none", null), new CliStatus("aider", "none", null));
        Assert.Equal(Prerequisites.Clis, Prerequisites.MissingToOffer(s, []));
        Assert.Empty(Prerequisites.MissingToOffer(SetupStatus.Empty, ["claude"]));
    }

    [Fact]
    public void AliasIsOfferedOnlyForInstalledClisWithoutAliasOrFunction()
    {
        var s = Status(new CliStatus("claude", "alias", "/bin/claude"), new CliStatus("codex", "file", "/bin/codex"), new CliStatus("agy", "function", "/bin/agy"));
        Assert.Equal(["codex"], Prerequisites.AliasesToOffer(s, []));
        Assert.Empty(Prerequisites.AliasesToOffer(s, ["codex"]));
        Assert.Empty(Prerequisites.AliasesToOffer(Status(new CliStatus("claude", "none", null)), []));
    }

    [Fact]
    public void AliasLinesPerCli()
    {
        Assert.Equal("alias claude='claude --dangerously-skip-permissions'", Prerequisites.AliasLine("claude"));
        Assert.Equal("alias codex='codex --dangerously-bypass-approvals-and-sandbox'", Prerequisites.AliasLine("codex"));
        Assert.Equal("alias agy='agy --dangerously-skip-permissions'", Prerequisites.AliasLine("agy"));
        Assert.Equal("alias aider='aider --yes-always'", Prerequisites.AliasLine("aider"));
        Assert.Throws<ArgumentException>(() => Prerequisites.AliasLine(Catalog.ShellOnly));
    }

    [Fact]
    public void AddAliasesWritesAMarkedBlockAndKeepsTheFile()
    {
        const string bashrc = "# my bashrc\nexport EDITOR=vim";
        var result = Prerequisites.AddAliases(bashrc, ["claude", "agy"]);
        Assert.StartsWith(bashrc + "\n\n" + Prerequisites.BlockStart + "\n", result);
        Assert.EndsWith("alias claude='claude --dangerously-skip-permissions'\nalias agy='agy --dangerously-skip-permissions'\n" + Prerequisites.BlockEnd + "\n", result);
        Assert.StartsWith(Prerequisites.BlockStart, Prerequisites.AddAliases(null, ["codex"]));
    }

    [Fact]
    public void AddAliasesNeverDuplicatesOrOverwrites()
    {
        const string bashrc = "alias claude='claude --permission-mode auto'\nagy() { command agy \"$@\"; }\n";
        // claude has the user's own alias, agy a function: only codex is added.
        var once = Prerequisites.AddAliases(bashrc, ["claude", "codex", "agy"]);
        Assert.StartsWith(bashrc, once);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(once, "alias claude="));
        Assert.Contains("alias codex='codex --dangerously-bypass-approvals-and-sandbox'", once);
        Assert.DoesNotContain("alias claude='claude --dangerously", once);
        Assert.DoesNotContain("alias agy=", once);
        // A second run changes nothing; a later CLI joins the same block.
        Assert.Equal(once, Prerequisites.AddAliases(once, ["codex"]));
        Assert.Equal(once, Prerequisites.AddAliases(bashrc, []) + once[bashrc.Length..]);
        var later = Prerequisites.AddAliases(Prerequisites.AddAliases("", ["codex"]), ["claude"]);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(later, ">>> BoosterAI"));
        Assert.Contains("alias codex='codex --dangerously-bypass-approvals-and-sandbox'\nalias claude='claude --dangerously-skip-permissions'\n" + Prerequisites.BlockEnd, later);
    }

    [Theory]
    [InlineData("alias claude='x'", "claude")]
    [InlineData("  alias -- codex=\"codex --x\"", "codex")]
    [InlineData("function agy {\n}", "agy")]
    [InlineData("function agy() {\n}", "agy")]
    [InlineData("claude () { :\n}", "claude")]
    public void DefinedFindsAliasesAndFunctions(string text, string name) => Assert.Contains(name, Prerequisites.Defined(text));

    [Theory]
    [InlineData("claude", "curl -fsSL https://claude.ai/install.sh | bash", "run claude here")]
    [InlineData("codex", "npm install -g @openai/codex", "run codex login here")]
    [InlineData("agy", "curl -fsSL https://antigravity.google/cli/install.sh | bash", "run agy here")]
    [InlineData("aider", "curl -LsSf https://aider.chat/install.sh | bash", "run aider here")]
    [InlineData("interpreter", "curl -fsSL https://www.openinterpreter.com/install | sh", "run interpreter here")]
    public void InstallScriptUsesTheOfficialInstaller(string cli, string command, string login)
    {
        var script = Prerequisites.InstallScript(cli);
        Assert.Contains(command, script);
        Assert.Contains(login, script);
        Assert.StartsWith("cd ~ && ", script);
        Assert.EndsWith(" && exec bash -l", script);
        // Windows Terminal splits its command line on ';' (see Launcher.TabScript).
        Assert.DoesNotContain(';', script);
    }

    [Fact]
    public void InstallTabRunsTheScriptOnBothPlatforms()
    {
        var script = Prerequisites.InstallScript("claude");
        var tab = new TabConfig { Name = "Installera claude", Directory = "~", Llm = Catalog.ShellOnly, Script = script };
        Assert.Equal(script, Launcher.TabScript(tab));
        var wt = Launcher.WindowsTerminalArgs([tab], "Ubuntu");
        var at = wt.IndexOf("wsl.exe");
        Assert.Equal(["wsl.exe", "-d", "Ubuntu", "--exec", "bash", "-lic", script], wt.Skip(at).Take(7));
        var linux = Assert.Single(Launcher.LinuxCommands([tab], "kitty", null, [null], 0));
        Assert.Equal(["bash", "-lic", script], linux.Args.TakeLast(3));
        // The script is never saved in config.json.
        Assert.DoesNotContain("Script", System.Text.Json.JsonSerializer.Serialize(tab, ConfigJsonContext.Default.TabConfig));
    }

    [Fact]
    public void WslInstallIsVisibleAndAsksForAdmin() =>
        Assert.Equal(["-NoExit", "-Command", "wsl.exe --install"], Prerequisites.WslInstall.Args);

    /// <summary>A temporary home with stub programs; deleted afterwards.</summary>
    private sealed class TempHome : IDisposable
    {
        public string Home { get; } = Directory.CreateTempSubdirectory("boosterai-setup-").FullName;
        public string Bin => Path.Combine(Home, "bin");

        public TempHome() => Directory.CreateDirectory(Bin);

        [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
        public void Stub(string name, string body)
        {
            var path = Path.Combine(Bin, name);
            File.WriteAllText(path, "#!/bin/sh\n" + body + "\n");
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        public System.Diagnostics.ProcessStartInfo Bash(params string[] args)
        {
            var psi = Prerequisites.Hidden("/bin/bash", args);
            psi.Environment.Clear();
            psi.Environment["HOME"] = Home;
            psi.Environment["PATH"] = Bin;
            psi.WorkingDirectory = Home;
            return psi;
        }

        public void Dispose() => Directory.Delete(Home, recursive: true);
    }

    [Fact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public void DetectScriptFindsProgramsAliasesAndFunctionsInALoginShell()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var home = new TempHome();
        home.Stub("claude", "exit 0");
        home.Stub("codex", "exit 0");
        home.Stub("interpreter", "exit 0");
        // Like Ubuntu's default ~/.profile: a login shell reads .bashrc through it.
        File.WriteAllText(Path.Combine(home.Home, ".profile"), "[ -f ~/.bashrc ] && . ~/.bashrc\n");
        File.WriteAllText(Path.Combine(home.Home, ".bashrc"),
            "echo noise from bashrc\nalias claude='claude --permission-mode auto'\nagy() { echo hi\n}\n" + Prerequisites.AddAliases(null, ["codex"]).Replace("alias codex=", "#alias codex="));
        var psi = home.Bash("-lic", Prerequisites.DetectScript);
        psi.Environment["PATH"] = home.Bin + ":/usr/bin:/bin";
        var (started, code, output) = Prerequisites.Run(psi, TimeSpan.FromSeconds(20));
        Assert.True(started);
        Assert.Equal(0, code);
        var (clis, _, block) = Prerequisites.ParseDetect(output!);
        Assert.True(block);
        Assert.Equal(new CliStatus("claude", "alias", Path.Combine(home.Bin, "claude")) { AliasDefinition = "alias claude='claude --permission-mode auto'" }, clis[0]);
        Assert.Equal(new CliStatus("codex", "file", Path.Combine(home.Bin, "codex")), clis[1]);
        Assert.Equal(new CliStatus("agy", "function", null), clis[2] with { FunctionDefinition = null });
        Assert.Contains("echo hi", clis[2].FunctionDefinition);
        Assert.Equal(new CliStatus("interpreter", "file", Path.Combine(home.Bin, "interpreter")), clis.Single(c => c.Cli == "interpreter"));
    }

    [Fact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public void RunReturnsWithinTheLimitWhenABackgroundJobKeepsStdoutOpen()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var home = new TempHome();
        // Like a .bashrc starting `daemon &`: the shell exits at once, the job inherits the pipe and holds it open.
        var psi = home.Bash("-c", "sleep 30 & echo $!; echo hi");
        psi.Environment["PATH"] = "/usr/bin:/bin";
        int? pid = null;
        try
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var (started, code, output) = Prerequisites.Run(psi, TimeSpan.FromSeconds(3));
            watch.Stop();
            if (output is not null && int.TryParse(output.Split('\n')[0].Trim(), out var p)) pid = p;
            Assert.True(started);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(6), $"Run took {watch.Elapsed}");
            Assert.Equal(0, code);
            Assert.NotNull(output);
            Assert.Contains("hi", output);
        }
        finally
        {
            // Never by name: only the sleep this test started, by the PID it printed.
            if (pid is { } id)
                try { System.Diagnostics.Process.GetProcessById(id).Kill(); }
                catch (Exception e) when (e is ArgumentException or InvalidOperationException) { }
        }
    }

    [Fact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public void DetectScriptCapturesHelpWrittenToStderr()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var home = new TempHome();
        // Like agy (a Go flag usage): the help goes to stderr, nothing to stdout.
        home.Stub("agy", "case \"$1\" in --version) echo 'agy 1.5.2';; *) echo '  --mode  Set the mode (accept-edits, auto, plan)' >&2; echo '  --dangerously-skip-permissions  Auto-approve' >&2;; esac");
        home.Stub("claude", "case \"$1\" in --version) echo '2.0.0';; *) echo '  --permission-mode  acceptEdits';; esac");
        var psi = home.Bash("-lic", Prerequisites.DetectScript);
        psi.Environment["PATH"] = home.Bin + ":/usr/bin:/bin";
        var (started, code, output) = Prerequisites.Run(psi, TimeSpan.FromSeconds(20));
        Assert.True(started);
        Assert.Equal(0, code);
        var (clis, _, _) = Prerequisites.ParseDetect(output!);
        var agy = clis.Single(c => c.Cli == "agy");
        Assert.Contains("--dangerously-skip-permissions", agy.Help);
        Assert.Equal("1.5.2", agy.Version);
        var modes = AgentModes.Choices(agy).Select(c => c.Mode).ToArray();
        Assert.Contains(AgentMode.AutoApprove, modes);
        Assert.Contains(AgentMode.Yolo, modes);
        Assert.Contains("--permission-mode", clis.Single(c => c.Cli == "claude").Help);
    }

    [Fact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public void InstallScriptReportsSuccessAndFailureAndKeepsAShell()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var home = new TempHome();
        File.CreateSymbolicLink(Path.Combine(home.Bin, "bash"), "/bin/bash");
        // A stand-in for the network: the "installer" it serves only prints a line.
        home.Stub("curl", "echo 'echo fake installer ran'");
        var (_, _, ok) = Prerequisites.Run(home.Bash("-c", Prerequisites.InstallScript("claude")), TimeSpan.FromSeconds(20));
        Assert.Contains("fake installer ran", ok);
        Assert.Contains("BoosterAI: done.", ok);
        Assert.DoesNotContain("did not finish", ok);

        home.Stub("curl", "exit 22");
        var (_, _, failed) = Prerequisites.Run(home.Bash("-c", Prerequisites.InstallScript("agy")), TimeSpan.FromSeconds(20));
        Assert.Contains("did not finish", failed);
        Assert.DoesNotContain("BoosterAI: done.", failed);

        // No npm: the tab explains how to get Node.js and never calls an installer.
        var (_, _, noNpm) = Prerequisites.Run(home.Bash("-c", Prerequisites.InstallScript("codex")), TimeSpan.FromSeconds(20));
        Assert.Contains("npm was not found", noNpm);
        Assert.Contains("did not finish", noNpm);

        home.Stub("npm", "echo \"npm $*\"");
        var (_, _, withNpm) = Prerequisites.Run(home.Bash("-c", Prerequisites.InstallScript("codex")), TimeSpan.FromSeconds(20));
        Assert.Contains("npm install -g @openai/codex", withNpm);
        Assert.Contains("run codex login here", withNpm);
    }

    [Fact]
    public void SetupViewModelListsSupportedAgents()
    {
        var vm = new SetupViewModel();
        Assert.Equal(Prerequisites.Clis, vm.Agents.Select(a => a.Cli));
        Assert.All(vm.Agents, a => Assert.False(a.IsInstalled));

        vm.Apply(Status(
            new CliStatus("claude", "file", "/bin/claude"),
            new CliStatus("codex", "none", null),
            new CliStatus("agy", "file", "/bin/agy"),
            new CliStatus("aider", "file", "/bin/aider")), "Ubuntu");

        Assert.True(vm.Agents.First(a => a.Cli == "claude").IsInstalled);
        Assert.False(vm.Agents.First(a => a.Cli == "codex").IsInstalled);
        Assert.True(vm.Agents.First(a => a.Cli == "agy").IsInstalled);
        Assert.True(vm.Agents.First(a => a.Cli == "aider").IsInstalled);
    }

    [Fact]
    public void TabViewModelLlmsFiltersToInstalledClis()
    {
        var appConfig = new AppConfig { Tabs = [new TabConfig { Llm = "claude" }] };
        var main = new MainViewModel(appConfig);
        var tab = main.Tabs[0];

        // Before setup check completes, all Catalog.Llms are available as fallback
        Assert.Equal(Catalog.Llms, tab.Llms);

        // After setup check finds only codex installed
        main.Setup.Apply(Status(
            new CliStatus("claude", "none", null),
            new CliStatus("codex", "file", "/bin/codex"),
            new CliStatus("agy", "none", null)), "Ubuntu");

        // tab.Llm is "claude", so "claude" is retained even though missing; "codex" is installed; "agy" is omitted.
        Assert.Contains("codex", tab.Llms);
        Assert.Contains("claude", tab.Llms);
        Assert.DoesNotContain("agy", tab.Llms);
        Assert.Contains(Catalog.ShellOnly, tab.Llms);

        // If tab changes to codex, claude (uninstalled) drops off tab.Llms
        tab.Llm = "codex";
        Assert.DoesNotContain("claude", tab.Llms);
        Assert.Contains("codex", tab.Llms);
        Assert.Contains(Catalog.ShellOnly, tab.Llms);
    }
}
