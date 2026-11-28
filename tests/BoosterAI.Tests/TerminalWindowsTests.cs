using BoosterAI;
using Xunit;

namespace BoosterAI.Tests;

/// <summary>#283: a closed terminal window closes its tabs for Start, even while their shells still look alive.</summary>
[Collection(ConfigFileCollection.Name)]
public sealed class TerminalWindowsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bs-windows-" + Guid.NewGuid().ToString("N"));

    public TerminalWindowsTests()
    {
        Directory.CreateDirectory(_root);
        ConfigStore.UseFile(Path.Combine(_root, "config.json"));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class FakeWindows
    {
        public readonly Dictionary<string, nint> Marked = [];
        public readonly HashSet<nint> Alive = [];
        public TerminalWindows Attach(TerminalWindows windows)
        {
            windows.Find = name => Marked.GetValueOrDefault(name);
            windows.Exists = Alive.Contains;
            return windows;
        }
    }

    [Fact]
    public void OnlyAWindowSeenBeforeAndNowDestroyedCountsAsClosedAndOnlyOnce()
    {
        var fake = new FakeWindows();
        var windows = fake.Attach(new TerminalWindows());
        // Never seen marked (an unowned or older window): nothing is known about it.
        Assert.Empty(windows.Closed(["w1"]));

        fake.Marked["w1"] = 7;
        fake.Alive.Add(7);
        Assert.Empty(windows.Closed(["w1", "w1"]));

        // The mark is briefly missing from a window that still exists (hidden, minimized or handed over): not closed.
        fake.Marked.Remove("w1");
        Assert.Empty(windows.Closed(["w1"]));

        fake.Alive.Remove(7);
        Assert.Equal(["w1"], windows.Closed(["w1"]));
        Assert.Empty(windows.Closed(["w1"]));

        // The next launch into the same name opens a new window, tracked anew.
        fake.Marked["w1"] = 9;
        fake.Alive.Add(9);
        Assert.Empty(windows.Closed(["w1"]));
        fake.Marked.Remove("w1");
        fake.Alive.Remove(9);
        Assert.Equal(["w1"], windows.Closed(["w1"]));
    }

    [Fact]
    public async Task StartCountsEveryTabOfAClosedWindowWhileTheirShellsStillLookAlive()
    {
        if (!OperatingSystem.IsLinux()) return;
        var vm = new MainViewModel(new AppConfig { Tabs = [
            new() { Name = "Atlas", Directory = "/srv/atlas" }, new() { Name = "Boreal", Directory = "/srv/boreal" },
            new() { Name = "Cirrus", Directory = "/srv/cirrus" }] });
        var fake = new FakeWindows();
        fake.Attach(vm.TerminalWindows);
        var session = new TabSession(Path.Combine(_root, "sessions"), DateTime.UtcNow);
        // The test process stands in for the live shells: nothing is started, and nothing is written to it.
        var pid = Environment.ProcessId;
        var start = TabStatus.ParseProcStartTime(File.ReadAllText($"/proc/{pid}/stat"))!.Value;
        foreach (var row in vm.Tabs)
        {
            row.Session = session;
            row.Track = session.NewTrack()!;
            row.LaunchedAs = row.ToConfig(forLaunch: true);
            row.LaunchedAt = DateTime.UtcNow;
            File.WriteAllText(Path.Combine(session.Directory, row.Track.Id + ".log"), $"1 start pid={pid} starttime={start}\n2 cli-start\n");
            row.SetState(TabState.Running, null);
        }
        async Task Poll() { vm.Poll(); await vm.PollTask; }

        fake.Marked[session.WindowName] = 42;
        fake.Alive.Add(42);
        await Poll();
        Assert.All(vm.Tabs, t => Assert.True(t.IsRunning));
        Assert.Equal(0, vm.ToStartCount);

        // One tab is closed on its own first: its shell exits and only it is counted.
        File.AppendAllText(Path.Combine(session.Directory, vm.Tabs[0].TrackId + ".log"), "3 exit\n");
        await Poll();
        Assert.Equal(1, vm.ToStartCount);

        // Then the whole window: the other shells still look alive, yet all three tabs are closed for Start.
        fake.Marked.Remove(session.WindowName);
        fake.Alive.Remove(42);
        await Poll();
        Assert.All(vm.Tabs, t => Assert.False(t.IsAlive));
        Assert.Equal(3, vm.ToStartCount);
        await Poll();
        Assert.Equal(3, vm.ToStartCount);

        // A relaunch is a new track: that tab is followed normally again.
        var boreal = vm.Tabs[1];
        boreal.Track = session.NewTrack()!;
        boreal.LaunchedAt = DateTime.UtcNow;
        File.WriteAllText(Path.Combine(session.Directory, boreal.Track.Id + ".log"), $"1 start pid={pid} starttime={start}\n2 cli-start\n");
        await Poll();
        Assert.True(boreal.IsRunning);
        Assert.Equal(2, vm.ToStartCount);
    }
}
