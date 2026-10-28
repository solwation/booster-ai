using System.Text;
using BoosterAI;
using Xunit;

public class TabLogTests
{
    [Fact]
    public void ParsesAllEvents()
    {
        var log = TabLog.Parse("1793007089922 start pid=7926 starttime=41427\n1793007089928 cli-start\n" +
                               "1793007090936 cli-exit rc=3\n1793007161230 exit\n");
        Assert.Equal(new TabLog(7926, 41427, true, 3, true), log);
    }

    [Fact]
    public void IgnoresBlankBrokenAndHalfWrittenLines()
    {
        var log = TabLog.Parse("\r\ngarbage line\n1793007089922 start pid=12 starttime=99\r\n1793007089928 cli-start\n17930070");
        Assert.Equal(new TabLog(12, 99, true, null, false), log);
    }

    [Fact]
    public void MissingValuesStayNull() =>
        Assert.Equal(new TabLog(null, null, false, -1, false), TabLog.Parse("1 start pid=x\n2 cli-exit\n"));
}

public class TabStatusTests
{
    private static readonly TimeSpan Soon = TimeSpan.FromSeconds(3);
    private static readonly TabLog Running = new(10, 500, true, null, false);
    private static long? Alive(int pid) => 500;

    [Fact]
    public void NoLogIsStartingThenNotStarted()
    {
        Assert.Equal(TabState.Starting, TabStatus.Evaluate(null, Soon, Alive));
        Assert.Equal(TabState.NotStarted, TabStatus.Evaluate(null, TabStatus.StartTimeout, Alive));
    }

    [Fact]
    public void RunningWhileTheShellLives() => Assert.Equal(TabState.Running, TabStatus.Evaluate(Running, Soon, Alive));

    [Fact]
    public void CliExitCodes()
    {
        Assert.Equal(TabState.CliExited, TabStatus.Evaluate(Running with { CliExit = 3 }, Soon, Alive));
        Assert.Equal(TabState.CliExited, TabStatus.Evaluate(Running with { CliExit = 0 }, Soon, Alive));
        Assert.Equal(TabState.CliMissing, TabStatus.Evaluate(Running with { CliExit = 127 }, Soon, Alive));
        Assert.Equal(TabState.CliMissing, TabStatus.Evaluate(Running with { CliExit = 126 }, Soon, Alive));
    }

    [Fact]
    public void FailedCdBeforeTheCliIsNotStarted() =>
        Assert.Equal(TabState.NotStarted, TabStatus.Evaluate(Running with { CliStarted = false, CliExit = 1 }, Soon, Alive));

    [Fact]
    public void ExitLineMeansClosedEvenIfTheProcessIsGone()
    {
        Assert.Equal(TabState.Closed, TabStatus.Evaluate(Running with { Exited = true }, Soon, _ => null));
        Assert.Equal(TabState.Closed, TabStatus.Evaluate(Running with { CliExit = 3, Exited = true }, Soon, Alive));
    }

    [Fact]
    public void LostWhenThePidIsGoneOrReused()
    {
        Assert.Equal(TabState.Lost, TabStatus.Evaluate(Running, Soon, _ => null));
        Assert.Equal(TabState.Lost, TabStatus.Evaluate(Running, Soon, _ => 501));
        Assert.Equal(TabState.Lost, TabStatus.Evaluate(Running with { CliExit = 3 }, Soon, _ => null));
    }

    [Fact]
    public void WithoutLivenessOrStartTimeTrustTheLog()
    {
        Assert.Equal(TabState.Running, TabStatus.Evaluate(Running, Soon, null));
        Assert.Equal(TabState.Running, TabStatus.Evaluate(Running with { StartTime = null }, Soon, _ => 123));
    }

    [Fact]
    public void OnlyUnexpectedStatesNotify()
    {
        Assert.False(TabStatus.IsUnexpected(TabState.Running, null));
        Assert.False(TabStatus.IsUnexpected(TabState.CliExited, 0));
        Assert.True(TabStatus.IsUnexpected(TabState.CliExited, 1));
        foreach (var s in new[] { TabState.Closed, TabState.Lost, TabState.NotStarted, TabState.CliMissing })
            Assert.True(TabStatus.IsUnexpected(s, null));
    }

    [Fact]
    public void ProcStatStartTimeSurvivesSpacesInTheName()
    {
        const string rest = " S 1 2 2 0 -1 4194560 100 0 0 0 1 2 0 0 20 0 1 0 41427 9000 100";
        Assert.Equal(41427, TabStatus.ParseProcStartTime("7926 (bash)" + rest));
        Assert.Equal(41427, TabStatus.ParseProcStartTime("7926 (my ) cli)" + rest));
        Assert.Null(TabStatus.ParseProcStartTime("garbage"));
    }

    [Fact]
    public void SessionWritesAnLfLibAndNumbersTheTabs()
    {
        var root = Path.Combine(Path.GetTempPath(), "bs-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var s = new TabSession(root, new DateTime(2026, 10, 26, 12, 0, 0));
            Assert.Equal("BoosterAI-20261026-120000", s.WindowName);
            Assert.DoesNotContain('\r', File.ReadAllText(Path.Combine(s.Directory, "tab-lib.sh")));
            var a = s.NewTrack()!;
            Assert.Equal("tab1", a.Id);
            Assert.Equal("tab2", s.NewTrack()!.Id);
            Assert.EndsWith("/tab-lib.sh", a.Lib);
            File.WriteAllText(Path.Combine(s.Directory, "tab1.log"), "1 start pid=5 starttime=6\n");
            Assert.Equal(5, s.ReadLog("tab1")!.Pid);
            Assert.Null(s.ReadLog("tab2"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void OutputActivityBecomesIdleAfterOneQuietMinuteAndHooksWin()
    {
        var root = Path.Combine(Path.GetTempPath(), "bs-activity-" + Guid.NewGuid().ToString("N"));
        var now = DateTime.UtcNow;
        try
        {
            var session = new TabSession(root, now);
            var track = session.NewTrack()!;
            var log = new TabLog(5, 6, true, null, false);
            var timing = Path.Combine(session.Directory, track.Id + ".timing");
            Assert.Equal(Activity.Unknown, session.ReadActivity(track.Id, log, now));
            File.WriteAllText(timing, "0.1 7\n");
            File.SetLastWriteTimeUtc(timing, now.AddSeconds(-2));
            Assert.Equal(Activity.Busy, session.ReadActivity(track.Id, log, now));

            File.SetLastWriteTimeUtc(timing, now.AddSeconds(-TabStatus.OutputIdleTimeout.TotalSeconds - 1));
            Assert.Equal(Activity.Idle, session.ReadActivity(track.Id, log, now));
            Assert.Equal(Activity.Busy, session.ReadActivity(track.Id, log with { Activity = Activity.Busy }, now));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void TrackedInteractiveCommandShowsOutputAndWritesOnlyTiming()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/script")) return;
        var root = Path.Combine(Path.GetTempPath(), "bs-pty-" + Guid.NewGuid().ToString("N"));
        try
        {
            var session = new TabSession(root, DateTime.UtcNow);
            File.WriteAllText(Path.Combine(session.Directory, "tab-lib.sh"), TabSession.Lib);
            var psi = new System.Diagnostics.ProcessStartInfo("bash") { RedirectStandardOutput = true, WorkingDirectory = session.Directory };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add("shopt -s expand_aliases && alias printf='printf alias:%s' && . ./tab-lib.sh . tab1 && bs_cli && bs_run 'printf sample-output'");
            using var process = System.Diagnostics.Process.Start(psi)!;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            Assert.Equal(0, process.ExitCode);
            Assert.Contains("alias:sample-output", output);
            var timing = File.ReadAllText(Path.Combine(session.Directory, "tab1.timing"));
            Assert.DoesNotContain("sample-output", timing);
            Assert.Equal(Activity.Busy, session.ReadActivity("tab1", session.ReadLog("tab1"), DateTime.UtcNow));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}

public class TrackedScriptTests
{
    private static readonly TabTrack Track = new("/mnt/c/Users/o'l w/s/tab-lib.sh", "/mnt/c/Users/o'l w/s", "tab1");

    [Fact]
    public void LogsAroundTheCliAndKeepsTheShellAsAChild()
    {
        var script = Launcher.TabScript(new TabConfig { Directory = "~/proj", Llm = "claude", Model = "claude-opus-5-5", Orkester = true }, null, Track);
        Assert.Contains("(bs_cli 2>/dev/null || true) && bs_run ", script);
        Assert.Contains("claude --model", script);
        Assert.Contains("/orkester", script);
        Assert.Contains("(bs_done 0 2>/dev/null || true) || (bs_done $? 2>/dev/null || true)", script);
        Assert.EndsWith("&& bash", script);
    }

    [Fact]
    public void ShellOnlyTabIsTrackedToo()
    {
        var script = Launcher.TabScript(new TabConfig { Llm = Catalog.ShellOnly }, null, Track);
        Assert.Contains("cd ~ || true && (bs_cli 2>/dev/null || true) && bs_run 'bash'", script);
    }

    [Fact]
    public void TrackedScriptsNeverContainSemicolons()
    {
        foreach (var llm in Catalog.Llms)
        {
            Assert.DoesNotContain(';', Launcher.TabScript(new TabConfig { Llm = llm, Orkester = true, Model = "x" }, null, Track));
            Assert.DoesNotContain(';', Launcher.TabScript(new TabConfig { Llm = llm }, "hej", Track));
        }
    }

    [Fact]
    public void RestartsJoinTheSessionWindow()
    {
        var args = Launcher.WindowsTerminalArgs([new TabConfig { Name = "A" }, new TabConfig { Name = "B" }], "Ubuntu", null,
            [Track, Track with { Id = "tab2" }], "BoosterAI-x");
        Assert.Equal(["-w", "BoosterAI-x"], args.Take(2));
        Assert.Contains(args, a => a.Contains("'tab2'"));
    }
}

public class NotifierTests
{
    [Fact]
    public void ToastEscapesXmlAndPowerShellQuotes()
    {
        var script = Notifier.ToastScript("BoosterAI", "Rock 'n' <roll> & co");
        Assert.Contains("Rock &apos;n&apos; &lt;roll&gt; &amp; co", script);
        Assert.Contains($"CreateToastNotifier('{Notifier.AppId}')", script);
    }

    [Fact]
    public void ToastRunsHiddenWithTheScriptEncoded()
    {
        var args = Notifier.ToastArgs("T", "B");
        Assert.Equal(["-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-EncodedCommand"], args[..5]);
        Assert.Equal(Notifier.ToastScript("T", "B"), Encoding.Unicode.GetString(Convert.FromBase64String(args[5])));
    }
}

public class DeniedCommandTests
{
    private static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));

    [Fact]
    public void ParsesDeniedCommandLines()
    {
        var log = TabLog.Parse($"1 start pid=5 starttime=9\n2 cli-start\n3 denied cli=agy cmd={B64("git status && gh auth status")}\n4 cli-exit rc=0\n");
        Assert.Equal(["git status && gh auth status"], log.Denied!);
        Assert.Equal(TabState.Denied, TabStatus.Evaluate(log, TimeSpan.Zero, null));
        Assert.True(TabStatus.IsUnexpected(TabState.Denied, 0));
    }

    [Fact]
    public void BrokenOrEmptyCommandStillCountsAsDenied() =>
        Assert.Equal(["", ""], TabLog.Parse("1 denied cli=agy cmd=\n2 denied cli=agy cmd=%%%\n").Denied!);

    [Fact]
    public void NoDenialKeepsTheNormalExit() =>
        Assert.Equal(TabState.CliExited, TabStatus.Evaluate(TabLog.Parse("1 start pid=5\n2 cli-start\n3 cli-exit rc=0\n"), TimeSpan.Zero, null));

    /// <summary>The real tab-lib.sh formatter on an agy stream-json run that stopped at a denied command (recorded 2026-10-26).</summary>
    [Fact]
    public void FormatterLogsTheDeniedAgyCommand()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/python3")) return;
        var dir = Directory.CreateTempSubdirectory("bs-denied").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "tab-lib.sh"), TabSession.Lib);
            File.WriteAllText(Path.Combine(dir, "stream.jsonl"), """
                {"event":"init","conversation_id":"c","init":{"model":"m"}}
                {"event":"step_update","step_update":{"step_index":2,"state":"ACTIVE","step_type":"tool","tool_name":"run_command","tool_info":{"name":"run_command","parameters":{"CommandLine":"git status && gh auth status"}}}}
                {"event":"result","result":{"status":"SUCCESS","response":"","num_turns":1,"denied_actions":[{"action":"command","display_name":"RunCommand"}]}}
                """);
            var psi = new System.Diagnostics.ProcessStartInfo("bash") { RedirectStandardOutput = true, WorkingDirectory = dir };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(". ./tab-lib.sh . t && bs_stream cat stream.jsonl");
            using var p = System.Diagnostics.Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            Assert.Contains("denied without asking: git status && gh auth status", output);
            Assert.Equal(["git status && gh auth status"], TabLog.Parse(File.ReadAllText(Path.Combine(dir, "t.log"))).Denied!);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private const string TwoTurns = """
        {"type":"assistant","message":{"content":[{"type":"text","text":"first"}]}}
        {"type":"result","is_error":false,"num_turns":11,"duration_ms":62000}
        {"type":"assistant","message":{"content":[{"type":"text","text":"second"}]}}
        {"type":"result","is_error":false,"num_turns":3,"duration_ms":9000}
        """;

    private static string RunStream(string stream, string? path = null) => RunStreamWithLog(stream, path).Output;

    /// <summary>The formatter's output and the tab log it wrote (turn-done / turn-start events).</summary>
    private static (string Output, string Log) RunStreamWithLog(string stream, string? path = null)
    {
        var dir = Directory.CreateTempSubdirectory("bs-turns").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "tab-lib.sh"), TabSession.Lib);
            File.WriteAllText(Path.Combine(dir, "stream.jsonl"), stream + "\n");
            var psi = new System.Diagnostics.ProcessStartInfo("/bin/bash") { RedirectStandardOutput = true, WorkingDirectory = dir };
            if (path != null) psi.Environment["PATH"] = path;
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(". ./tab-lib.sh . t && bs_stream sh -c 'cat stream.jsonl; exit 3'; echo \"rc=$?\"");
            using var p = System.Diagnostics.Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            var log = Path.Combine(dir, "t.log");
            return (output, File.Exists(log) ? File.ReadAllText(log) : "");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private static string[] TurnEvents(string log) => log.Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(l => l.Split(' ')[1]).Where(e => e.StartsWith("turn-", StringComparison.Ordinal)).ToArray();

    /// <summary>Each turn end is logged for BoosterAI (Watching), and the next output logs that the CLI works again.</summary>
    [Fact]
    public void FormatterLogsTurnEndsAndResumedOutput()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/python3")) return;
        var (_, log) = RunStreamWithLog(TwoTurns);
        Assert.Equal(["turn-done", "turn-start", "turn-done"], TurnEvents(log));
        var parsed = TabLog.Parse(log);
        Assert.NotNull(parsed.WatchingSince);
    }

    [Fact]
    public void FormatterLogsAgyTurnEndsToo()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/python3")) return;
        var (_, log) = RunStreamWithLog("""
            {"event":"result","result":{"status":"SUCCESS","num_turns":2}}
            {"event":"step_update","step_update":{"text_delta":"again"}}
            """);
        Assert.Equal(["turn-done", "turn-start"], TurnEvents(log));
        Assert.Null(TabLog.Parse(log).WatchingSince);
    }

    /// <summary>Every result event ends a turn, not the run: only the CLI's exit prints the final line, with its own code.</summary>
    [Fact]
    public void FormatterSeparatesTurnEndsFromTheRunExit()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/python3")) return;
        var lines = RunStream(TwoTurns).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(["first", "== turn done: 11 turns, 62 s", "second", "== turn done: 3 turns, 9 s"], lines[..4]);
        Assert.Matches(@"^== exit 3, total \d+ s$", lines[4]);
        Assert.Equal("rc=3", lines[5]);
    }

    [Fact]
    public void FallbackFormatterFollowsTheSamePrinciple()
    {
        if (!OperatingSystem.IsLinux()) return;
        var bin = Directory.CreateTempSubdirectory("bs-nopy").FullName;
        try
        {
            foreach (var tool in new[] { "sh", "cat", "grep", "sed", "touch", "seq" })
                File.CreateSymbolicLink(Path.Combine(bin, tool), "/usr/bin/" + tool);
            var (output, log) = RunStreamWithLog(TwoTurns, bin);
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(["first", "== turn done", "second", "== turn done"], lines[..4]);
            Assert.Equal(["turn-done", "turn-start", "turn-done"], TurnEvents(log));
            Assert.Matches(@"^== exit 3, total \d+ s$", lines[4]);
            Assert.Equal("rc=3", lines[5]);
        }
        finally { Directory.Delete(bin, recursive: true); }
    }

    /// <summary>bs_stream applies the user's alias for the CLI (agy='agy --dangerously-skip-permissions'), which bash itself skips for an argument.</summary>
    [Fact]
    public void StreamAppliesTheCliAlias()
    {
        if (!OperatingSystem.IsLinux()) return;
        var dir = Directory.CreateTempSubdirectory("bs-alias").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "tab-lib.sh"), TabSession.Lib);
            var psi = new System.Diagnostics.ProcessStartInfo("bash") { RedirectStandardOutput = true, WorkingDirectory = dir };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add("shopt -s expand_aliases && alias printf='printf %s,%s,%s --skip' && . ./tab-lib.sh . t && bs_stream printf 'two words' x");
            using var p = System.Diagnostics.Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            Assert.StartsWith("--skip,two words,x", output.Trim());
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}

public class InPlaceRestartTests
{
    private static readonly TabTrack Track = new("/s/tab-lib.sh", "/s", "tab1");
    private static TabConfig Tab(string llm, string model = "x", bool orkester = true) =>
        new() { Directory = "~/proj", Llm = llm, Model = model, Orkester = orkester };

    [Fact]
    public void ModelChangeOnClaudeContinuesTheConversation()
    {
        var script = Launcher.RestartScript(Tab("claude", "y"), Track, resume: true);
        Assert.Contains("&& bs_run 'claude --continue --model", script);
        Assert.Contains("--settings", script);
        Assert.Contains("(bs_done 0 2>/dev/null || true) || (bs_done $? 2>/dev/null || true)", script);
    }

    [Fact]
    public void ModelChangeOnCodexResumesTheLastSession() => Assert.Contains(
        "&& bs_run 'codex resume --last --model", Launcher.RestartScript(Tab("codex", "y"), Track, resume: true));

    [Fact]
    public void LlmChangeStartsFreshWithTheOrchestra() => Assert.Contains(
        "&& bs_run 'agy --model", Launcher.RestartScript(Tab("agy"), Track, resume: false));

    [Fact]
    public void RestartScriptsHaveNoSemicolonsAndNoTrailingShell()
    {
        foreach (var llm in Catalog.Llms)
            foreach (var resume in new[] { false, true })
            {
                var script = Launcher.RestartScript(Tab(llm), Track, resume);
                Assert.DoesNotContain(';', script);
                Assert.False(script.EndsWith("bash"), script);
            }
    }

    [Theory]
    [InlineData("claude", "claude", false, true)]
    [InlineData("codex", "codex", false, true)]
    [InlineData("agy", "agy", false, false)]
    [InlineData("claude", "codex", false, false)]
    [InlineData("claude", "claude", true, false)]   // a one-shot run starts fresh
    public void ResumesOnlyAModelChangeOnClaudeOrCodex(string from, string to, bool once, bool resumes)
    {
        var target = Tab(to);
        target.NonInteractive = once;
        Assert.Equal(resumes, Launcher.CanResume(Tab(from), target));
    }

    [Fact]
    public void TurnEventsTrackWatchingUntilOutputOrANewCli()
    {
        Assert.Null(TabLog.Parse("1 start pid=5\n2 cli-start\n").WatchingSince);
        Assert.Equal(300, TabLog.Parse("1 start pid=5\n2 cli-start\n300 turn-done\n").WatchingSince);
        Assert.Equal(500, TabLog.Parse("1 start pid=5\n2 cli-start\n300 turn-done\n400 turn-start\n500 turn-done\n").WatchingSince);
        Assert.Null(TabLog.Parse("1 start pid=5\n2 cli-start\n300 turn-done\n400 turn-start\n").WatchingSince);
        Assert.Null(TabLog.Parse("1 start pid=5\n2 cli-start\n300 turn-done\n400 cli-exit rc=0\n500 cli-start\n").WatchingSince);
        // Watching is still a running tab: the CLI lives.
        Assert.Equal(TabState.Running, TabStatus.Evaluate(TabLog.Parse("1 start pid=5\n2 cli-start\n3 turn-done\n"), TimeSpan.Zero, null));
    }

    [Fact]
    public void ReadActivityShowsWatchingUntilOutputResumesOrTheCliExits()
    {
        var dir = Directory.CreateTempSubdirectory("bs-watch").FullName;
        try
        {
            var session = TabSession.Open(dir);
            var now = DateTime.UtcNow;
            File.WriteAllText(Path.Combine(dir, "tab1.timing"), "");
            File.SetLastWriteTimeUtc(Path.Combine(dir, "tab1.timing"), now.AddMinutes(-20));
            var watching = TabLog.Parse("1 start pid=5\n2 cli-start\n3 turn-done\n");
            Assert.Equal(Activity.Watching, session.ReadActivity("tab1", watching, now));
            // Output again: back to the output timing (busy while it writes).
            File.SetLastWriteTimeUtc(Path.Combine(dir, "tab1.timing"), now);
            Assert.Equal(Activity.Busy, session.ReadActivity("tab1", TabLog.Parse("1 start pid=5\n2 cli-start\n3 turn-done\n4 turn-start\n"), now));
            // The CLI exited: no longer watching (the state becomes CliExited).
            var exited = TabLog.Parse("1 start pid=5\n2 cli-start\n3 turn-done\n4 cli-exit rc=0\n");
            Assert.NotEqual(Activity.Watching, session.ReadActivity("tab1", exited, now));
            Assert.Equal(TabState.CliExited, TabStatus.Evaluate(exited, TimeSpan.Zero, null));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void WatchingMinutesCountFromTheTurnEnd()
    {
        var now = new DateTime(2026, 10, 28, 10, 0, 0, DateTimeKind.Utc);
        var ms = new DateTimeOffset(now.AddMinutes(-12).AddSeconds(-30)).ToUnixTimeMilliseconds();
        Assert.Equal(12, TabStatus.WatchingMinutes(ms, now));
        Assert.Equal(0, TabStatus.WatchingMinutes(null, now));
        Assert.Equal(0, TabStatus.WatchingMinutes(new DateTimeOffset(now.AddMinutes(1)).ToUnixTimeMilliseconds(), now));
    }

    [Fact]
    public void OneShotTabGoesRunningWatchingRunningIdle()
    {
        var vm = new MainViewModel(new AppConfig { Tabs = [new TabConfig()] });
        var tab = vm.Tabs[0];
        var launched = tab.ToConfig(forLaunch: true);
        launched.NonInteractive = true;
        launched.Orkester = true;
        tab.LaunchedAs = launched;

        tab.SetState(TabState.Running, null, activity: Activity.Busy);
        Assert.Equal(Loc.Get("tabstate.busy"), tab.StatusText);
        var running = ((Avalonia.Media.ISolidColorBrush)tab.StatusBrush).Color;

        var since = DateTimeOffset.UtcNow.AddMinutes(-12).ToUnixTimeMilliseconds();
        tab.SetState(TabState.Running, null, activity: Activity.Watching, watchingSince: since);
        Assert.Equal(Activity.Watching, tab.Activity);
        Assert.Equal(Loc.Format("tabstate.watching", 12), tab.StatusText);
        Assert.NotEqual(Loc.Get("tabstate.idle"), tab.StatusText);
        Assert.NotEqual(running, ((Avalonia.Media.ISolidColorBrush)tab.StatusBrush).Color);
        Assert.NotEqual(tab.StatusText, tab.StatusTip);
        Assert.False(tab.CanRestart);
        // A repeated poll changes nothing (no notification) but keeps the minutes current.
        Assert.False(tab.SetState(TabState.Running, null, activity: Activity.Watching, watchingSince: since));

        // Output again (timing idle after a quiet minute still counts as busy for a one-shot run).
        tab.SetState(TabState.Running, null, activity: Activity.Idle);
        Assert.Equal(Loc.Get("tabstate.busy"), tab.StatusText);

        tab.SetState(TabState.Running, null, activity: Activity.Watching, watchingSince: since);
        tab.SetState(TabState.CliExited, 0);
        Assert.Equal(Loc.Get("tabstate.idle"), tab.StatusText);
        Assert.True(tab.CanRestart);
    }

    [Theory]
    [InlineData("claude", true)]
    [InlineData("codex", true)]
    [InlineData("agy", true)]
    [InlineData(Catalog.ShellOnly, false)]
    public void ExplicitContinueIsOfferedOnlyAfterAnInteractiveTabEnds(string llm, bool supported)
    {
        var vm = new MainViewModel(new AppConfig { Tabs = [new TabConfig { Llm = llm }] });
        var tab = vm.Tabs[0];
        tab.LaunchedAs = tab.ToConfig(forLaunch: true);
        Assert.False(tab.CanContinueClosed);
        var raised = new List<string?>();
        tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        tab.SetState(TabState.Running, null, activity: Activity.Idle);
        Assert.False(tab.CanContinueClosed);
        tab.SetState(TabState.CliExited, 0);
        Assert.Equal(supported, tab.CanContinueClosed);
        Assert.Contains(nameof(TabViewModel.CanContinueClosed), raised);
        tab.SetState(TabState.Closed, null);
        Assert.Equal(supported, tab.CanContinueClosed);
        tab.SetState(TabState.Starting, null);
        Assert.False(tab.CanContinueClosed);
        tab.LaunchedAs.NonInteractive = true;
        tab.LaunchedAs.Orkester = true;
        tab.SetState(TabState.CliExited, 0);
        Assert.False(tab.CanContinueClosed);
    }

    [Fact]
    public void ActivityFollowsTheHooksAndANewCliStartsOver()
    {
        Assert.Equal(Activity.Unknown, TabLog.Parse("1 start pid=5\n2 cli-start\n").Activity);
        Assert.Equal(Activity.Busy, TabLog.Parse("1 start pid=5\n2 cli-start\n3 busy\n").Activity);
        Assert.Equal(Activity.Idle, TabLog.Parse("1 start pid=5\n2 cli-start\n3 busy\n4 idle\n").Activity);
        var restarted = TabLog.Parse("1 start pid=5\n2 cli-start\n3 idle\n4 cli-exit rc=143\n5 cli-start\n");
        Assert.Equal(Activity.Unknown, restarted.Activity);
        Assert.Null(restarted.CliExit);
        Assert.Equal(TabState.Running, TabStatus.Evaluate(restarted, TimeSpan.Zero, null));
    }

    [Fact]
    public void StopsOnlyAForegroundGroupThatIsNotTheShell()
    {
        var script = Launcher.StopForegroundScript(42);
        Assert.Contains("/proc/42/stat", script);
        Assert.Contains("[ \"$g\" != 42 ]", script);
        Assert.EndsWith("kill -TERM -- -$g", script);
    }

    private static TabViewModel Running(string llm, string model)
    {
        var vm = new MainViewModel(new AppConfig { Tabs = [new TabConfig { Llm = llm, Model = model }] });
        var tab = vm.Tabs[0];
        tab.LaunchedAs = tab.ToConfig(forLaunch: true);
        tab.SetState(TabState.Running, null);
        return tab;
    }

    [Fact]
    public void ChangingLlmOrModelOfARunningTabOffersARestart()
    {
        var tab = Running("claude", "claude-opus-5-5");
        Assert.False(tab.IsChanged);
        Assert.False(tab.CanRestart);
        tab.Model = "claude-sonnet-5-5";
        Assert.True(tab.IsChanged);
        Assert.True(tab.CanRestart);
        Assert.True(tab.RestartResumes);
        tab.Llm = "codex";
        Assert.False(tab.RestartResumes);
        tab.Llm = "claude";
        tab.Model = "claude-opus-5-5";
        Assert.False(tab.IsChanged);
    }

    [Fact]
    public void WaitingForIdleEndsWhenTheChoiceIsUndoneOrTheTabStops()
    {
        var tab = Running("claude", "claude-opus-5-5");
        tab.Model = "claude-sonnet-5-5";
        tab.PendingRestart = true;
        tab.Model = "claude-opus-5-5";
        Assert.False(tab.PendingRestart);
        tab.Model = "claude-sonnet-5-5";
        tab.PendingRestart = true;
        tab.SetState(TabState.Closed, null);
        Assert.False(tab.PendingRestart);
    }

    /// <summary>The real tab-lib.sh: bs_next runs every queued restart and removes it; the hook command logs busy.</summary>
    [Fact]
    public void LibRunsQueuedRestartsAndHooksLogActivity()
    {
        if (!OperatingSystem.IsLinux()) return;
        var dir = Directory.CreateTempSubdirectory("bs-next").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "tab-lib.sh"), TabSession.Lib);
            File.WriteAllText(Path.Combine(dir, "t.next"), "echo first >> out && printf 'echo second >> out\\n' > t.next\n");
            var hook = System.Text.Json.JsonDocument.Parse(TabSession.HooksJson(Path.Combine(dir, "t.log")))
                .RootElement.GetProperty("hooks").GetProperty("UserPromptSubmit")[0].GetProperty("hooks")[0].GetProperty("command").GetString()!;
            var psi = new System.Diagnostics.ProcessStartInfo("bash") { WorkingDirectory = dir };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(". ./tab-lib.sh . t && bs_next && sh -c \"$0\"");
            psi.ArgumentList.Add(hook);
            using (var p = System.Diagnostics.Process.Start(psi)!) p.WaitForExit();
            Assert.Equal("first\nsecond\n", File.ReadAllText(Path.Combine(dir, "out")));
            Assert.False(File.Exists(Path.Combine(dir, "t.next")));
            Assert.Equal(Activity.Busy, TabLog.Parse(File.ReadAllText(Path.Combine(dir, "t.log"))).Activity);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}

public class TabActivityStatusTests
{
    [Fact]
    public void RunningActivityUsesRunningAndSuccessfulExitBecomesIdleWithRestart()
    {
        var vm = new MainViewModel(new AppConfig { Tabs = [new TabConfig()] });
        var tab = vm.Tabs[0];

        tab.SetState(TabState.Running, null, activity: Activity.Busy);
        Assert.Equal(Loc.Get("tabstate.busy"), tab.StatusText);
        tab.SetState(TabState.Running, null, activity: Activity.Idle);
        Assert.Equal(Loc.Get("tabstate.idle"), tab.StatusText);
        tab.SetState(TabState.Running, null);
        Assert.Equal(Loc.Get("tabstate.unknownActivity"), tab.StatusText);

        tab.LaunchedAs = tab.ToConfig(forLaunch: true);
        tab.SetState(TabState.CliExited, 0);
        Assert.Equal(Loc.Get("tabstate.idle"), tab.StatusText);
        Assert.True(tab.CanRestart);

        tab.SetState(TabState.CliExited, 1);
        Assert.Equal(Loc.Format("tabstate.CliExited", 1), tab.StatusText);
    }
}

public class TabOrderTests
{
    [Fact]
    public void MoveTabPersistsTheNewOrder()
    {
        var root = Path.Combine(Path.GetTempPath(), "bs-order-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            ConfigStore.UseFile(Path.Combine(root, "config.json"));
            var vm = new MainViewModel(new AppConfig
            {
                Tabs = [new TabConfig { Name = "Alpha" }, new TabConfig { Name = "Beta" }, new TabConfig { Name = "Gamma" }],
            });

            vm.MoveTab(0, 2);

            Assert.Equal(["Beta", "Gamma", "Alpha"], ConfigStore.Load().Tabs.Select(t => t.Name));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}

public class FindRunningTabsTests
{
    private static string Session(string root, string name, params (string Id, string Log, TabConfig? Tab)[] tabs)
    {
        var dir = Path.Combine(root, name);
        Directory.CreateDirectory(dir);
        foreach (var (id, log, tab) in tabs)
        {
            File.WriteAllText(Path.Combine(dir, id + ".log"), log);
            if (tab is not null)
                File.WriteAllText(Path.Combine(dir, id + ".tab.json"),
                    System.Text.Json.JsonSerializer.Serialize(tab, ConfigJsonContext.Default.TabConfig));
        }
        return dir;
    }

    // PID 10 runs since 100, PID 20 since 200; everything else is gone.
    private static long? Live(int pid) => pid switch { 10 => 100, 20 => 200, _ => null };

    [Fact]
    public void FindsOnlyLiveShellsWithTheirChoicesNewestSessionFirst()
    {
        var root = Directory.CreateTempSubdirectory("bs-find").FullName;
        try
        {
            var a = new TabConfig { Name = "a", Directory = "~/a", Llm = "codex", Model = "gpt-6-sol" };
            Session(root, "20261027-080000",
                ("tab1", "1 start pid=10 starttime=100\n2 cli-start\n", a),
                ("tab2", "1 start pid=11 starttime=5\n", new TabConfig { Name = "gone" }),          // process gone
                ("tab3", "1 start pid=20 starttime=999\n", new TabConfig { Name = "reused" }),      // PID reused
                ("tab4", "1 start pid=10 starttime=100\n2 exit\n", new TabConfig { Name = "exited" }),
                ("tab5", "1 start pid=10 starttime=100\n", null));                                 // no choices saved
            Session(root, "20261027-090000", ("tab1", "1 start pid=20 starttime=200\n3 busy\n", new TabConfig { Name = "b" }));

            var found = TabSession.FindRunning(root, Live);
            Assert.Equal(["b", "a"], found.Select(f => f.Tab.Name));
            Assert.Equal("gpt-6-sol", found[1].Tab.Model);
            Assert.Equal("tab1", found[1].Id);
            Assert.Empty(TabSession.FindRunning(root, null));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void CleanUpKeepsOldSessionsWhoseTabsStillRun()
    {
        var root = Directory.CreateTempSubdirectory("bs-clean").FullName;
        try
        {
            var live = Session(root, "old-live", ("tab1", "1 start pid=10 starttime=100\n", null));
            var dead = Session(root, "old-dead", ("tab1", "1 start pid=11 starttime=5\n", null));
            foreach (var d in new[] { live, dead }) Directory.SetCreationTime(d, DateTime.Now.AddDays(-3));
            TabSession.CleanUp(root, DateTime.Now, Live);
            Assert.True(Directory.Exists(live));
            Assert.False(Directory.Exists(dead));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void ReopenedSessionContinuesTheTabNumbers()
    {
        var root = Directory.CreateTempSubdirectory("bs-open").FullName;
        try
        {
            var dir = Session(root, "s", ("tab1", "", null), ("tab3", "", null));
            var session = TabSession.Open(dir);
            Assert.Equal("tab4", session.NewTrack()!.Id);
            Assert.Equal("tab3", session.Track("tab3").Id);
            Assert.True(File.Exists(Path.Combine(dir, "tab-lib.sh")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void RunningTabMatchesByNameAndDirectoryAndStartSkipsIt()
    {
        var vm = new MainViewModel(new AppConfig { Tabs =
        [
            new TabConfig { Name = "a", Directory = "~/a" },
            new TabConfig { Name = "b", Directory = "~/b" },
        ] });
        Assert.Null(vm.Match(new TabConfig { Name = "a", Directory = "~/other" }));
        var a = vm.Match(new TabConfig { Name = "a ", Directory = "~/a" });
        Assert.Same(vm.Tabs[0], a);
        Assert.Equal(2, vm.ToStartCount);
        a!.LaunchedAs = a.ToConfig(forLaunch: true);
        a.SetState(TabState.Running, null);
        vm.TabsChanged();
        Assert.Null(vm.Match(new TabConfig { Name = "a", Directory = "~/a" }));   // already watched
        Assert.Equal(1, vm.ToStartCount);
        Assert.True(vm.CanLaunch);
        vm.Tabs[1].SetState(TabState.Starting, null);
        Assert.Equal(0, vm.ToStartCount);
        Assert.False(vm.CanLaunch);
        Assert.Equal(Loc.Get("launch.allRunning"), vm.LaunchLabel);
    }
}
