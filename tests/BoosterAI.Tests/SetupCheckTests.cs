using BoosterAI;
using Xunit;

public class SetupCheckTests
{
    private static SetupStatus Installed(string version = "1.0") => new(WslState.Ok, [],
        [new("claude", "file", "/sample/bin/claude") { Version = version }], true, false);

    [Fact]
    public void ConstructorsAndPageOpeningDoNotEnableDetection()
    {
        var calls = 0;
        var vm = new MainViewModel(new AppConfig { Tabs = [] })
        {
            DetectSetupAsync = _ => { calls++; return Task.FromResult(Installed()); },
        };
        vm.SetupAgentsOpened();
        vm.SetupWindowActive();
        vm.CheckSetup();
        Assert.Equal(0, calls);
        Assert.False(vm.Setup.IsChecking);
        Assert.Equal(Loc.Get("agents.status.unknown"), vm.Setup.CheckNotice);
        Assert.All(vm.Setup.Agents, a => Assert.Equal(Loc.Get("agents.status.unknown.short"), a.StatusText));
    }

    [Fact]
    public async Task OpeningAndActivationShareThirtySecondCacheAndNeverOverlap()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var pending = new TaskCompletionSource<SetupStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var vm = new MainViewModel(new AppConfig { Tabs = [] })
        {
            SetupUtcNow = () => now,
            DetectSetupAsync = _ => { calls++; return pending.Task; },
        };
        vm.StartSetupChecks();
        Assert.True(vm.Setup.IsChecking);
        Assert.Equal(Loc.Get("agents.status.checking"), vm.Setup.CheckNotice);
        now = now.AddMinutes(1);
        vm.SetupAgentsOpened();
        vm.SetupWindowActive();
        vm.CheckSetup();
        Assert.Equal(1, calls);
        pending.SetResult(Installed());
        await vm.SetupCheckTask;
        Assert.False(vm.Setup.IsChecking);

        pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.SetupAgentsOpened();
        Assert.Equal(2, calls);
        pending.SetResult(Installed("2.0"));
        await vm.SetupCheckTask;
        now = now.AddSeconds(29);
        vm.SetupWindowActive();
        vm.SetupAgentsOpened();
        Assert.Equal(2, calls);
        now = now.AddSeconds(1);
        vm.SetupAgentsOpened();
        await vm.SetupCheckTask;
        Assert.Equal(3, calls);
        Assert.Equal("2.0", vm.Setup.Agents.Single(a => a.Cli == "claude").VersionText);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedRefreshKeepsPreviousFactsAndRetriesAfterCache(bool throws)
    {
        var now = DateTime.UtcNow;
        var calls = 0;
        var vm = new MainViewModel(new AppConfig { Tabs = [] })
        {
            SetupUtcNow = () => now,
            DetectSetupAsync = _ => Task.FromResult(Installed()),
        };
        vm.StartSetupChecks();
        await vm.SetupCheckTask;
        var status = vm.Setup.Status;
        var agent = vm.Setup.Agents.Single(a => a.Cli == "claude");
        vm.Setup.SelectedAgent = agent;
        agent.Section = 1;
        vm.DetectSetupAsync = _ =>
        {
            calls++;
            return throws ? Task.FromException<SetupStatus>(new IOException("sample failure"))
                : Task.FromResult(SetupStatus.Empty);
        };
        now = now.AddSeconds(30);
        vm.SetupAgentsOpened();
        await vm.SetupCheckTask;
        Assert.Same(status, vm.Setup.Status);
        Assert.Same(agent, vm.Setup.SelectedAgent);
        Assert.True(agent.IsInstalled);
        Assert.Equal("1.0", agent.VersionText);
        Assert.Equal(1, agent.Section);
        Assert.False(vm.Setup.IsChecking);
        Assert.Equal(Loc.Get("agents.status.failed.previous"), vm.Setup.CheckNotice);
        Assert.Equal(Loc.Get("setup.summary.failed"), vm.Setup.WslInstallationStatus);
        Assert.Equal(Loc.Get("wsl.info.unknown"), vm.Setup.InstalledWslDistros);
        Assert.Equal(Loc.Get("setup.summary.failed"), vm.Setup.Summary);
        vm.SetupAgentsOpened();
        Assert.Equal(1, calls);
        vm.DetectSetupAsync = _ => { calls++; return Task.FromResult(Installed("2.0")); };
        now = now.AddSeconds(30);
        vm.SetupAgentsOpened();
        await vm.SetupCheckTask;
        Assert.Equal(2, calls);
        Assert.Equal("2.0", agent.VersionText);
        Assert.Equal("", vm.Setup.CheckNotice);
    }

    [Fact]
    public async Task ChangedDistributionDiscardsInflightResultAndBypassesOldCache()
    {
        var config = new AppConfig { Tabs = [], WslDistro = "Sample-A" };
        var first = new TaskCompletionSource<SetupStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<SetupStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        var distros = new List<string>();
        var vm = new MainViewModel(config)
        {
            DetectSetupAsync = distro => { distros.Add(distro); return distro == "Sample-A" ? first.Task : second.Task; },
        };
        vm.StartSetupChecks();
        var initial = vm.SetupCheckTask;
        // Change the shared config directly to avoid querying a real WSL home in this scheduler test.
        config.WslDistro = "Sample-B";
        vm.SetupAgentsOpened();
        first.SetResult(Installed("old"));
        await initial;
        Assert.Equal(new[] { "Sample-A", "Sample-B" }, distros);
        Assert.False(vm.Setup.Status.Checked);
        Assert.True(vm.Setup.IsChecking);
        second.SetResult(Installed("new"));
        await vm.SetupCheckTask;
        Assert.Equal("new", vm.Setup.Agents.Single(a => a.Cli == "claude").VersionText);
    }

    [Fact]
    public async Task ExplicitPostInstallCheckBypassesFreshCache()
    {
        var calls = 0;
        var vm = new MainViewModel(new AppConfig { Tabs = [] })
        {
            DetectSetupAsync = _ => { calls++; return Task.FromResult(Installed()); },
        };
        vm.StartSetupChecks();
        await vm.SetupCheckTask;
        vm.SetupAgentsOpened();
        Assert.Equal(1, calls);
        vm.CheckSetup();
        await vm.SetupCheckTask;
        Assert.Equal(2, calls);
    }
    [Fact]
    public async Task FailureWithoutFactsShowsUnknownAndDoesNotLeakOtherDistributionFacts()
    {
        var config = new AppConfig { Tabs = [], WslDistro = "Sample-A" };
        var vm = new MainViewModel(config)
        {
            DetectSetupAsync = _ => Task.FromException<SetupStatus>(new IOException("sample failure")),
        };
        vm.StartSetupChecks();
        await vm.SetupCheckTask;
        Assert.Equal(Loc.Get("agents.status.failed"), vm.Setup.CheckNotice);
        Assert.All(vm.Setup.Agents, a => Assert.Equal(Loc.Get("agents.status.unknown.short"), a.StatusText));
        vm.DetectSetupAsync = _ => Task.FromResult(Installed());
        vm.CheckSetup();
        await vm.SetupCheckTask;
        Assert.True(vm.Setup.Status.Checked);
        config.WslDistro = "Sample-B";
        vm.DetectSetupAsync = _ => Task.FromException<SetupStatus>(new IOException("sample failure"));
        vm.SetupAgentsOpened();
        await vm.SetupCheckTask;
        Assert.False(vm.Setup.Status.Checked);
        Assert.Equal(Loc.Get("agents.status.failed"), vm.Setup.CheckNotice);
        Assert.All(vm.Setup.Agents, a => Assert.False(a.IsInstalled));
    }
}
