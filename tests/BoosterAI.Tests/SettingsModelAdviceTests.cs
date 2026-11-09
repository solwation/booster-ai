using BoosterAI;
using Xunit;

namespace BoosterAI.Tests;

public class SettingsModelAdviceTests
{
    private const long GiB = LocalModelAdvice.GiB;
    private static HardwareSnapshot Hardware(long available, params GpuMemory[] gpus) => new(16 * GiB, available, "Example CPU", 8, gpus);
    private static string State(SettingsModelAdviceViewModel advice) => advice.CardStatus;

    [Theory]
    [InlineData("aider")]
    [InlineData("interpreter")]
    [InlineData("openhands")]
    public void AdviceIsAvailableBeforeInstallationAndModelSelection(string cli)
    {
        var hardware = new SettingsHardwareAdviceState(_ => throw new InvalidOperationException("Construction must not probe"));
        var agent = new AgentInfo(new(cli, "none", null), hardware);
        Assert.False(agent.IsInstalled);
        Assert.True(agent.ShowModelAdvice);
        Assert.True(agent.Advice.IsSupported);
        Assert.Equal(Loc.Get("settingsAdvice.status.pending"), State(agent.Advice));
        hardware.SetSnapshot(Hardware(8 * GiB));
        Assert.Equal(Loc.Get("settingsAdvice.status.modelDependent"), State(agent.Advice));
        Assert.False(agent.Advice.HasProfile);
    }

    [Fact]
    public void AdviceAppliesToLocalModelsNotCloudOnlyAgents()
    {
        var agent = new AgentInfo(new("claude", "none", null));
        Assert.False(agent.Advice.IsSupported);
        Assert.False(agent.ShowModelAdvice);
    }

    [Fact]
    public void InstalledAgentShowsAdviceOnOverviewAndDoesNotCrowdOtherSections()
    {
        var agent = new AgentInfo(new("aider", "file", "/sample/aider"));
        Assert.True(agent.ShowModelAdvice);
        agent.Section = 1;
        Assert.False(agent.ShowModelAdvice);
        agent.Section = 2;
        Assert.False(agent.ShowModelAdvice);
    }

    [Fact]
    public async Task HardwareReadIsSharedCoalescedAndCachedAcrossAgents()
    {
        var completed = new TaskCompletionSource<HardwareSnapshot>();
        var reads = 0;
        var hardware = new SettingsHardwareAdviceState(_ => { reads++; return completed.Task; });
        var aider = new AgentInfo(new("aider", "none", null), hardware);
        var interpreter = new AgentInfo(new("interpreter", "none", null), hardware);
        var first = hardware.EnsureAsync(CancellationToken.None);
        await hardware.EnsureAsync(CancellationToken.None);
        Assert.Equal(1, reads);
        Assert.Equal(Loc.Get("settingsAdvice.status.checking"), State(aider.Advice));
        completed.SetResult(Hardware(8 * GiB));
        await first;
        await hardware.EnsureAsync(CancellationToken.None);
        Assert.Equal(1, reads);
        Assert.Equal(aider.Advice.HardwareSummary, interpreter.Advice.HardwareSummary);
        Assert.Equal(Loc.Get("settingsAdvice.status.modelDependent"), State(aider.Advice));
    }

    [Fact]
    public async Task ClosingDuringReadCannotPublishLateDataAndAllowsRetry()
    {
        var completed = new TaskCompletionSource<HardwareSnapshot>();
        var reads = 0;
        var hardware = new SettingsHardwareAdviceState(_ => ++reads == 1 ? completed.Task : Task.FromResult(Hardware(8 * GiB)));
        using var cancellation = new CancellationTokenSource();
        var first = hardware.EnsureAsync(cancellation.Token);
        cancellation.Cancel();
        completed.SetResult(Hardware(2 * GiB));
        await first;
        Assert.False(hardware.HasReading);
        Assert.False(hardware.IsReading);
        await hardware.EnsureAsync(CancellationToken.None);
        Assert.Equal(2, reads);
        Assert.True(hardware.HasReading);
    }

    [Fact]
    public void CpuPlanningUsesDocumentedFileEstimateAndReactsToUpdatedResources()
    {
        var hardware = new SettingsHardwareAdviceState();
        var agent = new AgentInfo(new("aider", "none", null), hardware);
        var advice = agent.Advice;
        advice.Profile = advice.Profiles.Single(p => p.Id == "cpu7b");
        hardware.SetSnapshot(Hardware(2 * GiB));
        Assert.Equal(Loc.Get("settingsAdvice.status.cpuRamSmall"), State(advice));
        hardware.SetSnapshot(Hardware(8 * GiB));
        Assert.Equal(Loc.Get("settingsAdvice.status.cpuRoom"), State(advice));
        Assert.Equal("cpu7b", advice.Profile.Id);
        hardware.SetSnapshot(HardwareSnapshot.Empty);
        Assert.Equal(Loc.Get("settingsAdvice.status.ramUnknown"), State(advice));
    }

    [Fact]
    public void OpenHandsProfileNeverTreatsUnknownGpuAsMissingOrSumsSmallCards()
    {
        var hardware = new SettingsHardwareAdviceState();
        var advice = new AgentInfo(new("openhands", "none", null), hardware).Advice;
        advice.Profile = advice.Profiles.Single(p => p.Id == "gpu35b");
        hardware.SetSnapshot(Hardware(8 * GiB));
        Assert.Equal(Loc.Get("settingsAdvice.status.gpuUnknown"), State(advice));
        hardware.SetSnapshot(Hardware(8 * GiB, new GpuMemory("GPU A", 12 * GiB, 8 * GiB), new GpuMemory("GPU B", 12 * GiB, 8 * GiB)));
        Assert.Equal(Loc.Get("settingsAdvice.status.gpuSmall"), State(advice));
        // A nominal 24 GB card may report a few MiB below 24 GiB. Published GB are compared as decimal bytes.
        hardware.SetSnapshot(Hardware(8 * GiB, new GpuMemory("GPU C", 24 * GiB - 32 * 1024 * 1024, 12 * GiB)));
        Assert.Equal(Loc.Get("settingsAdvice.status.gpuLevel"), State(advice));
        advice.Profile = advice.Profiles[0];
        Assert.Equal(Loc.Get("settingsAdvice.status.modelDependent"), State(advice));
    }
}
