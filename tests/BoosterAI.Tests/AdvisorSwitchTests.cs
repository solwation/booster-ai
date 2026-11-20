using BoosterAI;
using Xunit;

/// <summary>The main window's Advisor switch (#180): the Advisor row under Claude projects shows only on demand.</summary>
[Collection(ConfigFileCollection.Name)]
public sealed class AdvisorSwitchTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("bs-advisor-switch-").FullName;

    public AdvisorSwitchTests() => ConfigStore.UseFile(Path.Combine(_root, "config.json"));
    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void RowsAreHiddenByDefaultAndTheSwitchShowsThemForClaudeProjectsOnly()
    {
        var vm = new MainViewModel(new AppConfig { Tabs = [new() { Llm = "claude", Model = "sonnet" }, new() { Llm = "codex" }] });
        var (claude, codex) = (vm.Tabs[0], vm.Tabs[1]);
        var child = vm.AddSubTab(claude);

        Assert.True(vm.CanShowAdvisor);
        Assert.False(vm.ShowAdvisor);
        Assert.False(claude.AdvisorVisible);

        vm.ShowAdvisor = true;
        Assert.True(claude.AdvisorVisible);
        Assert.False(codex.AdvisorVisible);
        Assert.False(child.AdvisorVisible); // a subtab uses its project's row

        vm.ShowAdvisor = false;
        Assert.False(claude.AdvisorVisible);
    }

    [Fact]
    public void TheSwitchFollowsWhetherAnyProjectRunsClaude()
    {
        var vm = new MainViewModel(new AppConfig { Tabs = [new() { Llm = "codex" }] });
        var project = vm.Tabs[0];
        var child = vm.AddSubTab(project);
        Assert.False(vm.CanShowAdvisor);

        child.Llm = "claude"; // a Claude subtab alone has no row to show: the advisor is chosen on the project
        Assert.False(vm.CanShowAdvisor);

        project.Llm = "claude";
        Assert.True(vm.CanShowAdvisor);

        project.Llm = "codex";
        Assert.False(vm.CanShowAdvisor);
    }

    [Fact]
    public void AChosenAdvisorIsNeverHiddenAndStartsWithTheSwitchOn()
    {
        var vm = new MainViewModel(new AppConfig { Tabs = [new() { Llm = "claude", Model = "sonnet", Advisor = "opus" }, new() { Llm = "claude", Model = "sonnet" }] });
        var (chosen, off) = (vm.Tabs[0], vm.Tabs[1]);
        Assert.True(vm.ShowAdvisor);

        vm.ShowAdvisor = false;
        Assert.True(chosen.AdvisorVisible);
        Assert.False(off.AdvisorVisible);

        // Clearing the field mid-edit keeps the row; it hides the next time the switch turns off.
        chosen.Advisor = "";
        Assert.True(chosen.AdvisorVisible);
        vm.ShowAdvisor = true;
        vm.ShowAdvisor = false;
        Assert.False(chosen.AdvisorVisible);

        // Hiding never changes what is saved or launched.
        Assert.Equal(AdvisorPolicy.Disabled, chosen.ToConfig(forLaunch: true).Advisor);
    }

    [Fact]
    public void TheSwitchIsSavedAndReadBack()
    {
        var vm = new MainViewModel(new AppConfig { Tabs = [new() { Llm = "claude", Model = "sonnet" }] });
        vm.ShowAdvisor = true;
        vm.Save();

        var reloaded = new MainViewModel(ConfigStore.Load());
        Assert.True(reloaded.ShowAdvisor);
        Assert.True(reloaded.Tabs[0].AdvisorVisible);
    }
}
