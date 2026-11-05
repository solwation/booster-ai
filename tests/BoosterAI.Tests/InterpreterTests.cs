using System.Diagnostics;
using BoosterAI;
using Xunit;

public class InterpreterTests
{
    [Fact]
    public void ConfigAndInstalledChoicePreserveInterpreter()
    {
        var cfg = ConfigStore.Parse("""{"Tabs":[{"Llm":"interpreter","Model":"ollama/qwen2.5-coder:7b"}]}""")!;
        var vm = new MainViewModel(ConfigStore.Copy(cfg));
        vm.Setup.Apply(new SetupStatus(WslState.Ok, [],
            [new CliStatus("interpreter", "file", "/opt/tools/interpreter")], false, false), "Ubuntu");
        var row = Assert.Single(vm.Tabs);
        Assert.Equal("interpreter", row.Llm);
        Assert.Contains("interpreter", row.Llms);
        Assert.Equal("ollama/qwen2.5-coder:7b", row.ToConfig(forLaunch: true).Model);
        Assert.DoesNotContain(vm.Setup.Missing, offer => offer.Cli == "interpreter");
        Assert.False(Launcher.CanContinue(row.ToConfig(forLaunch: true)));
        Assert.False(Launcher.CanSaveAndClose(row.ToConfig(forLaunch: true)));
        Assert.False(Launcher.CanResume(row.ToConfig(forLaunch: true), row.ToConfig(forLaunch: true)));
    }

    [Fact]
    public void DetectorIncludesInterpreterAndKeepsAliasOnlyUnavailable()
    {
        Assert.Contains("interpreter", Prerequisites.DetectScript);
        var installed = Prerequisites.ParseDetect("bsdetect interpreter file /opt/my tools/interpreter\n").Clis.Single();
        Assert.True(installed.Installed);
        Assert.Equal("/opt/my tools/interpreter", installed.Path);
        Assert.False(Prerequisites.ParseDetect("bsdetect interpreter alias -\n").Clis.Single().Installed);
        Assert.DoesNotContain("alias interpreter", Prerequisites.AddAliases("", ["interpreter"]));
    }

    [Theory]
    [InlineData(false, "standard", "")]
    [InlineData(true, "standard", "")]
    [InlineData(false, "custom-model", "--model|custom-model")]
    [InlineData(true, "custom-model", "--model|custom-model")]
    [InlineData(false, "local", "--local")]
    [InlineData(true, "local", "--oss")]
    [InlineData(false, "ollama/qwen2.5-coder:7b", "--model|ollama/qwen2.5-coder:7b")]
    [InlineData(true, "ollama/qwen2.5-coder:7b", "--oss|--local-provider|ollama|--model|qwen2.5-coder:7b")]
    public void StartsBothGenerationsInTheSelectedDirectory(bool current, string model, string expected)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fake = new FakeInterpreter(current);
        var tab = new TabConfig { Directory = fake.Project, Llm = "interpreter", Model = model };
        var result = fake.Run(Launcher.TabScript(tab));
        Assert.Equal(0, result.Code);
        Assert.Equal(fake.Project, File.ReadAllText(fake.Cwd).TrimEnd());
        Assert.Equal(expected == "" ? [] : expected.Split('|'), File.ReadAllLines(fake.Args));
        if (model is "standard" or "custom-model") Assert.False(File.Exists(fake.Probes));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PromptsAndRunOnceRequireTheCurrentCli(bool current, bool once)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fake = new FakeInterpreter(current);
        var tab = new TabConfig { Llm = "interpreter", Orkester = true, NonInteractive = once };
        var result = fake.Run(InterpreterCli.Command(tab, null));
        if (!current)
        {
            Assert.NotEqual(0, result.Code);
            Assert.Contains("kräver aktuella Open Interpreter", result.Error);
            Assert.False(File.Exists(fake.Args));
            return;
        }
        Assert.Equal(0, result.Code);
        var args = File.ReadAllLines(fake.Args);
        Assert.Equal(once ? "exec" : "/orkester", args[0]);
        Assert.Equal(once ? "/orkester " + Launcher.ForegroundWatchNote : "/orkester", args[^1]);
        Assert.DoesNotContain("-p", args);
    }

    [Fact]
    public void FailedOrTimedOutProbeNeverRunsAnAdvancedCommand()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fake = new FakeInterpreter(true);
        // Simulate timeout's exit 124 without spending ten seconds or starting a child.
        fake.Write("timeout", "exit 124");
        var result = fake.Run(InterpreterCli.Command(new TabConfig { Llm = "interpreter", Orkester = true }, null));
        Assert.NotEqual(0, result.Code);
        Assert.False(File.Exists(fake.Args));
        Assert.Contains("kräver aktuella", result.Error);
    }

    [Fact]
    public void FailedCurrentRunDoesNotFallBackToLegacyOrRunTwice()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fake = new FakeInterpreter(true, exitCode: 7);
        var result = fake.Run(InterpreterCli.Command(new TabConfig { Llm = "interpreter", Model = "local" }, null));
        Assert.Equal(7, result.Code);
        Assert.Equal(["--oss"], File.ReadAllLines(fake.Args));
        Assert.Single(File.ReadAllLines(fake.Cwd));
    }

    [Fact]
    public void InPlaceRestartLogsExitAndKeepsTheOwningShell()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fake = new FakeInterpreter(true);
        var lib = Path.Combine(fake.Root, "lib.sh");
        File.WriteAllText(lib, TabSession.Lib);
        var track = new TabTrack(lib, fake.Root, "tab1");
        var tab = new TabConfig { Directory = fake.Project, Llm = "interpreter", Model = "local" };
        var restart = Launcher.RestartScript(tab, track, resume: false);
        Assert.DoesNotContain(';', restart);
        var result = fake.Run(". " + Launcher.Quote(lib) + " " + Launcher.Quote(fake.Root) + " tab1\n" + restart + "\necho shell-survived");
        Assert.Equal(0, result.Code);
        Assert.Contains("shell-survived", result.Output);
        var log = TabLog.Parse(File.ReadAllText(Path.Combine(fake.Root, "tab1.log")));
        Assert.True(log.CliStarted);
        Assert.Equal(0, log.CliExit);
        Assert.Equal(["--oss"], File.ReadAllLines(fake.Args));
    }

    [Fact]
    public void RealPtyRestartsInterpreterInPlaceAndClosesItsShell()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/script")) return;
        using var fake = new FakeInterpreter(true);
        var session = new TabSession(fake.Root, DateTime.UtcNow);
        var track = session.NewTrack()!;
        var tab = new TabConfig { Directory = fake.Project, Llm = "interpreter" };
        var psi = new ProcessStartInfo("/usr/bin/script")
        {
            ArgumentList = { "-qefc", "bash --noprofile --norc -ic " + Launcher.Quote(Launcher.TabScript(tab, track: track)), "/dev/null" },
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        fake.SetEnvironment(psi);
        using var process = Process.Start(psi)!;
        try
        {
            _ = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            Assert.True(SpinWait.SpinUntil(() => session.ReadLog(track.Id) is { CliExit: 0, CanRestartExited: true }, 20000));
            var original = session.ReadLog(track.Id)!;
            Assert.True(SpinWait.SpinUntil(() => File.Exists(Path.Combine(session.Directory, track.Id + ".next.idle")), 20000));
            session.QueueNext(track.Id, Launcher.RestartScript(tab, track, resume: false));
            Launcher.RunTabControl(Launcher.WakeIdleShellScript(track), "Ubuntu");
            Assert.True(SpinWait.SpinUntil(() => File.ReadAllLines(fake.Cwd).Length == 2 && session.ReadLog(track.Id)?.CliExit == 0, 20000));
            Assert.Equal(original.Pid, session.ReadLog(track.Id)!.Pid);
            Launcher.RunTabControl(Launcher.CloseTabScript(original.Pid!.Value, original.StartTime!.Value, exitCleanly: true), "Ubuntu");
            Assert.True(process.WaitForExit(20000));
            Assert.Equal(0, process.ExitCode);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }
    }

    private sealed class FakeInterpreter : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("booster-interpreter-").FullName;
        public string Project => Path.Combine(Root, "project's folder");
        private string Bin => Path.Combine(Root, "bin");
        public string Args => Path.Combine(Root, "args");
        public string Cwd => Path.Combine(Root, "cwd");
        public string Probes => Path.Combine(Root, "probes");

        public FakeInterpreter(bool current, int exitCode = 0)
        {
            Directory.CreateDirectory(Bin);
            Directory.CreateDirectory(Project);
            Write("interpreter", $"""
                if [ "$1" = --help ]; then
                  echo probe >> '{Probes}'
                  echo '{(current ? "--oss" : "--local")}'
                  exit 0
                fi
                pwd >> '{Cwd}'
                for argument in "$@"; do printf '%s\n' "$argument"; done >> '{Args}'
                exit {exitCode}
                """);
        }

        public void Write(string name, string body)
        {
            var file = Path.Combine(Bin, name);
            File.WriteAllText(file, "#!/bin/bash\n" + body + "\n");
            if (OperatingSystem.IsLinux()) File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        public void SetEnvironment(ProcessStartInfo psi)
        {
            TestShell.Configure(psi, Root, bin: Bin, home: Root);
        }

        public (int Code, string Output, string Error) Run(string script)
        {
            var psi = new ProcessStartInfo("/bin/bash")
            {
                ArgumentList = { "--noprofile", "--norc", "-c", script }, WorkingDirectory = Root,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            SetEnvironment(psi);
            using var process = Process.Start(psi)!;
            try
            {
                process.StandardInput.Close();
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                Assert.True(process.WaitForExit(20000));
                return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
            }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
        }

        public void Dispose() { TestShell.StopLeftovers(Root); Directory.Delete(Root, true); }
    }
}
