using BoosterAI;
using Xunit;

/// <summary>The test run never reads or writes the user's real config folder (#282); see <see cref="TestConfig"/>.</summary>
public sealed class ConfigIsolationTests
{
    [Fact]
    public void TheRealFolderIsTheAppsOwnApplicationDataFolder()
    {
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BoosterAI");
        Assert.Equal(expected, TestConfig.RealFolder);
    }

    [Fact]
    public void TheConfigAndTheFilesNextToItAreOutsideTheRealFolder()
    {
        foreach (var path in new[] { ConfigStore.PathOnDisk, ConfigStore.FileNextToConfig("limit-levels.json"),
                     ConfigStore.FileNextToConfig("main-window.json"), ConfigStore.FileNextToConfig("models-cache.json") })
            Assert.False(Path.GetFullPath(path).StartsWith(TestConfig.RealFolder, StringComparison.OrdinalIgnoreCase), path);
    }

    [Fact]
    public void EveryWayIntoTheRealFolderThrows()
    {
        Assert.Throws<InvalidOperationException>(() => ConfigStore.Guard(ConfigStore.RealPath));
        Assert.Throws<InvalidOperationException>(() => ConfigStore.Guard(TestConfig.RealFolder));
        Assert.Throws<InvalidOperationException>(() => ConfigStore.Guard(Path.Combine(TestConfig.RealFolder, "releases-cache", "releases.json")));
        Assert.Throws<InvalidOperationException>(() => ConfigStore.UseFile(ConfigStore.RealPath));
        Assert.Throws<InvalidOperationException>(() => Installer.WriteConfig(ConfigStore.RealPath, null, []));
        // A sibling folder that only shares the prefix is not the real folder.
        var sibling = TestConfig.RealFolder + "-other";
        Assert.Equal(sibling, ConfigStore.Guard(sibling));
    }

    [Fact]
    public void TheRealFolderIsUnchangedSinceTheRunStarted()
    {
        var now = TestConfig.Snapshot(TestConfig.RealFolder);
        if (TestConfig.RealFolderAtStart is null) Assert.Null(now);
        else Assert.Equal(TestConfig.RealFolderAtStart, now);
    }
}
