using BoosterAI;
using Xunit;

/// <summary>CPU activity per tab (#215): /proc parsing, the process tree, threshold and hysteresis, with fake /proc data.</summary>
[Collection(ConfigFileCollection.Name)] // switches Loc's global language, and a real tab wants an unloaded machine
public class TabCpuTests(Xunit.Abstractions.ITestOutputHelper output)
{
    /// <summary>A /proc/&lt;pid&gt;/stat line with the fields TabCpu reads (ppid, utime, stime, starttime) and harmless others.</summary>
    private static string Stat(int pid, string name, int ppid, long utime, long stime = 0, long start = 100, long cutime = 0) =>
        $"{pid} ({name}) S {ppid} {pid} {pid} 34816 {pid} 4194560 100 0 0 0 {utime} {stime} {cutime} 0 20 0 1 0 {start} 1000 100 18446744073709551615";

    private sealed record P(int Pid, string Name, int PPid, long Ticks, long Start = 100);

    private static ProcSnapshot Snap(long uptime, params P[] procs) =>
        new(procs.ToDictionary(p => p.Pid, p => new ProcEntry(p.PPid, p.Name, p.Ticks, p.Start)), uptime);

    [Fact]
    public void ParsesStatWithSpacesAndParenthesesInTheName()
    {
        var e = ProcSnapshot.ParseStat(Stat(42, "my (odd) prog", 7, utime: 30, stime: 12, start: 5373, cutime: 999));
        Assert.Equal(new ProcEntry(7, "my (odd) prog", 42, 5373), e);
        Assert.Null(ProcSnapshot.ParseStat("42 (cut"));
        Assert.Null(ProcSnapshot.ParseStat("42 (short) S 1 2 3"));
        Assert.Equal(1234567, ProcSnapshot.ParseUptime("12345.67 98765.43\n"));
        Assert.Null(ProcSnapshot.ParseUptime(""));
    }

    [Fact]
    public void ReadsAFakeProcDirectoryInOneScan()
    {
        var root = Directory.CreateTempSubdirectory("booster-fake-proc").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "uptime"), "50.00 10.00\n");
            void Add(int pid, string stat) { Directory.CreateDirectory(Path.Combine(root, pid.ToString())); File.WriteAllText(Path.Combine(root, pid.ToString(), "stat"), stat); }
            Add(1, Stat(1, "init", 0, 5));
            Add(300, Stat(300, "bash", 1, 2, start: 4000));
            Add(301, Stat(301, "dotnet", 300, 150, stime: 50, start: 4500));
            Add(302, "garbage");
            Directory.CreateDirectory(Path.Combine(root, "self"));
            Directory.CreateDirectory(Path.Combine(root, "303")); // ended during the scan: no stat
            var snap = ProcSnapshot.Read(root)!;
            Assert.Equal(5000, snap.Uptime);
            Assert.Equal([1, 300, 301], snap.Processes.Keys.Order());
            Assert.Equal(new ProcEntry(300, "dotnet", 200, 4500), snap.Processes[301]);
            Assert.Equal([300, 301], snap.Subtree(300).Order());
            Assert.Null(ProcSnapshot.Read(Path.Combine(root, "missing")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void CountsTheShellAndAllDescendantsOnly()
    {
        // 1 init; 10 tab shell → 11 script → 12 claude → 13 bash → 14 dotnet (busy); 20 another tab's busy process.
        var before = Snap(10_000, new P(1, "init", 0, 0), new P(10, "bash", 1, 5), new P(11, "script", 10, 1), new P(12, "claude", 11, 300),
            new P(13, "bash", 12, 2), new P(14, "dotnet", 13, 1000), new P(20, "yes", 1, 0));
        var after = Snap(10_200, new P(1, "init", 0, 0), new P(10, "bash", 1, 6), new P(11, "script", 10, 1), new P(12, "claude", 11, 304),
            new P(13, "bash", 12, 2), new P(14, "dotnet", 13, 1300), new P(20, "yes", 1, 200));
        var s = TabCpu.Measure(before, after, 10, 100)!;
        // (1 + 4 + 300) ticks over 200 ticks (2 s): 1.525 cores; dotnet alone 1.5.
        Assert.Equal(1.525, s.Share, 3);
        Assert.Equal("dotnet", s.Busiest);
        Assert.Equal(1.5, s.BusiestShare, 3);
        Assert.Equal(1.0, TabCpu.Measure(before, after, 20, null)!.Share, 3);
        // No baseline, a shell that is gone, or a PID that now is another process: no reading.
        Assert.Null(TabCpu.Measure(null, after, 10, 100));
        Assert.Null(TabCpu.Measure(before, after, 99, null));
        Assert.Null(TabCpu.Measure(before, after, 10, 555));
    }

    [Fact]
    public void ANewProcessCountsWithAllItsTimeAndAReusedPidDoesNotGoNegative()
    {
        var before = Snap(10_000, new P(10, "bash", 1, 0), new P(15, "old", 10, 5000, Start: 200));
        // 16 started after the earlier scan (start 10 050 ≥ uptime 10 000): all 150 ticks are new. 15 is a reused PID now
        // (another start time, also after the scan), counted from zero, not 40 − 5000. 17 started before the earlier scan
        // but was not in it (a failed read): no baseline, so nothing is counted for it yet.
        var after = Snap(10_200, new P(10, "bash", 1, 0), new P(15, "make", 10, 40, Start: 10_100), new P(16, "cc1", 15, 150, Start: 10_050),
            new P(17, "missed", 10, 9000, Start: 300));
        var s = TabCpu.Measure(before, after, 10, null)!;
        Assert.Equal(0.95, s.Share, 3);
        Assert.Equal("cc1", s.Busiest);
    }

    [Fact]
    public void AQuietChildLikeSleepStaysIdle()
    {
        var cpu = new CpuActivity();
        var now = DateTime.UtcNow;
        var prev = Snap(10_000, new P(10, "bash", 1, 50), new P(11, "claude", 10, 900), new P(12, "sleep", 11, 0));
        for (var i = 1; i <= 5; i++)
        {
            // The idle agent and the tab's helper loops use a little (≤ 6 %), the sleep nothing.
            var next = Snap(10_000 + 200 * i, new P(10, "bash", 1, 50 + i), new P(11, "claude", 10, 900 + 11 * i), new P(12, "sleep", 11, 0));
            Assert.False(cpu.Update(TabCpu.Measure(prev, next, 10, null), now.AddSeconds(2 * i)));
            prev = next;
        }
        Assert.Null(cpu.ActiveAt);
        Assert.Equal(Activity.Idle, TabCpu.Combine(Activity.Idle, cpu.Active));
    }

    private static CpuSample Reading(double share, string name = "dotnet") => new(share, name, share);

    [Fact]
    public void ActiveOnlyAfterTwoReadingsAboveTheThreshold()
    {
        var cpu = new CpuActivity();
        var t = DateTime.UtcNow;
        // A single spike, also a large one, does not count; at the threshold is not above it.
        Assert.False(cpu.Update(Reading(0.9), t));
        Assert.False(cpu.Update(Reading(0.02), t));
        Assert.False(cpu.Update(Reading(TabCpu.Threshold), t));
        Assert.False(cpu.Update(Reading(TabCpu.Threshold), t));
        Assert.False(cpu.Update(Reading(0.5), t));
        Assert.True(cpu.Update(Reading(0.12), t.AddSeconds(2)));
        Assert.Equal(t.AddSeconds(2), cpu.ActiveAt);
        Assert.Equal("dotnet", cpu.Busy!.Busiest);
    }

    [Fact]
    public void StaysActiveThroughOneDipAndReturnsToIdleAfterTwo()
    {
        var cpu = new CpuActivity();
        var t = DateTime.UtcNow;
        cpu.Update(Reading(1.0, "yes"), t);
        Assert.True(cpu.Update(Reading(1.0, "yes"), t.AddSeconds(2)));
        // One quiet reading (a build waiting on its disk) keeps it working; the tooltip keeps the busy process.
        Assert.True(cpu.Update(Reading(0.01, "bash"), t.AddSeconds(4)));
        Assert.Equal("yes", cpu.Busy!.Busiest);
        Assert.True(cpu.Update(Reading(0.8, "yes"), t.AddSeconds(6)));
        Assert.True(cpu.Update(Reading(0.0), t.AddSeconds(8)));
        // Two quiet readings in a row (about 4 s): idle again, and ActiveAt is the last active poll.
        Assert.False(cpu.Update(Reading(0.0), t.AddSeconds(10)));
        Assert.Equal(t.AddSeconds(8), cpu.ActiveAt);
        // No reading (tab not running, scan failed) is idle at once and needs two readings again.
        cpu.Update(Reading(1.0), t);
        Assert.True(cpu.Update(Reading(1.0), t));
        Assert.False(cpu.Update(null, t));
        Assert.False(cpu.Update(Reading(1.0), t));
    }

    [Fact]
    public void WorkingReplacesOnlyIdleAndUnknown()
    {
        Assert.Equal(Activity.Working, TabCpu.Combine(Activity.Idle, true));
        Assert.Equal(Activity.Working, TabCpu.Combine(Activity.Unknown, true));
        Assert.Equal(Activity.Busy, TabCpu.Combine(Activity.Busy, true));
        Assert.Equal(Activity.Watching, TabCpu.Combine(Activity.Watching, true));
        Assert.Equal(Activity.Idle, TabCpu.Combine(Activity.Idle, false));
    }

    [Fact]
    public void WorkingStatusNamesTheBusiestProcessInBothLanguages()
    {
        var vm = new MainViewModel(new AppConfig { Tabs = [new TabConfig { Name = "Atlas", Llm = "claude" }] });
        var row = vm.Tabs[0];
        row.LaunchedAs = row.ToConfig(forLaunch: true);
        var t = DateTime.UtcNow;
        row.Cpu.Update(Reading(0.854, "testhost"), t);
        row.Cpu.Update(Reading(0.854, "testhost"), t);
        try
        {
            foreach (var (lang, text, tip) in new[] { ("sv", "Arbetar", "testhost (85 % av en kärna)"), ("en", "Working", "testhost (85% of one core)") })
            {
                Loc.Use(lang);
                row.SetState(TabState.Running, null, activity: Activity.Idle);
                row.SetState(TabState.Running, null, activity: TabCpu.Combine(Activity.Idle, row.Cpu.Active));
                Assert.Equal(text, row.StatusText);
                Assert.Contains(tip, row.StatusTip);
                Assert.True(row.HasPeekDetail);
            }
        }
        finally { Loc.Use("en"); }
    }

    [Fact]
    public void AutoClearWaitsForCpuActivityAndCountsIdleFromItsEnd()
    {
        const long busy = 1_800_000_000_000, idle = busy + 60_000;
        var idleTime = DateTimeOffset.FromUnixTimeMilliseconds(idle).UtcDateTime;
        var log = new TabLog(10, 500, true, null, false, Activity: Activity.Idle) { BusyAt = busy, IdleAt = idle, InputReady = true };
        var claude = new TabConfig { Llm = "claude" };
        AutoClear.Request? Due(Activity activity, DateTime? cpuActive, DateTime now) =>
            AutoClear.Due(true, 5, claude, TabState.Running, activity, false, false, log, idleTime, null, now, cpuActive);
        Assert.NotNull(Due(Activity.Idle, null, idleTime.AddMinutes(5)));
        // While the processes work, never; once they stopped, the five minutes count from their last active poll.
        Assert.Null(Due(Activity.Working, idleTime.AddMinutes(20), idleTime.AddMinutes(20)));
        Assert.Null(Due(Activity.Idle, idleTime.AddMinutes(20), idleTime.AddMinutes(24)));
        Assert.NotNull(Due(Activity.Idle, idleTime.AddMinutes(20), idleTime.AddMinutes(25)));
        // Activity that ended before the turn did changes nothing.
        Assert.NotNull(Due(Activity.Idle, idleTime.AddMinutes(-3), idleTime.AddMinutes(5)));
    }

    /// <summary>The machine's idle and I/O wait time (the first line of /proc/stat), in hundredths of a second.</summary>
    private static long IdleTicks() =>
        File.ReadLines("/proc/stat").First().Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(4).Take(2).Sum(long.Parse);

    /// <summary>
    /// A real tab (tab-lib.sh under a PTY, as in the restart tests) whose CLI first waits quietly, then runs a busy child
    /// (yes), then a quiet one (sleep): only the busy phase counts as working, named after yes, and the tab returns to idle.
    /// </summary>
    [Fact]
    public void RealTabTreeIsWorkingOnlyWhileAChildBurnsCpu()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/script") || !File.Exists("/usr/bin/mkfifo")) return;
        var root = Directory.CreateTempSubdirectory("booster-tab-cpu").FullName;
        var session = new TabSession(root, DateTime.UtcNow);
        var track = session.NewTrack()!;
        var tab = new TabConfig { Llm = "codex", Directory = session.Directory };
        var go = Path.Combine(session.Directory, "go");
        // Each line read from the fifo moves the fake CLI on: quiet wait, yes, then a quiet sleep.
        var body = "codex() {\n mkfifo go && touch quiet && read -r _ < go\n yes > /dev/null &\n y=$!\n read -r _ < go\n kill $y\n sleep 300\n}\n"
            + "export -f codex\n" + Launcher.TabScript(tab, track: track);
        var psi = new System.Diagnostics.ProcessStartInfo("script") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add("-qefc");
        psi.ArgumentList.Add("bash --noprofile --norc -ic '" + body.Replace("'", "'\\''") + "'");
        psi.ArgumentList.Add("/dev/null");
        TestShell.Configure(psi, root);
        using var process = System.Diagnostics.Process.Start(psi)!;
        try
        {
            Assert.True(SpinWait.SpinUntil(() => File.Exists(Path.Combine(session.Directory, "quiet")), 30000), "the fake CLI did not start");
            var log = session.ReadLog(track.Id)!;
            var cpu = new CpuActivity();
            var samples = new List<CpuSample?>();
            var previous = ProcSnapshot.Read("/proc");
            var idleBefore = IdleTicks();
            var idleCores = 0.0;
            // Opening the fifo blocks until the fake CLI reads it; never wait forever for a CLI that died.
            void Next() => Assert.True(Task.Run(() => File.AppendAllText(go, "\n")).Wait(15000), "the fake CLI stopped reading");
            bool Step()
            {
                Thread.Sleep(1000);
                var current = ProcSnapshot.Read("/proc")!;
                var idle = IdleTicks();
                // Cores the machine left idle meanwhile (/proc/stat and the uptime both count hundredths of a second).
                idleCores = previous is null ? 0 : (double)(idle - idleBefore) / (current.Uptime - previous.Uptime);
                idleBefore = idle;
                var sample = TabCpu.Measure(previous, current, log.Pid!.Value, log.StartTime);
                previous = current;
                samples.Add(sample);
                return cpu.Update(sample, DateTime.UtcNow);
            }
            // Startup noise under load may keep the first readings busy; it must settle to idle and then stay idle.
            Assert.True(SpinWait.SpinUntil(() => !Step(), 30000), "never quiet: " + string.Join(", ", samples.Select(s => s?.Share)));
            for (var i = 0; i < 3; i++) Assert.False(Step(), "quiet: " + string.Join(", ", samples.Select(s => s?.Share)));
            Next();
            Assert.True(SpinWait.SpinUntil(Step, 45000), "busy: " + string.Join(", ", samples.Select(s => s?.Share)));
            Assert.Equal("yes", cpu.Busy!.Busiest);
            // yes alone is the tab's load, counted in cores: at most one, and on a machine with a core to spare about one.
            // On a loaded machine (the full suite next to another one, #278) it only gets its share of the cores, so the
            // lower bound applies only when the machine had a core idle during that reading.
            Assert.InRange(cpu.Busy.BusiestShare, cpu.Busy.Share * 0.8, 1.1);
            Assert.True(cpu.Busy.Share > 0.5 || idleCores < 1, $"{cpu.Busy.Share:P1} with {idleCores:F1} cores idle");
            Next();
            Assert.True(SpinWait.SpinUntil(() => !Step(), 45000), "quiet again: " + string.Join(", ", samples.Select(s => s?.Share)));
            for (var i = 0; i < 3; i++) Assert.False(Step());
            output.WriteLine(string.Join("\n", samples.Select(s => s is null ? "-" : $"{s.Share:P1} {s.Busiest} {s.BusiestShare:P1}")));
            var last = ProcSnapshot.Read("/proc")!;
            Assert.Contains(last.Subtree(log.Pid!.Value), pid => last.Processes[pid].Name == "sleep");
            Assert.DoesNotContain(last.Subtree(log.Pid!.Value), pid => last.Processes[pid].Name == "yes");
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            process.WaitForExit(10000);
            TestShell.StopLeftovers(root);
            Directory.Delete(root, true);
        }
    }
}
