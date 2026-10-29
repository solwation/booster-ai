using BoosterAI;
using Xunit;

/// <summary>Tests that point ConfigStore at their own file; ConfigStore.PathOnDisk is global, so they never run in parallel.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class ConfigFileCollection
{
    public const string Name = "ConfigFile";
}

[Collection(ConfigFileCollection.Name)]
public class ZoomSaveTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bs-zoom-" + Guid.NewGuid().ToString("N"));

    public ZoomSaveTests()
    {
        Directory.CreateDirectory(_root);
        ConfigStore.UseFile(Path.Combine(_root, "config.json"));
        ConfigStore.Save(new AppConfig { Tabs = [new TabConfig { Name = "Alpha" }] });
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static MainViewModel Started() => new(ConfigStore.Load());

    private static ZoomChoice Choice(MainViewModel vm, int percent) => vm.ZoomChoices.Single(c => c.Percent == percent);

    [Fact]
    public void AZoomChoiceIsSavedAtOnceAndSurvivesARestart()
    {
        var vm = Started();
        vm.SelectedZoom = Choice(vm, 80);

        // No Settings close or app exit: the install script stops the app with Stop-Process -Force.
        Assert.Equal(80, ConfigStore.Load().ZoomPercent);
        Assert.Equal(80, Started().SelectedZoom.Percent);
    }

    [Fact]
    public void OtherSavesOfTheSameInstanceKeepTheZoom()
    {
        var vm = Started();
        vm.SelectedZoom = Choice(vm, 125);
        vm.Tabs.Add(new TabViewModel(new TabConfig { Name = "Beta" }, vm, 1));
        vm.MoveTab(0, 1);
        vm.Save();

        Assert.Equal(125, ConfigStore.Load().ZoomPercent);
        Assert.Equal(["Beta", "Alpha"], ConfigStore.Load().Tabs.Select(t => t.Name));
    }

    [Fact]
    public void AnOlderInstanceDoesNotPutBackTheOldZoom()
    {
        var tray = Started();
        var settings = Started();
        settings.SelectedZoom = Choice(settings, 80);

        // The instance in the tray still holds 0 and saves its whole config, e.g. after an update check.
        tray.Save();

        Assert.Equal(80, ConfigStore.Load().ZoomPercent);
        Assert.Equal(80, tray.SelectedZoom.Percent);
        // Its own later choice still wins.
        tray.SelectedZoom = Choice(tray, 150);
        settings.Save();
        Assert.Equal(150, ConfigStore.Load().ZoomPercent);
    }
}

public class ConfigMergeTests
{
    [Fact]
    public void EverySettingOutsideTheTabsIsTakenFromAnotherInstance()
    {
        var baseline = new AppConfig();
        var disk = new AppConfig
        {
            WslDistro = "Debian", LinuxTerminal = "kitty", ColorTheme = "Other", Language = "en", AppTheme = "dark",
            CloseAfterLaunch = true, MinimizeToTray = false, Notifications = false, ZoomPercent = 80, AutoUpdateClis = false, CheckForUpdates = false,
            SkippedVersion = "9.9.9", LastUpdateCheck = new DateTime(2026, 10, 29, 9, 18, 39, DateTimeKind.Utc),
        };
        var mine = new AppConfig();

        Assert.True(ConfigMerge.TakeOthers(mine, baseline, disk));

        // A new setting must be added to ConfigMerge, or a second instance silently reverts it.
        foreach (var p in typeof(AppConfig).GetProperties().Where(p => p.Name != nameof(AppConfig.Tabs)))
        {
            Assert.NotEqual(p.GetValue(baseline), p.GetValue(disk));
            Assert.Equal(p.GetValue(disk), p.GetValue(mine));
        }
    }

    [Fact]
    public void ThisInstancesOwnChangesAndTabsAreKept()
    {
        var baseline = new AppConfig { ZoomPercent = 0, Tabs = [new TabConfig { Name = "Old" }] };
        var mine = new AppConfig { ZoomPercent = 90, Tabs = [new TabConfig { Name = "Mine" }] };
        var disk = new AppConfig { ZoomPercent = 80, Language = "en", Tabs = [new TabConfig { Name = "Theirs" }] };

        Assert.True(ConfigMerge.TakeOthers(mine, baseline, disk));

        Assert.Equal(90, mine.ZoomPercent);
        Assert.Equal("en", mine.Language);
        Assert.Equal("Mine", mine.Tabs.Single().Name);
        Assert.False(ConfigMerge.TakeOthers(mine, ConfigStore.Copy(mine), ConfigStore.Copy(mine)));
    }
}
