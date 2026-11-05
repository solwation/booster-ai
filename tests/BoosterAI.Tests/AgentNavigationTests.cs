using BoosterAI;
using Xunit;

public class AgentNavigationTests
{
    [Fact]
    public void NavigationPreservesEditorsDraftsAndHasExactlyOneSelection()
    {
        var vm = new MainViewModel(new AppConfig { Tabs = [] });
        vm.ShowSampleAgents();
        var agents = vm.Setup.Agents.ToArray();
        var claude = agents.Single(a => a.Cli == "claude");
        var editor = claude.Compact!;
        claude.SelectedMode = claude.ModeChoices.Single(c => c.Mode == AgentMode.Yolo);
        editor.Mode = editor.Modes.Single(m => m.Id == "custom");
        editor.Custom = "654321";
        foreach (var agent in agents.Concat(agents.Reverse()))
        {
            vm.Setup.SelectedAgent = agent;
            Assert.Same(agent, Assert.Single(agents, a => a.IsSelected));
            Assert.Same(agent, vm.Setup.SelectedAgent);
            Assert.Same(agent, vm.Setup.Agents.Single(a => a.Cli == agent.Cli));
        }
        Assert.Same(editor, claude.Compact);
        Assert.Equal(AgentMode.Yolo, claude.SelectedMode.Mode);
        Assert.Equal("custom", editor.Mode.Id);
        Assert.Equal("654321", editor.Custom);
    }

    [Fact]
    public void RefreshKeepsAgentSectionAndOverviewPreservesDrafts()
    {
        var setup = new SetupViewModel();
        setup.SelectedAgent = setup.Agents.Single(a => a.Cli == "interpreter");
        setup.SelectedAgent!.Section = 2;
        setup.SelectedAgent = new AgentInfo(new("unknown", "none", null));
        Assert.Equal("interpreter", setup.SelectedAgent!.Cli);
        setup.Apply(SetupStatus.Empty, "Ubuntu");
        Assert.Same(setup.SelectedAgent, setup.Agents.Single(a => a.Cli == "interpreter"));
        Assert.Single(setup.Agents, a => a.IsSelected);
        Assert.Equal(2, setup.SelectedAgent!.Section);
        setup.SelectedAgent = null;
        Assert.True(setup.IsAgentOverview);
        Assert.DoesNotContain(setup.Agents, a => a.IsSelected);
        setup.Apply(SetupStatus.Empty, "Ubuntu");
        Assert.True(setup.IsAgentOverview);
    }
}
