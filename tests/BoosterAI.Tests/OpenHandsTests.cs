using System.Diagnostics;
using BoosterAI;
using Xunit;

public class OpenHandsTests
{
    [Fact]
    public void ConfigurationAndDetectionExposeOnlyInstalledOpenHands()
    {
        var config = ConfigStore.Parse("""{"Tabs":[{"Llm":"openhands","Model":"provider/custom-model"}]}""")!;
        var vm = new MainViewModel(config);
        Assert.Equal("openhands", vm.Tabs[0].Llm);
        Assert.Equal("provider/custom-model", vm.Tabs[0].Model);
        vm.Tabs[0].Llm = "claude";
        var (clis, _, _) = Prerequisites.ParseDetect("bsdetect openhands file /bin/openhands\n");
        vm.Setup.Apply(new SetupStatus(WslState.Ok, ["Ubuntu"], clis, false, false), "Ubuntu");
        Assert.Contains("openhands", vm.Tabs[0].Llms);
        (clis, _, _) = Prerequisites.ParseDetect("bsdetect openhands alias -\n");
        vm.Setup.Apply(new SetupStatus(WslState.Ok, ["Ubuntu"], clis, false, false), "Ubuntu");
        Assert.DoesNotContain("openhands", vm.Tabs[0].Llms);
        Assert.Contains("openhands", Prerequisites.DetectScript);
    }

    [Fact]
    public void InteractiveLaunchUsesSavedSettingsOrExplicitEnvironmentModel()
    {
        var tab = new TabConfig { Llm = "openhands", Directory = "~/a b" };
        Assert.Equal("cd ~/'a b' && openhands || true && exec bash", Launcher.TabScript(tab));
        tab.Model = "provider/custom-model";
        Assert.Equal("cd ~/'a b' && LLM_MODEL='provider/custom-model' openhands --override-with-envs || true && exec bash", Launcher.TabScript(tab));
        var script = Launcher.TabScript(tab, "it's a task");
        Assert.Contains("--task 'it'\\''s a task'", script);
        Assert.DoesNotContain("--model", script);
        Assert.DoesNotContain("--headless", script);
        Assert.DoesNotContain("--always-approve", script);
    }

    [Fact]
    public void RunOnceUsesHeadlessTaskWithoutAnotherAgentsStreamArguments()
    {
        var tab = new TabConfig { Llm = "openhands", Orkester = true, NonInteractive = true };
        var script = Launcher.TabScript(tab, track: new TabTrack("/s/lib.sh", "/s", "tab1"));
        Assert.Contains("openhands --headless --task", script);
        Assert.Contains(Launcher.ForegroundWatchNote, script);
        Assert.DoesNotContain("bs_stream", script);
        Assert.DoesNotContain("--output-format", script);
        Assert.DoesNotContain(';', script);
        Assert.DoesNotContain("--headless", Launcher.TabScript(tab, "install"));
    }

    [Fact]
    public void RestartDoesNotSelectTheGlobalLatestConversationOrSaveIt()
    {
        var tab = new TabConfig { Llm = "openhands" };
        Assert.False(Launcher.CanContinue(tab));
        Assert.False(Launcher.CanResume(tab, tab));
        Assert.False(Launcher.CanSaveAndClose(tab));
        Assert.True(CloseAllLogic.NeedsSave(tab, TabState.Running, BoosterAI.Activity.Idle));
        Assert.False(CloseAllLogic.NeedsSave(tab, TabState.CliExited, BoosterAI.Activity.Idle));
        Assert.DoesNotContain("--resume", Launcher.RestartScript(tab, new TabTrack("/s/lib.sh", "/s", "tab1"), resume: true));
        Assert.Throws<ArgumentException>(() => Launcher.SaveAndCloseScript(tab, "save"));
    }

    [Fact]
    public void InstallerUsesOfficialStandaloneCliAndAliasRequiresSeparateAction()
    {
        var script = Prerequisites.InstallScript("openhands");
        Assert.Contains("set -o pipefail && curl -fsSL https://install.openhands.dev/install.sh | sh", script);
        Assert.DoesNotContain(';', script);
        Assert.DoesNotContain("--always-approve", script);
        Assert.Equal("alias openhands='openhands --always-approve'", Prerequisites.AliasLine("openhands"));
    }

    [Fact]
    public async Task RealPtyTracksRestartInPlaceAndClosesItsProcessTree()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/script")) return;
        var root = Directory.CreateTempSubdirectory("booster-openhands-pty").FullName;
        var session = new TabSession(root, DateTime.UtcNow);
        var track = session.NewTrack()!;
        var tab = new TabConfig { Llm = "openhands", Directory = session.Directory, Model = "provider/first" };
        var body = "openhands() { printf '%s\\n' \"$PWD\" \"$LLM_MODEL\" \"$@\" > \"$LLM_MODEL_FILE\"; sleep 60 & child=$!; printf '%s\\n' \"$child\" >> child-pids; wait \"$child\"; }\nexport -f openhands\n"
            + Launcher.TabScript(tab, track: track);
        var argsFile = Path.Combine(session.Directory, "received");
        var psi = new ProcessStartInfo("script") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.Environment["LLM_MODEL_FILE"] = argsFile;
        psi.ArgumentList.Add("-qefc");
        psi.ArgumentList.Add("bash --noprofile --norc -ic '" + body.Replace("'", "'\\''") + "'");
        psi.ArgumentList.Add("/dev/null");
        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            Assert.True(SpinWait.SpinUntil(() => File.Exists(argsFile) && File.ReadAllLines(argsFile).Length >= 3, 30000));
            Assert.Equal([session.Directory, "provider/first", "--override-with-envs"], File.ReadAllLines(argsFile));
            var log = session.ReadLog(track.Id)!;
            Assert.True(log.CliStarted);
            var pid = log.Pid!.Value;
            var start = log.StartTime!.Value;
            tab.Model = "provider/second";
            session.QueueNext(track.Id, Launcher.RestartScript(tab, track, resume: false));
            Launcher.RunTabControl(Launcher.StopTrackedForegroundScript(pid, start), "Ubuntu");
            Assert.True(SpinWait.SpinUntil(() => File.ReadAllText(argsFile).Contains("provider/second"), 30000));
            Assert.Equal([session.Directory, "provider/second", "--override-with-envs"], File.ReadAllLines(argsFile));
            Assert.Equal(pid, session.ReadLog(track.Id)!.Pid);
            Launcher.RunTabControl(Launcher.CloseTabScript(pid, start), "Ubuntu");
            Assert.True(process.WaitForExit(30000));
            foreach (var child in File.ReadAllLines(Path.Combine(session.Directory, "child-pids")))
                Assert.True(SpinWait.SpinUntil(() => !File.Exists($"/proc/{child}/stat")
                    || File.ReadAllText($"/proc/{child}/stat").Contains(") Z "), 5000));
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            process.WaitForExit(30000);
            await Task.WhenAll(output, error).WaitAsync(TimeSpan.FromSeconds(30));
            var children = Path.Combine(session.Directory, "child-pids");
            if (File.Exists(children))
                foreach (var child in File.ReadAllLines(children))
                    try
                    {
                        using var leftover = Process.GetProcessById(int.Parse(child));
                        if (!leftover.HasExited) leftover.Kill(entireProcessTree: true);
                        leftover.WaitForExit(5000);
                    }
                    catch (ArgumentException) { } // Already reaped by the shell.
            Directory.Delete(root, true);
        }
    }
}
