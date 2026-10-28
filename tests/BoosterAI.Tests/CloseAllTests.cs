using System.Diagnostics;
using Activity = BoosterAI.Activity;
using BoosterAI;
using Xunit;

public class CloseAllTests
{
    [Theory]
    [InlineData("claude", TabState.Running, Activity.Busy, false, true)]
    [InlineData("codex", TabState.Running, Activity.Idle, false, true)]
    [InlineData("agy", TabState.Running, Activity.Unknown, false, true)]
    [InlineData("claude", TabState.Running, Activity.Watching, true, false)]
    [InlineData("claude", TabState.CliExited, Activity.Idle, false, false)]
    [InlineData("codex", TabState.CliMissing, Activity.Unknown, false, false)]
    [InlineData(Catalog.ShellOnly, TabState.Running, Activity.Busy, false, false)]
    public void OnlyLivingAiGetsASaveTurn(string llm, TabState state, Activity activity, bool once, bool expected) =>
        Assert.Equal(expected, CloseAllLogic.NeedsSave(new TabConfig { Llm = llm, Orkester = once, NonInteractive = once }, state, activity));

    [Fact]
    public void PreviousExitIdleAndTurnDoneCannotFinishTheSaveTurn()
    {
        const string previous = "1 start pid=42 starttime=7\n2 cli-start\n3 idle\n4 turn-done\n5 cli-exit rc=0\n";
        Assert.False(CloseAllLogic.SaveTurnDone(TabLog.Parse(previous), "new"));
        var beginning = previous + "6 cli-start\n7 close-save token=new\n";
        Assert.False(CloseAllLogic.SaveTurnDone(TabLog.Parse(beginning), "new"));
        Assert.False(CloseAllLogic.SaveTurnDone(TabLog.Parse(beginning + "8 idle\n"), "new"));
        Assert.True(CloseAllLogic.SaveTurnDone(TabLog.Parse(beginning + "8 turn-done\n"), "new"));
        Assert.True(CloseAllLogic.SaveTurnDone(TabLog.Parse(beginning + "8 cli-exit rc=0\n"), "new"));
        Assert.False(CloseAllLogic.SaveTurnDone(TabLog.Parse(beginning + "8 cli-start\n9 cli-exit rc=0\n"), "new"));
    }

    [Fact]
    public void ClosingAfterAFailureReplacesTheErrorWithProgress()
    {
        var vm = new MainViewModel(new AppConfig { Tabs = [new TabConfig()] });
        var item = new ClosingTab(vm.Tabs[0]);
        item.Fail("permission denied");
        Assert.Equal("permission denied", item.Text);
        item.Phase = ClosePhase.Closing;
        Assert.Equal(Loc.Get("close.phase.Closing"), item.Text);
        item.Phase = ClosePhase.Closed;
        Assert.Equal(Loc.Get("close.phase.Closed"), item.Text);
    }

    [Fact]
    public void TimeoutHasAnExactBoundary()
    {
        var start = DateTime.UtcNow;
        Assert.False(CloseAllLogic.HasTimedOut(start, start.AddMinutes(10).AddTicks(-1)));
        Assert.True(CloseAllLogic.HasTimedOut(start, start.AddMinutes(10)));
    }

    [Theory]
    [InlineData("claude", "bs_stream claude --continue", "-p")]
    [InlineData("codex", "bs_run 'codex exec resume --last", "Stop now.")]
    [InlineData("agy", "bs_stream agy --continue", "-p")]
    public void SavingContinuesWithANewPromptAndOwnMarker(string llm, string command, string argument)
    {
        var script = Launcher.SaveAndCloseScript(new TabConfig { Llm = llm, Directory = "/tmp/it's here", Orkester = true }, "token");
        Assert.Contains(command, script);
        Assert.Contains(argument, script);
        Assert.Contains("bs_cli && bs_ev close-save token='token'", script);
        Assert.Contains("commit and push", script);
        Assert.DoesNotContain("/orkester", script);
        Assert.DoesNotContain("dangerously", script);
        Assert.DoesNotContain(';', script);
    }

    [Fact]
    public void SaveContinuationMapsAWindowsDirectoryIntoWsl()
    {
        Assert.Equal("/mnt/c/Work/project", PathMapping.ToWslPath(@"C:\Work\project"));
        Assert.StartsWith("cd '/mnt/c/Work/project' &&", Launcher.SaveAndCloseScript(
            new TabConfig { Directory = @"C:\Work\project", Llm = "claude" }, "saved"));
    }

    [Fact]
    public void CancelRemovesOnlyPendingWorkAndKeepsAlreadyClosedTabsClosed()
    {
        var vm = new MainViewModel(new AppConfig { Tabs = [new TabConfig(), new TabConfig()] });
        vm.ShowSampleStatus();
        var operation = vm.PrepareCloseAll(sample: true);
        Assert.True(vm.IsClosingAll);
        operation.Cancel();
        Assert.False(vm.IsClosingAll);
        Assert.Equal(ClosePhase.Closed, operation.Tabs[1].Phase);
        Assert.Equal(ClosePhase.Saving, operation.Tabs[0].Phase);
    }

    [Fact]
    public void RealSaveScriptRunsTheContinuationAndLogsItsOwnExit()
    {
        if (!OperatingSystem.IsLinux()) return;
        var root = Directory.CreateTempSubdirectory("booster-close").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "lib.sh"), TabSession.Lib);
            // A shell function stands in for the CLI: verify the arguments without contacting an AI service.
            var script = ". ./lib.sh . tab1\ncodex() { printf '%s\\n' \"$@\" > args; }\nexport -f codex\n"
                + Launcher.SaveAndCloseScript(new TabConfig { Llm = "codex", Directory = root }, "saved");
            var psi = new ProcessStartInfo("bash") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true };
            psi.ArgumentList.Add("-c"); psi.ArgumentList.Add(script);
            using var p = Process.Start(psi)!;
            Assert.True(p.WaitForExit(10000));
            Assert.Equal(0, p.ExitCode);
            var args = File.ReadAllLines(Path.Combine(root, "args"));
            Assert.Equal(["exec", "resume", "--last", Launcher.SaveAndClosePrompt], args);
            var log = TabLog.Parse(File.ReadAllText(Path.Combine(root, "tab1.log")));
            Assert.True(CloseAllLogic.SaveTurnDone(log, "saved"));
            Assert.Equal(0, log.CliExit);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void CloseScriptChecksTheShellIdentityAndNeverTargetsTheTerminalServer()
    {
        var script = Launcher.CloseTabScript(4242, 12345);
        Assert.Contains("/proc/4242/stat", script);
        Assert.DoesNotContain("cut -d", script);
        Assert.Contains("= 12345 ] || exit 0", script);
        Assert.Contains("kill -TERM -- -$g", script);
        Assert.Contains("/proc/4242/task/4242/children", script);
        Assert.Contains("kill -HUP 4242", script);
        Assert.Contains("bs_signal KILL", script);
        Assert.DoesNotContain("pkill", script);
        Assert.DoesNotContain("wt.exe", script);
    }

    [Fact]
    public void RealCloseStopsTheRecordedShellButPreservesAnUnrelatedProcess()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var shell = Process.Start(new ProcessStartInfo("bash") { ArgumentList = { "-c", "trap '' TERM HUP; sleep 60 & wait; while :; do :; done" }, RedirectStandardOutput = true })!;
        using var unrelated = Process.Start("sleep", "60")!;
        try
        {
            var start = TabStatus.ParseProcStartTime(File.ReadAllText($"/proc/{shell.Id}/stat"))!.Value;
            // An incorrect start time may belong to a reused PID and must do nothing.
            Launcher.RunTabControl(Launcher.CloseTabScript(shell.Id, start + 1), "Ubuntu");
            Assert.False(shell.HasExited);
            Launcher.RunTabControl(Launcher.CloseTabScript(shell.Id, start), "Ubuntu");
            Assert.True(shell.WaitForExit(5000));
            Assert.False(unrelated.HasExited);
        }
        finally
        {
            if (!shell.HasExited) shell.Kill(entireProcessTree: true);
            unrelated.Kill();
        }
    }
    [Fact]
    public void RealPtyRunsQueuedSaveAfterStoppingTheOriginalCliThenClosesOnlyItsTab()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/script")) return;
        var root = Directory.CreateTempSubdirectory("booster-close-pty").FullName;
        var session = new TabSession(root, DateTime.UtcNow);
        var track = session.NewTrack()!;
        var body = "codex() { if [ \"$1\" = exec ]; then printf '%s\\n' \"$@\" > saved-args; else sleep 60; fi; }\nexport -f codex\n"
            + Launcher.TabScript(new TabConfig { Llm = "codex", Directory = session.Directory }, track: track);
        var psi = new ProcessStartInfo("script") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add("-qefc");
        psi.ArgumentList.Add("bash --noprofile --norc -ic " + "'" + body.Replace("'", "'\\''") + "'");
        psi.ArgumentList.Add("/dev/null");
        using var process = Process.Start(psi)!;
        try
        {
            TabLog? log = null;
            Assert.True(SpinWait.SpinUntil(() => (log = session.ReadLog(track.Id))?.CliStarted == true, 5000));
            var pid = log!.Pid!.Value;
            var start = log.StartTime!.Value;
            session.QueueNext(track.Id, Launcher.SaveAndCloseScript(new TabConfig { Llm = "codex", Directory = session.Directory }, "pty-save"));
            Launcher.RunTabControl(Launcher.StopTrackedForegroundScript(pid, start), "Ubuntu");
            Assert.True(SpinWait.SpinUntil(() => (log = session.ReadLog(track.Id)) is not null && CloseAllLogic.SaveTurnDone(log, "pty-save"), 10000));
            Assert.Equal(0, log!.CliExit);
            Assert.Equal(["exec", "resume", "--last", Launcher.SaveAndClosePrompt], File.ReadAllLines(Path.Combine(session.Directory, "saved-args")));
            Launcher.RunTabControl(Launcher.CloseTabScript(pid, start), "Ubuntu");
            Assert.True(process.WaitForExit(5000));
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            Directory.Delete(root, true);
        }
    }

}
