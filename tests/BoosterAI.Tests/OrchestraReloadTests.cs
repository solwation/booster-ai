using System.Diagnostics;
using System.Text.Json;
using BoosterAI;
using Xunit;
using Activity = BoosterAI.Activity;

public class OrchestraReloadTests
{
    private static readonly TabConfig Claude = new() { Llm = "claude", Orkester = true };
    private static readonly long Idle = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeMilliseconds();
    private static readonly DateTime Output = DateTimeOffset.FromUnixTimeMilliseconds(Idle).UtcDateTime;
    private static readonly TabLog Ready = new(42, 123, true, null, false, Activity: Activity.Idle)
    { BusyAt = Idle - 1000, IdleAt = Idle, InputReady = true, InputReadyAt = Idle - 2000, InputProtocol = 2 };

    private static AutoClear.Request? Due(TabLog? log = null, TabConfig? tab = null, Activity activity = Activity.Idle,
        TabState state = TabState.Running, bool pending = false, bool closing = false, long? requested = null) =>
        OrchestraReload.Due(tab ?? Claude, state, activity, pending, closing, log ?? Ready, Output, requested, DateTime.UtcNow);

    [Fact]
    public void RequiresExplicitIdleAndCurrentInputProtocol()
    {
        Assert.NotNull(Due());
        Assert.Null(Due(Ready with { Activity = Activity.Unknown }));
        Assert.Null(Due(Ready with { Activity = Activity.Busy }));
        Assert.Null(Due(Ready with { InputReady = false }));
        Assert.Null(Due(Ready with { InputProtocol = 0 }));
        Assert.Null(Due(Ready with { InputReadyAt = null }));
        Assert.Null(Due(Ready with { CliExit = 0 }));
        Assert.Null(Due(Ready with { Exited = true }));
        Assert.Null(Due(Ready with { InputTypedAt = Idle + 1 }));
        Assert.Null(Due(Ready with { OrchestraIdleAt = Idle }));
        Assert.Null(Due(requested: Idle));
        Assert.Null(Due(pending: true));
        Assert.Null(Due(closing: true));
        foreach (var state in Enum.GetValues<TabState>().Where(s => s != TabState.Running)) Assert.Null(Due(state: state));
        foreach (var activity in Enum.GetValues<Activity>().Where(a => a != Activity.Idle)) Assert.Null(Due(activity: activity));
        Assert.Null(Due(tab: new TabConfig { Llm = "claude", Orkester = false }));
        Assert.Null(Due(tab: new TabConfig { Llm = "claude", Orkester = true, NonInteractive = true }));
        foreach (var llm in new[] { "codex", "agy", "aider", "interpreter", "openhands", Catalog.ShellOnly })
            Assert.Null(Due(tab: new TabConfig { Llm = llm, Orkester = true }));
        Assert.Null(OrchestraReload.Due(Claude, TabState.Running, Activity.Idle, false, false, Ready, DateTime.UtcNow, null, DateTime.UtcNow));
    }

    [Fact]
    public void AutoClearAndReloadHaveDistinctEventsAndOneAtomicInputSlot()
    {
        var root = Directory.CreateTempSubdirectory("booster-reload-requests").FullName;
        try
        {
            var session = new TabSession(root, DateTime.UtcNow);
            var track = session.NewTrack()!;
            Assert.True(session.TryRequestInput(track.Id, Idle - 1000, OrchestraReload.Command, Ready, "orkester"));
            Assert.False(session.TryRequestInput(track.Id, Idle - 1000, AutoClear.Command, Ready, "clear"));
            session.RequestInput(track.Id, Idle, AutoClear.Command);
            var file = Path.Combine(session.Directory, track.Id + ".input");
            using var request = JsonDocument.Parse(File.ReadAllText(file));
            Assert.Equal("/orkester\r", request.RootElement.GetProperty("text").GetString());
            var log = TabLog.Parse($"1 cli-start\n2 input-ready protocol=2\n3 busy\n{Idle} idle\n{Idle + 2} input-sent kind=clear idle={Idle}\n");
            Assert.NotNull(AutoClear.ClearedAt(log));
            Assert.NotNull(Due(log)); // Manual reload still works after auto-clear.
            log = TabLog.Parse($"1 cli-start\n2 input-ready protocol=2\n3 busy\n{Idle} idle\n{Idle + 2} input-sent kind=orkester idle={Idle}\n");
            Assert.Null(AutoClear.ClearedAt(log));
            Assert.Null(Due(log)); // Survives a BoosterAI restart: no second request for this idle period.
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ReloadVisibilityDoesNotChangeRestartAllOrContinuation()
    {
        var root = Directory.CreateTempSubdirectory("booster-reload-ui").FullName;
        try
        {
            var vm = new MainViewModel(new AppConfig { Tabs = [new TabConfig { Llm = "claude", Orkester = true }] });
            var row = vm.Tabs[0];
            row.LaunchedAs = Claude;
            row.Session = new TabSession(root, DateTime.UtcNow);
            row.Track = row.Session.NewTrack();
            row.SetState(TabState.Running, null, activity: Activity.Idle);
            row.RefreshInputState(Ready, Output);
            Assert.True(row.ShowReload);
            Assert.True(row.CanReloadOrchestra);
            Assert.False(row.CanRestart);
            Assert.Equal(0, vm.RestartableCount);
            Assert.False(row.CanContinueClosed);
            Assert.Equal(Loc.Get("restart.orkester.tip"), row.RestartTip);
            row.Orkester = false;
            Assert.False(row.ShowReload);
            row.Orkester = true;
            row.Model = "different";
            Assert.True(row.CanRestart);
            Assert.False(row.CanReloadOrchestra);
            Assert.NotEqual(Loc.Get("restart.orkester.tip"), row.RestartTip);
            row.Model = Catalog.StandardModel;
            row.SetState(TabState.CliExited, 0);
            Assert.True(row.CanRestart);
            Assert.False(row.CanReloadOrchestra);
            Assert.True(row.CanContinueClosed);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ClickQueuesOnlyTheCorrectTabAndRejectsDoubleClickAndStaleBusy()
    {
        if (!OperatingSystem.IsLinux()) return;
        var root = Directory.CreateTempSubdirectory("booster-reload-click").FullName;
        try
        {
            var name = "reload-" + Guid.NewGuid().ToString("N");
            var vm = new MainViewModel(new AppConfig { Tabs = [new TabConfig { Name = name, Llm = "claude", Orkester = true }] });
            var row = vm.Tabs[0];
            row.LaunchedAs = Claude;
            row.Session = new TabSession(root, DateTime.UtcNow);
            row.Track = row.Session.NewTrack()!;
            var other = row.Session.NewTrack()!;
            var pid = Environment.ProcessId;
            var start = TabStatus.ParseProcStartTime(File.ReadAllText($"/proc/{pid}/stat"));
            var path = Path.Combine(row.Session.Directory, row.Track.Id);
            var text = $"1 start pid={pid} starttime={start}\n2 cli-start\n3 input-ready protocol=2\n4 busy\n{Idle} idle\n";
            File.WriteAllText(path + ".log", text);
            File.WriteAllText(path + ".timing", "output");
            File.SetLastWriteTimeUtc(path + ".timing", Output);
            row.SetState(TabState.Running, null, activity: Activity.Idle);
            row.RefreshInputState(row.Session.ReadLog(row.Track.Id), Output);
            var originalTrack = row.Track;
            var originalLaunch = row.LaunchedAs;
            Assert.True(vm.ReloadOrchestra(row));
            Assert.False(vm.ReloadOrchestra(row));
            Assert.False(File.Exists(Path.Combine(row.Session.Directory, other.Id + ".input")));
            Assert.False(File.Exists(path + ".next"));
            Assert.Empty(TestTerminal.For(name));
            Assert.Same(originalTrack, row.Track);
            Assert.Same(originalLaunch, row.LaunchedAs);
            File.Delete(path + ".input");
            row.OrchestraRequestedFor = null;
            File.AppendAllText(path + ".log", $"{Idle + 1} busy\n");
            Assert.False(vm.ReloadOrchestra(row));
            Assert.False(File.Exists(path + ".input"));
        }
        finally { Directory.Delete(root, true); }
    }
}

public class OrchestraReloadPtyTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("busy")]
    [InlineData("draft")]
    [InlineData("generation")]
    [InlineData("output")]
    public async Task ExistingCliReceivesOnePromptOnlyWhenTheExpectedIdleStillApplies(string mode)
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/script")) return;
        var root = Directory.CreateTempSubdirectory("booster-reload-pty").FullName;
        var session = new TabSession(root, DateTime.UtcNow);
        var track = session.NewTrack()!; // No auto-clear: orchestra alone enables the PTY input channel.
        var tab = new TabConfig { Name = "reload-pty-" + Guid.NewGuid().ToString("N"), Llm = "claude", Directory = session.Directory, Orkester = true };
        var body = "claude() { printf '%s\\n' \"$BASHPID\" > cli-pid; while IFS= read -r l; do printf '%s\\n' \"$l\" >> received; done; }\nexport -f claude\n"
            + Launcher.TabScript(tab, track: track);
        var psi = new ProcessStartInfo("script") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add("-qefc");
        psi.ArgumentList.Add("bash --noprofile --norc -ic '" + body.Replace("'", "'\\''") + "'");
        psi.ArgumentList.Add("/dev/null");
        TestShell.Configure(psi, root);
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        var logPath = Path.Combine(session.Directory, track.Id + ".log");
        var received = Path.Combine(session.Directory, "received");
        string Lines() => File.Exists(received) ? File.ReadAllText(received) : "";
        try
        {
            string? cliPid = null;
            Assert.True(SpinWait.SpinUntil(() =>
            {
                var pidPath = Path.Combine(session.Directory, "cli-pid");
                if (session.ReadLog(track.Id) is not { InputProtocol: 2 } || !File.Exists(pidPath)) return false;
                var written = File.ReadAllText(pidPath);
                // Redirection creates the file before printf writes the PID. Capture only its complete line.
                if (!written.EndsWith('\n') || !int.TryParse(written, out var pid) || pid <= 0) return false;
                cliPid = written;
                return true;
            }, 30000));
            var idle = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            File.AppendAllText(logPath, $"{idle - 1} busy\n{idle} idle\n");
            var expected = session.ReadLog(track.Id)!;
            var shellPid = expected.Pid!.Value;
            var shellStart = expected.StartTime!.Value;
            File.SetLastWriteTimeUtc(Path.Combine(session.Directory, track.Id + ".timing"), DateTime.UtcNow.AddSeconds(-3));
            if (mode == "output") File.SetLastWriteTimeUtc(Path.Combine(session.Directory, track.Id + ".timing"), DateTime.UtcNow);
            if (mode == "busy") File.AppendAllText(logPath, $"{idle + 1} busy\n");
            if (mode == "generation") File.AppendAllText(logPath, $"{idle + 1} input-ready protocol=2\n");
            if (mode == "draft")
            {
                process.StandardInput.Write("draft");
                process.StandardInput.Flush();
                Assert.True(SpinWait.SpinUntil(() => session.ReadLog(track.Id)!.InputTypedAt > idle - 1, 5000));
            }
            if (mode == "valid")
            {
                Assert.True(session.TryRequestInput(track.Id, idle - 1, AutoClear.Command, expected, "clear"));
                Assert.True(SpinWait.SpinUntil(() => Lines() == "/clear\n", 10000));
                Assert.True(SpinWait.SpinUntil(() => session.ReadLog(track.Id)!.InputKind == "clear", 5000));
                await Task.Delay(2100); // Reload requires two quiet seconds after the clear redraw.
            }
            Assert.True(session.TryRequestInput(track.Id, idle - 1, OrchestraReload.Command, expected, "orkester"));
            Assert.True(SpinWait.SpinUntil(() => File.ReadAllText(logPath).Contains(mode == "valid" ? "input-sent kind=orkester" : "input-skipped"), 10000));
            if (mode == "valid")
            {
                Assert.True(SpinWait.SpinUntil(() => Lines() == "/clear\n/orkester\n", 5000));
                Assert.Null(AutoClear.ClearedAt(session.ReadLog(track.Id)));
                // Another UI instance or auto-clear cannot send a second command for the same idle period.
                Assert.True(session.TryRequestInput(track.Id, idle - 1, OrchestraReload.Command, expected, "orkester"));
                Assert.True(SpinWait.SpinUntil(() => File.ReadAllText(logPath).Contains("input-skipped reason=stale"), 5000));
                Assert.Equal("/clear\n/orkester\n", Lines());
            }
            else Assert.Equal("", Lines());
            Assert.Equal(cliPid, File.ReadAllText(Path.Combine(session.Directory, "cli-pid")));
            Assert.Equal(shellPid, session.ReadLog(track.Id)!.Pid);
            Assert.Equal(1, File.ReadAllText(logPath).Split('\n').Count(l => l.EndsWith(" cli-start", StringComparison.Ordinal)));
            Assert.False(File.Exists(Path.Combine(session.Directory, track.Id + ".next")));
            Assert.Empty(TestTerminal.For(tab.Name));
            Launcher.RunTabControl(Launcher.CloseTabScript(shellPid, shellStart), "Ubuntu");
            Assert.True(process.WaitForExit(30000));
            Assert.True(SpinWait.SpinUntil(() => InDirectory(session.Directory).Length == 0, 10000));
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
            foreach (var pid in InDirectory(session.Directory))
                try
                {
                    using var leftover = Process.GetProcessById(pid);
                    if (!leftover.HasExited) leftover.Kill(entireProcessTree: true);
                    leftover.WaitForExit(5000);
                }
                catch (ArgumentException) { }
            await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(10));
            Directory.Delete(root, true);
        }
    }

    private static int[] InDirectory(string dir) => Directory.GetDirectories("/proc")
        .Select(d => (Pid: int.TryParse(Path.GetFileName(d), out var n) ? n : 0, Dir: d)).Where(x => x.Pid > 0)
        .Where(x => { try { return new DirectoryInfo(Path.Combine(x.Dir, "cwd")).LinkTarget == dir; } catch (IOException) { return false; } catch (UnauthorizedAccessException) { return false; } })
        .Select(x => x.Pid).ToArray();
}
