using System.Text.Json;
using BoosterAI;
using Xunit;

public class TerminalPlacementTests
{
    private static readonly TerminalBounds Work = new(1920, 0, 1920, 1040);
    private static readonly TerminalWindowState State = new(new(1920, 0, 960, 1040), "SECONDARY", Work, false);
    private static readonly TerminalDisplay[] Displays = [new("MAIN", new(0, 0, 1920, 1040), true), new("SECONDARY", Work, false)];

    [Fact]
    public void RetainsDockedGeometryOnTheSameDisplay() => Assert.Equal(State.Bounds, TerminalPlacement.RestoreBounds(State, Displays));

    [Fact]
    public void MissingDisplayFallsBackToPrimaryAndKeepsTheWindowVisible() =>
        Assert.Equal(new TerminalBounds(0, 0, 960, 1040), TerminalPlacement.RestoreBounds(State, Displays.Take(1).ToArray()));

    [Fact]
    public void ChangedResolutionAndNegativeDisplayOriginScaleTheDockedRegion() =>
        Assert.Equal(new TerminalBounds(-2560, -200, 1280, 1400), TerminalPlacement.RestoreBounds(State,
            [new("SECONDARY", new(-2560, -200, 2560, 1400), true)]));

    [Fact]
    public void OffscreenAndOversizedSavedBoundsAreClamped() =>
        Assert.Equal(new TerminalBounds(1920, 0, 1920, 1040), TerminalPlacement.RestoreBounds(
            State with { Bounds = new(10000, -500, 3000, 2000) }, Displays));

    [Fact]
    public void InvalidAndMissingStateNeverBlocksDefaultLaunch()
    {
        Assert.Null(TerminalPlacement.RestoreBounds(null, Displays));
        Assert.Null(TerminalPlacement.RestoreBounds(State with { WorkArea = new(0, 0, 0, 20) }, Displays));
        Assert.Null(TerminalPlacement.RestoreBounds(State with { Bounds = new(int.MinValue, 0, 10, 10) }, Displays));
        Assert.Null(TerminalPlacement.RestoreBounds(State, []));
        Assert.Null(TerminalPlacement.Read(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".missing")));
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("{\"Bounds\":null,\"WorkArea\":null}")]
    public void CorruptStateIsIgnored(string json)
    {
        var file = Path.GetTempFileName();
        try { File.WriteAllText(file, json); Assert.Null(TerminalPlacement.Read(file)); }
        finally { File.Delete(file); }
    }

    [Fact]
    public void StateSurvivesANewReaderAndAtomicSaveLeavesNoTemporaryFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "booster-placement-test-" + Guid.NewGuid());
        var file = Path.Combine(dir, "placement.json");
        try
        {
            TerminalPlacement.Write(file, State with { Maximized = true });
            Assert.Equal(State with { Maximized = true }, TerminalPlacement.Read(file));
            Assert.Single(Directory.GetFiles(dir));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void IdentificationRequiresExactNonceNewHandleTerminalClassAndProcess()
    {
        var r = new TerminalWatchRequest("nonce", "window", "temp", "state", [1, 2]);
        Assert.True(TerminalPlacement.IsCandidate(3, "nonce", "CASCADIA_HOSTING_WINDOW", "WindowsTerminal", r));
        Assert.False(TerminalPlacement.IsCandidate(1, "nonce", "CASCADIA_HOSTING_WINDOW", "WindowsTerminal", r));
        Assert.False(TerminalPlacement.IsCandidate(3, "other-title", "CASCADIA_HOSTING_WINDOW", "WindowsTerminal", r));
        Assert.False(TerminalPlacement.IsCandidate(3, "nonce", "other", "WindowsTerminal", r));
        Assert.False(TerminalPlacement.IsCandidate(3, "nonce", "CASCADIA_HOSTING_WINDOW", "other", r));
    }

    [Fact]
    public void BootstrapRetainsRealTabNamesAndCommandAndUsesOneBoundedHandshakeTab()
    {
        var tabs = new[] { new TabConfig { Name = "-lic", Llm = Catalog.ShellOnly, Directory = "~" } };
        var original = Launcher.WindowsTerminalArgs(tabs, "Ubuntu", window: "BoosterAI-test");
        var request = new TerminalWatchRequest("nonce", "BoosterAI-test", "C:\\Temp\\has spaces", "state", []);
        var args = TerminalPlacement.WithBootstrap(original, request);
        Assert.Equal(original.Take(2), args.Take(2));
        var separator = args.IndexOf(";");
        var real = args.Skip(separator + 1).ToArray();
        var expected = original.Skip(2).ToArray();
        var script = Array.LastIndexOf(expected, "-lic") + 1;
        Assert.EndsWith("\n" + expected[script], real[script]);
        Assert.Contains("real-ready", real[script]);
        real[script] = expected[script];
        Assert.Equal(expected, real);
        Assert.Contains("nonce", args);
        Assert.Contains("C:\\Temp\\has spaces/bootstrap.cmd".Replace('/', Path.DirectorySeparatorChar), args);
        Assert.Equal(2, args.Count(a => a == "new-tab"));
        Assert.Equal("0", args[^1]);
        Assert.All(args.Where(a => a != ";"), a => Assert.DoesNotContain(';', a));
    }

    [Fact]
    public void CachedWatcherReusesOneCopyAndRetiresOldVersionsWithoutTouchingTheSource()
    {
        var dir = Path.Combine(Path.GetTempPath(), "booster-watcher-cache-test-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var source = Path.Combine(dir, "source.exe");
        var cache = Path.Combine(dir, "cache");
        try
        {
            File.WriteAllText(source, "version one");
            var first = TerminalPlacement.CachedWatcher(source, cache);
            Assert.NotEqual(source, first);
            Assert.Equal("version one", File.ReadAllText(first));
            Assert.Equal(first, TerminalPlacement.CachedWatcher(source, cache));
            File.WriteAllText(source, "version two is longer");
            var second = TerminalPlacement.CachedWatcher(source, cache);
            Assert.NotEqual(first, second);
            Assert.False(File.Exists(first));
            Assert.Equal(File.ReadAllText(source), File.ReadAllText(second));
            Assert.Single(Directory.GetDirectories(cache));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void WatcherDoesNotRestartTheTestHost()
    {
        var psi = TerminalPlacement.WatcherStart("request.json");
        Assert.DoesNotContain("testhost", psi.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.True(psi.CreateNoWindow);
        Assert.Equal(TerminalPlacement.WatchArgument, psi.ArgumentList[^2]);
        Assert.Equal("request.json", psi.ArgumentList[^1]);
    }
}
