using BoosterAI;
using Xunit;

public class AfterLaunchPolicyTests
{
    [Theory]
    [InlineData("keep", false, false, false, false)]
    [InlineData("keep", true, false, true, false)]
    [InlineData("minimize", false, false, false, true)]
    [InlineData("minimize", true, false, true, true)]
    [InlineData("close", false, true, false, false)]
    [InlineData("close", true, true, false, false)]
    [InlineData(null, true, false, true, false)]
    [InlineData("nonsense", false, false, false, false)]
    public void DecidesTheWindowActionsAfterStart(string? mode, bool windows, bool close, bool tray, bool minimize) =>
        Assert.Equal((close, tray, minimize), AfterLaunchPolicy.Decide(mode, windows));

    [Fact]
    public void DefaultKeepsTheWindowOpen() => Assert.Equal("keep", new AppConfig().AfterLaunch);
}

[Collection(ConfigFileCollection.Name)]
public class AfterLaunchConfigTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bs-after-" + Guid.NewGuid().ToString("N"));

    public AfterLaunchConfigTests()
    {
        Directory.CreateDirectory(_root);
        ConfigStore.UseFile(Path.Combine(_root, "config.json"));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static AppConfig Load(string json)
    {
        File.WriteAllText(ConfigStore.PathOnDisk, json);
        return ConfigStore.Load();
    }

    [Fact]
    public void OldCloseAfterLaunchTrueMeansClose() =>
        Assert.Equal("close", Load("""{"CloseAfterLaunch": true, "Tabs": []}""").AfterLaunch);

    [Theory]
    [InlineData("""{"CloseAfterLaunch": false}""")]
    [InlineData("""{"Tabs": []}""")]
    [InlineData("""{"AfterLaunch": "bogus"}""")]
    public void MissingOrOldFalseOrUnknownKeepsOpen(string json) => Assert.Equal("keep", Load(json).AfterLaunch);

    [Fact]
    public void NewValueWinsOverTheOldOne() =>
        Assert.Equal("minimize", Load("""{"CloseAfterLaunch": true, "AfterLaunch": "minimize"}""").AfterLaunch);

    [Theory]
    [InlineData("keep")]
    [InlineData("minimize")]
    [InlineData("close")]
    public void ValueRoundTrips(string mode)
    {
        ConfigStore.Save(new AppConfig { AfterLaunch = mode });
        Assert.Equal(mode, ConfigStore.Load().AfterLaunch);
        Assert.Equal(mode, ConfigStore.TryRead()!.AfterLaunch);
    }

    [Fact]
    public void ViewModelChoiceIsStored()
    {
        var vm = new MainViewModel(new AppConfig { AfterLaunch = "close" });
        Assert.Equal("close", vm.SelectedAfterLaunch.Id);
        vm.SelectedAfterLaunch = vm.AfterLaunchChoices.Single(c => c.Id == "minimize");
        Assert.Equal("minimize", vm.AfterLaunch);
    }
}
