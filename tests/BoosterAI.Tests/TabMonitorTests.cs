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
}

public class TrackedScriptTests
{
    private static readonly TabTrack Track = new("/mnt/c/Users/o'l w/s/tab-lib.sh", "/mnt/c/Users/o'l w/s", "tab1");

    [Fact]
    public void LogsAroundTheCliAndKeepsTheShellAsAChild() => Assert.Equal(
        @". '/mnt/c/Users/o'\''l w/s/tab-lib.sh' '/mnt/c/Users/o'\''l w/s' 'tab1' 2>/dev/null || true && " +
        "cd ~/'proj' && (bs_cli 2>/dev/null || true) && claude --model 'claude-opus-5-5' '/orkester' && " +
        "(bs_done 0 2>/dev/null || true) || (bs_done $? 2>/dev/null || true) && bash",
        Launcher.TabScript(new TabConfig { Directory = "~/proj", Llm = "claude", Model = "claude-opus-5-5", Orkester = true }, null, Track));

    [Fact]
    public void ShellOnlyTabIsTrackedToo() => Assert.Equal(
        @". '/mnt/c/Users/o'\''l w/s/tab-lib.sh' '/mnt/c/Users/o'\''l w/s' 'tab1' 2>/dev/null || true && cd ~ || true && bash",
        Launcher.TabScript(new TabConfig { Llm = Catalog.ShellOnly }, null, Track));

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
            Assert.Equal("--skip,two words,x", output.Trim());
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
