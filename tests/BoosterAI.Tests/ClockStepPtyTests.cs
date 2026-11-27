using System.Diagnostics;
using BoosterAI;
using Xunit;

/// <summary>
/// tab-lib.sh's bs_pty on a real pseudo-terminal while its wall clock steps (#280): WSL steps the clock by about 13 s every
/// few seconds on some machines (#278). The tab's python3 gets an injected wall clock (a sitecustomize that adds the
/// seconds in a file to time.time(); time.monotonic() is left alone), which the test steps at chosen moments. A terminal
/// reply split over two reads stays a reply, a lone Escape is a key half a second later, and a reload request is typed only
/// after two quiet seconds of output and is never dropped as too old because of the step.
/// </summary>
[Collection(ConfigFileCollection.Name)] // real PTY timing (#218): never next to the parallel CPU-heavy classes
public class ClockStepPtyTests
{
    private const string E = "\u001b";

    private const string SiteCustomize = """
import os, time
_clock = os.environ.get("BS_TEST_CLOCK")
if _clock:
    _real = time.time
    def _stepped():
        try:
            with open(_clock) as f:
                return _real() + float(f.read())
        except (OSError, ValueError):
            return _real()
    time.time = _stepped
""";

    [Fact]
    public void ASplitReplyStaysAReplyAndALoneEscapeIsAKeyHoweverTheClockSteps()
    {
        if (!Available) return;
        using var tab = new Tab("booster-clock-keys", "claude() { stty raw -echo; : > ready; cat > got; }");
        long[] Typed() => File.ReadAllLines(tab.LogPath).Where(l => l.EndsWith(" input-typed", StringComparison.Ordinal))
            .Select(l => long.Parse(l.Split(' ')[0])).ToArray();
        long Got() => File.Exists(tab.Path("got")) ? new FileInfo(tab.Path("got")).Length : 0;
        Assert.True(SpinWait.SpinUntil(() => tab.Session.ReadLog(tab.Track.Id) is { InputReady: true } && File.Exists(tab.Path("ready")), 30000));
        tab.AssertClockInjected();

        // A device attributes reply in two reads, the clock stepping 13 s forward between them, at least one poll of the tab
        // (0.25 s) before the second read: on the wall clock the unfinished first part is 13 s old, still it is not taken for
        // the Escape key. Only an attempt whose two reads were less than 0.45 s apart (a real Escape key waits 0.5 s) tells;
        // under load an attempt can take longer, so up to three are made.
        var sent = "";
        var told = false;
        for (var attempt = 0; attempt < 3 && !told; attempt++)
        {
            string[] reply = [E + "[?62;2", "2c"];
            var before = Typed().Length;
            tab.Step(0);
            Thread.Sleep(300);
            var apart = Stopwatch.StartNew();
            tab.Send(reply[0]);
            Assert.True(SpinWait.SpinUntil(() => Got() == sent.Length + reply[0].Length, 10000));
            tab.Step(13);
            Thread.Sleep(300);
            tab.Send(reply[1]);
            sent += string.Concat(reply);
            Assert.True(SpinWait.SpinUntil(() => Got() == sent.Length, 10000));
            if (apart.ElapsedMilliseconds >= 450) continue;
            told = true;
            Thread.Sleep(1500);
            Assert.Equal(before, Typed().Length);
        }
        Assert.True(told, "the reads were never less than 0.45 s apart");

        // A lone Escape, the clock stepping 13 s back right after it was read: it still counts as a key after half a
        // second, not 13.5 s later.
        tab.Step(0);
        Thread.Sleep(300);
        tab.Send(E);
        var typed = Typed().Length;
        Assert.True(SpinWait.SpinUntil(() => Got() == sent.Length + 1, 10000));
        var read = Stopwatch.StartNew();
        tab.Step(-13);
        Assert.True(SpinWait.SpinUntil(() => Typed().Length > typed, 30000));
        Assert.InRange(read.ElapsedMilliseconds, 0, 6000);
        Assert.Equal(sent + E, File.ReadAllText(tab.Path("got")));
        tab.Close();
    }

    [Theory]
    [InlineData(13)]
    [InlineData(-13)]
    [InlineData(100_000)]
    public void AReloadWaitsForQuietOutputAndIsTypedAfterAClockStep(double step)
    {
        if (!Available) return;
        // A CLI that draws a dot every 0.1 s while the file "noisy" exists (a busy TUI redrawing) and records the lines it reads.
        using var tab = new Tab("booster-clock-reload",
            "claude() { : > ready; ( while [ ! -f stop ]; do [ ! -f noisy ] || printf .; sleep 0.1; done ) & "
            + "while IFS= read -r l; do printf '%s\\n' \"$l\" >> received; done; }", orkester: true);
        string Received() => File.Exists(tab.Path("received")) ? File.ReadAllText(tab.Path("received")) : "";
        int Count(string text) => File.ReadAllLines(tab.LogPath).Count(l => l.Contains(text, StringComparison.Ordinal));
        long Output() => File.Exists(tab.TimingPath) ? new FileInfo(tab.TimingPath).Length : 0;
        Assert.True(SpinWait.SpinUntil(() => tab.Session.ReadLog(tab.Track.Id) is { InputProtocol: 2 } && File.Exists(tab.Path("ready")), 30000));
        tab.AssertClockInjected();
        // The tab's clock now stands step seconds away from ours, which wrote the request's creation time.
        tab.Step(step);
        var idle = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        File.AppendAllText(tab.LogPath, $"{idle - 1} busy\n{idle} idle\n");
        var expected = tab.Session.ReadLog(tab.Track.Id)!;

        // Output in the last two seconds: on the stepped wall clock it may look old or new, still the reload waits.
        File.WriteAllText(tab.Path("noisy"), "");
        var before = Output();
        Assert.True(SpinWait.SpinUntil(() => Output() > before, 10000));
        Assert.True(tab.Session.TryRequestInput(tab.Track.Id, idle - 1, OrchestraReload.Command, expected, "orkester"));
        Assert.True(SpinWait.SpinUntil(() => Count(" input-skipped") > 0, 30000), File.ReadAllText(tab.LogPath));
        Assert.Equal(1, Count(" input-skipped reason=stale kind=orkester"));

        // Two quiet seconds later the same request is typed, text and Enter, not dropped as too old.
        File.Delete(tab.Path("noisy"));
        Thread.Sleep(300);
        before = Output();
        Thread.Sleep(2200);
        Assert.Equal(before, Output());
        Assert.True(tab.Session.TryRequestInput(tab.Track.Id, idle - 1, OrchestraReload.Command, expected, "orkester"));
        Assert.True(SpinWait.SpinUntil(() => Count(" input-sent kind=orkester") > 0 || Count(" input-skipped") > 1, 30000), File.ReadAllText(tab.LogPath));
        Assert.Equal(1, Count(" input-sent kind=orkester"));
        Assert.True(SpinWait.SpinUntil(() => Received() == "/orkester\n", 10000), Received());
        File.WriteAllText(tab.Path("stop"), "");
        tab.Close();
    }

    private static bool Available => OperatingSystem.IsLinux() && File.Exists("/usr/bin/script") && OnPath("python3");

    /// <summary>One tab with tab-lib.sh, its CLI a shell function, under script on a pseudo-terminal; disposing stops all of it.</summary>
    private sealed class Tab : IDisposable
    {
        private readonly string _root;
        private readonly string _clock;
        private readonly Process _process;

        public TabSession Session { get; }
        public TabTrack Track { get; }
        public string LogPath => Path(Track.Id + ".log");
        public string TimingPath => Path(Track.Id + ".timing");
        public string Path(string name) => System.IO.Path.Combine(Session.Directory, name);

        public Tab(string name, string cli, bool orkester = false)
        {
            _root = Directory.CreateTempSubdirectory(name).FullName;
            Session = new TabSession(_root, DateTime.UtcNow);
            Track = (orkester ? Session.NewTrack() : Session.NewTrack(autoClear: true))!;
            var tab = new TabConfig { Llm = "claude", Directory = Session.Directory, Orkester = orkester };
            var site = Directory.CreateDirectory(System.IO.Path.Combine(_root, "pyclock")).FullName;
            File.WriteAllText(System.IO.Path.Combine(site, "sitecustomize.py"), SiteCustomize);
            _clock = System.IO.Path.Combine(_root, "clock");
            // Far ahead until the test checks that the tab reads this clock.
            Step(1_000_000);
            var body = cli + "\nexport -f claude\n" + Launcher.TabScript(tab, track: Track);
            var psi = new ProcessStartInfo("script") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            psi.ArgumentList.Add("-qefc");
            psi.ArgumentList.Add("bash --noprofile --norc -ic '" + body.Replace("'", "'\\''") + "'");
            psi.ArgumentList.Add("/dev/null");
            TestShell.Configure(psi, _root);
            psi.Environment["PYTHONPATH"] = site;
            psi.Environment["BS_TEST_CLOCK"] = _clock;
            _process = Process.Start(psi)!;
            _ = _process.StandardOutput.ReadToEndAsync();
            _ = _process.StandardError.ReadToEndAsync();
        }

        /// <summary>Sets the tab's wall clock to ours plus <paramref name="seconds"/>, in one atomic write.</summary>
        public void Step(double seconds)
        {
            var temporary = _clock + ".tmp";
            File.WriteAllText(temporary, seconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            File.Move(temporary, _clock, overwrite: true);
        }

        /// <summary>bs_pty's input-ready line carries the injected clock (a million seconds ahead), so the steps reach it.</summary>
        public void AssertClockInjected()
        {
            var ready = File.ReadAllLines(LogPath).Last(l => l.Contains(" input-ready", StringComparison.Ordinal));
            Assert.True(long.Parse(ready.Split(' ')[0]) > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 900_000_000L, ready);
        }

        public void Send(string s)
        {
            _process.StandardInput.Write(s);
            _process.StandardInput.Flush();
        }

        /// <summary>Ends the CLI with a hangup of the tab (the window closes) and checks that nothing is left.</summary>
        public void Close()
        {
            _process.Kill();
            Assert.True(_process.WaitForExit(10000));
            Assert.True(SpinWait.SpinUntil(() => InDirectory(Session.Directory).Length == 0, 15000));
        }

        public void Dispose()
        {
            try
            {
                if (!_process.HasExited) _process.Kill(entireProcessTree: true);
                _process.WaitForExit(5000);
                foreach (var pid in InDirectory(Session.Directory))
                    try
                    {
                        using var leftover = Process.GetProcessById(pid);
                        if (!leftover.HasExited) leftover.Kill(entireProcessTree: true);
                        leftover.WaitForExit(5000);
                    }
                    catch (ArgumentException) { }
                TestShell.StopLeftovers(_root);
            }
            finally
            {
                _process.Dispose();
                Directory.Delete(_root, true);
            }
        }
    }

    private static bool OnPath(string exe) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':').Any(d => File.Exists(System.IO.Path.Combine(d, exe)));

    private static int[] InDirectory(string dir) => Directory.GetDirectories("/proc")
        .Select(d => (Pid: int.TryParse(System.IO.Path.GetFileName(d), out var n) ? n : 0, Dir: d)).Where(x => x.Pid > 0)
        .Where(x => { try { return new DirectoryInfo(System.IO.Path.Combine(x.Dir, "cwd")).LinkTarget == dir; } catch (IOException) { return false; } catch (UnauthorizedAccessException) { return false; } })
        .Select(x => x.Pid).ToArray();
}
