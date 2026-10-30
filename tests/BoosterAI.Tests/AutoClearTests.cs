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

    private static AutoClear.Request? Due(TabLog? log = null, DateTime? now = null, bool enabled = true, int minutes = 5,
        TabConfig? tab = null, TabState state = TabState.Running, Activity activity = Activity.Idle, bool pendingRestart = false,
        bool closing = false, long? requestedFor = null) =>
        AutoClear.Due(enabled, minutes, tab ?? Claude, state, activity, pendingRestart, closing, log ?? IdleLog, requestedFor,
            now ?? IdleTime.AddMinutes(5));

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
        Assert.Null(AutoClear.Due(true, 5, null, TabState.Running, Activity.Idle, false, false, IdleLog, null, IdleTime.AddHours(1)));
        Assert.Null(Due(IdleLog with { InputReady = false }));
        Assert.Null(AutoClear.Due(true, 5, Claude, TabState.Running, Activity.Idle, false, false, null, null, IdleTime.AddHours(1)));
        // Idle without a hook "idle" (timing-based): no turn has ended.
        Assert.Null(Due(IdleLog with { IdleAt = null, Activity = Activity.Unknown }));
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
    public void OnlyAnInteractiveClaudeRunsUnderBsPtyAndOnlyWithTheSetting()
    {
        var on = new TabTrack("/s/tab-lib.sh", "/s", "tab1", AutoClear: true);
        var off = on with { AutoClear = false };
        var claude = new TabConfig { Llm = "claude", Orkester = true };
        Assert.Contains("' input && ", Launcher.TabScript(claude, track: on));
        Assert.Contains("' input && ", Launcher.RestartScript(claude, on, resume: true));
        Assert.DoesNotContain(" input", Launcher.TabScript(claude, track: off));
        Assert.DoesNotContain(" input", Launcher.RestartScript(claude, off, resume: false));
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
            Directory.Delete(root, true);
        }
    }

    private static bool Sleeping(string dir) =>
        InDirectory(dir).Any(pid => { try { return File.ReadAllText($"/proc/{pid}/comm").Trim() == "sleep"; } catch (IOException) { return false; } });

    private static bool OnPath(string exe) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':').Any(d => File.Exists(Path.Combine(d, exe)));

    private static int[] InDirectory(string dir) => Directory.GetDirectories("/proc")
        .Select(d => (Pid: int.TryParse(Path.GetFileName(d), out var n) ? n : 0, Dir: d)).Where(x => x.Pid > 0)
        .Where(x => { try { return new DirectoryInfo(Path.Combine(x.Dir, "cwd")).LinkTarget == dir; } catch (IOException) { return false; } catch (UnauthorizedAccessException) { return false; } })
        .Select(x => x.Pid).ToArray();
}
