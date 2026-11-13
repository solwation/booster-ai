using System.Text.Json;
using BoosterAI;
using Xunit;

public class AutoClearDecisionTests
{
    private const long Busy = 1_800_000_000_000;
    private const long Idle = Busy + 60_000;
    private static readonly DateTime IdleTime = DateTimeOffset.FromUnixTimeMilliseconds(Idle).UtcDateTime;
    private static readonly TabConfig Claude = new() { Llm = "claude" };

    private static readonly TabLog IdleLog = new(10, 500, true, null, false, Activity: Activity.Idle)
    {
        BusyAt = Busy, IdleAt = Idle, InputReady = true,
    };

    /// <summary><paramref name="output"/>: the terminal's last output (default: when the turn ended); <paramref name="noOutput"/>: none recorded.</summary>
    private static AutoClear.Request? Due(TabLog? log = null, DateTime? now = null, bool enabled = true, int minutes = 5,
        TabConfig? tab = null, TabState state = TabState.Running, Activity activity = Activity.Idle, bool pendingRestart = false,
        bool closing = false, long? requestedFor = null, DateTime? output = null, bool noOutput = false) =>
        AutoClear.Due(enabled, minutes, tab ?? Claude, state, activity, pendingRestart, closing, log ?? IdleLog,
            noOutput ? null : output ?? IdleTime, requestedFor, now ?? IdleTime.AddMinutes(5));

    [Fact]
    public void FiresOnceTheTabHasBeenIdleForTheTimeout()
    {
        Assert.Null(Due(now: IdleTime.AddMinutes(5).AddSeconds(-1)));
        Assert.Equal(new AutoClear.Request(Idle, Busy), Due());
        Assert.Equal(new AutoClear.Request(Idle, Busy), Due(now: IdleTime.AddHours(3)));
        Assert.Null(Due(minutes: 10));
        Assert.NotNull(Due(minutes: 10, now: IdleTime.AddMinutes(10)));
    }

    [Fact]
    public void TimeoutIsClampedToTheAllowedRange()
    {
        Assert.NotNull(Due(minutes: 0, now: IdleTime.AddMinutes(1)));
        Assert.Null(Due(minutes: 0, now: IdleTime.AddSeconds(59)));
        Assert.Equal(AutoClear.MaxMinutes, AutoClear.Minutes(100_000));
        Assert.Null(Due(minutes: 100_000, now: IdleTime.AddMinutes(AutoClear.MaxMinutes - 1)));
    }

    [Fact]
    public void OncePerIdlePeriodUntilANewTurn()
    {
        // BoosterAI already asked for this period, or the tab already typed or skipped it (e.g. before a BoosterAI restart).
        Assert.Null(Due(requestedFor: Idle));
        Assert.Null(Due(IdleLog with { InputAt = Idle + 300_000, InputSent = true }));
        Assert.Null(Due(IdleLog with { InputAt = Idle + 300_000, InputSent = false }));
        // A new turn: busy, then the next idle starts a new period, which is cleared again.
        var later = IdleLog with { BusyAt = Idle + 400_000, IdleAt = Idle + 500_000, InputAt = Idle + 300_000, InputSent = true };
        Assert.Null(Due(later with { Activity = Activity.Busy }, now: IdleTime.AddHours(1), requestedFor: Idle));
        Assert.Equal(new AutoClear.Request(Idle + 500_000, Idle + 400_000), Due(later, now: IdleTime.AddHours(1), requestedFor: Idle));
    }

    [Fact]
    public void NeverForExcludedTabs()
    {
        Assert.Null(Due(enabled: false));
        Assert.Null(Due(activity: Activity.Busy));
        Assert.Null(Due(activity: Activity.Watching));
        Assert.Null(Due(activity: Activity.Unknown));
        Assert.Null(Due(IdleLog with { Activity = Activity.Busy }));
        foreach (var state in Enum.GetValues<TabState>().Where(s => s != TabState.Running)) Assert.Null(Due(state: state));
        Assert.Null(Due(IdleLog with { CliExit = 0 }));
        Assert.Null(Due(IdleLog with { Exited = true }));
        Assert.Null(Due(pendingRestart: true));
        Assert.Null(Due(closing: true));
        // Run once (bs_stream, claude -p).
        Assert.Null(Due(tab: new TabConfig { Llm = "claude", Orkester = true, NonInteractive = true }));
        Assert.NotNull(Due(tab: new TabConfig { Llm = "claude", Orkester = true }));
        // CLIs without a supported clear, and shells.
        foreach (var llm in new[] { "codex", "agy", Catalog.ShellOnly }) Assert.Null(Due(tab: new TabConfig { Llm = llm }));
        // Not started from this BoosterAI, or the CLI does not run under bs_pty (setting off at start, no python3, older lib).
        Assert.Null(AutoClear.Due(true, 5, null, TabState.Running, Activity.Idle, false, false, IdleLog, IdleTime, null, IdleTime.AddHours(1)));
        Assert.Null(Due(IdleLog with { InputReady = false }));
        Assert.Null(AutoClear.Due(true, 5, Claude, TabState.Running, Activity.Idle, false, false, null, IdleTime, null, IdleTime.AddHours(1)));
        // Idle without a hook "idle" (timing-based): no turn has ended.
        Assert.Null(Due(IdleLog with { IdleAt = null, Activity = Activity.Unknown }));
    }

    [Fact]
    public void IdleTimeCountsFromTheLastTerminalOutput()
    {
        // Output after the turn ended (a background agent's timer, a redraw, a key echo) moves the start of the idle time.
        Assert.Null(Due(output: IdleTime.AddMinutes(1), now: IdleTime.AddMinutes(5)));
        Assert.Null(Due(output: IdleTime.AddMinutes(1), now: IdleTime.AddMinutes(6).AddSeconds(-1)));
        Assert.Equal(new AutoClear.Request(Idle, Busy), Due(output: IdleTime.AddMinutes(1), now: IdleTime.AddMinutes(6)));
        // Output from before the turn ended does not shorten it.
        Assert.NotNull(Due(output: IdleTime.AddMinutes(-30)));
        Assert.Null(Due(output: IdleTime.AddMinutes(-30), now: IdleTime.AddMinutes(4)));
        // Fail-safe: nothing recorded about the terminal's output, no clear.
        Assert.Null(Due(noOutput: true, now: IdleTime.AddHours(3)));
    }

    /// <summary>
    /// Issue #71, tab "lunden-3d-apartment" (5 minute timeout): a conductor whose turns end while its background workers
    /// run on. The old hook logged "idle" and BoosterAI typed /clear four times into the running orchestra.
    /// </summary>
    private const string Issue71Log = """
        1793434519684 start pid=812 starttime=1373
        1793434529495 cli-start
        1793434529500 input-ready
        1793437802876 busy
        1793437828374 {0}
        1793437895399 busy
        1793437972629 {0}
        1793438112458 busy
        1793438127791 {0}
        """;

    private static DateTime At(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;

    [Fact]
    public void Issue71TurnsThatLeaveBackgroundWorkRunningAreNotIdle()
    {
        // The Stop hook sees the workers in background_tasks and logs "waiting": the tab works, however long it takes.
        var log = TabLog.Parse(string.Format(System.Globalization.CultureInfo.InvariantCulture, Issue71Log, "waiting"));
        Assert.Equal(Activity.Busy, log.Activity);
        var turnEnded = At(1793438127791);
        foreach (var after in new[] { 5, 17, 60, 240 })
            Assert.Null(AutoClear.Due(true, 5, Claude, TabState.Running, log.Activity, false, false, log, turnEnded, null, turnEnded.AddMinutes(after)));
        // A worker's task notification starts a turn (busy); when it ends with nothing left in the background, it is idle.
        var done = TabLog.Parse(string.Format(System.Globalization.CultureInfo.InvariantCulture, Issue71Log, "waiting")
            + "\n1793440256698 busy\n1793440304756 idle\n");
        Assert.Equal(new AutoClear.Request(1793440304756, 1793440256698),
            AutoClear.Due(true, 5, Claude, TabState.Running, done.Activity, false, false, done, At(1793440304756), null, At(1793440304756).AddMinutes(5)));
    }

    [Fact]
    public void Issue71OutputWhileWorkersRunHoldsOffAClaudeThatOnlyLogsIdle()
    {
        // A claude (or hooks file) without background_tasks logs "idle" as before. The workers' footer timer redraws the
        // screen every second (tab2.timing: at most 4 s without output between 10:15:27 and 10:20:29), so no clear.
        var log = TabLog.Parse(string.Format(System.Globalization.CultureInfo.InvariantCulture, Issue71Log, "idle"));
        var turnEnded = At(1793438127791);
        Assert.Equal(Activity.Idle, log.Activity);
        foreach (var after in new[] { 5, 17, 24, 36 })
        {
            var now = turnEnded.AddMinutes(after);
            Assert.Null(AutoClear.Due(true, 5, Claude, TabState.Running, log.Activity, false, false, log, now.AddSeconds(-3), null, now));
        }
        // The output stops (the workers are done): five quiet minutes later it is cleared.
        var quiet = turnEnded.AddMinutes(40);
        Assert.Null(AutoClear.Due(true, 5, Claude, TabState.Running, log.Activity, false, false, log, quiet, null, quiet.AddMinutes(4)));
        Assert.NotNull(AutoClear.Due(true, 5, Claude, TabState.Running, log.Activity, false, false, log, quiet, null, quiet.AddMinutes(5)));
    }

    [Fact]
    public void KeysAfterTheTurnStartedAreCheckedFromItsBusy()
    {
        Assert.Equal(Busy, Due()!.Value.Since);
        // No busy before the idle (or an odd order): from the idle.
        Assert.Equal(Idle, Due(IdleLog with { BusyAt = null })!.Value.Since);
        Assert.Equal(Idle, Due(IdleLog with { BusyAt = Idle + 1 })!.Value.Since);
    }

    [Fact]
    public void LogTracksInputEventsAndResetsThemForANewCli()
    {
        var log = TabLog.Parse("1 start pid=7 starttime=9\n2 cli-start\n3 input-ready\n4 busy\n5 idle\n6 input-sent\n");
        Assert.True(log.InputReady);
        Assert.Equal((4L, 5L, 6L, true), (log.BusyAt!.Value, log.IdleAt!.Value, log.InputAt!.Value, log.InputSent));
        Assert.Equal(6, AutoClear.ClearedAt(log));

        var skipped = TabLog.Parse("2 cli-start\n3 input-ready\n4 busy\n5 idle\n6 input-skipped reason=typed\n");
        Assert.False(skipped.InputSent);
        Assert.Null(AutoClear.ClearedAt(skipped));

        // A new turn hides the old clear; an exit ends input; a new CLI starts over.
        Assert.Null(AutoClear.ClearedAt(TabLog.Parse("2 cli-start\n3 input-ready\n5 idle\n6 input-sent\n7 busy\n")));
        Assert.Null(AutoClear.ClearedAt(TabLog.Parse("2 cli-start\n3 input-ready\n5 idle\n6 input-sent\n7 busy\n8 idle\n")));
        Assert.False(TabLog.Parse("2 cli-start\n3 input-ready\n4 cli-exit rc=0\n").InputReady);
        // A turn that ended with background work left ("waiting") is no idle period and hides nothing new.
        var waiting = TabLog.Parse("2 cli-start\n3 input-ready\n4 busy\n5 idle\n6 input-sent\n7 busy\n8 waiting\n");
        Assert.Equal((Activity.Busy, 7L, 5L), (waiting.Activity, waiting.BusyAt!.Value, waiting.IdleAt!.Value));
        Assert.Null(AutoClear.ClearedAt(waiting));
        var fresh = TabLog.Parse("2 cli-start\n3 input-ready\n4 busy\n5 idle\n6 input-sent\n7 cli-start\n");
        Assert.Equal((false, (long?)null, (long?)null, (long?)null), (fresh.InputReady, fresh.BusyAt, fresh.IdleAt, fresh.InputAt));
    }

    [Fact]
    public void StatusShowsWhenTheTabWasCleared()
    {
        var vm = new MainViewModel(new AppConfig { Tabs = [new TabConfig { Llm = "claude" }] });
        var tab = vm.Tabs[0];
        var at = new DateTimeOffset(2026, 10, 30, 14, 32, 0, TimeSpan.Zero);
        var local = at.ToLocalTime().ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);

        tab.SetState(TabState.Running, null, activity: Activity.Idle);
        Assert.Equal(Loc.Get("tabstate.idle"), tab.StatusText);
        tab.SetState(TabState.Running, null, activity: Activity.Idle, clearedAt: at.ToUnixTimeMilliseconds());
        Assert.Equal(Loc.Format("tabstate.idleCleared", local), tab.StatusText);
        Assert.Equal(Loc.Format("tabstate.idleCleared.tip", local), tab.StatusTip);
        tab.SetState(TabState.Running, null, activity: Activity.Busy);
        Assert.Equal(Loc.Get("tabstate.busy"), tab.StatusText);
    }
}

/// <summary>The Stop hook's command (#71) on real hook input: "waiting" while claude lists background work, else "idle".</summary>
public class AutoClearStopHookTests
{
    [Theory]
    // Captured from claude 2.1.316 (trimmed): a background shell and agent still running, then nothing left.
    [InlineData("""{"hook_event_name":"Stop","stop_hook_active":false,"last_assistant_message":"started","background_tasks":[{"id":"b85wgpx2k","type":"shell","status":"running","command":"sleep 20"},{"id":"af0f00c9582d29d52","type":"subagent","status":"running"}],"session_crons":[]}""", "waiting")]
    [InlineData("""{"hook_event_name":"Stop","stop_hook_active":false,"last_assistant_message":"noted","background_tasks":[],"session_crons":[]}""", "idle")]
    // A scheduled wakeup or /loop.
    [InlineData("""{"hook_event_name":"Stop","background_tasks":[],"session_crons":[{"id":"w1","prompt":"/loop"}]}""", "waiting")]
    [InlineData("""{"hook_event_name": "Stop", "background_tasks": [ ], "session_crons": [ {"id": "w1"} ]}""", "waiting")]
    // Only quoted inside a message: not a field.
    [InlineData("""{"hook_event_name":"Stop","last_assistant_message":"\"background_tasks\":[1]","background_tasks":[],"session_crons":[]}""", "idle")]
    // An older claude without the fields: as before (the output check still holds off a running agent).
    [InlineData("""{"hook_event_name":"Stop","stop_hook_active":false}""", "idle")]
    public void StopHookLogsWaitingWhileBackgroundWorkRuns(string input, string expected)
    {
        if (!OperatingSystem.IsLinux()) return;
        var dir = Directory.CreateTempSubdirectory("bs-stop-hook").FullName;
        try
        {
            var log = Path.Combine(dir, "it's.log");
            var hook = JsonDocument.Parse(TabSession.HooksJson(log)).RootElement.GetProperty("hooks").GetProperty("Stop")[0]
                .GetProperty("hooks")[0].GetProperty("command").GetString()!;
            var psi = new System.Diagnostics.ProcessStartInfo("sh") { RedirectStandardInput = true, WorkingDirectory = dir };
            TestShell.Configure(psi, dir);
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(hook);
            using var p = System.Diagnostics.Process.Start(psi)!;
            try
            {
                p.StandardInput.Write(input);
                p.StandardInput.Close();
                Assert.True(p.WaitForExit(10000));
            }
            finally { if (!p.HasExited) p.Kill(entireProcessTree: true); }
            var line = File.ReadAllText(log).Split(' ', '\n');
            Assert.True(long.TryParse(line[0], out _), line[0]);
            Assert.Equal(expected, line[1]);
            Assert.Equal(expected == "waiting" ? Activity.Busy : Activity.Idle, TabLog.Parse("1 cli-start\n" + File.ReadAllText(log)).Activity);
        }
        finally { TestShell.StopLeftovers(dir); Directory.Delete(dir, recursive: true); }
    }
}

public class AutoClearConfigTests
{
    [Fact]
    public void OffByDefaultWithFiveMinutesAndMissingInOlderConfigs()
    {
        Assert.False(new AppConfig().AutoClearIdle);
        Assert.Equal(5, new AppConfig().AutoClearMinutes);
        var old = JsonSerializer.Deserialize("{\"AutoUpdateClis\": false}", ConfigJsonContext.Default.AppConfig)!;
        Assert.False(old.AutoClearIdle);
        Assert.Equal(5, old.AutoClearMinutes);

        var json = JsonSerializer.Serialize(new AppConfig { AutoClearIdle = true, AutoClearMinutes = 12 }, ConfigJsonContext.Default.AppConfig);
        Assert.Contains("\"AutoClearIdle\": true", json);
        var back = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.AppConfig)!;
        Assert.Equal((true, 12), (back.AutoClearIdle, back.AutoClearMinutes));
    }

    [Fact]
    public void SettingsBoxClampsAndKeepsAValueWhenEmptied()
    {
        var cfg = new AppConfig();
        var vm = new MainViewModel(cfg);
        vm.AutoClearMinutes = 0;
        Assert.Equal(AutoClear.MinMinutes, cfg.AutoClearMinutes);
        vm.AutoClearMinutes = 15;
        vm.AutoClearMinutes = null;
        Assert.Equal(15, cfg.AutoClearMinutes);
        Assert.Equal(15m, vm.AutoClearMinutes);
    }

    [Fact]
    public void InteractiveOrchestraClaudeGetsInputEvenWithoutAutoClear()
    {
        var on = new TabTrack("/s/tab-lib.sh", "/s", "tab1", AutoClear: true);
        var off = on with { AutoClear = false };
        var claude = new TabConfig { Llm = "claude", Orkester = true };
        Assert.Contains("' input && ", Launcher.TabScript(claude, track: on));
        Assert.Contains("' input && ", Launcher.RestartScript(claude, on, resume: true));
        Assert.Contains(" input", Launcher.TabScript(claude, track: off));
        Assert.Contains(" input", Launcher.RestartScript(claude, off, resume: false));
        Assert.DoesNotContain(" input", Launcher.TabScript(new TabConfig { Llm = "claude" }, track: off));
        // Run once streams (bs_stream); codex, agy and shells are never typed into.
        Assert.DoesNotContain(" input", Launcher.TabScript(claude.WithOnce(), track: on));
        foreach (var llm in new[] { "codex", "agy", Catalog.ShellOnly })
            Assert.DoesNotContain(" input", Launcher.TabScript(new TabConfig { Llm = llm }, track: on));
        Assert.DoesNotContain(';', Launcher.TabScript(claude, track: on));
    }
}

internal static class TabConfigTestExtensions
{
    public static TabConfig WithOnce(this TabConfig tab) =>
        new() { Name = tab.Name, Directory = tab.Directory, Llm = tab.Llm, Model = tab.Model, Orkester = true, NonInteractive = true };
}

/// <summary>
/// tab-lib.sh's bs_pty on a real pseudo-terminal (#69): a request file is typed into the CLI, keys typed in the tab
/// still reach it, a request older than the last key is skipped, and nothing is left behind.
/// </summary>
public class AutoClearPtyTests
{
    [Fact]
    public void RequestIsTypedIntoTheCliAndKeyboardStillWorks()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/script") || !OnPath("python3")) return;
        var root = Directory.CreateTempSubdirectory("booster-autoclear-pty").FullName;
        var session = new TabSession(root, DateTime.UtcNow);
        var track = session.NewTrack(autoClear: true)!;
        var tab = new TabConfig { Llm = "claude", Directory = session.Directory };
        var got = Path.Combine(session.Directory, "got");
        // A fake claude: three lines, then the size of its terminal (the CLI's own pseudo-terminal under bs_pty).
        var body = "claude() { for i in 1 2 3; do IFS= read -r l && printf '%s\\n' \"$l\" >> got; done; stty size >> got; }\nexport -f claude\n"
            + Launcher.TabScript(tab, track: track);
        Assert.Contains("' input && ", body);
        var psi = new System.Diagnostics.ProcessStartInfo("script") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add("-qefc");
        psi.ArgumentList.Add("stty rows 30 cols 101 && bash --noprofile --norc -ic '" + body.Replace("'", "'\\''") + "'");
        psi.ArgumentList.Add("/dev/null");
        TestShell.Configure(psi, root);
        using var process = System.Diagnostics.Process.Start(psi)!;
        string Lines() => File.Exists(got) ? File.ReadAllText(got) : "";
        try
        {
            Assert.True(SpinWait.SpinUntil(() => session.ReadLog(track.Id) is { InputReady: true }, 30000));
            session.RequestInput(track.Id, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 1000, AutoClear.Command);
            Assert.True(SpinWait.SpinUntil(() => Lines() == "/clear\n", 10000), Lines());
            Assert.True(session.ReadLog(track.Id)!.InputSent);

            process.StandardInput.Write("typed by the user\n");
            process.StandardInput.Flush();
            Assert.True(SpinWait.SpinUntil(() => Lines().EndsWith("typed by the user\n", StringComparison.Ordinal), 10000), Lines());

            // A key after the request's start: skipped, so a draft is never sent along.
            session.RequestInput(track.Id, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 60_000, AutoClear.Command);
            Assert.True(SpinWait.SpinUntil(() => File.ReadAllText(Path.Combine(session.Directory, track.Id + ".log")).Contains("input-skipped reason=typed"), 10000));
            Assert.False(File.Exists(Path.Combine(session.Directory, track.Id + ".input")));
            session.RequestInput(track.Id, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 1000, "again\r");
            Assert.True(SpinWait.SpinUntil(() => Lines().Contains("30 101\n"), 10000), Lines());
            Assert.Equal("/clear\ntyped by the user\nagain\n30 101\n", Lines());
            Assert.True(SpinWait.SpinUntil(() => session.ReadLog(track.Id) is { CliExit: 0, InputReady: false }, 10000));

            process.StandardInput.Write("exit\n");
            process.StandardInput.Flush();
            Assert.True(process.WaitForExit(30000));
            Assert.True(SpinWait.SpinUntil(() => InDirectory(session.Directory).Length == 0, 5000));
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
            foreach (var pid in InDirectory(session.Directory))
                try { System.Diagnostics.Process.GetProcessById(pid).Kill(entireProcessTree: true); } catch (ArgumentException) { }
            TestShell.StopLeftovers(root);
            Directory.Delete(root, true);
        }
    }

    /// <summary>Stopping the foreground (an in-place restart, TERM to the tty's foreground group) also stops the CLI under bs_pty.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StoppingTheForegroundStopsTheCliUnderBsPty(bool autoClear)
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/script") || !OnPath("python3")) return;
        var root = Directory.CreateTempSubdirectory("booster-autoclear-stop").FullName;
        var session = new TabSession(root, DateTime.UtcNow);
        var track = session.NewTrack(autoClear: autoClear)!;
        var tab = new TabConfig { Llm = "claude", Directory = session.Directory };
        var body = "claude() { sleep 600; }\nexport -f claude\n" + Launcher.TabScript(tab, track: track);
        var psi = new System.Diagnostics.ProcessStartInfo("script") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add("-qefc");
        psi.ArgumentList.Add("bash --noprofile --norc -ic '" + body.Replace("'", "'\\''") + "'");
        psi.ArgumentList.Add("/dev/null");
        TestShell.Configure(psi, root);
        using var process = System.Diagnostics.Process.Start(psi)!;
        try
        {
            Assert.True(SpinWait.SpinUntil(() => session.ReadLog(track.Id) is { CliStarted: true, Pid: not null } log && log.InputReady == autoClear
                && Sleeping(session.Directory), 30000));
            Launcher.StopForeground(session.ReadLog(track.Id)!.Pid!.Value, "Ubuntu");
            Assert.True(SpinWait.SpinUntil(() => session.ReadLog(track.Id) is { CliExit: not null }, 10000));
            Assert.True(SpinWait.SpinUntil(() => !Sleeping(session.Directory), 5000));
            process.StandardInput.Write("exit\n");
            process.StandardInput.Flush();
            Assert.True(process.WaitForExit(30000));
            Assert.True(SpinWait.SpinUntil(() => InDirectory(session.Directory).Length == 0, 5000));
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
            foreach (var pid in InDirectory(session.Directory))
                try { System.Diagnostics.Process.GetProcessById(pid).Kill(entireProcessTree: true); } catch (ArgumentException) { }
            TestShell.StopLeftovers(root);
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// A hangup of the tab's terminal (the window or tab is closed, #70) ends script and the CLI under it, with and
    /// without bs_pty, and nothing of the tab is left: killing the outer script closes the pty master, like a terminal that exits.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HangingUpTheTabLeavesNoProcesses(bool autoClear)
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/script") || !OnPath("python3")) return;
        var root = Directory.CreateTempSubdirectory("booster-hangup").FullName;
        var session = new TabSession(root, DateTime.UtcNow);
        var track = session.NewTrack(autoClear: autoClear)!;
        var tab = new TabConfig { Llm = "claude", Directory = session.Directory };
        var body = "claude() { sleep 600; }\nexport -f claude\n" + Launcher.TabScript(tab, track: track);
        var psi = new System.Diagnostics.ProcessStartInfo("script") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add("-qefc");
        psi.ArgumentList.Add("bash --noprofile --norc -ic '" + body.Replace("'", "'\\''") + "'");
        psi.ArgumentList.Add("/dev/null");
        TestShell.Configure(psi, root);
        using var process = System.Diagnostics.Process.Start(psi)!;
        try
        {
            Assert.True(SpinWait.SpinUntil(() => session.ReadLog(track.Id) is { CliStarted: true, Pid: not null } log && log.InputReady == autoClear
                && Sleeping(session.Directory) && Running(session.Directory, "script"), 30000));
            process.Kill();
            Assert.True(process.WaitForExit(10000));
            Assert.True(SpinWait.SpinUntil(() => InDirectory(session.Directory).Length == 0, 15000),
                string.Join("\n", InDirectory(session.Directory).Select(pid => { try { return pid + " " + File.ReadAllText($"/proc/{pid}/cmdline").Replace('\0', ' '); } catch (IOException) { return pid + " gone"; } })));
            Assert.Contains(" exit\n", File.ReadAllText(Path.Combine(session.Directory, track.Id + ".log")));
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
            foreach (var pid in InDirectory(session.Directory))
                try { System.Diagnostics.Process.GetProcessById(pid).Kill(entireProcessTree: true); } catch (ArgumentException) { }
            TestShell.StopLeftovers(root);
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// bs_pty's own keys() (the text that ships in tab-lib.sh, run by python3) tells keys from terminal replies and mouse
    /// motion (#163): each case is one or more reads; the unfinished tail of a read is classified with the next one.
    /// </summary>
    [Fact]
    public void KeysAreToldFromTerminalReports()
    {
        if (!OperatingSystem.IsLinux() || !OnPath("python3")) return;
        var lib = TabSession.Lib;
        var start = lib.IndexOf("rep = re.compile(", StringComparison.Ordinal);
        var end = lib.IndexOf("\ndef key(", start, StringComparison.Ordinal);
        Assert.True(start > 0 && end > start);
        const string E = "\u001b";
        (string[] Reads, bool Typed, string Held)[] cases =
        [
            ([E + "[?62;22c" + E + "[>1;10;0c" + E + "[?2026;2$y" + E + "[2004;1$y" + E + "[?1u"], false, ""),
            ([E + "P>|Terminal 1.2" + E + "\\" + E + "]11;rgb:0000/0000/0000" + E + "\\" + E + "]10;rgb:ff/ff/ff\a"], false, ""),
            ([E + "[24;80R" + E + "[?5;7R" + E + "[?997;1n" + E + "[48;30;100;600;1000t" + E + "[I" + E + "[O"], false, ""),
            ([E + "[<35;10;5M" + E + "[<32;11;5M" + E + "[<64;1;1M" + E + "[<65;1;1M" + E + "[<80;1;1M"], false, ""),
            (["a"], true, ""),
            (["å"], true, ""),
            ([E + "[A"], true, ""),
            ([E + "x"], true, ""),
            ([E + "[c"], true, ""),
            ([E + "[1;5R"], true, ""), // Ctrl+F3 in xterm, or a cursor report from row 1: ambiguous, so typed.
            ([E + "[200~pasted" + E + "[201~"], true, ""),
            ([E + "[<0;5;5M"], true, ""),
            ([E + "[<0;5;5m"], true, ""),
            ([E + "[<128;5;5M"], true, ""),
            ([E + "[<35;10;5Mx" + E + "[?62c"], true, ""),
            (["y" + E + "[<35;1;1M"], true, ""),
            ([E + "[<35;1", "0;5M"], false, ""),
            ([E + "P>|Termi", "nal 1.2" + E, "\\"], false, ""),
            ([E + "]11;rgb:00", "00/0000/0000" + E + "\\k"], true, ""),
            (["k" + E + "[?62;2"], true, E + "[?62;2"),
            ([E], false, E),
            ([E, "[A"], true, ""),
            ([E + "]11;" + new string('x', 300)], true, ""),
        ];
        var driver = string.Join("\n",
            "import re, sys",
            lib[start..end],
            "for line in sys.stdin.read().splitlines():",
            "    typed, held = False, b\"\"",
            "    for read in line.split(\" \"):",
            "        t, held = keys(held + bytes.fromhex(read))",
            "        typed = typed or t",
            "    print(int(typed), held.hex())");
        var psi = new System.Diagnostics.ProcessStartInfo("python3") { RedirectStandardInput = true, RedirectStandardOutput = true };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(driver);
        using var process = System.Diagnostics.Process.Start(psi)!;
        try
        {
            static string Hex(string s) => Convert.ToHexStringLower(System.Text.Encoding.UTF8.GetBytes(s));
            process.StandardInput.Write(string.Join("\n", cases.Select(c => string.Join(" ", c.Reads.Select(Hex)))) + "\n");
            process.StandardInput.Close();
            var got = process.StandardOutput.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.True(process.WaitForExit(10000));
            Assert.Equal(cases.Select(c => (c.Typed ? "1 " : "0 ") + Hex(c.Held)), got);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }

    /// <summary>
    /// On a real pseudo-terminal (#163): terminal replies and mouse motion, also split over two writes, pass through
    /// unchanged and log nothing; a lone ESC counts as a key and skips a request; a burst of keys logs input-typed at
    /// most once a second, the last line with the last key's time.
    /// </summary>
    [Fact]
    public void TerminalReportsAreNotTypingAndKeysAreLoggedOnceASecond()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/script") || !OnPath("python3")) return;
        var root = Directory.CreateTempSubdirectory("booster-typed-pty").FullName;
        var session = new TabSession(root, DateTime.UtcNow);
        var track = session.NewTrack(autoClear: true)!;
        var tab = new TabConfig { Llm = "claude", Directory = session.Directory };
        const string E = "\u001b";
        string[] reports = [E + "[?62;22c" + E + "P>|Terminal 1.2" + E + "\\" + E + "[?1u" + E + "[?2026;2$y" + E + "]11;rgb:0000/0000/0000" + E + "\\"
            + E + "[24;80R" + E + "[?997;1n" + E + "[I" + E + "[<35;10;5M" + E + "[<64;10;5M" + E + "[<35;4", "0;7M"];
        var keys = Enumerable.Range(0, 10).Select(i => ((char)('a' + i)).ToString()).ToArray();
        var sent = string.Concat(reports) + E + string.Concat(keys);
        // A fake claude on a raw terminal: records every byte it gets, then stays a while (the once-a-second log is not its exit).
        var body = "claude() { stty raw -echo; : > ready; head -c " + sent.Length + " > got; sleep 3; }\nexport -f claude\n" + Launcher.TabScript(tab, track: track);
        var psi = new System.Diagnostics.ProcessStartInfo("script") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add("-qefc");
        psi.ArgumentList.Add("bash --noprofile --norc -ic '" + body.Replace("'", "'\\''") + "'");
        psi.ArgumentList.Add("/dev/null");
        TestShell.Configure(psi, root);
        using var process = System.Diagnostics.Process.Start(psi)!;
        var log = Path.Combine(session.Directory, track.Id + ".log");
        long[] Typed() => File.ReadAllLines(log).Where(l => l.EndsWith(" input-typed", StringComparison.Ordinal)).Select(l => long.Parse(l.Split(' ')[0])).ToArray();
        long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        void Send(string s) { process.StandardInput.Write(s); process.StandardInput.Flush(); }
        try
        {
            Assert.True(SpinWait.SpinUntil(() => session.ReadLog(track.Id) is { InputReady: true } && File.Exists(Path.Combine(session.Directory, "ready")), 30000));
            foreach (var r in reports) { Send(r); Thread.Sleep(100); }
            Thread.Sleep(1500);
            Assert.Empty(Typed());

            // The Escape key on its own: an unfinished sequence that never completes, so it is a key.
            var since = Now();
            Send(E);
            Thread.Sleep(100);
            session.RequestInput(track.Id, since, AutoClear.Command);
            Assert.True(SpinWait.SpinUntil(() => File.ReadAllText(log).Contains("input-skipped reason=typed"), 10000));
            Assert.True(SpinWait.SpinUntil(() => Typed().Length == 1, 5000));
            Thread.Sleep(1200);

            long first = Now(), last = 0;
            foreach (var k in keys) { last = Now(); Send(k); Thread.Sleep(30); }
            Assert.True(SpinWait.SpinUntil(() => Typed().Length > 1 && Typed()[^1] >= last, 5000), string.Join(" ", Typed()));
            Thread.Sleep(1500);
            // Ten separate keys in about a third of a second: the first at once, the rest in one line a second later.
            Assert.InRange(Typed().Length - 1, 1, 2 + (last - first) / 1000);

            Assert.True(SpinWait.SpinUntil(() => session.ReadLog(track.Id) is { CliExit: 0 }, 10000));
            Assert.Equal(sent, File.ReadAllText(Path.Combine(session.Directory, "got")));
            Send("exit\n");
            Assert.True(process.WaitForExit(30000));
            Assert.True(SpinWait.SpinUntil(() => InDirectory(session.Directory).Length == 0, 5000));
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
            foreach (var pid in InDirectory(session.Directory))
                try { System.Diagnostics.Process.GetProcessById(pid).Kill(entireProcessTree: true); } catch (ArgumentException) { }
            TestShell.StopLeftovers(root);
            Directory.Delete(root, true);
        }
    }

    /// <summary>The fake claude's "sleep 600" runs (not a helper's "sleep 1").</summary>
    private static bool Sleeping(string dir) =>
        InDirectory(dir).Any(pid => { try { return File.ReadAllText($"/proc/{pid}/cmdline") == "sleep\0600\0"; } catch (IOException) { return false; } });

    private static bool Running(string dir, string comm) =>
        InDirectory(dir).Any(pid => { try { return File.ReadAllText($"/proc/{pid}/comm").Trim() == comm; } catch (IOException) { return false; } });

    private static bool OnPath(string exe) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':').Any(d => File.Exists(Path.Combine(d, exe)));

    private static int[] InDirectory(string dir) => Directory.GetDirectories("/proc")
        .Select(d => (Pid: int.TryParse(Path.GetFileName(d), out var n) ? n : 0, Dir: d)).Where(x => x.Pid > 0)
        .Where(x => { try { return new DirectoryInfo(Path.Combine(x.Dir, "cwd")).LinkTarget == dir; } catch (IOException) { return false; } catch (UnauthorizedAccessException) { return false; } })
        .Select(x => x.Pid).ToArray();
}
