using BoosterAI;
using Xunit;

namespace BoosterAI.Tests;

public class ZoomRefreshTests
{
    [Fact]
    public void OrdinaryUnchangedChoiceDoesNotReload()
    {
        var reloads = 0;
        ZoomRefresh.Apply(false, () => 80, _ => false, () => reloads++);
        Assert.Equal(0, reloads);
    }

    [Theory]
    [InlineData(80, 100)]
    [InlineData(100, 110)]
    [InlineData(0, 100)]
    public void StartupForcesALiveToggleAndRestoresTheChoiceEvenWhenWritesReportUnchanged(int desired, int pulse)
    {
        var written = new List<int>();
        var reloads = 0;
        ZoomRefresh.Apply(true, () => desired, value => { written.Add(value); return false; }, () => reloads++);
        Assert.Equal([pulse, desired], written);
        Assert.Equal(2, reloads);
    }

    [Fact]
    public void ReloadFailureStillRestoresTheSavedProfile()
    {
        var current = 80;
        var reloads = 0;
        Assert.Throws<IOException>(() => ZoomRefresh.Apply(true, () => 80,
            value => { current = value; return true; },
            () => { if (++reloads == 1) throw new IOException("reload failed"); }));
        Assert.Equal(80, current);
        Assert.Equal(2, reloads);
    }

    [Fact]
    public void AFreshlySavedChoiceDuringThePulseWinsOverTheStartupValue()
    {
        var saved = 80;
        var current = saved;
        var reloads = 0;
        ZoomRefresh.Apply(true, () => saved, value => { current = value; return true; },
            () => { if (++reloads == 1) saved = 125; });
        Assert.Equal(125, saved);
        Assert.Equal(125, current);
    }

    [Fact]
    public void FailedPulseWriteAlsoAttemptsTheFinalRestoration()
    {
        var attempted = new List<int>();
        Assert.Throws<UnauthorizedAccessException>(() => ZoomRefresh.Apply(true, () => 80, value =>
        {
            attempted.Add(value);
            if (attempted.Count == 1) throw new UnauthorizedAccessException();
            return false;
        }, () => { }));
        Assert.Equal([100, 80], attempted);
    }
}

[Collection(ConfigFileCollection.Name)]
public class ZoomStartupIsolationTests
{
    [Fact]
    public void StartupRequestsRestorationButDryRunNeverRequestsARealProfileChange()
    {
        var root = Path.Combine(Path.GetTempPath(), "booster-zoom-start-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var output = Console.Out;
        try
        {
            ConfigStore.UseFile(Path.Combine(root, "config.json"));
            ConfigStore.Save(new AppConfig { ZoomPercent = 80 });
            var before = TestTerminal.ZoomRequests.Count;
            Launcher.RestoreZoomOnStartup(80);
            Assert.Equal((80, true), Assert.Single(TestTerminal.ZoomRequests.Skip(before)));
            before = TestTerminal.ZoomRequests.Count;
            Console.SetOut(new StringWriter());
            Program.Main(["--dry-run", "--config", ConfigStore.PathOnDisk]);
            Assert.Equal(before, TestTerminal.ZoomRequests.Count);
        }
        finally { Console.SetOut(output); Directory.Delete(root, recursive: true); }
    }
}
